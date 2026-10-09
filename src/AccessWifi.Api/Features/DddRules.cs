namespace AccessWifi.Api.Features;

/// <summary>
/// DDD do exemplo de telefone no portal ("(91) 90000-0000"). Vale para a empresa (Configurações) e,
/// se preenchido, para cada unidade (lojas da mesma empresa em estados diferentes). Só os DDDs que
/// existem no Brasil (lista da Anatel).
/// </summary>
public static class DddRules
{
    private static readonly HashSet<string> s_objValidos =
    [
        "11", "12", "13", "14", "15", "16", "17", "18", "19",
        "21", "22", "24", "27", "28",
        "31", "32", "33", "34", "35", "37", "38",
        "41", "42", "43", "44", "45", "46", "47", "48", "49",
        "51", "53", "54", "55",
        "61", "62", "63", "64", "65", "66", "67", "68", "69",
        "71", "73", "74", "75", "77", "79",
        "81", "82", "83", "84", "85", "86", "87", "88", "89",
        "91", "92", "93", "94", "95", "96", "97", "98", "99",
    ];

    /// <summary>Só os dígitos (aceita "(91)" ou " 91 "). Nulo continua nulo.</summary>
    public static string? Normalize(string? sDdd)
    {
        return sDdd is null ? null : new string(sDdd.Where(char.IsAsciiDigit).ToArray());
    }

    /// <summary>Vazio é aceito (sem DDD); senão, um DDD que existe. Devolve o erro, ou null.</summary>
    public static string? Validate(string? sDdd)
    {
        if (string.IsNullOrEmpty(sDdd) || s_objValidos.Contains(sDdd))
        {
            return null;
        }
        return "DDD inválido: use os 2 dígitos de um DDD do Brasil (ex.: 91).";
    }
}
