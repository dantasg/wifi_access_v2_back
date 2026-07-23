using AccessWifi.Api.Features.Authorize;
using Models.Persistence;
using AccessWifi.Api.Infrastructure.Unifi;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Models.DataBase;

namespace AccessWifi.Api.Controllers;

[ApiController]
[Route("authorize")]
[EnableRateLimiting("authorize")]
public class AuthorizeController : ControllerBase
{
    private const int DefaultAccessMinutes = 1440;

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

    /// <summary>Grava o lead e autoriza o dispositivo do visitante na controladora UniFi da empresa.</summary>
    [HttpPost]
    [RequestSizeLimit(16 * 1024)]
    public async Task<ActionResult<AuthorizeResponse>> Post(
        AuthorizeRequest objRequest, CancellationToken objCancellationToken)
    {
        if (string.IsNullOrWhiteSpace(objRequest.Company))
        {
            return BadRequest(new AuthorizeResponse(false, Error: "Empresa não informada."));
        }

        Company? objCompany = await _objDbContext.Companies
            .FirstOrDefaultAsync(company => company.Slug == objRequest.Company, objCancellationToken);
        if (objCompany is null || !objCompany.Active)
        {
            return BadRequest(new AuthorizeResponse(false, Error: "Empresa não encontrada ou inativa."));
        }

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
            IDCompany = objCompany.Id,
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

        // Tempo de liberação salvo nas Configurações da empresa; sem linha, usa o padrão.
        int iAccessMinutes = await _objDbContext.PortalSettings
            .AsNoTracking()
            .Where(settings => settings.IDCompany == objCompany.Id)
            .Select(settings => (int?)settings.AccessMinutes)
            .FirstOrDefaultAsync(objCancellationToken)
            ?? DefaultAccessMinutes;

        try
        {
            await _objUnifiClient.AuthorizeGuestAsync(
                objCompany.Unifi, objRequest.Mac, iAccessMinutes, objCancellationToken);
        }
        catch (UnifiException objException)
        {
            // Não logar dados pessoais — só a empresa e o motivo técnico da falha.
            _objLogger.LogError(
                objException, "Falha ao autorizar guest na UniFi da empresa {Slug}.", objCompany.Slug);
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
