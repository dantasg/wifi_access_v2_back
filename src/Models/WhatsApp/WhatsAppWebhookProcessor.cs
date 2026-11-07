using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Models.DataBase;
using Models.Persistence;

namespace Models.WhatsApp
{
    /// <summary>
    /// Trata um aviso da Meta (PROPOSTA_WHATSAPP.md, parte 4) já com a assinatura conferida:
    /// <list type="bullet">
    /// <item>situação das mensagens (enviada, entregue, lida, falhou): na mensagem, no registro de situações e no
    /// cliente da campanha;</item>
    /// <item>mensagens de texto de clientes cadastrados (D11, D13), com a campanha a que pertencem (D14) e o "SAIR"
    /// (D7);</item>
    /// <item>situação dos modelos (aprovado, recusado, pausado) e o limite do número.</item>
    /// </list>
    /// A Meta repete avisos por até 7 dias e não garante a ordem: tratar duas vezes não muda nada, e "lida" antes de
    /// "entregue" não faz a mensagem voltar atrás. Não grava: quem chama salva no fim.
    /// </summary>
    public sealed class WhatsAppWebhookProcessor
    {
        private static readonly JsonSerializerOptions s_objJsonOptions = new(JsonSerializerDefaults.Web);

        private readonly AppDbContext _objDbContext;
        private readonly Company _objCompany;
        private readonly DateTime _dtNowUtc;

        public WhatsAppWebhookProcessor(AppDbContext objDbContext, Company objCompany, DateTime dtNowUtc)
        {
            _objDbContext = objDbContext;
            _objCompany = objCompany;
            _dtNowUtc = dtNowUtc;
        }

        /// <summary>Os campos do aviso ("messages", "message_template_status_update"…), para o registro.</summary>
        public static string Fields(JsonNode? objPayload) =>
            string.Join(",", (objPayload?["entry"] as JsonArray ?? [])
                .SelectMany(objEntry => objEntry?["changes"] as JsonArray ?? [])
                .Select(objChange => Text(objChange?["field"]))
                .Where(sField => sField.Length > 0)
                .Distinct());

        public async Task ProcessAsync(JsonNode objPayload, CancellationToken objCancellationToken = default)
        {
            foreach (JsonNode? objEntry in objPayload["entry"] as JsonArray ?? [])
            {
                // O aviso precisa ser da conta do WhatsApp desta empresa.
                if (objEntry is null || Text(objEntry["id"]) != _objCompany.WhatsApp.BusinessAccountId)
                {
                    continue;
                }
                foreach (JsonNode? objChange in objEntry["changes"] as JsonArray ?? [])
                {
                    JsonNode? objValue = objChange?["value"];
                    if (objValue is null)
                    {
                        continue;
                    }
                    switch (Text(objChange!["field"]))
                    {
                        case "messages":
                            await MessagesAsync(objValue, objCancellationToken);
                            break;
                        case "message_template_status_update":
                            await TemplateStatusAsync(objValue, objCancellationToken);
                            break;
                        case "phone_number_quality_update":
                            await PhoneUpdateAsync(objValue, objCancellationToken);
                            break;
                    }
                }
            }
        }

        // ------------------------------------------------------------------ messages

        private async Task MessagesAsync(JsonNode objValue, CancellationToken objCancellationToken)
        {
            // E do número desta empresa.
            string sPhoneNumberId = Text(objValue["metadata"]?["phone_number_id"]);
            if (sPhoneNumberId.Length > 0 && sPhoneNumberId != _objCompany.WhatsApp.PhoneNumberId)
            {
                return;
            }
            foreach (JsonNode? objStatus in objValue["statuses"] as JsonArray ?? [])
            {
                if (objStatus is not null)
                {
                    await StatusAsync(objStatus, objCancellationToken);
                }
            }

            Dictionary<string, string> objProfiles = (objValue["contacts"] as JsonArray ?? [])
                .Where(objContact => objContact is not null)
                .GroupBy(objContact => Text(objContact!["wa_id"]))
                .ToDictionary(group => group.Key, group => Text(group.First()!["profile"]?["name"]));
            foreach (JsonNode? objMessage in objValue["messages"] as JsonArray ?? [])
            {
                if (objMessage is not null)
                {
                    await InboundAsync(objMessage, objProfiles, objCancellationToken);
                }
            }
        }

        /// <summary>Ordem das situações: uma mensagem não volta atrás ("lida" antes de "entregue" fica lida).</summary>
        private static int Rank(string sStatus) => sStatus switch
        {
            WhatsAppMessageStatus.Accepted => 0,
            WhatsAppMessageStatus.Sent => 1,
            WhatsAppMessageStatus.Delivered => 2,
            WhatsAppMessageStatus.Read => 3,
            _ => -1,
        };

        private async Task StatusAsync(JsonNode objStatus, CancellationToken objCancellationToken)
        {
            string sMetaId = Text(objStatus["id"]);
            string sStatus = Text(objStatus["status"]) switch
            {
                "sent" => WhatsAppMessageStatus.Sent,
                "delivered" => WhatsAppMessageStatus.Delivered,
                "read" => WhatsAppMessageStatus.Read,
                "failed" => WhatsAppMessageStatus.Failed,
                _ => "",
            };
            if (sMetaId.Length == 0 || sStatus.Length == 0)
            {
                return;
            }
            WhatsAppMessage? objMessage = await _objDbContext.WhatsAppMessages.FirstOrDefaultAsync(
                message => message.IDCompany == _objCompany.Id && message.MetaMessageId == sMetaId, objCancellationToken);
            if (objMessage is null)
            {
                return;
            }

            // Aviso repetido: já tratado.
            bool bRepeated = _objDbContext.WhatsAppMessageEvents.Local.Any(evt =>
                    evt.IDWhatsAppMessage == objMessage.Id && evt.Status == sStatus)
                || await _objDbContext.WhatsAppMessageEvents.AnyAsync(evt =>
                    evt.IDWhatsAppMessage == objMessage.Id && evt.Status == sStatus, objCancellationToken);
            if (bRepeated)
            {
                return;
            }

            DateTime dtAt = UnixTime(objStatus["timestamp"]) ?? _dtNowUtc;
            JsonNode? objError = (objStatus["errors"] as JsonArray)?.FirstOrDefault();
            int? iErrorCode = objError?["code"]?.GetValueKind() == JsonValueKind.Number ? objError["code"]!.GetValue<int>() : null;
            string sErrorText = sStatus == WhatsAppMessageStatus.Failed
                ? WhatsAppErrors.Describe(iErrorCode,
                    FirstNonEmpty(Text(objError?["error_data"]?["details"]), Text(objError?["title"]), Text(objError?["message"])))
                : "";
            JsonNode? objPricing = objStatus["pricing"];
            bool? bBillable = objPricing?["billable"]?.GetValueKind() is JsonValueKind.True or JsonValueKind.False
                ? objPricing["billable"]!.GetValue<bool>()
                : null;
            string sCategory = Text(objPricing?["category"]);
            string sPricingType = Text(objPricing?["type"]);

            _objDbContext.WhatsAppMessageEvents.Add(new WhatsAppMessageEvent
            {
                IDWhatsAppMessage = objMessage.Id,
                Status = sStatus,
                MetaTimestamp = dtAt,
                ErrorCode = iErrorCode,
                ErrorMessage = Truncate(sErrorText, 1000),
                PricingCategory = Truncate(sCategory, 40),
                PricingType = Truncate(sPricingType, 40),
                Billable = bBillable,
                PayloadJson = objStatus.ToJsonString(),
                CreatedAt = _dtNowUtc,
            });

            if (sCategory.Length > 0)
            {
                objMessage.PricingCategory = Truncate(sCategory, 40);
            }
            if (sPricingType.Length > 0)
            {
                objMessage.PricingType = Truncate(sPricingType, 40);
            }
            objMessage.Billable = bBillable ?? objMessage.Billable;

            // Fica a hora mais cedo que a Meta informou (os avisos podem chegar fora de ordem).
            switch (sStatus)
            {
                case WhatsAppMessageStatus.Sent:
                    objMessage.SentAt = Earliest(objMessage.SentAt, dtAt);
                    break;
                case WhatsAppMessageStatus.Delivered:
                    objMessage.DeliveredAt = Earliest(objMessage.DeliveredAt, dtAt);
                    break;
                case WhatsAppMessageStatus.Read:
                    // Lida na hora: a Meta não manda o "entregue". Se ele vier depois, corrige a hora.
                    objMessage.ReadAt = Earliest(objMessage.ReadAt, dtAt);
                    objMessage.DeliveredAt ??= dtAt;
                    break;
                case WhatsAppMessageStatus.Failed:
                    objMessage.FailedAt = Earliest(objMessage.FailedAt, dtAt);
                    break;
            }

            if (sStatus == WhatsAppMessageStatus.Failed)
            {
                // Falha só conta antes da entrega (depois de entregue ou lida, fica o que já era).
                if (objMessage.Status is WhatsAppMessageStatus.Accepted or WhatsAppMessageStatus.Sent)
                {
                    objMessage.Status = WhatsAppMessageStatus.Failed;
                    objMessage.ErrorCode = iErrorCode;
                    objMessage.ErrorMessage = Truncate(sErrorText, 1000);
                }
            }
            else if (objMessage.Status != WhatsAppMessageStatus.Failed && Rank(sStatus) > Rank(objMessage.Status))
            {
                objMessage.Status = sStatus;
            }

            await MirrorToRecipientAsync(objMessage, objCancellationToken);
        }

        /// <summary>O cliente da campanha acompanha a mensagem: a tela, o PDF e a espera do e-mail (D4) leem dele.</summary>
        private async Task MirrorToRecipientAsync(WhatsAppMessage objMessage, CancellationToken objCancellationToken)
        {
            if (objMessage.IDCampaignRecipient is not long lRecipientId)
            {
                return;
            }
            CampaignRecipient? objRecipient = await _objDbContext.CampaignRecipients
                .FirstOrDefaultAsync(recipient => recipient.Id == lRecipientId, objCancellationToken);
            if (objRecipient is null)
            {
                return;
            }
            objRecipient.WhatsAppStatus = objMessage.Status switch
            {
                WhatsAppMessageStatus.Sent => CampaignWhatsAppStatus.Sent,
                WhatsAppMessageStatus.Delivered => CampaignWhatsAppStatus.Delivered,
                WhatsAppMessageStatus.Read => CampaignWhatsAppStatus.Read,
                WhatsAppMessageStatus.Failed => CampaignWhatsAppStatus.Failed,
                _ => CampaignWhatsAppStatus.Accepted,
            };
            objRecipient.WhatsAppError = objMessage.Status == WhatsAppMessageStatus.Failed
                ? Truncate(objMessage.ErrorMessage, 300)
                : null;
        }

        private async Task InboundAsync(
            JsonNode objMessage, IReadOnlyDictionary<string, string> objProfiles, CancellationToken objCancellationToken)
        {
            // D11: só texto.
            if (Text(objMessage["type"]) != "text")
            {
                return;
            }
            string sMetaId = Text(objMessage["id"]);
            string sFrom = new string(Text(objMessage["from"]).Where(char.IsAsciiDigit).ToArray());
            if (sMetaId.Length == 0 || sFrom.Length == 0)
            {
                return;
            }
            bool bRepeated = _objDbContext.WhatsAppInboundMessages.Local.Any(inbound => inbound.MetaMessageId == sMetaId)
                || await _objDbContext.WhatsAppInboundMessages.AnyAsync(inbound => inbound.MetaMessageId == sMetaId,
                    objCancellationToken);
            if (bRepeated)
            {
                return;
            }

            // D13: só de clientes cadastrados. Primeiro pela mensagem que mandamos a esse número; senão, pelo telefone.
            Guid? objCustomerId = await _objDbContext.WhatsAppMessages.AsNoTracking()
                .Where(message => message.IDCompany == _objCompany.Id && message.IDCustomer != null
                    && (message.WaId == sFrom || message.ToPhone == sFrom))
                .OrderByDescending(message => message.CreatedAt)
                .Select(message => message.IDCustomer)
                .FirstOrDefaultAsync(objCancellationToken);
            if (objCustomerId is null)
            {
                List<string> objCandidates = PhoneCandidates(sFrom);
                objCustomerId = await _objDbContext.Customers.AsNoTracking()
                    .Where(customer => customer.IDCompany == _objCompany.Id && objCandidates.Contains(customer.Phone))
                    .Select(customer => (Guid?)customer.Id)
                    .FirstOrDefaultAsync(objCancellationToken);
            }
            if (objCustomerId is not Guid objCustomer)
            {
                return;
            }

            DateTime dtAt = UnixTime(objMessage["timestamp"]) ?? _dtNowUtc;
            // D14: pertence à última mensagem de campanha enviada a ele antes da resposta.
            var objCampaignMessage = await _objDbContext.WhatsAppMessages.AsNoTracking()
                .Where(message => message.IDCustomer == objCustomer && !message.IsTest && message.IDCampaign != null
                    && message.MetaMessageId != "" && message.CreatedAt <= dtAt)
                .OrderByDescending(message => message.CreatedAt)
                .Select(message => new { message.Id, message.IDCampaign, message.IDCampaignRun })
                .FirstOrDefaultAsync(objCancellationToken);

            string sText = Text(objMessage["text"]?["body"]);
            bool bOptOut = IsOptOut(sText);
            WhatsAppInboundMessage objInbound = new WhatsAppInboundMessage
            {
                IDCompany = _objCompany.Id,
                IDCustomer = objCustomer,
                MetaMessageId = Truncate(sMetaId, 200),
                FromPhone = Truncate(sFrom, 20),
                ProfileName = Truncate(objProfiles.GetValueOrDefault(sFrom, ""), 200),
                Text = Truncate(sText, 4096),
                IsOptOut = bOptOut,
                IDCampaign = objCampaignMessage?.IDCampaign,
                IDCampaignRun = objCampaignMessage?.IDCampaignRun,
                IDWhatsAppMessage = objCampaignMessage?.Id,
                ContextMessageId = Truncate(Text(objMessage["context"]?["id"]), 200),
                SentAt = dtAt,
                PayloadJson = objMessage.ToJsonString(),
                CreatedAt = _dtNowUtc,
            };
            _objDbContext.WhatsAppInboundMessages.Add(objInbound);

            if (bOptOut)
            {
                // D7: sai só do WhatsApp automático; continua no PDF do gestor, com o motivo.
                Customer? objStored = await _objDbContext.Customers
                    .FirstOrDefaultAsync(customer => customer.Id == objCustomer, objCancellationToken);
                if (objStored is not null && objStored.WhatsAppOptOutAt is null)
                {
                    await _objDbContext.SaveChangesAsync(objCancellationToken);
                    objStored.WhatsAppOptOutAt = dtAt;
                    objStored.IDWhatsAppOptOutMessage = objInbound.Id;
                }
            }
        }

        // -------------------------------------------------------------- modelos e número

        private async Task TemplateStatusAsync(JsonNode objValue, CancellationToken objCancellationToken)
        {
            string sTemplateId = Text(objValue["message_template_id"]);
            string sEvent = Text(objValue["event"]).ToUpperInvariant();
            if (sTemplateId.Length == 0 || sEvent.Length == 0)
            {
                return;
            }
            WhatsAppTemplate? objTemplate = await _objDbContext.WhatsAppTemplates.FirstOrDefaultAsync(
                template => template.IDCompany == _objCompany.Id && template.MetaTemplateId == sTemplateId,
                objCancellationToken);
            if (objTemplate is null)
            {
                return;
            }

            string sBefore = WhatsAppTemplateLookup.StatusLabel(objTemplate.Status);
            bool bStatusEvent = sEvent is "APPROVED" or "PENDING" or "IN_APPEAL" or "REJECTED" or "PAUSED" or "DISABLED"
                or "ARCHIVED" or "DELETED" or "PENDING_DELETION" or "LIMIT_EXCEEDED";
            if (!bStatusEvent)
            {
                // FLAGGED, REINSTATED, UNARCHIVED, LOCKED…: a situação real vem da próxima conferência do serviço.
                objTemplate.CheckedAt = null;
                AddAccountEvent(WhatsAppAccountEventKind.TemplateStatusChanged,
                    $"Modelo {objTemplate.Name}: aviso da Meta \"{sEvent}\"; a situação vai ser conferida.", objValue);
                if (sEvent == "FLAGGED")
                {
                    await WhatsAppAlerts.EnqueueAsync(_objDbContext, _objCompany.Id, "template-flagged:" + objTemplate.Name,
                        $"WhatsApp — {_objCompany.Name}: texto com qualidade baixa",
                        $"A Meta marcou o texto {objTemplate.Name} com qualidade baixa. Se continuar, ela pode pausar o "
                        + "texto. Revise o texto da campanha.", _dtNowUtc, objCancellationToken);
                }
                return;
            }

            objTemplate.CheckedAt = _dtNowUtc;
            if (!WhatsAppTemplateLookup.ApplyMetaStatus(objTemplate, sEvent, Text(objValue["message_template_category"]),
                    Text(objValue["reason"]), _dtNowUtc))
            {
                return;
            }
            AddAccountEvent(WhatsAppAccountEventKind.TemplateStatusChanged,
                $"Modelo {objTemplate.Name}: {sBefore} → {WhatsAppTemplateLookup.StatusLabel(objTemplate.Status)} (aviso da Meta)."
                + (objTemplate.Reason.Length > 0 ? " " + objTemplate.Reason : ""), objValue);
            if (WhatsAppTemplateLookup.NeedsAlert(objTemplate.Status))
            {
                await WhatsAppAlerts.EnqueueAsync(_objDbContext, _objCompany.Id, "template:" + objTemplate.Name,
                    $"WhatsApp — {_objCompany.Name}: texto {WhatsAppTemplateLookup.StatusLabel(objTemplate.Status)}",
                    $"O texto {objTemplate.Name} ficou {WhatsAppTemplateLookup.StatusLabel(objTemplate.Status)} na Meta. "
                    + objTemplate.Reason + " Enquanto isso, as campanhas usam o último texto aprovado; sem nenhum, os "
                    + "clientes ficam só no PDF do gestor.", _dtNowUtc, objCancellationToken);
            }
        }

        private async Task PhoneUpdateAsync(JsonNode objValue, CancellationToken objCancellationToken)
        {
            string sEvent = Text(objValue["event"]);
            // A Meta trocou current_limit por max_daily_conversations_per_business.
            string sLimit = FirstNonEmpty(
                Text(objValue["max_daily_conversations_per_business"]), Text(objValue["current_limit"]));
            string sBefore = _objCompany.WhatsApp.MessagingLimit;
            if (sLimit.Length > 0 && sLimit != sBefore)
            {
                _objCompany.WhatsApp.MessagingLimit = Truncate(sLimit, 30);
            }
            AddAccountEvent(WhatsAppAccountEventKind.PhoneUpdated,
                $"Aviso da Meta sobre o número ({sEvent}): limite {(sBefore.Length > 0 ? sBefore : "—")}"
                + $" → {(sLimit.Length > 0 ? sLimit : sBefore)}.", objValue);
            if (sEvent is "FLAGGED" or "DOWNGRADE")
            {
                await WhatsAppAlerts.EnqueueAsync(_objDbContext, _objCompany.Id, "phone:" + sEvent,
                    $"WhatsApp — {_objCompany.Name}: qualidade do número",
                    $"A Meta avisou \"{sEvent}\" sobre o número da empresa (limite atual: {sLimit}). Muitos bloqueios ou "
                    + "denúncias baixam a qualidade e o limite de envios. Veja o WhatsApp Manager.", _dtNowUtc,
                    objCancellationToken);
            }
        }

        private void AddAccountEvent(string sKind, string sDetails, JsonNode objValue)
        {
            _objDbContext.WhatsAppAccountEvents.Add(new WhatsAppAccountEvent
            {
                IDCompany = _objCompany.Id,
                Kind = sKind,
                Username = "",
                Details = Truncate(sDetails, 1000),
                ResponseJson = objValue.ToJsonString(),
                CreatedAt = _dtNowUtc,
            });
        }

        // ------------------------------------------------------------------ ajudas

        /// <summary>
        /// D7: "SAIR" com maiúscula ou minúscula, com ou sem ponto, com espaços (só as letras contam: "sair", "Sair.",
        /// " SAIR ").
        /// </summary>
        public static bool IsOptOut(string? sText) =>
            string.Equals(new string((sText ?? "").Where(char.IsLetter).ToArray()), "SAIR", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Os jeitos de o telefone do cliente estar cadastrado (só DDD + número), a partir do número do WhatsApp
        /// ("5593991234567"): com e sem o 55, e com e sem o 9 do celular (o WhatsApp de números antigos vem sem ele).
        /// </summary>
        public static List<string> PhoneCandidates(string sWaId)
        {
            string sLocal = sWaId.StartsWith("55", StringComparison.Ordinal) && sWaId.Length is 12 or 13 ? sWaId[2..] : sWaId;
            List<string> objCandidates = [sLocal, sWaId];
            if (sLocal.Length == 10)
            {
                objCandidates.Add(sLocal[..2] + "9" + sLocal[2..]);
            }
            else if (sLocal.Length == 11 && sLocal[2] == '9')
            {
                objCandidates.Add(sLocal[..2] + sLocal[3..]);
            }
            return objCandidates;
        }

        /// <summary>
        /// Confere a assinatura X-Hub-Signature-256 ("sha256=" + HMAC-SHA256 do corpo com a chave secreta do app), em
        /// tempo constante.
        /// </summary>
        public static bool IsSignatureValid(byte[] arrBody, string? sHeader, string sAppSecret)
        {
            if (string.IsNullOrEmpty(sAppSecret) || sHeader is null
                || !sHeader.StartsWith("sha256=", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
            byte[] arrExpected = HMACSHA256.HashData(Encoding.UTF8.GetBytes(sAppSecret), arrBody);
            byte[] arrGiven;
            try
            {
                arrGiven = Convert.FromHexString(sHeader[7..].Trim());
            }
            catch (FormatException)
            {
                return false;
            }
            return CryptographicOperations.FixedTimeEquals(arrExpected, arrGiven);
        }

        private static DateTime? UnixTime(JsonNode? objNode)
        {
            string sValue = Text(objNode);
            return long.TryParse(sValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out long lSeconds)
                ? DateTimeOffset.FromUnixTimeSeconds(lSeconds).UtcDateTime
                : null;
        }

        private static DateTime Earliest(DateTime? dtCurrent, DateTime dtNew) =>
            dtCurrent is DateTime dtValue && dtValue < dtNew ? dtValue : dtNew;

        private static string FirstNonEmpty(params string[] arrValues) => arrValues.FirstOrDefault(s => s.Length > 0) ?? "";

        private static string Text(JsonNode? objNode)
        {
            if (objNode is null)
            {
                return "";
            }
            return objNode.GetValueKind() == JsonValueKind.String ? objNode.GetValue<string>() : objNode.ToJsonString();
        }

        private static string Truncate(string sValue, int iMax) => sValue.Length <= iMax ? sValue : sValue[..iMax];
    }
}
