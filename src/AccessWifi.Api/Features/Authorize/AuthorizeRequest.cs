namespace AccessWifi.Api.Features.Authorize
{
    public record AuthorizeRequest(
        string Nome,
        string Instagram,
        string Telefone,
        string Nascimento,
        bool Consentimento,
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
