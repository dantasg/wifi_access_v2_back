using AccessWifi.Api.Features.Authorize;
using AccessWifi.Api.Features.Leads;
using AccessWifi.Api.Infrastructure.Persistence;
using AccessWifi.Api.Infrastructure.Unifi;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace AccessWifi.Api.Controllers;

[ApiController]
[Route("authorize")]
[EnableRateLimiting("authorize")]
public class AuthorizeController : ControllerBase
{
    private readonly AppDbContext _objDbContext;
    private readonly IUnifiClient _objUnifiClient;
    private readonly ILogger<AuthorizeController> _objLogger;

    public AuthorizeController(
        AppDbContext objDbContext,
        IUnifiClient objUnifiClient,
        ILogger<AuthorizeController> objLogger)
    {
        _objDbContext = objDbContext;
        _objUnifiClient = objUnifiClient;
        _objLogger = objLogger;
    }

    /// <summary>Grava o lead e autoriza o dispositivo do visitante na controladora UniFi.</summary>
    [HttpPost]
    [RequestSizeLimit(16 * 1024)]
    public async Task<ActionResult<AuthorizeResponse>> Post(
        AuthorizeRequest objRequest, CancellationToken objCancellationToken)
    {
        if (string.IsNullOrWhiteSpace(objRequest.Mac))
        {
            return BadRequest(new AuthorizeResponse(false, Error: "MAC do cliente ausente."));
        }

        if (!objRequest.Consentimento)
        {
            return BadRequest(new AuthorizeResponse(false, Error: "É necessário aceitar os termos (LGPD)."));
        }

        Lead objLead = new Lead
        {
            Nome = objRequest.Nome,
            Instagram = objRequest.Instagram,
            Telefone = objRequest.Telefone,
            Nascimento = objRequest.Nascimento,
            Mac = objRequest.Mac,
            Ap = objRequest.Ap,
            Ssid = objRequest.Ssid,
        };
        _objDbContext.Leads.Add(objLead);
        await _objDbContext.SaveChangesAsync(objCancellationToken);

        // Tempo de liberação salvo nas Configurações do admin; sem linha, usa o do appsettings.
        int? iAccessMinutes = await _objDbContext.PortalSettings
            .AsNoTracking()
            .Select(settings => (int?)settings.AccessMinutes)
            .FirstOrDefaultAsync(objCancellationToken);

        try
        {
            await _objUnifiClient.AuthorizeGuestAsync(objRequest.Mac, iAccessMinutes, objCancellationToken);
        }
        catch (UnifiException objException)
        {
            // Não logar dados pessoais — só o motivo técnico da falha.
            _objLogger.LogError(objException, "Falha ao autorizar guest na UniFi.");
            return StatusCode(
                StatusCodes.Status502BadGateway,
                new AuthorizeResponse(false, Error: "Falha ao autorizar na UniFi."));
        }

        string sRedirect = string.IsNullOrWhiteSpace(objRequest.Url)
            ? "https://www.google.com"
            : objRequest.Url;
        return Ok(new AuthorizeResponse(true, Redirect: sRedirect));
    }
}
