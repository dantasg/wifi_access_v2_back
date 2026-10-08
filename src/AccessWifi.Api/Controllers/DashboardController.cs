using System.Globalization;
using AccessWifi.Api.Features;
using AccessWifi.Api.Features.Dashboard;
using AccessWifi.Api.Infrastructure.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Models.DataBase;
using Models.Persistence;

namespace AccessWifi.Api.Controllers;

/// <summary>
/// Dashboard (PROPOSTA_DASHBOARD.md): números da empresa (todas as unidades que o usuário pode ver) ou de
/// uma unidade, num período. Mesmo acesso das outras telas: o super admin escolhe a empresa (?company=slug),
/// o admin da empresa vê a dele e o usuário de unidade só as unidades dele.
/// </summary>
[ApiController]
[Route("admin/dashboard")]
[Authorize]
public class DashboardController : ControllerBase
{
    /// <summary>Datas livres (D4) até 2 anos — o que a retenção guarda.</summary>
    private const int MaxDays = 731;
    private const int DefaultDays = 30;

    private readonly AppDbContext _objDbContext;

    public DashboardController(AppDbContext objDbContext)
    {
        _objDbContext = objDbContext;
    }

    /// <summary>
    /// <c>?unit=slug</c> = visão unidade; sem ela, visão empresa. <c>from</c>/<c>to</c> (aaaa-mm-dd, no fuso da
    /// empresa, inclusive); sem eles, os últimos 30 dias até hoje.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<DashboardDto>> Get(
        [FromQuery(Name = "company")] string? sCompanySlug,
        [FromQuery(Name = "unit")] string? sUnitSlug,
        [FromQuery(Name = "from")] string? sFrom,
        [FromQuery(Name = "to")] string? sTo,
        CancellationToken objCancellationToken)
    {
        Company? objCompany;
        Guid? objTokenCompanyId = User.GetCompanyId();
        if (objTokenCompanyId is null)
        {
            // Super admin: a empresa vem da query string.
            if (string.IsNullOrWhiteSpace(sCompanySlug))
            {
                return BadRequest(new ErrorResponse("Informe a empresa (?company=slug)."));
            }
            objCompany = await _objDbContext.Companies.AsNoTracking()
                .FirstOrDefaultAsync(company => company.Slug == sCompanySlug, objCancellationToken);
        }
        else
        {
            objCompany = await _objDbContext.Companies.AsNoTracking()
                .FirstOrDefaultAsync(company => company.Id == objTokenCompanyId, objCancellationToken);
        }
        if (objCompany is null)
        {
            return NotFound(new ErrorResponse("Empresa não encontrada."));
        }

        DateTime dtNowUtc = DateTime.UtcNow;
        DateOnly dtHoje = CompanyTimeZone.Today(CompanyTimeZone.Resolve(objCompany.TimeZone), dtNowUtc);
        (DateOnly dtFrom, DateOnly dtTo, string? sErro) = LerPeriodo(sFrom, sTo, dtHoje);
        if (sErro is not null)
        {
            return BadRequest(new ErrorResponse(sErro));
        }

        // Unidades que o usuário pode ver (usuário de unidade: só as dele).
        AccessScope objScope = await AccessScope.LoadAsync(_objDbContext, User, objCancellationToken);
        List<Unit> objUnits = await objScope.Apply(_objDbContext.Units.AsNoTracking()
                .Where(unit => unit.IDCompany == objCompany.Id))
            .OrderBy(unit => unit.Name)
            .ToListAsync(objCancellationToken);

        bool bVisaoUnidade = !string.IsNullOrWhiteSpace(sUnitSlug);
        if (bVisaoUnidade)
        {
            Unit? objUnit = objUnits.FirstOrDefault(unit => unit.Slug == sUnitSlug!.Trim());
            if (objUnit is null)
            {
                return NotFound(new ErrorResponse("Unidade não encontrada."));
            }
            objUnits = [objUnit];
        }

        DashboardDto objDashboard = await new DashboardBuilder(_objDbContext).BuildAsync(
            objCompany, objUnits, bVisaoUnidade, dtFrom, dtTo, dtNowUtc, objCancellationToken);
        return Ok(objDashboard);
    }

    private static (DateOnly From, DateOnly To, string? Erro) LerPeriodo(string? sFrom, string? sTo, DateOnly dtHoje)
    {
        DateOnly dtTo = dtHoje;
        if (!string.IsNullOrWhiteSpace(sTo) && !TryParse(sTo, out dtTo))
        {
            return (default, default, "Data final inválida (use aaaa-mm-dd).");
        }
        DateOnly dtFrom = dtTo.AddDays(-(DefaultDays - 1));
        if (!string.IsNullOrWhiteSpace(sFrom) && !TryParse(sFrom, out dtFrom))
        {
            return (default, default, "Data inicial inválida (use aaaa-mm-dd).");
        }
        if (dtFrom > dtTo)
        {
            return (default, default, "A data inicial é depois da final.");
        }
        if (dtTo.DayNumber - dtFrom.DayNumber + 1 > MaxDays)
        {
            return (default, default, "Escolha um período de até 2 anos.");
        }
        return (dtFrom, dtTo, null);
    }

    private static bool TryParse(string sValue, out DateOnly dtValue) =>
        DateOnly.TryParseExact(sValue.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out dtValue);
}
