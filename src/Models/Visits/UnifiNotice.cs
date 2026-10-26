using System.Globalization;
using System.Text.Json;
using Models.DataBase;

namespace Models.Visits
{
    public enum UnifiNoticeKind
    {
        /// <summary>"WiFi Client Connected": o aparelho entrou na rede.</summary>
        Connected,

        /// <summary>"... Disconnected": o aparelho saiu da rede.</summary>
        Disconnected,

        /// <summary>Outro aviso de cliente (ex.: trocou de ponto de acesso): só mostra que o aparelho continua ali.</summary>
        Other,
    }

    /// <summary>
    /// O que interessa num aviso do Alarm Manager da UniFi (PROPOSTA_VISITAS_UNIFI.md §2). Exemplo do que chega:
    /// <c>{"name":"WiFi Client Connected","parameters":{"UNIFIclientMac":"26:3e:04:44:81:cb","UNIFIwifiName":"PIX
    /// REGIONAL","UNIFIconnectedToDeviceMac":"8c:30:66:4e:9b:58","UNIFIclientHostname":"Redmi-Note-13",
    /// "UNIFIutcTime":"2026-10-23T18:05:30.538Z",...}}</c>.
    /// </summary>
    /// <param name="Mac">Endereço do aparelho do cliente, normalizado ("" = o aviso não traz).</param>
    /// <param name="WifiName">Nome da rede Wi-Fi ("" = cabo, ou o aviso não traz).</param>
    /// <param name="Ap">Ponto de acesso, normalizado ("" = não veio).</param>
    /// <param name="EventAtUtc">Hora do aviso na UniFi; nulo quando não veio ou não deu para ler.</param>
    public sealed record UnifiNotice(
        UnifiNoticeKind Kind, string Name, string Mac, string WifiName, string Ap, string Hostname, DateTime? EventAtUtc)
    {
        /// <summary>Lê o corpo do aviso. Nulo quando não é JSON ou não é um aviso de cliente (sem endereço do aparelho).</summary>
        public static UnifiNotice? Parse(string? sBody)
        {
            if (string.IsNullOrWhiteSpace(sBody))
            {
                return null;
            }

            try
            {
                using JsonDocument objDocument = JsonDocument.Parse(sBody);
                JsonElement objRoot = objDocument.RootElement;
                if (objRoot.ValueKind != JsonValueKind.Object
                    || !objRoot.TryGetProperty("parameters", out JsonElement objParameters)
                    || objParameters.ValueKind != JsonValueKind.Object)
                {
                    return null;
                }

                string sMac = MacAddress.Normalize(Text(objParameters, "UNIFIclientMac"));
                if (sMac.Length == 0)
                {
                    return null;
                }

                string sName = Text(objRoot, "name");
                return new UnifiNotice(
                    KindOf(sName),
                    sName,
                    sMac,
                    Text(objParameters, "UNIFIwifiName").Trim(),
                    MacAddress.Normalize(Text(objParameters, "UNIFIconnectedToDeviceMac")),
                    Cut(Text(objParameters, "UNIFIclientHostname").Trim(), 100),
                    ParseUtc(Text(objParameters, "UNIFIutcTime")));
            }
            catch (JsonException)
            {
                return null;
            }
        }

        private static UnifiNoticeKind KindOf(string sName)
        {
            if (sName.Contains("disconnect", StringComparison.OrdinalIgnoreCase))
            {
                return UnifiNoticeKind.Disconnected;
            }
            return sName.Contains("connected", StringComparison.OrdinalIgnoreCase)
                ? UnifiNoticeKind.Connected
                : UnifiNoticeKind.Other;
        }

        private static string Text(JsonElement objElement, string sProperty) =>
            objElement.TryGetProperty(sProperty, out JsonElement objValue) && objValue.ValueKind == JsonValueKind.String
                ? objValue.GetString() ?? ""
                : "";

        private static DateTime? ParseUtc(string sValue) =>
            DateTime.TryParse(sValue, CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out DateTime dtValue)
                ? DateTime.SpecifyKind(dtValue, DateTimeKind.Utc)
                : null;

        private static string Cut(string sValue, int iMax) => sValue.Length <= iMax ? sValue : sValue[..iMax];
    }
}
