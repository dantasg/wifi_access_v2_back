namespace AccessWifi.Api.Features.Admin;

/// <summary>Resposta do login: o front guarda o token só em memória.</summary>
public record LoginResponse(string Token);
