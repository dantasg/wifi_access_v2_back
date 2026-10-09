using System.Globalization;
using Models.DataBase;
using Models.Pdf;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using static Models.Pdf.PdfTheme;

namespace Models.Campaigns
{
    /// <summary>Um cliente no PDF. <paramref name="Message"/> é a mensagem já montada, que vai no link do WhatsApp.</summary>
    public sealed record CampaignPdfRow(
        string Name, string Phone, string Instagram, string Info, bool IsToday, string Message);

    /// <summary>Tudo o que o PDF de uma unidade mostra (D20).</summary>
    public sealed record CampaignPdfData(
        string CompanyName,
        string UnitName,
        string CampaignName,
        string Kind,
        DateOnly LocalDate,
        string MessageTemplate,
        string? LogoDataUrl,
        ThemeColors Colors,
        IReadOnlyList<CampaignPdfRow> Rows);

    /// <summary>
    /// O PDF de campanha que vai para o e-mail da unidade (D20): com a logo e as cores do portal da
    /// empresa, diz qual é a campanha, qual mensagem mandar e lista os clientes, com o WhatsApp (abre a
    /// conversa com a mensagem pronta) e o Instagram de cada um como link.
    /// </summary>
    public static class CampaignPdf
    {
        private static readonly string[] s_arrDays =
            ["domingo", "segunda-feira", "terça-feira", "quarta-feira", "quinta-feira", "sexta-feira", "sábado"];

        private static readonly string[] s_arrShortDays = ["dom", "seg", "ter", "qua", "qui", "sex", "sáb"];

        /// <summary>"segunda-feira, 12/10/2026" (nomes fixos em português: o servidor roda sem cultura instalada).</summary>
        public static string LongDate(DateOnly dtDate) =>
            $"{s_arrDays[(int)dtDate.DayOfWeek]}, {dtDate.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)}";

        /// <summary>"sex, 16/10".</summary>
        public static string ShortDate(DateOnly dtDate) =>
            $"{s_arrShortDays[(int)dtDate.DayOfWeek]}, {dtDate.ToString("dd/MM", CultureInfo.InvariantCulture)}";

        /// <summary>Título da coluna de informação, conforme o tipo. Vazio = sem coluna.</summary>
        public static string InfoHeader(string sKind) => sKind switch
        {
            CampaignKind.Birthday => "Aniversário",
            CampaignKind.SignupAnniversary => "Cadastro",
            CampaignKind.FrequentCustomer => "Visitas",
            CampaignKind.WeMissYou => "Última visita",
            _ => "",
        };

        public static byte[] Build(CampaignPdfData objData)
        {
            Palette objColors = Palette.From(objData.Colors);
            PdfTheme.Configure();
            Logo objLogo = Logo.Read(objData.LogoDataUrl);
            string sMessage = CampaignContact.WithoutEmoji(
                CampaignMessage.RenderShared(objData.MessageTemplate, objData.CompanyName, objData.UnitName),
                out bool bHadEmoji);
            string sInfoTitle = InfoHeader(objData.Kind);
            bool bWithInfo = sInfoTitle.Length > 0 && objData.Rows.Any(row => row.Info.Length > 0);

            return Document.Create(objDocument => objDocument.Page(objPage =>
            {
                objPage.Size(PageSizes.A4);
                objPage.MarginHorizontal(36);
                objPage.MarginVertical(32);
                objPage.PageColor(Colors.White);
                objPage.DefaultTextStyle(style => style.FontSize(10).FontColor(objColors.Ink));

                // ---------------------------------------------------------- Cabeçalho (toda página)
                Header(objPage, objLogo, objColors, objData.CompanyName, "PDF de campanha",
                    $"{objData.CompanyName} · {objData.UnitName}");

                // ---------------------------------------------------------------- Conteúdo
                objPage.Content().Column(objColumn =>
                {
                    objColumn.Spacing(14);

                    objColumn.Item().Column(objTitle =>
                    {
                        objTitle.Item().Text(objData.CampaignName).FontSize(18).Bold().FontColor(objColors.Ink);
                        if (!string.Equals(objData.CampaignName, CampaignKind.Label(objData.Kind), StringComparison.Ordinal))
                        {
                            objTitle.Item().Text($"Campanha de {CampaignKind.Label(objData.Kind).ToLowerInvariant()}").FontColor(objColors.Muted);
                        }
                    });

                    objColumn.Item().Row(objRow =>
                    {
                        Field(objRow.RelativeItem(), "Unidade", objData.UnitName, objColors);
                        Field(objRow.RelativeItem(), "Data", LongDate(objData.LocalDate), objColors);
                        Field(objRow.RelativeItem(), "Clientes", objData.Rows.Count.ToString(), objColors);
                    });

                    if (objData.Kind == CampaignKind.Birthday)
                    {
                        (DateOnly dtStart, DateOnly dtEnd) = CampaignCalendar.BirthdayRange(objData.LocalDate);
                        objColumn.Item().Text(objText =>
                        {
                            objText.Span("Aniversariantes de ").FontColor(objColors.Muted);
                            objText.Span($"{ShortDate(dtStart)} a {ShortDate(dtEnd)}").SemiBold();
                            objText.Span(". Quem faz aniversário hoje está marcado como ").FontColor(objColors.Muted);
                            objText.Span("Hoje").Bold().FontColor(objColors.BrandDark);
                            objText.Span(".").FontColor(objColors.Muted);
                        });
                    }

                    // A mensagem, como a empresa escreveu na tela de campanha.
                    objColumn.Item().Background(objColors.Surface).Border(1).BorderColor(objColors.Line).CornerRadius(8)
                        .Padding(14).Column(objBox =>
                        {
                            objBox.Spacing(6);
                            objBox.Item().Text("Mensagem para enviar").FontSize(12).Bold().FontColor(objColors.BrandDark);
                            objBox.Item().Text(sMessage).FontSize(12).FontColor(objColors.Ink);
                            if (sMessage.Contains('{'))
                            {
                                objBox.Item().Text(
                                    "O que está entre chaves muda para cada cliente: no link do WhatsApp já vai preenchido.")
                                    .FontSize(9).FontColor(objColors.Muted);
                            }
                            if (bHadEmoji)
                            {
                                objBox.Item().Text(
                                    "Os emojis da mensagem não aparecem neste PDF, mas vão junto no link do WhatsApp.")
                                    .FontSize(9).FontColor(objColors.Muted);
                            }
                        });

                    objColumn.Item().Column(objSteps =>
                    {
                        objSteps.Spacing(2);
                        objSteps.Item().Text("Como fazer").Bold().FontColor(objColors.BrandDark);
                        objSteps.Item().Text("1. Clique no WhatsApp do cliente: a conversa abre com a mensagem pronta, já com o nome dele.");
                        objSteps.Item().Text("2. Confira o texto e envie.");
                        objSteps.Item().Text("3. Se o cliente informou o Instagram, o @ também é um link para o perfil dele.");
                    });

                    objColumn.Item().Table(objTable =>
                    {
                        objTable.ColumnsDefinition(objColumns =>
                        {
                            objColumns.ConstantColumn(24);
                            objColumns.RelativeColumn(2.7f);
                            objColumns.RelativeColumn(2.1f);
                            objColumns.RelativeColumn(2f);
                            if (bWithInfo)
                            {
                                // Cabe "Hoje · seg, 12/10 · 28 anos" numa linha.
                                objColumns.RelativeColumn(2.8f);
                            }
                        });

                        objTable.Header(objHeader =>
                        {
                            ColumnTitle(objHeader.Cell(), "#", objColors);
                            ColumnTitle(objHeader.Cell(), "Cliente", objColors);
                            ColumnTitle(objHeader.Cell(), "WhatsApp", objColors);
                            ColumnTitle(objHeader.Cell(), "Instagram", objColors);
                            if (bWithInfo)
                            {
                                ColumnTitle(objHeader.Cell(), sInfoTitle, objColors);
                            }
                        });

                        int iLine = 0;
                        foreach (CampaignPdfRow objCustomer in objData.Rows)
                        {
                            iLine++;
                            Cell(objTable.Cell(), objColors).Text(iLine.ToString()).FontColor(objColors.Muted);
                            Cell(objTable.Cell(), objColors).Text(objCustomer.Name).SemiBold();
                            Cell(objTable.Cell(), objColors)
                                .Hyperlink(CampaignContact.WhatsAppUrl(objCustomer.Phone, objCustomer.Message))
                                .Text(CampaignContact.FormatPhone(objCustomer.Phone)).FontColor(objColors.Link).Underline();

                            IContainer objInstagram = Cell(objTable.Cell(), objColors);
                            string sProfile = InstagramHandle.ProfileUrl(objCustomer.Instagram);
                            if (sProfile.Length > 0)
                            {
                                objInstagram.Hyperlink(sProfile)
                                    .Text("@" + InstagramHandle.Normalize(objCustomer.Instagram)).FontColor(objColors.Link).Underline();
                            }
                            else if (objCustomer.Instagram.Length > 0)
                            {
                                // Fora do formato do Instagram (dado antigo): mostra como veio, sem link.
                                objInstagram.Text(objCustomer.Instagram);
                            }
                            else
                            {
                                objInstagram.Text("—").FontColor(objColors.Muted);
                            }

                            if (bWithInfo)
                            {
                                Cell(objTable.Cell(), objColors).Text(objText =>
                                {
                                    if (objCustomer.IsToday)
                                    {
                                        objText.Span("Hoje · ").Bold().FontColor(objColors.BrandDark);
                                    }
                                    objText.Span(objCustomer.Info);
                                });
                            }
                        }
                    });
                });

                // ------------------------------------------------------------------ Rodapé
                Footer(objPage, objColors);
            })).GeneratePdf();
        }
    }
}
