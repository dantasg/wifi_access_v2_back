using System.Text.RegularExpressions;

namespace Models.DataBase
{
    /// <summary>
    /// O "@" do Instagram no formato do próprio Instagram: de 1 a 30 caracteres, só letras sem acento,
    /// números, ponto e sublinhado, sem começar nem terminar com ponto e sem dois pontos seguidos. O portal
    /// não confere nada (para não atrasar o visitante): a regra fica só aqui.
    /// </summary>
    public static partial class InstagramHandle
    {
        public const int MaxChars = 30;

        public const string ProfileUrlPrefix = "https://www.instagram.com/";

        [GeneratedRegex(@"^(?:https?://)?(?:www\.|m\.)?(?:instagram\.com|instagr\.am)/([^/?#\s]+)", RegexOptions.IgnoreCase)]
        private static partial Regex ProfileLinkRegex();

        [GeneratedRegex("^[a-z0-9._]+$")]
        private static partial Regex HandleRegex();

        /// <summary>
        /// Tira os espaços das pontas, o "@" do começo (se houver) e o endereço do perfil
        /// (instagram.com/usuario vira usuario) e passa para minúsculas. Fora do formato vira vazio, como se
        /// a pessoa não tivesse digitado nada: um @ errado nunca impede a liberação do Wi-Fi.
        /// </summary>
        public static string Normalize(string? sValue)
        {
            string sHandle = (sValue ?? "").Trim();
            Match objLink = ProfileLinkRegex().Match(sHandle);
            if (objLink.Success)
            {
                sHandle = objLink.Groups[1].Value;
            }
            sHandle = sHandle.TrimStart('@').ToLowerInvariant();
            return IsValid(sHandle) ? sHandle : "";
        }

        /// <summary>
        /// O link do perfil (https://www.instagram.com/usuario) — é o que fica gravado, para quem olha os leads e
        /// os relatórios abrir o Instagram da pessoa com um clique. Fora do formato vira vazio.
        /// </summary>
        public static string ProfileUrl(string? sValue)
        {
            string sHandle = Normalize(sValue);
            return sHandle.Length == 0 ? "" : ProfileUrlPrefix + sHandle;
        }

        public static bool IsValid(string sHandle) =>
            sHandle.Length is >= 1 and <= MaxChars
            && HandleRegex().IsMatch(sHandle)
            && !sHandle.StartsWith('.')
            && !sHandle.EndsWith('.')
            && !sHandle.Contains("..");
    }
}
