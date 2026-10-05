using System.Net.Http.Headers;
using System.Text.Json;

namespace AccessWifi.Ops
{
    /// <summary>
    /// API de robôs do Telegram (https://core.telegram.org/bots/api). O token só existe dentro deste processo:
    /// nunca aparece na linha de comando nem no log.
    /// </summary>
    public class TelegramClient
    {
        private static readonly HttpClient s_objHttp = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };

        private readonly string _sToken;

        public TelegramClient(string sToken)
        {
            _sToken = sToken;
        }

        public async Task<JsonElement> CallAsync(
            string sMethod, IReadOnlyDictionary<string, string> dicFields, string? sFilePath = null,
            CancellationToken objCancellationToken = default)
        {
            string sUrl = $"https://api.telegram.org/bot{_sToken}/{sMethod}";
            HttpContent objContent;
            if (sFilePath is null)
            {
                objContent = new FormUrlEncodedContent(dicFields);
            }
            else
            {
                MultipartFormDataContent objMultipart = new MultipartFormDataContent();
                foreach (KeyValuePair<string, string> objPair in dicFields)
                {
                    objMultipart.Add(new StringContent(objPair.Value), objPair.Key);
                }
                ByteArrayContent objFile = new ByteArrayContent(
                    await File.ReadAllBytesAsync(sFilePath, objCancellationToken));
                objFile.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
                objMultipart.Add(objFile, "document", Path.GetFileName(sFilePath));
                objContent = objMultipart;
            }

            using HttpResponseMessage objResponse = await s_objHttp.PostAsync(sUrl, objContent, objCancellationToken);
            string sBody = await objResponse.Content.ReadAsStringAsync(objCancellationToken);
            JsonElement objJson;
            try
            {
                objJson = JsonDocument.Parse(sBody.Length > 0 ? sBody : "{}").RootElement.Clone();
            }
            catch (JsonException)
            {
                throw new InvalidOperationException($"Telegram respondeu HTTP {(int)objResponse.StatusCode} sem JSON.");
            }

            if (!objJson.TryGetProperty("ok", out JsonElement objOk) || !objOk.GetBoolean())
            {
                string sDescription = objJson.TryGetProperty("description", out JsonElement objDescription)
                    ? objDescription.GetString() ?? "sem detalhe"
                    : "sem detalhe";
                throw new InvalidOperationException($"Telegram recusou: {sDescription}");
            }
            return objJson.TryGetProperty("result", out JsonElement objResult) ? objResult : default;
        }

        public Task SendMessageAsync(string sChatId, string sText, CancellationToken objCancellationToken = default)
        {
            return Retry.RunAsync(() => CallAsync("sendMessage", new Dictionary<string, string>
            {
                ["chat_id"] = sChatId,
                ["text"] = sText.Length > 4000 ? sText[..4000] : sText,
            }, null, objCancellationToken));
        }

        public Task SendDocumentAsync(
            string sChatId, string sCaption, string sFilePath, CancellationToken objCancellationToken = default)
        {
            return Retry.RunAsync(() => CallAsync("sendDocument", new Dictionary<string, string>
            {
                ["chat_id"] = sChatId,
                ["caption"] = sCaption.Length > 1000 ? sCaption[..1000] : sCaption,
            }, sFilePath, objCancellationToken));
        }
    }
}
