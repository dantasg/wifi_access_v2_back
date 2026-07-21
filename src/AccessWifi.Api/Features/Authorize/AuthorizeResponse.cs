namespace AccessWifi.Api.Features.Authorize;

/// <summary>Resposta do POST /authorize — o front só considera sucesso se authorized == true.</summary>
public record AuthorizeResponse(bool Authorized, string? Redirect = null, string? Error = null);
