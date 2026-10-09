using System.Text.Json.Serialization;

namespace AccessWifi.Api.Features.Authorize
{
    /// <summary>Pedido do portal. Também aceita os nomes antigos dos campos (<see cref="AuthorizeRequestJsonConverter"/>).</summary>
    [JsonConverter(typeof(AuthorizeRequestJsonConverter))]
    public record AuthorizeRequest(
        string Name,
        string Instagram,
        string Phone,
        string BirthDate,
        bool Consent,
        string? Unit,
        string? Mac,
        string? Ap,
        string? Ssid,
        string? Url,
        /// <summary>
        /// Endereço em que o portal foi aberto. Usado para achar a unidade quando não veio
        /// "unit" — a UniFi não consegue mandar query string para o portal externo.
        /// </summary>
        string? Host = null);
}
