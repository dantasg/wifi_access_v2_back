using System.Text;
using Models.DataBase;

namespace Models.Campaigns
{
    /// <summary>
    /// Os links do PDF de campanha (D20): o WhatsApp abre a conversa com a mensagem já escrita, e o @ do
    /// Instagram abre o perfil. Os telefones são guardados só com dígitos, como o cliente digitou (DDD +
    /// número).
    /// </summary>
    public static class CampaignContact
    {
        private const string BrazilCode = "55";

        /// <summary>Número no formato do WhatsApp: código do Brasil + DDD + número, só dígitos.</summary>
        public static string WhatsAppNumber(string? sPhone)
        {
            string sDigits = CustomerDirectory.NormalizePhone(sPhone);
            // Já veio com o 55 na frente (12 ou 13 dígitos): mantém.
            bool bJaTemPais = sDigits.StartsWith(BrazilCode, StringComparison.Ordinal) && sDigits.Length >= 12;
            return bJaTemPais ? sDigits : BrazilCode + sDigits;
        }

        /// <summary>wa.me com o texto: no celular abre o WhatsApp, no computador o WhatsApp Web.</summary>
        public static string WhatsAppUrl(string? sPhone, string? sMessage)
        {
            string sUrl = "https://wa.me/" + WhatsAppNumber(sPhone);
            return string.IsNullOrWhiteSpace(sMessage) ? sUrl : sUrl + "?text=" + Uri.EscapeDataString(sMessage);
        }

        /// <summary>"93991234567" → "(93) 99123-4567"; o que não tiver 10 ou 11 dígitos fica como veio.</summary>
        public static string FormatPhone(string? sPhone)
        {
            string sDigits = CustomerDirectory.NormalizePhone(sPhone);
            if (sDigits.StartsWith(BrazilCode, StringComparison.Ordinal) && sDigits.Length is 12 or 13)
            {
                sDigits = sDigits[2..];
            }
            return sDigits.Length switch
            {
                11 => $"({sDigits[..2]}) {sDigits[2..7]}-{sDigits[7..]}",
                10 => $"({sDigits[..2]}) {sDigits[2..6]}-{sDigits[6..]}",
                _ => sDigits,
            };
        }

        /// <summary>
        /// Texto sem emojis, para o PDF: a fonte embutida não tem esses desenhos (sairiam como quadrados).
        /// A mensagem do link do WhatsApp continua com eles.
        /// </summary>
        public static string WithoutEmoji(string? sText, out bool bRemoved)
        {
            bRemoved = false;
            StringBuilder objResult = new StringBuilder();
            foreach (Rune objRune in (sText ?? "").EnumerateRunes())
            {
                if (IsEmoji(objRune.Value))
                {
                    bRemoved = true;
                    continue;
                }
                objResult.Append(objRune.ToString());
            }

            string sLimpo = objResult.ToString();
            // Emoji entre duas palavras deixa espaço dobrado.
            while (sLimpo.Contains("  ", StringComparison.Ordinal))
            {
                sLimpo = sLimpo.Replace("  ", " ", StringComparison.Ordinal);
            }
            return sLimpo.Replace(" \n", "\n", StringComparison.Ordinal).Trim();
        }

        private static bool IsEmoji(int iCodePoint) =>
            iCodePoint >= 0x1F000                              // rostos, objetos, bandeiras, tons de pele
            || iCodePoint is >= 0x2600 and <= 0x27BF           // símbolos e dingbats (☀ ✨ ❤ ✅)
            || iCodePoint is >= 0x2B00 and <= 0x2BFF           // ⭐ ⬆
            || iCodePoint is >= 0x2300 and <= 0x23FF           // ⌚ ⏰
            || iCodePoint is >= 0xFE00 and <= 0xFE0F           // seletores de variação
            || iCodePoint is 0x200D or 0x20E3;                 // junção de emojis, tecla (1️⃣)
    }
}
