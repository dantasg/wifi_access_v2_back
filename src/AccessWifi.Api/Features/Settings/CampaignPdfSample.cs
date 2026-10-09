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

    private const string SampleMessage =
        "Feliz aniversário, {primeiro_nome}! 🎉 A {empresa} deseja um dia incrível para você.";

    private static readonly (string sName, string sInstagram, int iAge)[] s_arrCustomers =
    [
        ("Ana Exemplo", "ana.exemplo", 28),
        ("Bruno Exemplo", "bruno.exemplo", 35),
        ("Carla Exemplo", "", 42),
        ("Diego Exemplo", "diego.exemplo", 19),
        ("Elisa Exemplo", "elisa.exemplo", 51),
        ("Felipe Exemplo", "", 33),
    ];

    /// <param name="sAreaCode">DDD da empresa, para os telefones de exemplo; vazio = "00".</param>
    public static CampaignPdfData Build(
        string sCompanyName, string sUnitName, string sAreaCode, string? sLogo, ThemeColors objColors, DateOnly dtToday)
    {
        // Dias da semana de aniversários que ainda vêm depois de hoje (no sábado, nenhum).
        (_, DateOnly dtEnd) = CampaignCalendar.BirthdayRange(dtToday);
        int iAfter = dtEnd.DayNumber - dtToday.DayNumber;
        string sPrefix = sAreaCode.Length == 2 ? sAreaCode : "00";

        List<CampaignPdfRow> objRows = s_arrCustomers
            .Select((objCustomer, iIndex) =>
            {
                // Os dois primeiros fazem aniversário hoje (aparecem em destaque); os outros, nos dias seguintes.
                DateOnly dtBirthday = iIndex < 2 || iAfter == 0 ? dtToday : dtToday.AddDays(1 + (iIndex - 2) % iAfter);
                string sMessage = CampaignMessage.Render(SampleMessage, new CampaignMessageData(
                    objCustomer.sName, sCompanyName, sUnitName, objCustomer.iAge, null));
                return (dtBirthday, Row: new CampaignPdfRow(
                    objCustomer.sName,
                    $"{sPrefix}90000{iIndex + 1:0000}",
                    objCustomer.sInstagram,
                    $"{CampaignPdf.ShortDate(dtBirthday)} · {objCustomer.iAge} anos",
                    dtBirthday == dtToday,
                    sMessage));
            })
            .OrderBy(item => item.dtBirthday)
            .Select(item => item.Row)
            .ToList();

        return new CampaignPdfData(
            sCompanyName, sUnitName, CampaignName, CampaignKind.Birthday, dtToday, SampleMessage, sLogo, objColors, objRows);
    }
}
