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
        private static readonly string[] s_arrDias =
            ["domingo", "segunda-feira", "terça-feira", "quarta-feira", "quinta-feira", "sexta-feira", "sábado"];

        private static readonly string[] s_arrDiasCurtos = ["dom", "seg", "ter", "qua", "qui", "sex", "sáb"];

        /// <summary>"segunda-feira, 12/10/2026" (nomes fixos em português: o servidor roda sem cultura instalada).</summary>
        public static string LongDate(DateOnly dtDate) =>
            $"{s_arrDias[(int)dtDate.DayOfWeek]}, {dtDate.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)}";

        /// <summary>"sex, 16/10".</summary>
        public static string ShortDate(DateOnly dtDate) =>
            $"{s_arrDiasCurtos[(int)dtDate.DayOfWeek]}, {dtDate.ToString("dd/MM", CultureInfo.InvariantCulture)}";

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
            Palette objCores = Palette.From(objData.Colors);
            PdfTheme.Configure();
            Logo objLogo = Logo.Read(objData.LogoDataUrl);
            string sMensagem = CampaignContact.WithoutEmoji(
                CampaignMessage.RenderShared(objData.MessageTemplate, objData.CompanyName, objData.UnitName),
                out bool bTinhaEmoji);
            string sTituloInfo = InfoHeader(objData.Kind);
            bool bComInfo = sTituloInfo.Length > 0 && objData.Rows.Any(row => row.Info.Length > 0);

            return Document.Create(objDocument => objDocument.Page(objPage =>
            {
                objPage.Size(PageSizes.A4);
                objPage.MarginHorizontal(36);
                objPage.MarginVertical(32);
                objPage.PageColor(Colors.White);
                objPage.DefaultTextStyle(style => style.FontSize(10).FontColor(objCores.Ink));

                // ---------------------------------------------------------- Cabeçalho (toda página)
                Header(objPage, objLogo, objCores, objData.CompanyName, "PDF de campanha",
                    $"{objData.CompanyName} · {objData.UnitName}");

                // ---------------------------------------------------------------- Conteúdo
                objPage.Content().Column(objColuna =>
                {
                    objColuna.Spacing(14);

                    objColuna.Item().Column(objTitulo =>
                    {
                        objTitulo.Item().Text(objData.CampaignName).FontSize(18).Bold().FontColor(objCores.Ink);
                        if (!string.Equals(objData.CampaignName, CampaignKind.Label(objData.Kind), StringComparison.Ordinal))
                        {
                            objTitulo.Item().Text($"Campanha de {CampaignKind.Label(objData.Kind).ToLowerInvariant()}").FontColor(objCores.Muted);
                        }
                    });

                    objColuna.Item().Row(objRow =>
                    {
                        Field(objRow.RelativeItem(), "Unidade", objData.UnitName, objCores);
                        Field(objRow.RelativeItem(), "Data", LongDate(objData.LocalDate), objCores);
                        Field(objRow.RelativeItem(), "Clientes", objData.Rows.Count.ToString(), objCores);
                    });

                    if (objData.Kind == CampaignKind.Birthday)
                    {
                        (DateOnly dtInicio, DateOnly dtFim) = CampaignCalendar.BirthdayRange(objData.LocalDate);
                        objColuna.Item().Text(objTexto =>
                        {
                            objTexto.Span("Aniversariantes de ").FontColor(objCores.Muted);
                            objTexto.Span($"{ShortDate(dtInicio)} a {ShortDate(dtFim)}").SemiBold();
                            objTexto.Span(". Quem faz aniversário hoje está marcado como ").FontColor(objCores.Muted);
                            objTexto.Span("Hoje").Bold().FontColor(objCores.BrandDark);
                            objTexto.Span(".").FontColor(objCores.Muted);
                        });
                    }

                    // A mensagem, como a empresa escreveu na tela de campanha.
                    objColuna.Item().Background(objCores.Surface).Border(1).BorderColor(objCores.Line).CornerRadius(8)
                        .Padding(14).Column(objCaixa =>
                        {
                            objCaixa.Spacing(6);
                            objCaixa.Item().Text("Mensagem para enviar").FontSize(12).Bold().FontColor(objCores.BrandDark);
                            objCaixa.Item().Text(sMensagem).FontSize(12).FontColor(objCores.Ink);
                            if (sMensagem.Contains('{'))
                            {
                                objCaixa.Item().Text(
                                    "O que está entre chaves muda para cada cliente: no link do WhatsApp já vai preenchido.")
                                    .FontSize(9).FontColor(objCores.Muted);
                            }
                            if (bTinhaEmoji)
                            {
                                objCaixa.Item().Text(
                                    "Os emojis da mensagem não aparecem neste PDF, mas vão junto no link do WhatsApp.")
                                    .FontSize(9).FontColor(objCores.Muted);
                            }
                        });

                    objColuna.Item().Column(objPassos =>
                    {
                        objPassos.Spacing(2);
                        objPassos.Item().Text("Como fazer").Bold().FontColor(objCores.BrandDark);
                        objPassos.Item().Text("1. Clique no WhatsApp do cliente: a conversa abre com a mensagem pronta, já com o nome dele.");
                        objPassos.Item().Text("2. Confira o texto e envie.");
                        objPassos.Item().Text("3. Se o cliente informou o Instagram, o @ também é um link para o perfil dele.");
                    });

                    objColuna.Item().Table(objTabela =>
                    {
                        objTabela.ColumnsDefinition(objColunas =>
                        {
                            objColunas.ConstantColumn(24);
                            objColunas.RelativeColumn(2.7f);
                            objColunas.RelativeColumn(2.1f);
                            objColunas.RelativeColumn(2f);
                            if (bComInfo)
                            {
                                // Cabe "Hoje · seg, 12/10 · 28 anos" numa linha.
                                objColunas.RelativeColumn(2.8f);
                            }
                        });

                        objTabela.Header(objCabecalho =>
                        {
                            ColumnTitle(objCabecalho.Cell(), "#", objCores);
                            ColumnTitle(objCabecalho.Cell(), "Cliente", objCores);
                            ColumnTitle(objCabecalho.Cell(), "WhatsApp", objCores);
                            ColumnTitle(objCabecalho.Cell(), "Instagram", objCores);
                            if (bComInfo)
                            {
                                ColumnTitle(objCabecalho.Cell(), sTituloInfo, objCores);
                            }
                        });

                        int iLinha = 0;
                        foreach (CampaignPdfRow objCliente in objData.Rows)
                        {
                            iLinha++;
                            Cell(objTabela.Cell(), objCores).Text(iLinha.ToString()).FontColor(objCores.Muted);
                            Cell(objTabela.Cell(), objCores).Text(objCliente.Name).SemiBold();
                            Cell(objTabela.Cell(), objCores)
                                .Hyperlink(CampaignContact.WhatsAppUrl(objCliente.Phone, objCliente.Message))
                                .Text(CampaignContact.FormatPhone(objCliente.Phone)).FontColor(objCores.Link).Underline();

                            IContainer objInstagram = Cell(objTabela.Cell(), objCores);
                            string sPerfil = InstagramHandle.ProfileUrl(objCliente.Instagram);
                            if (sPerfil.Length > 0)
                            {
                                objInstagram.Hyperlink(sPerfil)
                                    .Text("@" + InstagramHandle.Normalize(objCliente.Instagram)).FontColor(objCores.Link).Underline();
                            }
                            else if (objCliente.Instagram.Length > 0)
                            {
                                // Fora do formato do Instagram (dado antigo): mostra como veio, sem link.
                                objInstagram.Text(objCliente.Instagram);
                            }
                            else
                            {
                                objInstagram.Text("—").FontColor(objCores.Muted);
                            }

                            if (bComInfo)
                            {
                                Cell(objTabela.Cell(), objCores).Text(objTexto =>
                                {
                                    if (objCliente.IsToday)
                                    {
                                        objTexto.Span("Hoje · ").Bold().FontColor(objCores.BrandDark);
                                    }
                                    objTexto.Span(objCliente.Info);
                                });
                            }
                        }
                    });
                });

                // ------------------------------------------------------------------ Rodapé
                Footer(objPage, objCores);
            })).GeneratePdf();
        }
    }
}
