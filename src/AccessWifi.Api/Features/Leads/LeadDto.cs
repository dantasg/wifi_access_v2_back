namespace AccessWifi.Api.Features.Leads
{
    public record LeadDto(
        DateTime Timestamp,
        string Nome,
        string Instagram,
        string Telefone,
        string Nascimento,
        string? Mac,
        string? Ap,
        string? Ssid);
}
