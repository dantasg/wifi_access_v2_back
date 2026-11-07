namespace AccessWifi.Api.Features.Leads
{
    /// <param name="Timestamp">Último acesso: muda a cada vez que o aparelho reconecta.</param>
    /// <param name="CreatedAt">Primeiro cadastro: não muda depois de gravado.</param>
    /// <param name="WhatsAppOptOutAt">Quando o cliente desse telefone respondeu "SAIR" no WhatsApp (D7). Nulo = recebe.</param>
    public record LeadDto(
        DateTime Timestamp,
        DateTime CreatedAt,
        string Name,
        string Instagram,
        string Phone,
        string BirthDate,
        string? Mac,
        string? Ap,
        string? Ssid,
        string UnitSlug,
        string UnitName,
        DateTime? WhatsAppOptOutAt = null);
}
