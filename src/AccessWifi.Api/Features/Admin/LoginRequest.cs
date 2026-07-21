namespace AccessWifi.Api.Features.Admin;

/// <summary>Credenciais do admin (POST /admin/login).</summary>
public record LoginRequest(string Username, string Password);
