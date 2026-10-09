using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace AccessWifi.Api.Features.Settings;

/// <summary>
/// Imagens do tema (logo, favicon, banner) servidas como arquivo, fora da resposta do tema. Ficam
/// gravadas como data URL; o portal recebe só o endereço <c>/settings/image/{unidade}/{tipo}?v=</c>, e o
/// <c>v</c> muda quando a imagem muda — por isso o celular pode guardar o arquivo por um ano.
/// </summary>
public static partial class PortalImage
{
    public const string Logo = "logo";
    public const string Favicon = "favicon";
    public const string Banner = "banner";

    [GeneratedRegex("^image/[a-z0-9.+-]+$")]
    private static partial Regex ContentTypeRegex();

    /// <summary>Endereço da imagem no portal; null quando a empresa não tem essa imagem.</summary>
    public static string? Url(string sUnitSlug, string sKind, string? sDataUrl)
    {
        if (string.IsNullOrEmpty(sDataUrl))
        {
            return null;
        }
        return $"/settings/image/{Uri.EscapeDataString(sUnitSlug)}/{sKind}?v={Version(sDataUrl)}";
    }

    /// <summary>Versão da imagem: muda sempre que o conteúdo muda.</summary>
    public static string Version(string sDataUrl)
    {
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(sDataUrl)))[..16];
    }

    /// <summary>Abre o data URL (<c>data:image/png;base64,...</c>) em bytes e tipo. Falso se não for imagem em base64.</summary>
    public static bool TryDecode(string? sDataUrl, out byte[] arrBytes, out string sContentType)
    {
        arrBytes = [];
        sContentType = string.Empty;
        if (string.IsNullOrEmpty(sDataUrl) || !sDataUrl.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        int iComma = sDataUrl.IndexOf(',');
        if (iComma < 0)
        {
            return false;
        }

        string sHeader = sDataUrl[5..iComma];
        const string sBase64Suffix = ";base64";
        if (!sHeader.EndsWith(sBase64Suffix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string sType = sHeader[..^sBase64Suffix.Length].Split(';')[0].Trim().ToLowerInvariant();
        if (!ContentTypeRegex().IsMatch(sType))
        {
            return false;
        }

        try
        {
            arrBytes = Convert.FromBase64String(sDataUrl[(iComma + 1)..]);
        }
        catch (FormatException)
        {
            return false;
        }

        sContentType = sType;
        return arrBytes.Length > 0;
    }
}
