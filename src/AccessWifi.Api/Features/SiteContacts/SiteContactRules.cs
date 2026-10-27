using System.Net.Mail;
using System.Text;
using Models.DataBase;

namespace AccessWifi.Api.Features.SiteContacts;

/// <summary>
/// Confere e limpa o que veio do formulário do site. O site já valida antes de mandar; aqui é a garantia (qualquer
/// um pode chamar a rota direto). Os campos de origem nunca recusam o envio: só são cortados no tamanho da coluna.
/// </summary>
public static class SiteContactRules
{
    public const int MaxMessage = 1000;

    /// <summary>Monta o contato ou devolve a mensagem do primeiro problema (para mostrar no formulário).</summary>
    public static string? TryBuild(SiteContactRequest objRequest, DateTime dtNowUtc, out SiteContact? objContact)
    {
        objContact = null;

        string sName = Clean(objRequest.Name);
        if (sName.Length < 2 || sName.Length > 100)
        {
            return "Informe o seu nome.";
        }

        string sCompany = Clean(objRequest.Company);
        if (sCompany.Length < 2 || sCompany.Length > 120)
        {
            return "Informe o nome da empresa.";
        }

        string sPhone = NormalizePhone(objRequest.Phone);
        if (sPhone.Length == 0)
        {
            return "Informe o WhatsApp com DDD.";
        }

        string sEmail = Clean(objRequest.Email).ToLowerInvariant();
        if (sEmail.Length > 0 && !IsEmail(sEmail))
        {
            return "Confira o e-mail.";
        }

        string sCity = Clean(objRequest.City);
        if (sCity.Length < 2 || sCity.Length > 80)
        {
            return "Informe a cidade.";
        }

        string sState = Clean(objRequest.State).ToUpperInvariant();
        if (!SiteContactChoices.States.Contains(sState))
        {
            return "Escolha a UF.";
        }

        string sStores = Clean(objRequest.Stores);
        if (!SiteContactChoices.Stores.ContainsKey(sStores))
        {
            return "Escolha quantas lojas.";
        }

        string sUsesUnifi = Clean(objRequest.UsesUnifi);
        if (!SiteContactChoices.UsesUnifi.ContainsKey(sUsesUnifi))
        {
            return "Responda se já usa UniFi.";
        }

        string sMessage = Clean(objRequest.Message, bKeepLines: true);
        if (sMessage.Length > MaxMessage)
        {
            return $"A mensagem deve ter no máximo {MaxMessage} caracteres.";
        }

        if (!objRequest.Consent)
        {
            return "Marque a autorização de contato.";
        }

        objContact = new SiteContact
        {
            Name = sName,
            Company = sCompany,
            Phone = sPhone,
            Email = sEmail,
            City = sCity,
            State = sState,
            Stores = sStores,
            UsesUnifi = sUsesUnifi,
            Message = sMessage,
            ConsentAt = dtNowUtc,
            UtmSource = Cut(objRequest.UtmSource, 200),
            UtmMedium = Cut(objRequest.UtmMedium, 200),
            UtmCampaign = Cut(objRequest.UtmCampaign, 200),
            UtmTerm = Cut(objRequest.UtmTerm, 200),
            UtmContent = Cut(objRequest.UtmContent, 200),
            Gclid = Cut(objRequest.Gclid, 200),
            Fbclid = Cut(objRequest.Fbclid, 200),
            LandingPage = Cut(objRequest.LandingPage, 500),
            Referrer = Cut(objRequest.Referrer, 500),
            Page = Cut(objRequest.Page, 500),
            CreatedAt = dtNowUtc,
        };
        return null;
    }

    /// <summary>Só dígitos, com DDD, sem o 55 do Brasil. "" = inválido.</summary>
    public static string NormalizePhone(string? sPhone)
    {
        string sDigits = new string((sPhone ?? "").Where(char.IsAsciiDigit).ToArray());
        if (sDigits.Length is 12 or 13 && sDigits.StartsWith("55"))
        {
            sDigits = sDigits[2..];
        }
        if (sDigits.StartsWith('0'))
        {
            sDigits = sDigits.TrimStart('0');
        }
        return sDigits.Length is 10 or 11 && sDigits[0] != '0' ? sDigits : "";
    }

    private static bool IsEmail(string sEmail)
    {
        if (sEmail.Length > 160 || sEmail.Contains(' '))
        {
            return false;
        }
        try
        {
            MailAddress objAddress = new MailAddress(sEmail);
            return objAddress.Address == sEmail && objAddress.Host.Contains('.');
        }
        catch (FormatException)
        {
            return false;
        }
    }

    /// <summary>Tira caracteres de controle e espaços repetidos (as quebras de linha ficam só na mensagem).</summary>
    private static string Clean(string? sValue, bool bKeepLines = false)
    {
        if (string.IsNullOrEmpty(sValue))
        {
            return "";
        }
        StringBuilder objBuilder = new StringBuilder(sValue.Length);
        foreach (char cChar in sValue)
        {
            if (bKeepLines && cChar == '\n')
            {
                objBuilder.Append('\n');
            }
            else if (char.IsControl(cChar) || char.IsWhiteSpace(cChar))
            {
                objBuilder.Append(' ');
            }
            else
            {
                objBuilder.Append(cChar);
            }
        }
        string sResult = objBuilder.ToString();
        if (!bKeepLines)
        {
            return string.Join(' ', sResult.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        }
        // Na mensagem: cada linha sem espaços sobrando e no máximo uma linha em branco seguida.
        IEnumerable<string> objLines = sResult.Split('\n')
            .Select(sLine => string.Join(' ', sLine.Split(' ', StringSplitOptions.RemoveEmptyEntries)));
        string sText = string.Join('\n', objLines);
        while (sText.Contains("\n\n\n"))
        {
            sText = sText.Replace("\n\n\n", "\n\n");
        }
        return sText.Trim();
    }

    private static string Cut(string? sValue, int iMax)
    {
        string sClean = Clean(sValue);
        return sClean.Length <= iMax ? sClean : sClean[..iMax];
    }
}
