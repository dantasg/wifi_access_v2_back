using Models.Campaigns;
using Models.DataBase;

namespace AccessWifi.Api.Features.Settings;

/// <summary>Cores e logo do PDF de exemplo (os da tela de Configurações, mesmo antes de salvar).</summary>
public record CampaignPdfPreviewRequest(ThemeColorsDto Colors, string? Logo);

/// <summary>
/// PDF de campanha de exemplo (aniversariantes da semana) com clientes fictícios, para ver como o PDF que
/// vai no e-mail fica com o logo e as cores da empresa — e pedir ajuste de cor ou de layout. Nada é enviado.
/// </summary>
public static class CampaignPdfSample
{
    public const string CampaignName = "Aniversariantes da semana (exemplo)";

    private const string Mensagem =
        "Feliz aniversário, {primeiro_nome}! 🎉 A {empresa} deseja um dia incrível para você.";

    private static readonly (string sNome, string sInstagram, int iIdade)[] s_arrClientes =
    [
        ("Ana Exemplo", "ana.exemplo", 28),
        ("Bruno Exemplo", "bruno.exemplo", 35),
        ("Carla Exemplo", "", 42),
        ("Diego Exemplo", "diego.exemplo", 19),
        ("Elisa Exemplo", "elisa.exemplo", 51),
        ("Felipe Exemplo", "", 33),
    ];

    /// <param name="sDdd">DDD da empresa, para os telefones de exemplo; vazio = "00".</param>
    public static CampaignPdfData Build(
        string sCompanyName, string sUnitName, string sDdd, string? sLogo, ThemeColors objColors, DateOnly dtHoje)
    {
        // Dias da semana de aniversários que ainda vêm depois de hoje (no sábado, nenhum).
        (_, DateOnly dtFim) = CampaignCalendar.BirthdayRange(dtHoje);
        int iDepois = dtFim.DayNumber - dtHoje.DayNumber;
        string sPrefixo = sDdd.Length == 2 ? sDdd : "00";

        List<CampaignPdfRow> objRows = s_arrClientes
            .Select((objCliente, iIndice) =>
            {
                // Os dois primeiros fazem aniversário hoje (aparecem em destaque); os outros, nos dias seguintes.
                DateOnly dtAniversario = iIndice < 2 || iDepois == 0 ? dtHoje : dtHoje.AddDays(1 + (iIndice - 2) % iDepois);
                string sMensagem = CampaignMessage.Render(Mensagem, new CampaignMessageData(
                    objCliente.sNome, sCompanyName, sUnitName, objCliente.iIdade, null));
                return (dtAniversario, Row: new CampaignPdfRow(
                    objCliente.sNome,
                    $"{sPrefixo}90000{iIndice + 1:0000}",
                    objCliente.sInstagram,
                    $"{CampaignPdf.ShortDate(dtAniversario)} · {objCliente.iIdade} anos",
                    dtAniversario == dtHoje,
                    sMensagem));
            })
            .OrderBy(item => item.dtAniversario)
            .Select(item => item.Row)
            .ToList();

        return new CampaignPdfData(
            sCompanyName, sUnitName, CampaignName, CampaignKind.Birthday, dtHoje, Mensagem, sLogo, objColors, objRows);
    }
}
