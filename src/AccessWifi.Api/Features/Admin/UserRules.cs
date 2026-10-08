using System.Text;
using System.Text.RegularExpressions;

namespace AccessWifi.Api.Features.Admin;

/// <summary>
/// Regras de usuário e senha do painel — as mesmas no cadastro (UsersController) e no login
/// (AdminController). O front confere as mesmas regras antes de enviar.
/// </summary>
public static partial class UserRules
{
    public const int MinUsername = 3;
    public const int MaxUsername = 60;
    public const int MinPassword = 8;

    /// <summary>O BCrypt só usa os primeiros 72 bytes: o resto da senha seria ignorado sem aviso.</summary>
    public const int MaxPasswordBytes = 72;

    // Letras sem acento, números, ponto, hífen e sublinhado; começa e termina com letra ou número.
    // Nada de espaço (nem tab ou espaço invisível), acento ou @.
    [GeneratedRegex("^[a-z0-9](?:[a-z0-9._-]*[a-z0-9])?$")]
    private static partial Regex UsernameRegex();

    /// <summary>Como o usuário é guardado e procurado: sem espaços nas pontas e em minúsculas.</summary>
    public static string NormalizeUsername(string? sUsername) => (sUsername ?? "").Trim().ToLowerInvariant();

    /// <summary>Mensagem do erro, ou null quando o usuário (já normalizado) vale.</summary>
    public static string? ValidateUsername(string sUsername)
    {
        if (sUsername.Length < MinUsername || sUsername.Length > MaxUsername || !UsernameRegex().IsMatch(sUsername))
        {
            return $"Usuário inválido: de {MinUsername} a {MaxUsername} caracteres, só letras sem acento, números, "
                + "ponto, hífen ou sublinhado — sem espaços — começando e terminando com letra ou número.";
        }
        return null;
    }

    /// <summary>Mensagem do erro, ou null quando a senha vale.</summary>
    public static string? ValidatePassword(string? sPassword)
    {
        if (string.IsNullOrEmpty(sPassword) || sPassword.Length < MinPassword)
        {
            return $"Senha deve ter no mínimo {MinPassword} caracteres.";
        }
        if (string.IsNullOrWhiteSpace(sPassword))
        {
            return "A senha não pode ser só espaços.";
        }
        if (Encoding.UTF8.GetByteCount(sPassword) > MaxPasswordBytes)
        {
            return $"Senha muito longa: no máximo {MaxPasswordBytes} caracteres (menos, se tiver acentos).";
        }
        return null;
    }
}
