using System.Text.RegularExpressions;
using AccessWifi.Api.Features;
using AccessWifi.Api.Features.Companies;
using Models.Persistence;
using AccessWifi.Api.Infrastructure.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Models.Campaigns;
using Models.DataBase;

namespace AccessWifi.Api.Controllers;

/// <summary>Gestão de empresas — exclusivo do super admin.</summary>
[ApiController]
[Route("admin/companies")]
[Authorize(Roles = ClaimsExtensions.RoleSuperAdmin)]
public partial class CompaniesController : ControllerBase
{
    [GeneratedRegex("^[a-z0-9-]{2,40}$")]
    private static partial Regex SlugRegex();

    private readonly AppDbContext _objDbContext;

    public CompaniesController(AppDbContext objDbContext)
    {
        _objDbContext = objDbContext;
    }

    /// <summary>Lista todas as empresas (sem expor a senha da UniFi).</summary>
    [HttpGet]
    public async Task<ActionResult<List<CompanyDto>>> GetAll(CancellationToken objCancellationToken)
    {
        List<Company> objCompanies = await _objDbContext.Companies
            .AsNoTracking()
            .OrderBy(company => company.Name)
            .ToListAsync(objCancellationToken);
        Dictionary<Guid, List<string>> objKinds = (await _objDbContext.CompanyCampaignKinds.AsNoTracking()
                .Select(kind => new { kind.IDCompany, kind.Kind })
                .ToListAsync(objCancellationToken))
            .GroupBy(kind => kind.IDCompany)
            .ToDictionary(group => group.Key, group => group.Select(kind => kind.Kind).ToList());

        return Ok(objCompanies
            .Select(company => CompanyDto.FromEntity(company, objKinds.GetValueOrDefault(company.Id) ?? []))
            .ToList());
    }

    /// <summary>Cria uma empresa. O slug identifica o portal (?company=slug) e é imutável.</summary>
    [HttpPost]
    public async Task<ActionResult<CompanyDto>> Create(
        CreateCompanyRequest objRequest, CancellationToken objCancellationToken)
    {
        if (string.IsNullOrWhiteSpace(objRequest.Name) || objRequest.Name.Trim().Length > 120)
        {
            return BadRequest(new ErrorResponse("Nome é obrigatório (máximo de 120 caracteres)."));
        }

        if (objRequest.Slug is null || !SlugRegex().IsMatch(objRequest.Slug))
        {
            return BadRequest(new ErrorResponse(
                "Slug inválido: use só letras minúsculas, números e hífen (2 a 40 caracteres)."));
        }

        bool slugInUse = await _objDbContext.Companies
            .AnyAsync(company => company.Slug == objRequest.Slug, objCancellationToken);
        if (slugInUse)
        {
            return BadRequest(new ErrorResponse("Já existe uma empresa com esse slug."));
        }

        string? sReportError = ValidateReport(objRequest.ReportSendDay)
            ?? ValidateCampaignSettings(objRequest.TimeZone, objRequest.CampaignKinds);
        if (sReportError is not null)
        {
            return BadRequest(new ErrorResponse(sReportError));
        }

        Company objCompany = new Company
        {
            Name = objRequest.Name.Trim(),
            Slug = objRequest.Slug,
        };
        ApplyReport(objCompany, objRequest.ReportSendDay);
        if (objRequest.TimeZone is not null)
        {
            objCompany.TimeZone = objRequest.TimeZone.Trim();
        }

        _objDbContext.Companies.Add(objCompany);
        await ApplyCampaignKindsAsync(objCompany.Id, objRequest.CampaignKinds, objCancellationToken);
        await _objDbContext.SaveChangesAsync(objCancellationToken);

        return Ok(CompanyDto.FromEntity(objCompany, await KindsOfAsync(objCompany.Id, objCancellationToken)));
    }

    /// <summary>Atualiza nome, situação e configuração de relatório da empresa.</summary>
    [HttpPut("{id:guid}")]
    public async Task<ActionResult<CompanyDto>> Update(
        Guid id, UpdateCompanyRequest objRequest, CancellationToken objCancellationToken)
    {
        Company? objCompany = await _objDbContext.Companies
            .FirstOrDefaultAsync(company => company.Id == id, objCancellationToken);
        if (objCompany is null)
        {
            return NotFound(new ErrorResponse("Empresa não encontrada."));
        }

        if (string.IsNullOrWhiteSpace(objRequest.Name) || objRequest.Name.Trim().Length > 120)
        {
            return BadRequest(new ErrorResponse("Nome é obrigatório (máximo de 120 caracteres)."));
        }

        string? sReportError = ValidateReport(objRequest.ReportSendDay)
            ?? ValidateCampaignSettings(objRequest.TimeZone, objRequest.CampaignKinds);
        if (sReportError is not null)
        {
            return BadRequest(new ErrorResponse(sReportError));
        }

        objCompany.Name = objRequest.Name.Trim();
        objCompany.Active = objRequest.Active;
        ApplyReport(objCompany, objRequest.ReportSendDay);
        if (objRequest.TimeZone is not null)
        {
            objCompany.TimeZone = objRequest.TimeZone.Trim();
        }

        await ApplyCampaignKindsAsync(objCompany.Id, objRequest.CampaignKinds, objCancellationToken);
        await _objDbContext.SaveChangesAsync(objCancellationToken);

        // Fuso ou tipos liberados mudaram a agenda: recalcula o próximo disparo das campanhas.
        if (objRequest.TimeZone is not null || objRequest.CampaignKinds is not null)
        {
            await RefreshCampaignScheduleAsync(objCompany, objCancellationToken);
        }

        return Ok(CompanyDto.FromEntity(objCompany, await KindsOfAsync(objCompany.Id, objCancellationToken)));
    }

    private static string? ValidateCampaignSettings(string? sTimeZone, IReadOnlyList<string>? objKinds)
    {
        if (sTimeZone is not null && !CompanyTimeZone.IsValid(sTimeZone.Trim()))
        {
            return "Fuso horário inválido.";
        }
        if (objKinds is not null && objKinds.Any(sKind => !CampaignKind.IsValid(sKind)))
        {
            return "Tipo de campanha inválido.";
        }
        if (objKinds?.FirstOrDefault(sKind => !CampaignKind.IsAvailable(sKind)) is string sComingSoon)
        {
            // D23: a filtrada aparece como "em breve" e ainda não pode ser liberada.
            return $"A campanha \"{CampaignKind.Label(sComingSoon)}\" ainda não está disponível (em breve).";
        }
        return null;
    }

    /// <summary>Liga/desliga os tipos de campanha da empresa (D6). Nulo = manter como está.</summary>
    private async Task ApplyCampaignKindsAsync(
        Guid objCompanyId, IReadOnlyList<string>? objKinds, CancellationToken objCancellationToken)
    {
        if (objKinds is null)
        {
            return;
        }

        List<CompanyCampaignKind> objCurrent = await _objDbContext.CompanyCampaignKinds
            .Where(kind => kind.IDCompany == objCompanyId)
            .ToListAsync(objCancellationToken);
        HashSet<string> objWanted = objKinds.ToHashSet();

        _objDbContext.CompanyCampaignKinds.RemoveRange(
            objCurrent.Where(kind => !objWanted.Contains(kind.Kind)));
        foreach (string sKind in objWanted.Where(sKind => objCurrent.All(kind => kind.Kind != sKind)))
        {
            _objDbContext.CompanyCampaignKinds.Add(new CompanyCampaignKind
            {
                IDCompany = objCompanyId,
                Kind = sKind,
                EnabledBy = User.GetUsername() ?? "",
            });
        }
    }

    private async Task<List<string>> KindsOfAsync(Guid objCompanyId, CancellationToken objCancellationToken) =>
        await _objDbContext.CompanyCampaignKinds.AsNoTracking()
            .Where(kind => kind.IDCompany == objCompanyId)
            .Select(kind => kind.Kind)
            .ToListAsync(objCancellationToken);

    /// <summary>
    /// Tipo desligado: as campanhas dele ficam sem agenda (não disparam). Religado ou fuso trocado: o
    /// próximo disparo é recalculado a partir de agora.
    /// </summary>
    private async Task RefreshCampaignScheduleAsync(Company objCompany, CancellationToken objCancellationToken)
    {
        HashSet<string> objEnabled = (await KindsOfAsync(objCompany.Id, objCancellationToken)).ToHashSet();
        List<Campaign> objCampaigns = await _objDbContext.Campaigns
            .Where(campaign => campaign.IDCompany == objCompany.Id && campaign.Status == CampaignStatus.Active)
            .ToListAsync(objCancellationToken);
        TimeZoneInfo objZone = CompanyTimeZone.Resolve(objCompany.TimeZone);
        foreach (Campaign objCampaign in objCampaigns)
        {
            CampaignScheduling.RefreshNextRun(
                objCampaign, objZone, DateTime.UtcNow, objEnabled.Contains(objCampaign.Kind));
        }
        await _objDbContext.SaveChangesAsync(objCancellationToken);
    }

    /// <summary>Valida o dia de envio do relatório mensal (1 a 28). O e-mail fica em cada unidade (D24).</summary>
    private static string? ValidateReport(int? iReportSendDay)
    {
        if (iReportSendDay is not null && (iReportSendDay < 1 || iReportSendDay > 28))
        {
            return "Dia de envio do relatório deve ficar entre 1 e 28.";
        }

        return null;
    }

    private static void ApplyReport(Company objCompany, int? iReportSendDay)
    {
        // Dia nulo = mantém o atual (na criação, o padrão da entidade é 1).
        if (iReportSendDay is not null)
        {
            objCompany.ReportSendDay = iReportSendDay.Value;
        }
    }
}
