using Microsoft.EntityFrameworkCore;
using Models.DataBase;

namespace AccessWifi.Api.Features.Units;

/// <summary>
/// Acha a unidade do portal pelo slug (<c>?unit=</c>) ou pelo endereço em que ele foi aberto
/// (<c>PortalHost</c>).
///
/// O host existe porque o campo de portal externo da UniFi aceita só "IP ou FQDN" — sem caminho
/// e sem query string. Então em produção cada unidade tem o seu endereço
/// (ex.: "itaituba.wifi.exemplo.com.br") e é ele que diz de quem é o portal.
/// O slug continua tendo prioridade: nada do que já está configurado deixa de funcionar.
/// </summary>
public static class UnitResolver
{
    public static async Task<Unit?> FindAsync(
        IQueryable<Unit> objUnits, string? sUnitSlug, string? sPortalHost,
        CancellationToken objCancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(sUnitSlug))
        {
            string sSlug = sUnitSlug.Trim();
            return await objUnits.FirstOrDefaultAsync(
                unit => unit.Slug == sSlug, objCancellationToken);
        }

        string sHost = NormalizeHost(sPortalHost);
        if (sHost.Length == 0)
        {
            return null;
        }

        return await objUnits.FirstOrDefaultAsync(
            unit => unit.PortalHost == sHost, objCancellationToken);
    }

    /// <summary>
    /// Deixa o host comparável: minúsculas, sem espaços, sem porta e sem o ponto final do FQDN
    /// absoluto. O navegador pode mandar "Itaituba.Wifi.Exemplo.com.br:443" e continua sendo o
    /// mesmo endereço que o super admin cadastrou.
    /// </summary>
    public static string NormalizeHost(string? sHost)
    {
        string sValue = (sHost ?? "").Trim().ToLowerInvariant();

        int iPort = sValue.LastIndexOf(':');
        if (iPort > 0)
        {
            sValue = sValue[..iPort];
        }

        return sValue.TrimEnd('.');
    }
}
