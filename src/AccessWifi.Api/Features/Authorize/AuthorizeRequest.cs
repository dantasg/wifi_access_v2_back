namespace AccessWifi.Api.Features.Authorize;

/// <summary>Payload enviado pelo portal do visitante (POST /authorize).</summary>
public record AuthorizeRequest(
    string Nome,
    string Instagram,
    string Telefone,
    string Nascimento,
    bool Consentimento,
    string? Mac,
    string? Ap,
    string? Ssid,
    string? Url);
