using Models.Campaigns;
using Models.DataBase;
using Models.Pdf;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using static Models.Pdf.PdfTheme;

namespace Models.Reports
{
    /// <summary>
    /// Um cadastro no PDF, já como aparece na tela de Leads (datas formatadas pelo painel): assim o PDF traz
    /// exatamente a lista filtrada que a pessoa está vendo.
    /// </summary>
    public sealed record LeadsPdfRow(
        string Name, string Phone, string Instagram, string BirthDate, string Unit, string FirstSignup, string LastAccess);

    /// <param name="Scope">A unidade filtrada, ou "Todas as unidades".</param>
    /// <param name="Period">O período filtrado ("Outubro / 2026", "Todos os períodos").</param>
    /// <param name="Search">A busca por nome, se houver.</param>
    /// <param name="GeneratedAt">Quando foi gerado, já no horário da empresa ("09/10/2026 11:30").</param>
    public sealed record LeadsPdfData(
        string CompanyName,
        string Scope,
        string Period,
        string? Search,
        string GeneratedAt,
        string? LogoDataUrl,
        ThemeColors Colors,
        IReadOnlyList<LeadsPdfRow> Rows);

    /// <summary>
    /// PDF da tela de Leads, no mesmo padrão do PDF de campanha (<see cref="PdfTheme"/>): logo e cores do
    /// portal da empresa, quadro de resumo e a lista com o WhatsApp e o Instagram de cada cadastro como link.
    /// </summary>
    public static class LeadsPdf
    {
        public const string AllPeriods = "Todos os períodos";

        /// <summary>Espaçamento das linhas: a lista pode ter milhares de cadastros.</summary>
        private const float RowPadding = 4;

        public static byte[] Build(LeadsPdfData objData)
        {
            PdfTheme.Configure();
            Palette objColors = Palette.From(objData.Colors);
            Logo objLogo = Logo.Read(objData.LogoDataUrl);
            // Coluna da unidade só quando a lista mistura unidades.
            bool bShowUnit = objData.Rows.Select(row => row.Unit).Distinct(StringComparer.Ordinal).Count() > 1;
            string sTitle = objData.Period == AllPeriods ? "Todos os cadastros" : $"Cadastros de {objData.Period}";

            return Document.Create(objDocument => objDocument.Page(objPage =>
            {
                // Paisagem: a lista tem até oito colunas.
                objPage.Size(PageSizes.A4.Landscape());
                objPage.MarginHorizontal(36);
                objPage.MarginVertical(32);
                objPage.PageColor(Colors.White);
                objPage.DefaultTextStyle(style => style.FontSize(10).FontColor(objColors.Ink));

                Header(objPage, objLogo, objColors, objData.CompanyName, "Cadastros do WiFi",
                    $"{objData.CompanyName} · {objData.Scope}");

                objPage.Content().Column(objColumn =>
                {
                    objColumn.Spacing(14);

                    objColumn.Item().Column(objTitleBlock =>
                    {
                        objTitleBlock.Item().Text(sTitle).FontSize(18).Bold().FontColor(objColors.Ink);
                        objTitleBlock.Item().Text("Do último acesso mais recente para o mais antigo.").FontColor(objColors.Muted);
                    });

                    objColumn.Item().Row(objRow =>
                    {
                        Field(objRow.RelativeItem(), "Unidade", objData.Scope, objColors);
                        Field(objRow.RelativeItem(), "Período", objData.Period, objColors);
                        if (!string.IsNullOrWhiteSpace(objData.Search))
                        {
                            Field(objRow.RelativeItem(), "Busca por nome", $"“{objData.Search.Trim()}”", objColors);
                        }
                        Field(objRow.RelativeItem(), "Cadastros", objData.Rows.Count.ToString(), objColors);
                        Field(objRow.RelativeItem(), "Gerado em", objData.GeneratedAt, objColors);
                    });

                    if (objData.Rows.Count == 0)
                    {
                        objColumn.Item().Background(objColors.Surface).Border(1).BorderColor(objColors.Line).CornerRadius(8)
                            .Padding(14).Text("Nenhum cadastro neste filtro.").FontColor(objColors.Muted);
                        return;
                    }

                    objColumn.Item().Text("O telefone abre a conversa no WhatsApp e o @ abre o perfil no Instagram.")
                        .FontSize(9).FontColor(objColors.Muted);

                    objColumn.Item().DefaultTextStyle(style => style.FontSize(9)).Table(objTable =>
                    {
                        objTable.ColumnsDefinition(objColumns =>
                        {
                            objColumns.ConstantColumn(30);
                            objColumns.RelativeColumn(2.6f);
                            objColumns.RelativeColumn(1.8f);
                            objColumns.RelativeColumn(2f);
                            objColumns.RelativeColumn(1.2f);
                            if (bShowUnit)
                            {
                                objColumns.RelativeColumn(1.6f);
                            }
                            objColumns.RelativeColumn(1.6f);
                            objColumns.RelativeColumn(1.6f);
                        });

                        objTable.Header(objHeader =>
                        {
                            ColumnTitle(objHeader.Cell(), "#", objColors);
                            ColumnTitle(objHeader.Cell(), "Nome", objColors);
                            ColumnTitle(objHeader.Cell(), "WhatsApp", objColors);
                            ColumnTitle(objHeader.Cell(), "Instagram", objColors);
                            ColumnTitle(objHeader.Cell(), "Nascimento", objColors);
                            if (bShowUnit)
                            {
                                ColumnTitle(objHeader.Cell(), "Unidade", objColors);
                            }
                            ColumnTitle(objHeader.Cell(), "Primeiro cadastro", objColors);
                            ColumnTitle(objHeader.Cell(), "Último acesso", objColors);
                        });

                        int iRow = 0;
                        foreach (LeadsPdfRow objLead in objData.Rows)
                        {
                            iRow++;
                            Cell(objTable.Cell(), objColors, RowPadding).Text(iRow.ToString()).FontColor(objColors.Muted);
                            Cell(objTable.Cell(), objColors, RowPadding).Text(OrDash(objLead.Name)).SemiBold();
                            PhoneCell(Cell(objTable.Cell(), objColors, RowPadding), objLead.Phone, objColors);
                            InstagramCell(Cell(objTable.Cell(), objColors, RowPadding), objLead.Instagram, objColors);
                            Cell(objTable.Cell(), objColors, RowPadding).Text(OrDash(objLead.BirthDate));
                            if (bShowUnit)
                            {
                                Cell(objTable.Cell(), objColors, RowPadding).Text(OrDash(objLead.Unit));
                            }
                            Cell(objTable.Cell(), objColors, RowPadding).Text(OrDash(objLead.FirstSignup));
                            Cell(objTable.Cell(), objColors, RowPadding).Text(OrDash(objLead.LastAccess));
                        }
                    });
                });

                Footer(objPage, objColors);
            })).GeneratePdf();
        }

        private static string OrDash(string? sValue) => string.IsNullOrWhiteSpace(sValue) ? "—" : sValue;

        /// <summary>Telefone com DDD vira link do WhatsApp; fora disso, sai como foi digitado.</summary>
        private static void PhoneCell(IContainer objContainer, string sPhone, Palette objColors)
        {
            string sDigits = CustomerDirectory.NormalizePhone(sPhone);
            if (sDigits.Length >= 10)
            {
                objContainer.Hyperlink(CampaignContact.WhatsAppUrl(sPhone, null))
                    .Text(CampaignContact.FormatPhone(sPhone)).FontColor(objColors.Link).Underline();
            }
            else
            {
                objContainer.Text(OrDash(sPhone)).FontColor(sPhone.Length > 0 ? objColors.Ink : objColors.Muted);
            }
        }

        /// <summary>O @ vira link do perfil; o que não estiver no formato do Instagram sai como veio.</summary>
        private static void InstagramCell(IContainer objContainer, string sInstagram, Palette objColors)
        {
            string sProfileUrl = InstagramHandle.ProfileUrl(sInstagram);
            if (sProfileUrl.Length > 0)
            {
                objContainer.Hyperlink(sProfileUrl)
                    .Text("@" + InstagramHandle.Normalize(sInstagram)).FontColor(objColors.Link).Underline();
            }
            else if (sInstagram.Length > 0)
            {
                objContainer.Text(sInstagram);
            }
            else
            {
                objContainer.Text("—").FontColor(objColors.Muted);
            }
        }
    }
}
