using System.Text.RegularExpressions;
using Models.DataBase;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Models.Pdf
{
    /// <summary>
    /// O padrão dos PDFs com a marca da empresa (campanha, cadastros): a fonte, as cores do tema do portal,
    /// a logo, o cabeçalho, o quadro de dados, a tabela e o rodapé. Um PDF novo monta com estas peças para
    /// sair igual aos outros.
    /// </summary>
    internal static partial class PdfTheme
    {
        [GeneratedRegex("^#[0-9a-fA-F]{6}$")]
        private static partial Regex HexColorRegex();

        [GeneratedRegex(@"^data:image/(?<type>[a-z0-9.+-]+);base64,(?<data>.+)$", RegexOptions.Singleline)]
        private static partial Regex DataUrlRegex();

        static PdfTheme()
        {
            // Licença gratuita (empresa com faturamento anual abaixo de US$ 1 milhão).
            QuestPDF.Settings.License = LicenseType.Community;
            // Só a fonte que vai junto com o programa (Lato): o servidor não tem fontes instaladas, e o
            // PDF sai igual aqui e lá. O que a fonte não tem (emojis) é tirado antes (CampaignContact).
            QuestPDF.Settings.UseSystemFonts = false;
            QuestPDF.Settings.ThrowOnMissingTextGlyphs = false;
        }

        /// <summary>Aplica as configurações do QuestPDF (licença e fonte). Chamar antes de montar um PDF.</summary>
        public static void Configure()
        {
            // O construtor estático faz o trabalho; chamar este método garante que ele rodou.
        }

        /// <summary>
        /// Cabeçalho de toda página: a logo (ou o nome da empresa) à esquerda; o tipo do documento e o contexto
        /// à direita; uma linha na cor da marca embaixo.
        /// </summary>
        public static void Header(
            PageDescriptor objPage, Logo objLogo, Palette objColors, string sCompanyName, string sTitle, string sSubtitle)
        {
            objPage.Header().PaddingBottom(14).BorderBottom(2).BorderColor(objColors.Brand).PaddingBottom(10).Row(objRow =>
            {
                objLogo.Draw(objRow.RelativeItem().AlignLeft().AlignMiddle().Height(44), sCompanyName, objColors);

                objRow.RelativeItem().AlignRight().AlignMiddle().Column(objColumn =>
                {
                    objColumn.Item().AlignRight().Text(sTitle).FontSize(16).Bold().FontColor(objColors.BrandDark);
                    objColumn.Item().AlignRight().Text(sSubtitle).FontColor(objColors.Muted);
                });
            });
        }

        /// <summary>Rodapé de toda página: "Gerado pelo AccessWifi · página X de Y".</summary>
        public static void Footer(PageDescriptor objPage, Palette objColors)
        {
            objPage.Footer().PaddingTop(10).AlignCenter().Text(objText =>
            {
                objText.DefaultTextStyle(style => style.FontSize(8).FontColor(objColors.Muted));
                objText.Span("Gerado pelo AccessWifi · página ");
                objText.CurrentPageNumber();
                objText.Span(" de ");
                objText.TotalPages();
            });
        }

        /// <summary>Um dado do quadro de resumo: rótulo pequeno em maiúsculas e o valor embaixo.</summary>
        public static void Field(IContainer objContainer, string sLabel, string sValue, Palette objColors)
        {
            objContainer.Column(objColumn =>
            {
                objColumn.Item().Text(sLabel.ToUpperInvariant()).FontSize(8).SemiBold().FontColor(objColors.Muted);
                objColumn.Item().Text(sValue).FontSize(11).SemiBold();
            });
        }

        /// <summary>Título de coluna da tabela (fundo do tema, texto na cor de destaque).</summary>
        public static void ColumnTitle(IContainer objContainer, string sText, Palette objColors)
        {
            objContainer.Background(objColors.Surface).BorderBottom(1).BorderColor(objColors.Line)
                .PaddingVertical(6).PaddingHorizontal(6)
                .Text(sText).FontSize(9).Bold().FontColor(objColors.BrandDark);
        }

        /// <summary>
        /// Célula da tabela: linha fina embaixo, na cor das bordas do tema. Listas longas (cadastros) usam um
        /// espaçamento menor, para caber mais linhas por página.
        /// </summary>
        public static IContainer Cell(IContainer objContainer, Palette objColors, float fVerticalPadding = 7) =>
            objContainer.BorderBottom(1).BorderColor(objColors.Line).PaddingVertical(fVerticalPadding).PaddingHorizontal(6);

        /// <summary>
        /// A logo do tema (data URL): PNG/JPEG/WebP viram imagem, SVG é desenhado. Se não abrir (base64
        /// quebrado, arquivo que não é imagem de verdade), sai o nome da empresa no lugar — uma logo ruim não
        /// pode impedir um PDF de sair.
        /// </summary>
        public sealed class Logo
        {
            private readonly Image? _objImage;
            private readonly SvgImage? _objSvg;

            private Logo(Image? objImage, SvgImage? objSvg)
            {
                _objImage = objImage;
                _objSvg = objSvg;
            }

            public static Logo Read(string? sDataUrl)
            {
                Match objMatch = DataUrlRegex().Match(sDataUrl ?? "");
                if (!objMatch.Success)
                {
                    return new Logo(null, null);
                }
                try
                {
                    byte[] arrData = Convert.FromBase64String(objMatch.Groups["data"].Value);
                    return objMatch.Groups["type"].Value.StartsWith("svg", StringComparison.Ordinal)
                        ? new Logo(null, SvgImage.FromText(System.Text.Encoding.UTF8.GetString(arrData)))
                        : new Logo(Image.FromBinaryData(arrData), null);
                }
                catch (Exception)
                {
                    // Logo que não abre: o PDF sai com o nome da empresa no lugar dela.
                    return new Logo(null, null);
                }
            }

            public void Draw(IContainer objContainer, string sCompanyName, Palette objColors)
            {
                if (_objImage is not null)
                {
                    objContainer.Image(_objImage).FitHeight();
                }
                else if (_objSvg is not null)
                {
                    objContainer.Svg(_objSvg).FitHeight();
                }
                else
                {
                    objContainer.AlignMiddle().Text(sCompanyName).FontSize(16).Bold().FontColor(objColors.BrandDark);
                }
            }
        }

        /// <summary>
        /// As cores do tema da empresa (as mesmas do portal), com o padrão no que vier inválido. BrandDark é
        /// a cor dos títulos e links: escurecida até dar para ler no papel branco (um amarelo de marca, por
        /// exemplo, some como texto).
        /// </summary>
        public sealed record Palette(Color Brand, Color BrandDark, Color Surface, Color Ink, Color Muted, Color Line, Color Link)
        {
            public static Palette From(ThemeColors? objTheme)
            {
                ThemeColors objDefault = new ThemeColors();
                objTheme ??= objDefault;
                Color objTextColor = Color.FromHex(ToReadable(Hex(objTheme.BrandDark, objDefault.BrandDark)));
                return new Palette(
                    Color.FromHex(Hex(objTheme.Brand, objDefault.Brand)),
                    objTextColor,
                    Color.FromHex(Hex(objTheme.Surface, objDefault.Surface)),
                    Color.FromHex(ToReadable(Hex(objTheme.Ink, objDefault.Ink))),
                    Color.FromHex(ToReadable(Hex(objTheme.Muted, objDefault.Muted), 4.5)),
                    Color.FromHex(Hex(objTheme.Line, objDefault.Line)),
                    objTextColor);
            }

            private static string Hex(string? sHex, string sDefault) =>
                sHex is not null && HexColorRegex().IsMatch(sHex) ? sHex : sDefault;

            /// <summary>Escurece a cor até o contraste com o branco chegar ao mínimo de leitura (WCAG).</summary>
            private static string ToReadable(string sHex, double dMinimum = 4.5)
            {
                double dR = Convert.ToInt32(sHex[1..3], 16), dG = Convert.ToInt32(sHex[3..5], 16), dB = Convert.ToInt32(sHex[5..7], 16);
                for (int iStep = 0; iStep < 40 && ContrastWithWhite(dR, dG, dB) < dMinimum; iStep++)
                {
                    dR *= 0.92;
                    dG *= 0.92;
                    dB *= 0.92;
                }
                return $"#{(int)dR:X2}{(int)dG:X2}{(int)dB:X2}";
            }

            private static double ContrastWithWhite(double dR, double dG, double dB)
            {
                static double Channel(double dValue)
                {
                    double dC = dValue / 255;
                    return dC <= 0.03928 ? dC / 12.92 : Math.Pow((dC + 0.055) / 1.055, 2.4);
                }
                double dLuminance = 0.2126 * Channel(dR) + 0.7152 * Channel(dG) + 0.0722 * Channel(dB);
                return 1.05 / (dLuminance + 0.05);
            }
        }
    }
}
