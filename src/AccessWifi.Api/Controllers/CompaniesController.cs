using System.Text.RegularExpressions;
using AccessWifi.Api.Features;
using AccessWifi.Api.Features.Companies;
using Models.Persistence;
using AccessWifi.Api.Infrastructure.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
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

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$")]
    private static partial Regex EmailRegex();

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

        return Ok(objCompanies.Select(CompanyDto.FromEntity).ToList());
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

        bool slugEmUso = await _objDbContext.Companies
            .AnyAsync(company => company.Slug == objRequest.Slug, objCancellationToken);
        if (slugEmUso)
        {
            return BadRequest(new ErrorResponse("Já existe uma empresa com esse slug."));
        }

        string? sReportError = ValidateReport(objRequest.ReportEmail, objRequest.ReportSendDay);
        if (sReportError is not null)
        {
            return BadRequest(new ErrorResponse(sReportError));
        }

        Company objCompany = new Company
        {
            Name = objRequest.Name.Trim(),
            Slug = objRequest.Slug,
        };
        ApplyReport(objCompany, objRequest.ReportEmail, objRequest.ReportSendDay);

        _objDbContext.Companies.Add(objCompany);
        await _objDbContext.SaveChangesAsync(objCancellationToken);

        return Ok(CompanyDto.FromEntity(objCompany));
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

        string? sReportError = ValidateReport(objRequest.ReportEmail, objRequest.ReportSendDay);
        if (sReportError is not null)
        {
            return BadRequest(new ErrorResponse(sReportError));
        }

        objCompany.Name = objRequest.Name.Trim();
        objCompany.Active = objRequest.Active;
        ApplyReport(objCompany, objRequest.ReportEmail, objRequest.ReportSendDay);

        await _objDbContext.SaveChangesAsync(objCancellationToken);

        return Ok(CompanyDto.FromEntity(objCompany));
    }

    /// <summary>Valida e-mail do relatório (se informado) e o dia de envio (1 a 28).</summary>
    private static string? ValidateReport(string? sReportEmail, int? iReportSendDay)
    {
        if (!string.IsNullOrWhiteSpace(sReportEmail) && !EmailRegex().IsMatch(sReportEmail.Trim()))
        {
            return "E-mail do relatório inválido.";
        }

        if (iReportSendDay is not null && (iReportSendDay < 1 || iReportSendDay > 28))
        {
            return "Dia de envio do relatório deve ficar entre 1 e 28.";
        }

        return null;
    }

    private static void ApplyReport(Company objCompany, string? sReportEmail, int? iReportSendDay)
    {
        // Vazio limpa o e-mail (empresa deixa de receber relatório).
        objCompany.ReportEmail = string.IsNullOrWhiteSpace(sReportEmail) ? null : sReportEmail.Trim();
        // Dia nulo = mantém o atual (na criação, o padrão da entidade é 1).
        if (iReportSendDay is not null)
        {
            objCompany.ReportSendDay = iReportSendDay.Value;
        }
    }
}
