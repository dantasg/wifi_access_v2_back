using System.Globalization;
using System.Text.RegularExpressions;
using Models.DataBase;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

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
    public static partial class CampaignPdf
    {
        private static readonly string[] s_arrDias =
            ["domingo", "segunda-feira", "terça-feira", "quarta-feira", "quinta-feira", "sexta-feira", "sábado"];

        private static readonly string[] s_arrDiasCurtos = ["dom", "seg", "ter", "qua", "qui", "sex", "sáb"];

        [GeneratedRegex("^#[0-9a-fA-F]{6}$")]
        private static partial Regex HexColorRegex();

        [GeneratedRegex(@"^data:image/(?<tipo>[a-z0-9.+-]+);base64,(?<dados>.+)$", RegexOptions.Singleline)]
        private static partial Regex DataUrlRegex();

        static CampaignPdf()
        {
            // Licença gratuita (empresa com faturamento anual abaixo de US$ 1 milhão).
            QuestPDF.Settings.License = LicenseType.Community;
            // Só a fonte que vai junto com o programa (Lato): o servidor não tem fontes instaladas, e o
            // PDF sai igual aqui e lá. O que a fonte não tem (emojis) é tirado antes (CampaignContact).
            QuestPDF.Settings.UseSystemFonts = false;
            QuestPDF.Settings.ThrowOnMissingTextGlyphs = false;
        }

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
            Paleta objCores = Paleta.From(objData.Colors);
            byte[]? arrLogo = null;
            string? sLogoSvg = null;
            ReadLogo(objData.LogoDataUrl, ref arrLogo, ref sLogoSvg);
            string sMensagem = CampaignContact.WithoutEmoji(objData.MessageTemplate, out bool bTinhaEmoji);
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
                objPage.Header().PaddingBottom(14).BorderBottom(2).BorderColor(objCores.Brand).PaddingBottom(10).Row(objRow =>
                {
                    IContainer objLogo = objRow.RelativeItem().AlignLeft().AlignMiddle().Height(44);
                    if (arrLogo is not null)
                    {
                        objLogo.Image(arrLogo).FitHeight();
                    }
                    else if (sLogoSvg is not null)
                    {
                        objLogo.Svg(sLogoSvg).FitHeight();
                    }
                    else
                    {
                        objLogo.AlignMiddle().Text(objData.CompanyName).FontSize(16).Bold().FontColor(objCores.BrandDark);
                    }

                    objRow.RelativeItem().AlignRight().AlignMiddle().Column(objColuna =>
                    {
                        objColuna.Item().AlignRight().Text("PDF de campanha").FontSize(16).Bold().FontColor(objCores.BrandDark);
                        objColuna.Item().AlignRight().Text($"{objData.CompanyName} · {objData.UnitName}").FontColor(objCores.Muted);
                    });
                });

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
                        Dado(objRow.RelativeItem(), "Unidade", objData.UnitName, objCores);
                        Dado(objRow.RelativeItem(), "Data", LongDate(objData.LocalDate), objCores);
                        Dado(objRow.RelativeItem(), "Clientes", objData.Rows.Count.ToString(), objCores);
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
                                    "Os campos entre chaves, como {primeiro_nome}, viram os dados de cada cliente.")
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
                            Titulo(objCabecalho.Cell(), "#", objCores);
                            Titulo(objCabecalho.Cell(), "Cliente", objCores);
                            Titulo(objCabecalho.Cell(), "WhatsApp", objCores);
                            Titulo(objCabecalho.Cell(), "Instagram", objCores);
                            if (bComInfo)
                            {
                                Titulo(objCabecalho.Cell(), sTituloInfo, objCores);
                            }
                        });

                        int iLinha = 0;
                        foreach (CampaignPdfRow objCliente in objData.Rows)
                        {
                            iLinha++;
                            Celula(objTabela.Cell(), objCores).Text(iLinha.ToString()).FontColor(objCores.Muted);
                            Celula(objTabela.Cell(), objCores).Text(objCliente.Name).SemiBold();
                            Celula(objTabela.Cell(), objCores)
                                .Hyperlink(CampaignContact.WhatsAppUrl(objCliente.Phone, objCliente.Message))
                                .Text(CampaignContact.FormatPhone(objCliente.Phone)).FontColor(objCores.Link).Underline();

                            IContainer objInstagram = Celula(objTabela.Cell(), objCores);
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
                                Celula(objTabela.Cell(), objCores).Text(objTexto =>
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
                objPage.Footer().PaddingTop(10).AlignCenter().Text(objTexto =>
                {
                    objTexto.DefaultTextStyle(style => style.FontSize(8).FontColor(objCores.Muted));
                    objTexto.Span("Gerado pelo AccessWifi · página ");
                    objTexto.CurrentPageNumber();
                    objTexto.Span(" de ");
                    objTexto.TotalPages();
                });
            })).GeneratePdf();
        }

        private static void Dado(IContainer objContainer, string sRotulo, string sValor, Paleta objCores)
        {
            objContainer.Column(objColuna =>
            {
                objColuna.Item().Text(sRotulo.ToUpperInvariant()).FontSize(8).SemiBold().FontColor(objCores.Muted);
                objColuna.Item().Text(sValor).FontSize(11).SemiBold();
            });
        }

        private static void Titulo(IContainer objContainer, string sTexto, Paleta objCores)
        {
            objContainer.Background(objCores.Surface).BorderBottom(1).BorderColor(objCores.Line)
                .PaddingVertical(6).PaddingHorizontal(6)
                .Text(sTexto).FontSize(9).Bold().FontColor(objCores.BrandDark);
        }

        private static IContainer Celula(IContainer objContainer, Paleta objCores) =>
            objContainer.BorderBottom(1).BorderColor(objCores.Line).PaddingVertical(7).PaddingHorizontal(6);

        /// <summary>A logo do portal vem como data URL; PNG/JPEG/WebP viram imagem, SVG é desenhado. Inválida = sem logo.</summary>
        private static void ReadLogo(string? sDataUrl, ref byte[]? arrImagem, ref string? sSvg)
        {
            Match objMatch = DataUrlRegex().Match(sDataUrl ?? "");
            if (!objMatch.Success)
            {
                return;
            }
            try
            {
                byte[] arrDados = Convert.FromBase64String(objMatch.Groups["dados"].Value);
                if (objMatch.Groups["tipo"].Value.StartsWith("svg", StringComparison.Ordinal))
                {
                    sSvg = System.Text.Encoding.UTF8.GetString(arrDados);
                }
                else
                {
                    arrImagem = arrDados;
                }
            }
            catch (FormatException)
            {
                // Base64 quebrado: o PDF sai com o nome da empresa no lugar da logo.
            }
        }

        /// <summary>
        /// As cores do tema da empresa (as mesmas do portal), com o padrão da marca no que vier inválido.
        /// BrandDark é a cor dos títulos e links: escurecida até dar para ler no papel branco (um amarelo
        /// de marca, por exemplo, some como texto).
        /// </summary>
        private sealed record Paleta(Color Brand, Color BrandDark, Color Surface, Color Ink, Color Muted, Color Line, Color Link)
        {
            public static Paleta From(ThemeColors? objTema)
            {
                ThemeColors objPadrao = new ThemeColors();
                objTema ??= objPadrao;
                Color objTexto = Color.FromHex(ParaTexto(Hex(objTema.BrandDark, objPadrao.BrandDark)));
                return new Paleta(
                    Color.FromHex(Hex(objTema.Brand, objPadrao.Brand)),
                    objTexto,
                    Color.FromHex(Hex(objTema.Surface, objPadrao.Surface)),
                    Color.FromHex(ParaTexto(Hex(objTema.Ink, objPadrao.Ink))),
                    Color.FromHex(ParaTexto(Hex(objTema.Muted, objPadrao.Muted), 4.5)),
                    Color.FromHex(Hex(objTema.Line, objPadrao.Line)),
                    objTexto);
            }

            private static string Hex(string? sHex, string sPadrao) =>
                sHex is not null && HexColorRegex().IsMatch(sHex) ? sHex : sPadrao;

            /// <summary>Escurece a cor até o contraste com o branco chegar ao mínimo de leitura (WCAG).</summary>
            private static string ParaTexto(string sHex, double dMinimo = 4.5)
            {
                double dR = Convert.ToInt32(sHex[1..3], 16), dG = Convert.ToInt32(sHex[3..5], 16), dB = Convert.ToInt32(sHex[5..7], 16);
                for (int iPasso = 0; iPasso < 40 && Contraste(dR, dG, dB) < dMinimo; iPasso++)
                {
                    dR *= 0.92;
                    dG *= 0.92;
                    dB *= 0.92;
                }
                return $"#{(int)dR:X2}{(int)dG:X2}{(int)dB:X2}";
            }

            private static double Contraste(double dR, double dG, double dB)
            {
                static double Canal(double dValor)
                {
                    double dC = dValor / 255;
                    return dC <= 0.03928 ? dC / 12.92 : Math.Pow((dC + 0.055) / 1.055, 2.4);
                }
                double dLuminancia = 0.2126 * Canal(dR) + 0.7152 * Canal(dG) + 0.0722 * Canal(dB);
                return 1.05 / (dLuminancia + 0.05);
            }
        }
    }
}
