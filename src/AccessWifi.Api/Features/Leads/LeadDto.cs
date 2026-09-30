namespace AccessWifi.Api.Features.Leads
{
    /// <param name="Timestamp">Último acesso: muda a cada vez que o aparelho reconecta.</param>
    /// <param name="CreatedAt">Primeiro cadastro: não muda depois de gravado.</param>
    public record LeadDto(
        DateTime Timestamp,
        DateTime CreatedAt,
        string Nome,
        string Instagram,
        string Telefone,
        string Nascimento,
        string? Mac,
        string? Ap,
        string? Ssid,
        string UnitSlug,
        string UnitName);
}
