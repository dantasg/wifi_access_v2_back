namespace AccessWifi.Api.Features.Authorize
{
    public record AuthorizeRequest(
        string Nome,
        string Instagram,
        string Telefone,
        string Nascimento,
        bool Consentimento,
        string? Company,
        string? Mac,
        string? Ap,
        string? Ssid,
        string? Url);
}
