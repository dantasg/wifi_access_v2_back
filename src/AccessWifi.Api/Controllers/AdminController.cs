using AccessWifi.Api.Features;
using AccessWifi.Api.Features.Admin;
using AccessWifi.Api.Features.Companies;
using AccessWifi.Api.Features.Leads;
using Models.Persistence;
using AccessWifi.Api.Infrastructure.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Models.DataBase;

namespace AccessWifi.Api.Controllers;

[ApiController]
[Route("admin")]
public class AdminController : ControllerBase
{
    private readonly AppDbContext _objDbContext;
    private readonly TokenService _objTokenService;

    public AdminController(AppDbContext objDbContext, TokenService objTokenService)
    {
        _objDbContext = objDbContext;
        _objTokenService = objTokenService;
    }

    [HttpPost("login")]
    [EnableRateLimiting("admin-login")]
    public async Task<ActionResult<LoginResponse>> Login(LoginRequest objRequest, CancellationToken objCancellationToken)
    {
        AdminUser? objUser = await _objDbContext.Users
            .Include(user => user.Company)
            .FirstOrDefaultAsync(user => user.Username == objRequest.Username, objCancellationToken);

        if (objUser is null || !BCrypt.Net.BCrypt.Verify(objRequest.Password, objUser.PasswordHash))
        {
            return Unauthorized();
        }

        if (objUser.Company is not null && !objUser.Company.Active)
        {
            return Unauthorized();
        }

        string sToken = _objTokenService.GenerateToken(objUser);
        string sRole = objUser.IDCompany is null
            ? ClaimsExtensions.RoleSuperAdmin
            : ClaimsExtensions.RoleAdmin;
        CompanySummaryDto? objCompany = objUser.Company is null
            ? null
            : CompanySummaryDto.FromEntity(objUser.Company);

        return Ok(new LoginResponse(sToken, sRole, objCompany));
    }

    /// <summary>
    /// Leads da empresa, agregando todas as unidades. O admin vê a própria empresa; o super
    /// admin indica a empresa por ?company=slug. Filtro opcional ?unit=slug limita a uma
    /// unidade; sem ele, traz os leads de todas as unidades da empresa.
    /// </summary>
    [HttpGet("leads")]
    [Authorize]
    public async Task<ActionResult<List<LeadDto>>> GetLeads(
        [FromQuery(Name = "company")] string? sCompanySlug,
        [FromQuery(Name = "unit")] string? sUnitSlug,
        CancellationToken objCancellationToken)
    {
        Guid? objCompanyId = User.GetCompanyId();
        if (objCompanyId is null)
        {
            // Super admin: a empresa vem da query string.
            if (string.IsNullOrWhiteSpace(sCompanySlug))
            {
                return BadRequest(new ErrorResponse("Informe a empresa (?company=slug)."));
            }
            Company? objCompany = await _objDbContext.Companies
                .FirstOrDefaultAsync(company => company.Slug == sCompanySlug, objCancellationToken);
            if (objCompany is null)
            {
                return NotFound(new ErrorResponse("Empresa não encontrada."));
            }
            objCompanyId = objCompany.Id;
        }

        // Unidades da empresa; filtro opcional por slug de unidade.
        IQueryable<Unit> objUnitsQuery = _objDbContext.Units
            .AsNoTracking()
            .Where(unit => unit.IDCompany == objCompanyId);
        if (!string.IsNullOrWhiteSpace(sUnitSlug))
        {
            objUnitsQuery = objUnitsQuery.Where(unit => unit.Slug == sUnitSlug);
        }

        List<LeadDto> objLeads = await
            (from lead in _objDbContext.Leads.AsNoTracking()
             join unit in objUnitsQuery on lead.IDUnit equals unit.Id
             orderby lead.Timestamp descending
             select new LeadDto(
                 lead.Timestamp, lead.Nome, lead.Instagram, lead.Telefone,
                 lead.Nascimento, lead.Mac, lead.Ap, lead.Ssid,
                 unit.Slug, unit.Name))
            .ToListAsync(objCancellationToken);

        return Ok(objLeads);
    }
}
