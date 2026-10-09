namespace AccessWifi.Api.Features;

/// <summary>
/// Regra única da URL de redirecionamento (para onde o visitante vai depois de liberado): vale para
/// a "Geral" da empresa (Configurações) e para a própria de cada unidade (formulário da unidade).
/// </summary>
public static class RedirectUrlRules
{
    public const int MaxChars = 2048;

    /// <summary>
    /// Vazia é aceita (usa o padrão); senão, um endereço http/https completo dentro do limite.
    /// Devolve a mensagem de erro, ou null se a URL for aceitável.
    /// </summary>
    public static string? Validate(string? sUrl)
    {
        if (string.IsNullOrWhiteSpace(sUrl))
        {
            return null;
        }

        string sRedirectUrl = sUrl.Trim();
        if (sRedirectUrl.Length > MaxChars)
        {
            return $"URL de redirecionamento muito longa (máximo de {MaxChars} caracteres).";
        }

        bool bValidUrl =
            Uri.TryCreate(sRedirectUrl, UriKind.Absolute, out Uri? objUri) &&
            (objUri.Scheme == Uri.UriSchemeHttp || objUri.Scheme == Uri.UriSchemeHttps);
        if (!bValidUrl)
        {
            return "URL de redirecionamento inválida (informe um endereço http ou https completo).";
        }

        return null;
    }
}
