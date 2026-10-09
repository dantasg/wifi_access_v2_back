using Models.Reports;

namespace AccessWifi.Api.Features.Leads
{
    /// <summary>
    /// Pedido do PDF da tela de Leads: a lista já filtrada e formatada como está na tela (o mesmo que vai no
    /// CSV), o filtro de unidade (slug), o período e a busca, para o resumo do PDF.
    /// </summary>
    public record LeadsPdfRequest(string? Unit, string? Period, string? Search, IReadOnlyList<LeadsPdfRow>? Rows);
}
