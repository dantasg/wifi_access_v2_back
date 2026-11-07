using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Models.Campaigns;
using Models.DataBase;
using Models.Persistence;
using Models.Security;
using Models.WhatsApp;

namespace AccessWifiService.WhatsApp
{
    public sealed class CampaignWhatsAppOptions
    {
        public const string SectionName = "CampaignWhatsApp";

        /// <summary>Depois dos envios, quanto esperar os avisos da Meta antes do e-mail ao gestor (D4).</summary>
        public int WaitMinutes { get; set; } = 10;

        /// <summary>Envios por bloco: entre um bloco e outro, confere se a execução foi pausada ou cancelada.</summary>
        public int BatchSize { get; set; } = 50;

        /// <summary>Quanto tempo uma volta do serviço pode passar enviando (as outras execuções esperam).</summary>
        public int MaxSecondsPerTick { get; set; } = 120;

        /// <summary>Depois de uma falha temporária da Meta (velocidade, fora do ar), espera para tentar de novo.</summary>
        public int RetrySeconds { get; set; } = 60;

        /// <summary>Falhas temporárias por mais que isto: desiste, e quem faltou vai para o PDF com o motivo.</summary>
        public int GiveUpMinutes { get; set; } = 60;
    }

    /// <summary>
    /// A etapa do WhatsApp numa execução de campanha (PROPOSTA_WHATSAPP.md, parte 3), entre escolher os clientes e
    /// mandar o e-mail ao gestor:
    /// <list type="number">
    /// <item>escolhe o texto aprovado (o da versão da execução ou o último aprovado antes dela, §11.2);</item>
    /// <item>manda para cada cliente, respeitando quem saiu (SAIR), o WhatsApp da unidade (D16), o limite do dia
    /// da Meta e a pausa ou cancelamento pela tela;</item>
    /// <item>espera os avisos até 10 minutos (D4).</item>
    /// </list>
    /// Quem não pôde receber fica com o motivo e vai para a parte "Não receberam" do PDF. Se o WhatsApp não puder ser
    /// usado na hora (sem texto aprovado, token inválido…), todos vão para o PDF (D9).
    /// </summary>
    public sealed partial class CampaignWhatsAppSender
    {
        [GeneratedRegex("^TIER_([0-9]+)(K?)$")]
        private static partial Regex TierRegex();

        private readonly AppDbContext _objDbContext;
        private readonly IEncryptor _objEncryptor;
        private readonly WhatsAppCloudClient _objClient;
        private readonly CampaignWhatsAppOptions _objOptions;
        private readonly ILogger<CampaignWhatsAppSender> _objLogger;

        public CampaignWhatsAppSender(
            AppDbContext objDbContext,
            IEncryptor objEncryptor,
            WhatsAppCloudClient objClient,
            CampaignWhatsAppOptions objOptions,
            ILogger<CampaignWhatsAppSender> objLogger)
        {
            _objDbContext = objDbContext;
            _objEncryptor = objEncryptor;
            _objClient = objClient;
            _objOptions = objOptions;
            _objLogger = objLogger;
        }

        /// <summary>O que fazer com uma recusa da Meta no envio.</summary>
        private enum FailureKind
        {
            /// <summary>Só este cliente (número sem WhatsApp, bloqueou a empresa…).</summary>
            Recipient,

            /// <summary>Passageira (velocidade, Meta fora do ar): tenta de novo depois.</summary>
            Retry,

            /// <summary>Da conta ou do texto (token, pagamento, texto pausado): o WhatsApp para nesta execução.</summary>
            Stop,
        }

        /// <summary>
        /// Uma volta da etapa. Devolve true quando ela terminou (todos enviados ou com motivo, e a espera dos avisos
        /// acabou): o e-mail ao gestor pode sair. <paramref name="objRun"/> é a execução rastreada pelo motor.
        /// </summary>
        public async Task<bool> ProcessAsync(
            CampaignRun objRun, Campaign objCampaign, DateTime dtNowUtc, CancellationToken objCancellationToken)
        {
            if (!objRun.WhatsAppEnabled)
            {
                return true;
            }
            if (objRun.WhatsAppWaitUntil is DateTime dtWaitUntil)
            {
                return dtNowUtc >= dtWaitUntil || !await AnyWaitingAsync(objRun.Id, objCancellationToken);
            }
            if (objRun.WhatsAppRetryAt is DateTime dtRetryAt && dtNowUtc < dtRetryAt)
            {
                return false;
            }

            objRun.WhatsAppStartedAt ??= dtNowUtc;
            objRun.WhatsAppRetryAt = null;
            Company objCompany = await _objDbContext.Companies.AsNoTracking()
                .FirstAsync(company => company.Id == objRun.IDCompany, objCancellationToken);
            string sAccessToken = _objEncryptor.Decrypt(objCompany.WhatsApp.AccessToken) ?? "";

            string? sBlocked = !objCompany.WhatsAppEnabled
                ? "O envio automático pelo WhatsApp foi desligado antes do envio."
                : !objCompany.WhatsApp.CheckPassed || sAccessToken.Length == 0
                    || objCompany.WhatsApp.PhoneNumberId.Length == 0
                    ? "A conexão com a Meta não está aprovada (Configurações → WhatsApp → Testar conexão)."
                    : null;
            WhatsAppTemplate? objTemplate = sBlocked is null
                ? await TemplateAsync(objRun, objCompany, objCancellationToken)
                : null;
            if (sBlocked is null && objTemplate is null)
            {
                sBlocked = objRun.IDWhatsAppTemplate is null
                    ? "Nenhum texto desta campanha foi aprovado pela Meta ainda (Configurações → WhatsApp → "
                        + "Textos aprovados pela Meta)."
                    : "O texto que estava saindo deixou de estar aprovado na Meta (pausado ou desativado).";
            }
            if (sBlocked is not null || objTemplate is null)
            {
                await StopAsync(objRun, objCampaign, sBlocked!, dtNowUtc, objCancellationToken);
                return true;
            }

            List<string> objParameters = WhatsAppTemplateBuilder.ParametersFromJson(objTemplate.ParametersJson);
            Dictionary<Guid, Unit> objUnits = await _objDbContext.Units.AsNoTracking()
                .Where(unit => unit.IDCompany == objRun.IDCompany)
                .ToDictionaryAsync(unit => unit.Id, objCancellationToken);
            int iRemaining = await RemainingDailyLimitAsync(objCompany, dtNowUtc, objCancellationToken);
            int iDailyLimit = DailyLimit(objCompany.WhatsApp.MessagingLimit);

            Stopwatch objClock = Stopwatch.StartNew();
            while (objClock.Elapsed.TotalSeconds < Math.Max(5, _objOptions.MaxSecondsPerTick))
            {
                // Pausada ou cancelada pela tela entre um bloco e outro: para aqui; a retomada continua dos pendentes.
                string sStatus = await _objDbContext.CampaignRuns.AsNoTracking()
                    .Where(run => run.Id == objRun.Id).Select(run => run.Status).FirstAsync(objCancellationToken);
                if (sStatus != CampaignRunStatus.Running)
                {
                    return false;
                }

                List<CampaignRecipient> objBatch = await _objDbContext.CampaignRecipients
                    .Where(recipient => recipient.IDRun == objRun.Id
                        && recipient.WhatsAppStatus == CampaignWhatsAppStatus.Pending)
                    .OrderBy(recipient => recipient.Id)
                    .Take(Math.Max(1, _objOptions.BatchSize))
                    .ToListAsync(objCancellationToken);
                if (objBatch.Count == 0)
                {
                    break;
                }

                List<Guid> objCustomerIds = objBatch.Select(recipient => recipient.IDCustomer).ToList();
                Dictionary<Guid, Customer> objCustomers = await _objDbContext.Customers.AsNoTracking()
                    .Where(customer => objCustomerIds.Contains(customer.Id))
                    .ToDictionaryAsync(customer => customer.Id, objCancellationToken);

                foreach (CampaignRecipient objRecipient in objBatch)
                {
                    string? sSkip = SkipReason(objRecipient, objCustomers.GetValueOrDefault(objRecipient.IDCustomer), objUnits);
                    if (sSkip is null && iRemaining <= 0)
                    {
                        sSkip = $"Limite do dia da Meta atingido ({iDailyLimit} clientes em 24 horas).";
                    }
                    List<string>? objValues = null;
                    if (sSkip is null)
                    {
                        (objValues, string sMissing) = WhatsAppTemplateBuilder.Values(objParameters,
                            ValuesFor(objRun, objCampaign, objRecipient, objCustomers[objRecipient.IDCustomer], objUnits));
                        if (objValues is null)
                        {
                            sSkip = $"Falta o dado \"{sMissing}\" deste cliente para o texto do WhatsApp.";
                        }
                    }
                    if (sSkip is not null || objValues is null)
                    {
                        objRecipient.WhatsAppStatus = CampaignWhatsAppStatus.Skipped;
                        objRecipient.WhatsAppError = Truncate(sSkip ?? "", 300);
                        await _objDbContext.SaveChangesAsync(objCancellationToken);
                        continue;
                    }

                    FailureKind? objFailure = await SendAsync(
                        objRun, objCompany, sAccessToken, objTemplate, objRecipient, objValues, dtNowUtc, objCancellationToken);
                    if (objFailure is null)
                    {
                        iRemaining--;
                        continue;
                    }
                    if (objFailure == FailureKind.Stop)
                    {
                        await StopAsync(objRun, objCampaign, objRecipient.WhatsAppError ?? "", dtNowUtc, objCancellationToken);
                        return true;
                    }
                    if (objFailure == FailureKind.Retry)
                    {
                        return await RetryLaterAsync(objRun, objCampaign, objRecipient.WhatsAppError ?? "", dtNowUtc,
                            objCancellationToken);
                    }
                }
            }

            bool bHasPending = await _objDbContext.CampaignRecipients.AnyAsync(
                recipient => recipient.IDRun == objRun.Id && recipient.WhatsAppStatus == CampaignWhatsAppStatus.Pending,
                objCancellationToken);
            if (bHasPending)
            {
                // Acabou o tempo desta volta: a próxima continua.
                await _objDbContext.SaveChangesAsync(objCancellationToken);
                return false;
            }

            bool bWaiting = await AnyWaitingAsync(objRun.Id, objCancellationToken);
            objRun.WhatsAppWaitUntil = bWaiting ? dtNowUtc.AddMinutes(Math.Max(0, _objOptions.WaitMinutes)) : dtNowUtc;
            await _objDbContext.SaveChangesAsync(objCancellationToken);
            _objLogger.LogInformation("Campanha {Campanha}: envios pelo WhatsApp terminados; e-mail ao gestor até {Hora:HH:mm} UTC.",
                objCampaign.Name, objRun.WhatsAppWaitUntil);
            return !bWaiting;
        }

        /// <summary>
        /// O texto que sai: o da versão da execução, se aprovado; senão o último aprovado de uma versão anterior.
        /// Escolhido no começo da etapa e guardado na execução.
        /// </summary>
        private async Task<WhatsAppTemplate?> TemplateAsync(
            CampaignRun objRun, Company objCompany, CancellationToken objCancellationToken)
        {
            if (objRun.IDWhatsAppTemplate is Guid objChosen)
            {
                WhatsAppTemplate? objSaved = await _objDbContext.WhatsAppTemplates.AsNoTracking()
                    .FirstOrDefaultAsync(template => template.Id == objChosen, objCancellationToken);
                return objSaved?.Status == WhatsAppTemplateStatus.Approved ? objSaved : null;
            }

            List<WhatsAppTemplate> objTemplates = await _objDbContext.WhatsAppTemplates.AsNoTracking()
                .Where(template => template.IDCompany == objCompany.Id)
                .ToListAsync(objCancellationToken);
            List<(int iNumber, string sMessage)> objVersions = (await _objDbContext.CampaignVersions.AsNoTracking()
                    .Where(version => version.IDCampaign == objRun.IDCampaign && version.Number <= objRun.VersionNumber)
                    .OrderByDescending(version => version.Number)
                    .Select(version => new { version.Number, version.ConfigJson })
                    .ToListAsync(objCancellationToken))
                .Select(version => (version.Number, CampaignConfig.FromJson(version.ConfigJson).Message))
                .ToList();

            WhatsAppCampaignTemplate objFound = WhatsAppTemplateLookup.ForCampaign(objCompany.Name, objVersions, objTemplates);
            (WhatsAppTemplate? objTemplate, int? iVersion) = objFound.Current?.Status == WhatsAppTemplateStatus.Approved
                ? (objFound.Current, (int?)objRun.VersionNumber)
                : (objFound.Fallback, objFound.FallbackVersion);
            if (objTemplate is not null)
            {
                objRun.IDWhatsAppTemplate = objTemplate.Id;
                objRun.WhatsAppVersionNumber = iVersion;
            }
            return objTemplate;
        }

        /// <summary>Por que este cliente não recebe pelo WhatsApp (nulo = pode receber).</summary>
        private static string? SkipReason(CampaignRecipient objRecipient, Customer? objCustomer, IReadOnlyDictionary<Guid, Unit> objUnits)
        {
            if (objCustomer is null)
            {
                return "O cadastro do cliente não existe mais.";
            }
            if (objCustomer.WhatsAppOptOutAt is DateTime dtOptOut)
            {
                return "Pediu para sair do WhatsApp automático (respondeu SAIR) em "
                    + $"{dtOptOut.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)}.";
            }
            if (objRecipient.IDUnit is not Guid objUnitId || !objUnits.TryGetValue(objUnitId, out Unit? objUnit))
            {
                return "Cliente sem unidade: não há WhatsApp de loja para o fim da mensagem.";
            }
            if (objUnit.StorePhone.Length == 0)
            {
                // Curto: vai em cada cliente do PDF da própria unidade.
                return "A unidade não tem WhatsApp de atendimento cadastrado.";
            }
            string sPhone = CampaignContact.WhatsAppNumber(objRecipient.Phone);
            return sPhone.Length is 12 or 13 ? null : "Telefone do cliente inválido para o WhatsApp.";
        }

        /// <summary>Os dados do cliente para os campos, iguais aos da mensagem do PDF (D13: dia da execução).</summary>
        private static WhatsAppMessageValues ValuesFor(
            CampaignRun objRun, Campaign objCampaign, CampaignRecipient objRecipient, Customer objCustomer,
            IReadOnlyDictionary<Guid, Unit> objUnits)
        {
            Unit objUnit = objUnits[objRecipient.IDUnit!.Value];
            // Aniversário: a idade que completa no dia da semana do aniversário, como no PDF.
            int? iAge = objCampaign.Kind == CampaignKind.Birthday && objCustomer.BirthDate is DateOnly dtBirth
                ? CampaignMessage.FullYearsBetween(dtBirth, CampaignCalendar.BirthdayInRange(dtBirth, objRun.LocalDate) ?? objRun.LocalDate)
                : CampaignMessage.AgeOn(objCustomer.BirthDate, objRun.LocalDate);
            return new WhatsAppMessageValues(
                objRecipient.Name, objUnit.Name, iAge,
                CampaignMessage.FullYearsBetween(objCustomer.FirstVisitDate, objRun.LocalDate),
                CampaignContact.FormatPhone(objUnit.StorePhone));
        }

        /// <summary>Manda para um cliente e registra a mensagem. Devolve nulo se a Meta aceitou, ou o tipo da falha.</summary>
        private async Task<FailureKind?> SendAsync(
            CampaignRun objRun, Company objCompany, string sAccessToken, WhatsAppTemplate objTemplate,
            CampaignRecipient objRecipient, List<string> objValues, DateTime dtNowUtc, CancellationToken objCancellationToken)
        {
            string sToPhone = CampaignContact.WhatsAppNumber(objRecipient.Phone);
            WhatsAppMessage objMessage = new WhatsAppMessage
            {
                IDCompany = objCompany.Id,
                IDCustomer = objRecipient.IDCustomer,
                IDCampaign = objRun.IDCampaign,
                IDCampaignRun = objRun.Id,
                IDCampaignRecipient = objRecipient.Id,
                ToPhone = sToPhone,
                FromPhoneNumberId = objCompany.WhatsApp.PhoneNumberId,
                FromDisplayPhone = objCompany.WhatsApp.DisplayPhoneNumber,
                IDWhatsAppTemplate = objTemplate.Id,
                TemplateName = objTemplate.Name,
                TemplateLanguage = objTemplate.Language,
                VersionNumber = objRun.WhatsAppVersionNumber,
                ParametersJson = WhatsAppTemplateBuilder.ParametersToJson(objValues),
                Text = Truncate(WhatsAppTemplateBuilder.Render(objTemplate.BodyText, objValues)
                    + (objTemplate.FooterText.Length > 0 ? "\n\n" + objTemplate.FooterText : ""), 4000),
                CreatedAt = dtNowUtc,
            };

            FailureKind? objFailure = null;
            IReadOnlyList<WhatsAppCall> objCalls;
            try
            {
                (WhatsAppSendResult objResult, List<WhatsAppCall> objSendCalls) = await _objClient.SendTemplateAsync(
                    objCompany.WhatsApp.PhoneNumberId, sAccessToken, sToPhone, objTemplate.Name, objTemplate.Language,
                    objValues, objCancellationToken);
                objCalls = objSendCalls;
                objMessage.MetaMessageId = Truncate(objResult.MessageId, 200);
                objMessage.WaId = Truncate(objResult.WaId, 20);
                objMessage.MetaMessageStatus = Truncate(objResult.MessageStatus, 40);
                objMessage.Status = WhatsAppMessageStatus.Accepted;
                objMessage.AcceptedAt = dtNowUtc;
                objRecipient.WhatsAppStatus = CampaignWhatsAppStatus.Accepted;
                objRecipient.WhatsAppError = null;
            }
            catch (WhatsAppApiException objException)
            {
                objCalls = objException.Calls;
                (objFailure, string sReason) = Classify(objException);
                objRecipient.WhatsAppError = Truncate(sReason, 300);
                // Passageira: o cliente continua na fila, e a tentativa fica registrada só no log.
                if (objFailure == FailureKind.Retry)
                {
                    _objLogger.LogWarning("WhatsApp: falha passageira ao enviar (empresa {Slug}): {Motivo}",
                        objCompany.Slug, objException.Message);
                    return objFailure;
                }
                objMessage.Status = WhatsAppMessageStatus.Failed;
                objMessage.ErrorCode = objException.MetaCode;
                objMessage.ErrorMessage = Truncate(sReason, 1000);
                objMessage.FailedAt = dtNowUtc;
                objRecipient.WhatsAppStatus = CampaignWhatsAppStatus.Failed;
                if (objException.MetaCode is 132015 or 132016)
                {
                    // A Meta pausou ou desativou o texto: a tela de textos já mostra, sem esperar a próxima conferência.
                    WhatsAppTemplate? objStored = await _objDbContext.WhatsAppTemplates
                        .FirstOrDefaultAsync(template => template.Id == objTemplate.Id, objCancellationToken);
                    if (objStored is not null)
                    {
                        objStored.Status = objException.MetaCode == 132015
                            ? WhatsAppTemplateStatus.Paused
                            : WhatsAppTemplateStatus.Disabled;
                        objStored.Reason = sReason;
                        objStored.StatusChangedAt = dtNowUtc;
                    }
                }
            }

            if (objCalls.Count > 0)
            {
                WhatsAppCall objCall = objCalls[^1];
                objMessage.RequestJson = WhatsAppCallLog.RequestsJson(objCalls);
                objMessage.ResponseJson = WhatsAppCallLog.ResponsesJson(objCalls);
                objMessage.HttpStatus = objCall.HttpStatus;
                objMessage.DurationMs = objCall.DurationMs;
            }
            _objDbContext.WhatsAppMessages.Add(objMessage);
            // Grava a cada envio: se o serviço cair, ninguém recebe duas vezes.
            await _objDbContext.SaveChangesAsync(objCancellationToken);
            return objFailure;
        }

        /// <summary>
        /// O que fazer com a recusa da Meta (página oficial de códigos de erro do Cloud API) e o motivo em português
        /// (<see cref="WhatsAppErrors"/>, o mesmo dos avisos).
        /// </summary>
        private static (FailureKind objKind, string sReason) Classify(WhatsAppApiException objException)
        {
            FailureKind objKind = objException.MetaCode switch
            {
                null or 4 or 80007 or 130429 or 131000 or 131016 or 131057 or 133004 or 2494100 => FailureKind.Retry,
                0 or 190 or 3 or 10 or 200 or 131005 or 368 or 131031 or 131042 or 131045 or 133010 or 131037 or 131048
                    or 131064 or 132001 or 132015 or 132016 or 132000 or 132005 or 132007 or 132012 => FailureKind.Stop,
                _ => FailureKind.Recipient,
            };
            string sReason = objKind == FailureKind.Retry
                ? "A Meta não aceitou agora (velocidade ou instabilidade): " + objException.Message
                : WhatsAppErrors.Describe(objException.MetaCode, objException.Message);
            return (objKind, sReason);
        }

        /// <summary>
        /// O WhatsApp não pode ser usado nesta execução (D9): quem faltou vai para o PDF com o motivo, e o e-mail sai
        /// sem esperar. O texto pausado ou desativado na Meta já fica marcado no modelo.
        /// </summary>
        private async Task StopAsync(
            CampaignRun objRun, Campaign objCampaign, string sReason, DateTime dtNowUtc, CancellationToken objCancellationToken)
        {
            List<CampaignRecipient> objPending = await _objDbContext.CampaignRecipients
                .Where(recipient => recipient.IDRun == objRun.Id && recipient.WhatsAppStatus == CampaignWhatsAppStatus.Pending)
                .ToListAsync(objCancellationToken);
            foreach (CampaignRecipient objRecipient in objPending)
            {
                objRecipient.WhatsAppStatus = CampaignWhatsAppStatus.Skipped;
                objRecipient.WhatsAppError = Truncate(sReason, 300);
            }
            objRun.WhatsAppError = Truncate(sReason, 1000);
            objRun.WhatsAppRetryAt = null;
            // D9: o super admin fica sabendo (o mesmo motivo não se repete em 12 horas).
            await WhatsAppAlerts.EnqueueAsync(_objDbContext, objRun.IDCompany, "stop:" + Truncate(sReason, 80),
                $"WhatsApp — campanha \"{objCampaign.Name}\" sem WhatsApp",
                $"A campanha \"{objCampaign.Name}\" de {objRun.LocalDate:dd/MM/yyyy} não pôde usar o WhatsApp: {sReason} "
                + "Quem faltava foi no PDF do gestor, com o motivo.", dtNowUtc, objCancellationToken);
            objRun.WhatsAppWaitUntil = await AnyWaitingAsync(objRun.Id, objCancellationToken)
                ? dtNowUtc.AddMinutes(Math.Max(0, _objOptions.WaitMinutes))
                : dtNowUtc;
            await _objDbContext.SaveChangesAsync(objCancellationToken);
            _objLogger.LogError("Campanha {Campanha}: WhatsApp não usado na execução {Execucao}: {Motivo}",
                objCampaign.Name, objRun.Id, sReason);
        }

        /// <summary>Falha passageira: tenta de novo daqui a pouco; passada 1 hora desde o começo, desiste (D9).</summary>
        private async Task<bool> RetryLaterAsync(
            CampaignRun objRun, Campaign objCampaign, string sReason, DateTime dtNowUtc, CancellationToken objCancellationToken)
        {
            DateTime dtStarted = objRun.WhatsAppStartedAt ?? dtNowUtc;
            if (dtNowUtc >= dtStarted.AddMinutes(Math.Max(1, _objOptions.GiveUpMinutes)))
            {
                await StopAsync(objRun, objCampaign,
                    $"A Meta ficou {Math.Max(1, _objOptions.GiveUpMinutes)} minutos sem aceitar os envios. {sReason}",
                    dtNowUtc, objCancellationToken);
                return true;
            }
            objRun.WhatsAppRetryAt = dtNowUtc.AddSeconds(Math.Max(5, _objOptions.RetrySeconds));
            await _objDbContext.SaveChangesAsync(objCancellationToken);
            return false;
        }

        /// <summary>Algum cliente com a mensagem aceita pela Meta, ainda sem o aviso de entrega ou falha.</summary>
        private Task<bool> AnyWaitingAsync(Guid objRunId, CancellationToken objCancellationToken) =>
            _objDbContext.CampaignRecipients.AnyAsync(
                recipient => recipient.IDRun == objRunId
                    && (recipient.WhatsAppStatus == CampaignWhatsAppStatus.Accepted
                        || recipient.WhatsAppStatus == CampaignWhatsAppStatus.Sent),
                objCancellationToken);

        /// <summary>
        /// Quantos clientes diferentes ainda cabem no limite da Meta (clientes por 24 horas, lido no teste de conexão).
        /// Conta as mensagens aceitas nas últimas 24 horas, de campanhas e de testes.
        /// </summary>
        private async Task<int> RemainingDailyLimitAsync(
            Company objCompany, DateTime dtNowUtc, CancellationToken objCancellationToken)
        {
            int iLimit = DailyLimit(objCompany.WhatsApp.MessagingLimit);
            if (iLimit == int.MaxValue)
            {
                return iLimit;
            }
            DateTime dtSince = dtNowUtc.AddHours(-24);
            int iUsed = await _objDbContext.WhatsAppMessages.AsNoTracking()
                .Where(message => message.IDCompany == objCompany.Id && message.MetaMessageId != ""
                    && message.Status != WhatsAppMessageStatus.Failed && message.CreatedAt >= dtSince)
                .Select(message => message.ToPhone)
                .Distinct()
                .CountAsync(objCancellationToken);
            return Math.Max(0, iLimit - iUsed);
        }

        /// <summary>"TIER_250" → 250, "TIER_2K" → 2000, "TIER_UNLIMITED" → sem limite. Desconhecido: 250, o da conta nova.</summary>
        public static int DailyLimit(string sMessagingLimit)
        {
            if (sMessagingLimit == "TIER_UNLIMITED")
            {
                return int.MaxValue;
            }
            Match objMatch = TierRegex().Match(sMessagingLimit);
            if (!objMatch.Success || !int.TryParse(objMatch.Groups[1].Value, out int iValue))
            {
                return 250;
            }
            return objMatch.Groups[2].Value == "K" ? iValue * 1000 : iValue;
        }

        private static string Truncate(string sValue, int iMax) => sValue.Length <= iMax ? sValue : sValue[..iMax];
    }
}
