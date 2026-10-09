using System.Globalization;
using AccessWifi.Api.Features;
using AccessWifi.Api.Features.Leads;
using AccessWifi.Api.Infrastructure.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Models.DataBase;
using Models.Persistence;
using Models.Reports;

namespace AccessWifi.Api.Controllers;

/// <summary>
/// PDF da tela de Leads, no mesmo padrão do PDF de campanha (logo e cores do portal da empresa). O painel
/// manda a lista que a pessoa está vendo (filtrada e ordenada), e o servidor só monta o documento.
/// </summary>
[ApiController]
[Route("admin/leads")]
[Authorize]
public class LeadsExportController : ControllerBase
{
    /// <summary>Teto de linhas num PDF (passa disso, melhor filtrar por período ou usar o CSV).</summary>
    public const int MaxRows = 20_000;
    private const int MaxFieldChars = 200;

    private readonly AppDbContext _objDbContext;

    public LeadsExportController(AppDbContext objDbContext)
    {
        _objDbContext = objDbContext;
    }

    [HttpPost("pdf")]
    [RequestSizeLimit(20 * 1024 * 1024)]
    public async Task<IActionResult> Pdf(
        LeadsPdfRequest objRequest,
        [FromQuery(Name = "company")] string? sCompanySlug,
        CancellationToken objCancellationToken)
    {
        AccessScope objScope = await AccessScope.LoadAsync(_objDbContext, User, objCancellationToken);
        if (objScope.IsSuperAdmin && string.IsNullOrWhiteSpace(sCompanySlug))
        {
            return BadRequest(new ErrorResponse("Informe a empresa (?company=slug)."));
        }
        Company? objCompany = objScope.IDCompany is Guid objTokenCompanyId
            ? await _objDbContext.Companies.AsNoTracking()
                .FirstOrDefaultAsync(company => company.Id == objTokenCompanyId, objCancellationToken)
            : await _objDbContext.Companies.AsNoTracking()
                .FirstOrDefaultAsync(company => company.Slug == sCompanySlug, objCancellationToken);
        if (objCompany is null)
        {
            return NotFound(new ErrorResponse("Empresa não encontrada."));
        }

        IReadOnlyList<LeadsPdfRow> objRows = objRequest.Rows ?? [];
        if (objRows.Count > MaxRows)
        {
            return BadRequest(new ErrorResponse(
                $"São {objRows.Count:N0} cadastros: o PDF vai até {MaxRows:N0}. Filtre por período ou use o CSV."));
        }

        // Unidades que o usuário vê na empresa (usuário de unidade: só as dele) — para o resumo do PDF.
        List<Unit> objUnits = await objScope.Apply(_objDbContext.Units.AsNoTracking()
                .Where(unit => unit.IDCompany == objCompany.Id))
            .OrderBy(unit => unit.Name)
            .ToListAsync(objCancellationToken);
        Unit? objSelectedUnit = string.IsNullOrWhiteSpace(objRequest.Unit)
            ? null
            : objUnits.FirstOrDefault(unit => unit.Slug == objRequest.Unit);
        string sScope = objSelectedUnit?.Name
            ?? (objScope.IsUnitRestricted && objUnits.Count is > 0 and <= 3
                ? string.Join(", ", objUnits.Select(unit => unit.Name))
                : "Todas as unidades");

        PortalSettings? objTheme = await _objDbContext.PortalSettings.AsNoTracking()
            .FirstOrDefaultAsync(settings => settings.IDCompany == objCompany.Id, objCancellationToken);
        DateTime dtNow = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, CompanyTimeZone.Resolve(objCompany.TimeZone));
        string sPeriod = Truncate(objRequest.Period);

        LeadsPdfData objData = new LeadsPdfData(
            objCompany.Name,
            sScope,
            sPeriod.Length > 0 ? sPeriod : LeadsPdf.AllPeriods,
            Truncate(objRequest.Search),
            dtNow.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture),
            objTheme?.Logo,
            objTheme?.Colors ?? new ThemeColors(),
            objRows.Select(row => new LeadsPdfRow(
                Truncate(row.Name), Truncate(row.Phone), Truncate(row.Instagram), Truncate(row.BirthDate),
                Truncate(row.Unit), Truncate(row.FirstSignup), Truncate(row.LastAccess))).ToList());

        string sFileName = $"cadastros-{objCompany.Slug}{(objSelectedUnit is null ? "" : "-" + objSelectedUnit.Slug)}.pdf";
        return File(LeadsPdf.Build(objData), "application/pdf", sFileName);
    }

    private static string Truncate(string? sValue)
    {
        string sTrimmed = (sValue ?? "").Trim();
        return sTrimmed.Length <= MaxFieldChars ? sTrimmed : sTrimmed[..MaxFieldChars];
    }
}
