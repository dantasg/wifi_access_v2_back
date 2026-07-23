using System.Text.RegularExpressions;
using AccessWifi.Api.Features;
using AccessWifi.Api.Features.Units;
using Models.Persistence;
using AccessWifi.Api.Infrastructure.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Models.DataBase;

namespace AccessWifi.Api.Controllers;

/// <summary>
/// Unidades (franquias). A listagem é aberta a qualquer admin (o admin de empresa vê só as
/// da sua empresa); criar/editar é exclusivo do super admin.
/// </summary>
[ApiController]
[Route("admin/units")]
[Authorize]
public partial class UnitsController : ControllerBase
{
    [GeneratedRegex("^[a-z0-9-]{2,40}$")]
    private static partial Regex SlugRegex();

    private readonly AppDbContext _objDbContext;

    public UnitsController(AppDbContext objDbContext)
    {
        _objDbContext = objDbContext;
    }

    /// <summary>
    /// Lista unidades (sem expor a senha da UniFi). O admin de empresa recebe só as unidades
    /// da própria empresa (o filtro ?company é ignorado). O super admin lista todas, com filtro
    /// opcional ?company={id da empresa}.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<List<UnitDto>>> GetAll(
        [FromQuery(Name = "company")] Guid? objCompanyId, CancellationToken objCancellationToken)
    {
        IQueryable<Unit> objQuery = _objDbContext.Units.AsNoTracking();

        Guid? objTokenCompanyId = User.GetCompanyId();
        if (objTokenCompanyId is not null)
        {
            // Admin de empresa: sempre restrito à própria empresa.
            objQuery = objQuery.Where(unit => unit.IDCompany == objTokenCompanyId);
        }
        else if (objCompanyId is not null)
        {
            // Super admin: filtro opcional por empresa.
            objQuery = objQuery.Where(unit => unit.IDCompany == objCompanyId);
        }

        List<Unit> objUnits = await objQuery
            .OrderBy(unit => unit.Name)
            .ToListAsync(objCancellationToken);

        return Ok(objUnits.Select(UnitDto.FromEntity).ToList());
    }

    /// <summary>Cria uma unidade. O slug identifica o portal (?unit=slug), é único e imutável.</summary>
    [HttpPost]
    [Authorize(Roles = ClaimsExtensions.RoleSuperAdmin)]
    public async Task<ActionResult<UnitDto>> Create(
        CreateUnitRequest objRequest, CancellationToken objCancellationToken)
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

        bool bCompanyExiste = await _objDbContext.Companies
            .AnyAsync(company => company.Id == objRequest.IDCompany, objCancellationToken);
        if (!bCompanyExiste)
        {
            return BadRequest(new ErrorResponse("Empresa não encontrada."));
        }

        bool bSlugEmUso = await _objDbContext.Units
            .AnyAsync(unit => unit.Slug == objRequest.Slug, objCancellationToken);
        if (bSlugEmUso)
        {
            return BadRequest(new ErrorResponse("Já existe uma unidade com esse slug."));
        }

        Unit objUnit = new Unit
        {
            IDCompany = objRequest.IDCompany,
            Name = objRequest.Name.Trim(),
            Slug = objRequest.Slug,
        };
        ApplyUnifi(objUnit, objRequest.Unifi);

        _objDbContext.Units.Add(objUnit);
        await _objDbContext.SaveChangesAsync(objCancellationToken);

        return Ok(UnitDto.FromEntity(objUnit));
    }

    /// <summary>Atualiza nome, situação e config UniFi (senha nula = manter a atual).</summary>
    [HttpPut("{id:guid}")]
    [Authorize(Roles = ClaimsExtensions.RoleSuperAdmin)]
    public async Task<ActionResult<UnitDto>> Update(
        Guid id, UpdateUnitRequest objRequest, CancellationToken objCancellationToken)
    {
        Unit? objUnit = await _objDbContext.Units
            .FirstOrDefaultAsync(unit => unit.Id == id, objCancellationToken);
        if (objUnit is null)
        {
            return NotFound(new ErrorResponse("Unidade não encontrada."));
        }

        if (string.IsNullOrWhiteSpace(objRequest.Name) || objRequest.Name.Trim().Length > 120)
        {
            return BadRequest(new ErrorResponse("Nome é obrigatório (máximo de 120 caracteres)."));
        }

        objUnit.Name = objRequest.Name.Trim();
        objUnit.Active = objRequest.Active;
        ApplyUnifi(objUnit, objRequest.Unifi);

        await _objDbContext.SaveChangesAsync(objCancellationToken);

        return Ok(UnitDto.FromEntity(objUnit));
    }

    private static void ApplyUnifi(Unit objUnit, UnitUnifiRequest? objUnifi)
    {
        if (objUnifi is null)
        {
            return;
        }

        objUnit.Unifi.Host = objUnifi.Host?.Trim() ?? "";
        objUnit.Unifi.Site = string.IsNullOrWhiteSpace(objUnifi.Site) ? "default" : objUnifi.Site.Trim();
        objUnit.Unifi.Username = objUnifi.Username?.Trim() ?? "";
        objUnit.Unifi.UnifiOs = objUnifi.UnifiOs;
        objUnit.Unifi.VerifySsl = objUnifi.VerifySsl;
        if (objUnifi.Password is not null)
        {
            objUnit.Unifi.Password = objUnifi.Password;
        }
    }
}
