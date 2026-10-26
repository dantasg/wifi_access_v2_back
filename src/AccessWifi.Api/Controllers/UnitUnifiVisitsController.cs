using System.Security.Cryptography;
using AccessWifi.Api.Features;
using AccessWifi.Api.Features.Units;
using AccessWifi.Api.Infrastructure.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Models.DataBase;
using Models.Persistence;

namespace AccessWifi.Api.Controllers;

/// <summary>
/// Visitas pela UniFi de uma unidade (PROPOSTA_VISITAS_UNIFI.md): o endereço que recebe os avisos do Alarm Manager,
/// como está chegando, os aparelhos vistos na rede de clientes e os aparelhos da loja, que não contam (D8). Só o
/// super admin, como o resto da configuração da UniFi.
/// </summary>
[ApiController]
[Route("admin/units/{id:guid}/unifi-visits")]
[Authorize(Roles = ClaimsExtensions.RoleSuperAdmin)]
public class UnitUnifiVisitsController : ControllerBase
{
    private const int MaxDeviceDays = 30;

    private readonly AppDbContext _objDbContext;

    public UnitUnifiVisitsController(AppDbContext objDbContext)
    {
        _objDbContext = objDbContext;
    }

    [HttpGet]
    public async Task<ActionResult<UnifiVisitsStatusDto>> GetStatus(Guid id, CancellationToken objCancellationToken)
    {
        Unit? objUnit = await _objDbContext.Units.AsNoTracking().FirstOrDefaultAsync(unit => unit.Id == id, objCancellationToken);
        if (objUnit is null)
        {
            return NotFound(new ErrorResponse("Unidade não encontrada."));
        }

        (DateOnly dtToday, string sGuestNetwork) = await TodayAndNetworkAsync(objUnit, objCancellationToken);
        List<WifiPresence> objToday = await _objDbContext.WifiPresences.AsNoTracking()
            .Where(presence => presence.IDUnit == id && presence.LocalDate == dtToday)
            .ToListAsync(objCancellationToken);
        var objVisits = await _objDbContext.Visits.AsNoTracking()
            .Where(visit => visit.IDUnit == id && visit.LocalDate == dtToday)
            .GroupBy(visit => visit.Source)
            .Select(group => new { Source = group.Key, Count = group.Count() })
            .ToListAsync(objCancellationToken);

        return Ok(new UnifiVisitsStatusDto(
            objUnit.UnifiWebhookHash.Length > 0,
            objUnit.UnifiWebhookCreatedAt,
            objUnit.UnifiWebhookLastAt,
            sGuestNetwork,
            objToday.Count,
            objToday.Sum(presence => presence.Connects),
            objToday.Sum(presence => presence.Disconnects),
            objVisits.Sum(item => item.Count),
            objVisits.Where(item => item.Source == VisitSource.Unifi).Sum(item => item.Count)));
    }

    /// <summary>
    /// Gera um endereço novo para a unidade (o anterior deixa de funcionar). O código só aparece nesta resposta; no
    /// banco fica só a impressão (D1).
    /// </summary>
    [HttpPost("address")]
    public async Task<ActionResult<UnifiAddressDto>> CreateAddress(Guid id, CancellationToken objCancellationToken)
    {
        Unit? objUnit = await _objDbContext.Units.FirstOrDefaultAsync(unit => unit.Id == id, objCancellationToken);
        if (objUnit is null)
        {
            return NotFound(new ErrorResponse("Unidade não encontrada."));
        }

        string sToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        objUnit.UnifiWebhookHash = UnifiWebhookController.HashToken(sToken);
        objUnit.UnifiWebhookCreatedAt = DateTime.UtcNow;
        await _objDbContext.SaveChangesAsync(objCancellationToken);
        return Ok(new UnifiAddressDto($"{Request.Scheme}://{Request.Host}/webhooks/unifi/{sToken}"));
    }

    /// <summary>Apaga o endereço: os avisos que chegarem por ele passam a ser recusados.</summary>
    [HttpDelete("address")]
    public async Task<IActionResult> DeleteAddress(Guid id, CancellationToken objCancellationToken)
    {
        Unit? objUnit = await _objDbContext.Units.FirstOrDefaultAsync(unit => unit.Id == id, objCancellationToken);
        if (objUnit is null)
        {
            return NotFound(new ErrorResponse("Unidade não encontrada."));
        }
        objUnit.UnifiWebhookHash = "";
        objUnit.UnifiWebhookCreatedAt = null;
        await _objDbContext.SaveChangesAsync(objCancellationToken);
        return NoContent();
    }

    /// <summary>Aparelhos vistos na rede de clientes nos últimos dias (padrão 7) e os aparelhos da loja.</summary>
    [HttpGet("devices")]
    public async Task<ActionResult<UnifiDevicesDto>> GetDevices(
        Guid id, [FromQuery(Name = "days")] int iDays, CancellationToken objCancellationToken)
    {
        Unit? objUnit = await _objDbContext.Units.AsNoTracking().FirstOrDefaultAsync(unit => unit.Id == id, objCancellationToken);
        if (objUnit is null)
        {
            return NotFound(new ErrorResponse("Unidade não encontrada."));
        }

        iDays = iDays <= 0 ? 7 : Math.Min(iDays, MaxDeviceDays);
        (DateOnly dtToday, _) = await TodayAndNetworkAsync(objUnit, objCancellationToken);
        DateOnly dtFrom = dtToday.AddDays(-(iDays - 1));

        List<UnitIgnoredDevice> objIgnored = await _objDbContext.UnitIgnoredDevices.AsNoTracking()
            .Where(ignored => ignored.IDUnit == id)
            .OrderBy(ignored => ignored.Label)
            .ToListAsync(objCancellationToken);
        HashSet<string> objIgnoredMacs = objIgnored.Select(ignored => ignored.Mac).ToHashSet();

        List<WifiPresence> objPresences = await _objDbContext.WifiPresences.AsNoTracking()
            .Where(presence => presence.IDUnit == id && presence.LocalDate >= dtFrom)
            .ToListAsync(objCancellationToken);
        List<UnifiDeviceDto> objDevices = objPresences
            .Where(presence => !objIgnoredMacs.Contains(presence.Mac))
            .GroupBy(presence => presence.Mac)
            .Select(group =>
            {
                List<WifiPresence> objMeasured = group.Where(presence => presence.Disconnects > 0).ToList();
                return new UnifiDeviceDto(
                    group.Key,
                    group.OrderByDescending(presence => presence.LastSeenAt)
                        .Select(presence => presence.Hostname)
                        .FirstOrDefault(sName => sName.Length > 0) ?? "",
                    group.Count(),
                    group.Max(presence => presence.LastSeenAt),
                    objMeasured.Count == 0 ? null : Math.Round(objMeasured.Average(presence => presence.StaySeconds) / 60.0, 1),
                    group.Any(presence => presence.IDCustomer is not null));
            })
            .OrderByDescending(device => device.Days)
            .ThenByDescending(device => device.LastSeenAt)
            .ToList();

        return Ok(new UnifiDevicesDto(
            objDevices,
            objIgnored.Select(ignored => new UnitIgnoredDeviceDto(ignored.Mac, ignored.Label, ignored.Username, ignored.CreatedAt)).ToList()));
    }

    /// <summary>Marca um aparelho como da loja: os avisos dele deixam de virar visita e tempo na loja (D8).</summary>
    [HttpPost("ignored")]
    public async Task<ActionResult<UnitIgnoredDeviceDto>> Ignore(
        Guid id, IgnoreDeviceRequest objRequest, CancellationToken objCancellationToken)
    {
        if (!await _objDbContext.Units.AnyAsync(unit => unit.Id == id, objCancellationToken))
        {
            return NotFound(new ErrorResponse("Unidade não encontrada."));
        }

        string sMac = MacAddress.Normalize(objRequest.Mac);
        if (sMac.Length == 0)
        {
            return BadRequest(new ErrorResponse("Endereço do aparelho inválido."));
        }
        string sLabel = (objRequest.Label ?? "").Trim();
        if (sLabel.Length > 100)
        {
            return BadRequest(new ErrorResponse("O nome deve ter no máximo 100 caracteres."));
        }

        UnitIgnoredDevice? objIgnored = await _objDbContext.UnitIgnoredDevices.FirstOrDefaultAsync(
            ignored => ignored.IDUnit == id && ignored.Mac == sMac, objCancellationToken);
        if (objIgnored is null)
        {
            objIgnored = new UnitIgnoredDevice { IDUnit = id, Mac = sMac };
            _objDbContext.UnitIgnoredDevices.Add(objIgnored);
        }
        objIgnored.Label = sLabel;
        objIgnored.Username = User.GetUsername() ?? "";
        objIgnored.CreatedAt = DateTime.UtcNow;
        await _objDbContext.SaveChangesAsync(objCancellationToken);
        return Ok(new UnitIgnoredDeviceDto(objIgnored.Mac, objIgnored.Label, objIgnored.Username, objIgnored.CreatedAt));
    }

    /// <summary>O aparelho volta a contar (só daqui para a frente: o que foi ignorado não volta).</summary>
    [HttpDelete("ignored/{mac}")]
    public async Task<IActionResult> Unignore(Guid id, string mac, CancellationToken objCancellationToken)
    {
        string sMac = MacAddress.Normalize(mac);
        UnitIgnoredDevice? objIgnored = await _objDbContext.UnitIgnoredDevices.FirstOrDefaultAsync(
            ignored => ignored.IDUnit == id && ignored.Mac == sMac, objCancellationToken);
        if (objIgnored is null)
        {
            return NotFound(new ErrorResponse("Aparelho não está na lista da loja."));
        }
        _objDbContext.UnitIgnoredDevices.Remove(objIgnored);
        await _objDbContext.SaveChangesAsync(objCancellationToken);
        return NoContent();
    }

    private async Task<(DateOnly Today, string GuestNetwork)> TodayAndNetworkAsync(
        Unit objUnit, CancellationToken objCancellationToken)
    {
        string? sTimeZone = await _objDbContext.Companies.AsNoTracking()
            .Where(company => company.Id == objUnit.IDCompany)
            .Select(company => company.TimeZone)
            .FirstOrDefaultAsync(objCancellationToken);
        string sNetwork = await _objDbContext.PortalSettings.AsNoTracking()
            .Where(settings => settings.IDCompany == objUnit.IDCompany)
            .Select(settings => settings.Ssid)
            .FirstOrDefaultAsync(objCancellationToken) ?? "";
        return (CompanyTimeZone.Today(CompanyTimeZone.Resolve(sTimeZone), DateTime.UtcNow), sNetwork.Trim());
    }
}
