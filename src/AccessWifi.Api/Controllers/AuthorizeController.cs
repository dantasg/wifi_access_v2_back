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

    /// <summary>Grava o lead e autoriza o dispositivo do visitante na controladora UniFi da unidade.</summary>
    [HttpPost]
    [RequestSizeLimit(16 * 1024)]
    public async Task<ActionResult<AuthorizeResponse>> Post(
        AuthorizeRequest objRequest, CancellationToken objCancellationToken)
    {
        if (string.IsNullOrWhiteSpace(objRequest.Unit))
        {
            return BadRequest(new AuthorizeResponse(false, Error: "Unidade não informada."));
        }

        Unit? objUnit = await _objDbContext.Units
            .FirstOrDefaultAsync(unit => unit.Slug == objRequest.Unit, objCancellationToken);
        if (objUnit is null || !objUnit.Active)
        {
            return BadRequest(new AuthorizeResponse(false, Error: "Unidade não encontrada ou inativa."));
        }

        if (string.IsNullOrWhiteSpace(objRequest.Mac))
        {
            return BadRequest(new AuthorizeResponse(false, Error: "MAC do cliente ausente."));
        }

        if (!objRequest.Consentimento)
        {
            return BadRequest(new AuthorizeResponse(false, Error: "É necessário aceitar os termos (LGPD)."));
        }

        // Upsert por (IDUnit, Mac): o mesmo aparelho voltando na mesma unidade atualiza o
        // cadastro em vez de duplicar. O Mac é obrigatório (validado acima), então é uma chave
        // sempre presente.
        Lead? objLead = await _objDbContext.Leads
            .FirstOrDefaultAsync(
                lead => lead.IDUnit == objUnit.Id && lead.Mac == objRequest.Mac, objCancellationToken);
        if (objLead is null)
        {
            objLead = new Lead { IDUnit = objUnit.Id, Mac = objRequest.Mac };
            _objDbContext.Leads.Add(objLead);
        }

        objLead.Nome = objRequest.Nome;
        objLead.Instagram = objRequest.Instagram;
        objLead.Telefone = objRequest.Telefone;
        objLead.Nascimento = objRequest.Nascimento;
        objLead.Ap = objRequest.Ap;
        objLead.Ssid = objRequest.Ssid;
        objLead.Timestamp = DateTime.UtcNow;
        await _objDbContext.SaveChangesAsync(objCancellationToken);

        // Configurações da empresa dona da unidade (tempo de liberação + URL de redirecionamento).
        var objCompanySettings = await _objDbContext.PortalSettings
            .AsNoTracking()
            .Where(settings => settings.IDCompany == objUnit.IDCompany)
            .Select(settings => new { settings.AccessMinutes, settings.RedirectUrl })
            .FirstOrDefaultAsync(objCancellationToken);

        int iAccessMinutes = objCompanySettings?.AccessMinutes ?? DefaultAccessMinutes;

        try
        {
            await _objUnifiClient.AuthorizeGuestAsync(
                objUnit.Unifi, objRequest.Mac, iAccessMinutes, objCancellationToken);
        }
        catch (UnifiException objException)
        {
            // Não logar dados pessoais — só a unidade e o motivo técnico da falha.
            _objLogger.LogError(
                objException, "Falha ao autorizar guest na UniFi da unidade {Slug}.", objUnit.Slug);
            return StatusCode(
                StatusCodes.Status502BadGateway,
                new AuthorizeResponse(false, Error: "Falha ao autorizar na UniFi."));
        }

        // Precedência: URL configurada pela empresa (ex.: Instagram) vence; senão a URL que a
        // UniFi enviou; por fim, o fallback fixo.
        string sRedirect = objCompanySettings?.RedirectUrl is { Length: > 0 } sCompanyUrl
            ? sCompanyUrl
            : string.IsNullOrWhiteSpace(objRequest.Url)
                ? "https://www.google.com"
                : objRequest.Url;
        return Ok(new AuthorizeResponse(true, Redirect: sRedirect));
    }
}
