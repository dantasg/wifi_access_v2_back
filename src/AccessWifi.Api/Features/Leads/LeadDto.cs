namespace AccessWifi.Api.Features.Leads;

/// <summary>Formato de lead que o front consome em GET /admin/leads.</summary>
public record LeadDto(
    DateTime Timestamp,
    string Nome,
    string Instagram,
    string Telefone,
    string Nascimento,
    string? Mac,
    string? Ap,
    string? Ssid);
