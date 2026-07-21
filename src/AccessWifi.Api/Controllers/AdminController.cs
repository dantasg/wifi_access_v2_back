using AccessWifi.Api.Features.Admin;
using AccessWifi.Api.Features.Leads;
using AccessWifi.Api.Infrastructure.Persistence;
using AccessWifi.Api.Infrastructure.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AccessWifi.Api.Controllers;

[ApiController]
[Route("admin")]
public class AdminController : ControllerBase
{
    private readonly AppDbContext _objDbContext;
    private readonly TokenService _objTokenService;
    private readonly AdminOptions _objAdminOptions;

    public AdminController(
        AppDbContext objDbContext,
        TokenService objTokenService,
        IOptions<AdminOptions> objAdminOptions)
    {
        _objDbContext = objDbContext;
        _objTokenService = objTokenService;
        _objAdminOptions = objAdminOptions.Value;
    }

    /// <summary>Valida as credenciais do admin e devolve o JWT.</summary>
    [HttpPost("login")]
    [EnableRateLimiting("admin-login")]
    public ActionResult<LoginResponse> Login(LoginRequest objRequest)
    {
        // Sem hash configurado não há como validar ninguém — trata como credencial inválida.
        if (string.IsNullOrWhiteSpace(_objAdminOptions.PasswordHash))
        {
            return Unauthorized();
        }

        if (objRequest.Username != _objAdminOptions.Username ||
            !BCrypt.Net.BCrypt.Verify(objRequest.Password, _objAdminOptions.PasswordHash))
        {
            return Unauthorized();
        }

        string sToken = _objTokenService.GenerateToken(objRequest.Username);
        return Ok(new LoginResponse(sToken));
    }

    /// <summary>Lista os cadastros do portal (mais recentes primeiro).</summary>
    [HttpGet("leads")]
    [Authorize]
    public async Task<ActionResult<List<LeadDto>>> GetLeads(CancellationToken objCancellationToken)
    {
        List<LeadDto> objLeads = await _objDbContext.Leads
            .AsNoTracking()
            .OrderByDescending(lead => lead.Timestamp)
            .Select(lead => new LeadDto(
                lead.Timestamp, lead.Nome, lead.Instagram, lead.Telefone,
                lead.Nascimento, lead.Mac, lead.Ap, lead.Ssid))
            .ToListAsync(objCancellationToken);

        return Ok(objLeads);
    }
}
