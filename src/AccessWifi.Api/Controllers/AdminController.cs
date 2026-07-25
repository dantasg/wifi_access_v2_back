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
    // Hash "isca" com o mesmo custo dos hashes reais: verificado quando o usuário não existe,
    // para o tempo de resposta não revelar se um usuário é válido (anti-enumeração por timing).
    private static readonly string s_sDummyPasswordHash =
        BCrypt.Net.BCrypt.HashPassword("timing-guard-not-a-real-account");

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

        // Verifica sempre um hash (o do usuário ou o isca) para gastar o mesmo tempo, exista o
        // usuário ou não. Só autentica se o usuário existe E a senha confere.
        string sHashParaVerificar = objUser?.PasswordHash ?? s_sDummyPasswordHash;
        bool bSenhaConfere = BCrypt.Net.BCrypt.Verify(objRequest.Password, sHashParaVerificar);
        if (objUser is null || !bSenhaConfere)
        {
            return Unauthorized();
        }

        if (objUser.Company is not null && !objUser.Company.Active)
        {
            return Unauthorized();
        }

        LoginResponse objResponse = await IssueTokensAsync(objUser, objCancellationToken);
        return Ok(objResponse);
    }

    /// <summary>
    /// Troca um refresh token válido por um novo access token (curto) + um novo refresh token
    /// (rotação: o antigo é revogado). Público — o próprio refresh token é a credencial.
    /// </summary>
    [HttpPost("refresh")]
    [EnableRateLimiting("admin-login")]
    public async Task<ActionResult<LoginResponse>> Refresh(
        RefreshRequest objRequest, CancellationToken objCancellationToken)
    {
        if (string.IsNullOrWhiteSpace(objRequest.RefreshToken))
        {
            return Unauthorized();
        }

        string sHash = TokenService.HashRefreshToken(objRequest.RefreshToken);
        RefreshToken? objStored = await _objDbContext.RefreshTokens
            .FirstOrDefaultAsync(token => token.TokenHash == sHash, objCancellationToken);
        if (objStored is null || objStored.RevokedAt is not null || objStored.ExpiresAt <= DateTime.UtcNow)
        {
            return Unauthorized();
        }

        AdminUser? objUser = await _objDbContext.Users
            .Include(user => user.Company)
            .FirstOrDefaultAsync(user => user.Id == objStored.IDUser, objCancellationToken);
        if (objUser is null || (objUser.Company is not null && !objUser.Company.Active))
        {
            return Unauthorized();
        }

        // Rotação: revoga o token usado antes de emitir o novo par.
        objStored.RevokedAt = DateTime.UtcNow;
        LoginResponse objResponse = await IssueTokensAsync(objUser, objCancellationToken);
        return Ok(objResponse);
    }

    /// <summary>Revoga um refresh token (logout). Idempotente: sempre 204.</summary>
    [HttpPost("logout")]
    public async Task<IActionResult> Logout(RefreshRequest objRequest, CancellationToken objCancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(objRequest.RefreshToken))
        {
            string sHash = TokenService.HashRefreshToken(objRequest.RefreshToken);
            RefreshToken? objStored = await _objDbContext.RefreshTokens
                .FirstOrDefaultAsync(
                    token => token.TokenHash == sHash && token.RevokedAt == null, objCancellationToken);
            if (objStored is not null)
            {
                objStored.RevokedAt = DateTime.UtcNow;
                await _objDbContext.SaveChangesAsync(objCancellationToken);
            }
        }

        return NoContent();
    }

    /// <summary>Emite access token + refresh token (persistido) e monta a resposta de login.</summary>
    private async Task<LoginResponse> IssueTokensAsync(AdminUser objUser, CancellationToken objCancellationToken)
    {
        string sAccessToken = _objTokenService.GenerateToken(objUser);
        (string sRefreshToken, RefreshToken objRefreshEntity) = _objTokenService.CreateRefreshToken(objUser.Id);
        _objDbContext.RefreshTokens.Add(objRefreshEntity);
        await _objDbContext.SaveChangesAsync(objCancellationToken);

        string sRole = objUser.IDCompany is null
            ? ClaimsExtensions.RoleSuperAdmin
            : ClaimsExtensions.RoleAdmin;
        CompanySummaryDto? objCompany = objUser.Company is null
            ? null
            : CompanySummaryDto.FromEntity(objUser.Company);

        return new LoginResponse(sAccessToken, sRefreshToken, sRole, objCompany);
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
