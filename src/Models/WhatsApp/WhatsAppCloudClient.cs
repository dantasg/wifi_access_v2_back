using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Models.WhatsApp
{
    /// <summary>Onde fica a API da Meta (Graph API). Configurável só para testar contra uma Meta falsa.</summary>
    public sealed class WhatsAppOptions
    {
        public const string SectionName = "WhatsApp";

        public string GraphBaseUrl { get; set; } = "https://graph.facebook.com/";

        /// <summary>Versão da Graph API (a documentação da Meta usa a v26.0 nos exemplos de 10/2026).</summary>
        public string GraphVersion { get; set; } = "v26.0";
    }

    /// <summary>
    /// Uma ida à Meta, para o registro: o que foi pedido (sem o token) e o que ela respondeu. RequestBody é o
    /// JSON enviado, nos pedidos que têm corpo (criar modelo).
    /// </summary>
    public sealed record WhatsAppCall(
        string Request, int HttpStatus, long DurationMs, string Response, string? RequestBody = null);

    /// <summary>O número, como a Meta mostra no teste de conexão.</summary>
    public sealed record WhatsAppPhoneInfo(
        string DisplayPhoneNumber, string VerifiedName, string QualityRating, string MessagingLimit);

    /// <summary>Um modelo de mensagem como a Meta devolve: id, nome, situação, categoria e motivo da recusa.</summary>
    public sealed record WhatsAppTemplateInfo(
        string Id, string Name, string Status, string Category, string RejectedReason);

    /// <summary>A Meta aceitou a mensagem: o id dela (wamid), o número do cliente no WhatsApp e a situação de entrega.</summary>
    public sealed record WhatsAppSendResult(string MessageId, string WaId, string MessageStatus);

    /// <summary>A Meta recusou (ou não respondeu). A mensagem já vem em português, pronta para a tela.</summary>
    public sealed class WhatsAppApiException : Exception
    {
        public WhatsAppApiException(
            string sMessage, int? iMetaCode, IReadOnlyList<WhatsAppCall> objCalls, int? iMetaSubcode = null)
            : base(sMessage)
        {
            MetaCode = iMetaCode;
            MetaSubcode = iMetaSubcode;
            Calls = objCalls;
        }

        /// <summary>O código de erro da Meta (ex.: 190 = token inválido), quando ela devolve um.</summary>
        public int? MetaCode { get; }

        /// <summary>O subcódigo (ex.: 2388024 = já existe modelo com esse nome e idioma).</summary>
        public int? MetaSubcode { get; }

        /// <summary>As idas à Meta até o erro, para o registro.</summary>
        public IReadOnlyList<WhatsAppCall> Calls { get; }
    }

    /// <summary>
    /// Fala com a API oficial do WhatsApp (Cloud API, na Graph API da Meta) com as credenciais de uma empresa
    /// (PROPOSTA_WHATSAPP.md). Compartilhado pela API (teste de conexão) e pelo serviço de campanhas (modelos e
    /// envios). O token vai só no cabeçalho Authorization e nunca entra no registro das chamadas.
    /// </summary>
    public partial class WhatsAppCloudClient
    {
        /// <summary>Já existe modelo com esse nome nesse idioma (template-management).</summary>
        public const int DuplicateTemplateSubcode = 2388024;

        private const int MaxTemplatePages = 20;

        // Os ids da Meta entram no caminho da URL: só dígitos, para um valor errado não escapar do caminho.
        [GeneratedRegex("^[0-9]{5,30}$")]
        private static partial Regex MetaIdRegex();

        [GeneratedRegex("^[a-z0-9_]{1,512}$")]
        private static partial Regex TemplateNameRegex();

        private readonly HttpClient _objHttpClient;
        private readonly WhatsAppOptions _objOptions;

        public WhatsAppCloudClient(HttpClient objHttpClient, WhatsAppOptions objOptions)
        {
            _objHttpClient = objHttpClient;
            _objOptions = objOptions;
        }

        public static bool IsValidMetaId(string? sValue) => sValue is not null && MetaIdRegex().IsMatch(sValue);

        /// <summary>
        /// Teste de conexão: lê o número (nome, qualidade e limite de envio) e confere se ele é mesmo da conta do
        /// WhatsApp informada, que é a usada nos modelos de mensagem. Não envia nada para ninguém.
        /// </summary>
        public async Task<(WhatsAppPhoneInfo objPhone, List<WhatsAppCall> objCalls)> CheckConnectionAsync(
            string sPhoneNumberId, string sBusinessAccountId, string sAccessToken,
            CancellationToken objCancellationToken = default)
        {
            List<WhatsAppCall> objCalls = [];
            if (!IsValidMetaId(sPhoneNumberId) || !IsValidMetaId(sBusinessAccountId))
            {
                throw new WhatsAppApiException(
                    "O id do número e o id da conta do WhatsApp são só números (como aparecem no painel da Meta).",
                    null, objCalls);
            }

            JsonNode objPhone = await SendAsync(HttpMethod.Get,
                $"{sPhoneNumberId}?fields=display_phone_number,verified_name,quality_rating,whatsapp_business_manager_messaging_limit",
                null, sAccessToken, objCalls, true, objCancellationToken);

            JsonNode objNumbers = await SendAsync(HttpMethod.Get,
                $"{sBusinessAccountId}/phone_numbers?fields=id", null, sAccessToken, objCalls, true, objCancellationToken);
            bool bBelongs = objNumbers["data"] is JsonArray objList
                && objList.Any(objItem => objItem?["id"]?.ToString() == sPhoneNumberId);
            if (!bBelongs)
            {
                throw new WhatsAppApiException(
                    "O número não aparece nessa conta do WhatsApp. Confira o id da conta (WABA ID) no painel da Meta.",
                    null, objCalls);
            }

            WhatsAppPhoneInfo objInfo = new WhatsAppPhoneInfo(
                DisplayPhoneNumber: Text(objPhone["display_phone_number"]),
                VerifiedName: Text(objPhone["verified_name"]),
                QualityRating: Text(objPhone["quality_rating"]),
                MessagingLimit: Text(objPhone["whatsapp_business_manager_messaging_limit"]));
            return (objInfo, objCalls);
        }

        /// <summary>
        /// Cria um modelo de marketing na conta do WhatsApp: corpo com {{1}}, {{2}}… e um exemplo de cada, e o rodapé.
        /// A Meta responde com o id, a situação (em geral PENDING) e a categoria.
        /// </summary>
        public async Task<(WhatsAppTemplateInfo objTemplate, List<WhatsAppCall> objCalls)> CreateTemplateAsync(
            string sBusinessAccountId, string sAccessToken, string sName, string sLanguage, string sCategory,
            string sBodyText, IReadOnlyList<string> objExamples, string sFooterText,
            CancellationToken objCancellationToken = default)
        {
            List<WhatsAppCall> objCalls = [];
            if (!IsValidMetaId(sBusinessAccountId) || !TemplateNameRegex().IsMatch(sName))
            {
                throw new WhatsAppApiException(
                    "O id da conta do WhatsApp ou o nome do modelo é inválido.", null, objCalls);
            }

            JsonObject objBody = new JsonObject { ["type"] = "BODY", ["text"] = sBodyText };
            if (objExamples.Count > 0)
            {
                JsonArray objValues = [];
                foreach (string sExample in objExamples)
                {
                    objValues.Add(sExample);
                }
                objBody["example"] = new JsonObject { ["body_text"] = new JsonArray(objValues) };
            }
            JsonArray objComponents = [objBody];
            if (sFooterText.Length > 0)
            {
                objComponents.Add(new JsonObject { ["type"] = "FOOTER", ["text"] = sFooterText });
            }
            JsonObject objRequest = new JsonObject
            {
                ["name"] = sName,
                ["language"] = sLanguage,
                ["category"] = sCategory,
                ["components"] = objComponents,
            };

            JsonNode objResponse = await SendAsync(HttpMethod.Post, $"{sBusinessAccountId}/message_templates",
                objRequest, sAccessToken, objCalls, false, objCancellationToken);
            WhatsAppTemplateInfo objInfo = new WhatsAppTemplateInfo(
                Id: Text(objResponse["id"]),
                Name: sName,
                Status: Text(objResponse["status"]),
                Category: Text(objResponse["category"]),
                RejectedReason: "");
            return (objInfo, objCalls);
        }

        /// <summary>
        /// Manda um modelo aprovado para um cliente (POST /{número}/messages). O número vai com "+" e o código do país,
        /// como a Meta recomenda; os valores vão na ordem de {{1}}, {{2}}… A resposta só diz que a Meta aceitou: a
        /// entrega chega depois, nos avisos.
        /// </summary>
        public async Task<(WhatsAppSendResult objResult, List<WhatsAppCall> objCalls)> SendTemplateAsync(
            string sPhoneNumberId, string sAccessToken, string sToPhone, string sTemplateName, string sLanguage,
            IReadOnlyList<string> objValues, CancellationToken objCancellationToken = default)
        {
            List<WhatsAppCall> objCalls = [];
            if (!IsValidMetaId(sPhoneNumberId) || !TemplateNameRegex().IsMatch(sTemplateName)
                || sToPhone.Length is < 10 or > 15 || !sToPhone.All(char.IsAsciiDigit))
            {
                throw new WhatsAppApiException("O número do cliente, o id do número ou o nome do modelo é inválido.",
                    null, objCalls);
            }

            JsonArray objParameters = [];
            foreach (string sValue in objValues)
            {
                objParameters.Add(new JsonObject { ["type"] = "text", ["text"] = sValue });
            }
            JsonObject objTemplate = new JsonObject
            {
                ["name"] = sTemplateName,
                ["language"] = new JsonObject { ["code"] = sLanguage },
            };
            if (objValues.Count > 0)
            {
                objTemplate["components"] = new JsonArray(new JsonObject { ["type"] = "body", ["parameters"] = objParameters });
            }
            JsonObject objRequest = new JsonObject
            {
                ["messaging_product"] = "whatsapp",
                ["recipient_type"] = "individual",
                ["to"] = "+" + sToPhone,
                ["type"] = "template",
                ["template"] = objTemplate,
            };

            JsonNode objResponse = await SendAsync(HttpMethod.Post, $"{sPhoneNumberId}/messages", objRequest, sAccessToken,
                objCalls, false, objCancellationToken);
            WhatsAppSendResult objResult = new WhatsAppSendResult(
                MessageId: Text(objResponse["messages"]?[0]?["id"]),
                WaId: Text(objResponse["contacts"]?[0]?["wa_id"]),
                MessageStatus: Text(objResponse["messages"]?[0]?["message_status"]));
            if (objResult.MessageId.Length == 0)
            {
                throw new WhatsAppApiException("A Meta respondeu sem o id da mensagem.", null, objCalls);
            }
            return (objResult, objCalls);
        }

        /// <summary>
        /// Liga a conta do WhatsApp ao app (POST /{conta}/subscribed_apps): sem isso a Meta não manda os avisos dessa
        /// conta ("You must individually subscribe to every Messaging account"). Pode repetir sem problema.
        /// </summary>
        public async Task<List<WhatsAppCall>> SubscribeAppAsync(
            string sBusinessAccountId, string sAccessToken, CancellationToken objCancellationToken = default)
        {
            List<WhatsAppCall> objCalls = [];
            if (!IsValidMetaId(sBusinessAccountId))
            {
                throw new WhatsAppApiException("O id da conta do WhatsApp é inválido.", null, objCalls);
            }
            await SendAsync(HttpMethod.Post, $"{sBusinessAccountId}/subscribed_apps", new JsonObject(), sAccessToken,
                objCalls, true, objCancellationToken);
            return objCalls;
        }

        /// <summary>A situação atual de um modelo, pelo id.</summary>
        public async Task<(WhatsAppTemplateInfo objTemplate, List<WhatsAppCall> objCalls)> GetTemplateAsync(
            string sTemplateId, string sAccessToken, CancellationToken objCancellationToken = default)
        {
            List<WhatsAppCall> objCalls = [];
            if (!IsValidMetaId(sTemplateId))
            {
                throw new WhatsAppApiException("O id do modelo na Meta é inválido.", null, objCalls);
            }
            JsonNode objResponse = await SendAsync(HttpMethod.Get,
                $"{sTemplateId}?fields=id,name,status,category,rejected_reason", null, sAccessToken, objCalls, false,
                objCancellationToken);
            return (ReadTemplate(objResponse), objCalls);
        }

        /// <summary>
        /// Procura um modelo pelo nome e idioma na conta do WhatsApp (a lista da Meta não filtra por nome: lê as
        /// páginas). Usado quando a criação diz que o nome já existe, para adotar o modelo que já está lá.
        /// </summary>
        public async Task<(WhatsAppTemplateInfo? objTemplate, List<WhatsAppCall> objCalls)> FindTemplateAsync(
            string sBusinessAccountId, string sAccessToken, string sName, string sLanguage,
            CancellationToken objCancellationToken = default)
        {
            List<WhatsAppCall> objCalls = [];
            if (!IsValidMetaId(sBusinessAccountId))
            {
                throw new WhatsAppApiException("O id da conta do WhatsApp é inválido.", null, objCalls);
            }

            string sAfter = "";
            for (int iPage = 0; iPage < MaxTemplatePages; iPage++)
            {
                string sPath = $"{sBusinessAccountId}/message_templates?fields=id,name,status,category,rejected_reason,language&limit=100"
                    + (sAfter.Length > 0 ? "&after=" + Uri.EscapeDataString(sAfter) : "");
                JsonNode objPage = await SendAsync(
                    HttpMethod.Get, sPath, null, sAccessToken, objCalls, false, objCancellationToken);

                if (objPage["data"] is JsonArray objList)
                {
                    foreach (JsonNode? objItem in objList)
                    {
                        if (objItem is not null && Text(objItem["name"]) == sName && Text(objItem["language"]) == sLanguage)
                        {
                            return (ReadTemplate(objItem), objCalls);
                        }
                    }
                }

                sAfter = Text(objPage["paging"]?["cursors"]?["after"]);
                if (sAfter.Length == 0 || objPage["paging"]?["next"] is null)
                {
                    break;
                }
            }
            return (null, objCalls);
        }

        /// <summary>
        /// Uma chamada à Graph API. Erro da Meta vira <see cref="WhatsAppApiException"/> com o motivo em português.
        /// bIdErrors: o código 100 quer dizer "id não encontrado" (teste de conexão); nas outras chamadas é
        /// parâmetro inválido, e o motivo é o da Meta.
        /// </summary>
        private async Task<JsonNode> SendAsync(
            HttpMethod objMethod, string sPathAndQuery, JsonNode? objBody, string sAccessToken,
            List<WhatsAppCall> objCalls, bool bIdErrors, CancellationToken objCancellationToken)
        {
            string sRelative = $"{_objOptions.GraphVersion.Trim('/')}/{sPathAndQuery}";
            Uri objUri = new Uri(new Uri(EnsureTrailingSlash(_objOptions.GraphBaseUrl)), sRelative);
            using HttpRequestMessage objRequest = new HttpRequestMessage(objMethod, objUri);
            objRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", sAccessToken);
            string? sRequestBody = objBody?.ToJsonString();
            if (sRequestBody is not null)
            {
                objRequest.Content = new StringContent(sRequestBody, Encoding.UTF8, "application/json");
            }

            string sRequestLine = $"{objMethod.Method} /{sRelative}";
            Stopwatch objClock = Stopwatch.StartNew();
            HttpResponseMessage objResponse;
            try
            {
                objResponse = await _objHttpClient.SendAsync(objRequest, objCancellationToken);
            }
            catch (Exception objException) when (objException is HttpRequestException or TaskCanceledException
                && !objCancellationToken.IsCancellationRequested)
            {
                objCalls.Add(new WhatsAppCall(
                    sRequestLine, 0, objClock.ElapsedMilliseconds, objException.Message, sRequestBody));
                throw new WhatsAppApiException(
                    "Não foi possível falar com a Meta agora (sem resposta). Tente de novo em alguns minutos.",
                    null, objCalls);
            }

            using (objResponse)
            {
                string sResponse = await objResponse.Content.ReadAsStringAsync(objCancellationToken);
                objCalls.Add(new WhatsAppCall(
                    sRequestLine, (int)objResponse.StatusCode, objClock.ElapsedMilliseconds, sResponse, sRequestBody));

                JsonNode? objJson = TryParse(sResponse);
                if (objResponse.IsSuccessStatusCode && objJson is not null)
                {
                    return objJson;
                }
                throw BuildError(objResponse.StatusCode, objJson, objCalls, bIdErrors);
            }
        }

        /// <summary>
        /// Traduz o erro da Graph API ({"error":{"message","code","error_subcode","error_user_msg"}}) para a tela.
        /// </summary>
        private static WhatsAppApiException BuildError(
            HttpStatusCode objStatus, JsonNode? objJson, List<WhatsAppCall> objCalls, bool bIdErrors)
        {
            JsonNode? objError = objJson?["error"];
            int? iCode = Number(objError?["code"]);
            int? iSubcode = Number(objError?["error_subcode"]);
            // error_user_msg é a explicação que a Meta escreve para pessoas; message é a técnica.
            string sMetaMessage = Text(objError?["error_user_msg"]);
            if (sMetaMessage.Length == 0)
            {
                sMetaMessage = Text(objError?["message"]);
            }
            string sSuffix = iCode is null ? "" : $" (código {iCode} da Meta)";

            string sMessage = iCode switch
            {
                190 => "Token inválido ou vencido. Gere um novo token de usuário do sistema no painel da Meta.",
                10 or 200 => "O token não tem permissão para essa conta do WhatsApp. Confira se o usuário do sistema "
                    + "tem acesso à conta e as permissões whatsapp_business_management e whatsapp_business_messaging.",
                100 when bIdErrors => "A Meta não achou o número ou a conta com esse id, ou o token não tem acesso a eles. "
                    + "Confira os ids no painel da Meta.",
                _ => string.IsNullOrWhiteSpace(sMetaMessage)
                    ? $"A Meta recusou o pedido (HTTP {(int)objStatus})."
                    : "A Meta recusou o pedido: " + sMetaMessage,
            };
            return new WhatsAppApiException(sMessage + sSuffix, iCode, objCalls, iSubcode);
        }

        private static WhatsAppTemplateInfo ReadTemplate(JsonNode objNode) => new WhatsAppTemplateInfo(
            Id: Text(objNode["id"]),
            Name: Text(objNode["name"]),
            Status: Text(objNode["status"]),
            Category: Text(objNode["category"]),
            RejectedReason: Text(objNode["rejected_reason"]));

        private static int? Number(JsonNode? objNode) =>
            objNode?.GetValueKind() == JsonValueKind.Number ? objNode.GetValue<int>() : null;

        private static JsonNode? TryParse(string sBody)
        {
            try
            {
                return string.IsNullOrWhiteSpace(sBody) ? null : JsonNode.Parse(sBody);
            }
            catch (JsonException)
            {
                return null;
            }
        }

        private static string Text(JsonNode? objNode)
        {
            if (objNode is null)
            {
                return "";
            }
            return objNode.GetValueKind() == JsonValueKind.String ? objNode.GetValue<string>() : objNode.ToJsonString();
        }

        private static string EnsureTrailingSlash(string sUrl) => sUrl.EndsWith('/') ? sUrl : sUrl + "/";
    }

    /// <summary>
    /// As idas à Meta em JSON válido, para as colunas jsonb do registro. Pedidos: método e caminho (o token vai só
    /// no cabeçalho), mais o corpo quando há. Respostas: status, tempo e o corpo como a Meta devolveu.
    /// </summary>
    public static class WhatsAppCallLog
    {
        public static string RequestsJson(IReadOnlyList<WhatsAppCall> objCalls)
        {
            JsonArray objArray = [];
            foreach (WhatsAppCall objCall in objCalls)
            {
                if (objCall.RequestBody is null)
                {
                    objArray.Add(objCall.Request);
                    continue;
                }
                objArray.Add(new JsonObject
                {
                    ["request"] = objCall.Request,
                    ["body"] = ParseOrText(objCall.RequestBody),
                });
            }
            return objArray.ToJsonString();
        }

        public static string ResponsesJson(IReadOnlyList<WhatsAppCall> objCalls)
        {
            JsonArray objArray = [];
            foreach (WhatsAppCall objCall in objCalls)
            {
                objArray.Add(new JsonObject
                {
                    ["status"] = objCall.HttpStatus,
                    ["durationMs"] = objCall.DurationMs,
                    ["body"] = ParseOrText(objCall.Response),
                });
            }
            return objArray.ToJsonString();
        }

        private static JsonNode? ParseOrText(string sValue)
        {
            try
            {
                return string.IsNullOrWhiteSpace(sValue) ? null : JsonNode.Parse(sValue);
            }
            catch (JsonException)
            {
                return sValue;
            }
        }
    }
}
