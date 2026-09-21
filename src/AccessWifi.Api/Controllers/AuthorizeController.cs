using AccessWifi.Api.Features.Authorize;
using AccessWifi.Api.Features.Units;
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

    /// <summary>
    /// Chamado pelo portal assim que abre, enquanto o visitante ainda preenche o formulário:
    /// adianta a busca do aparelho na controladora para o toque em "Conectar" só precisar
    /// autorizar. Responde 202 na hora — o trabalho segue em segundo plano e, se não der certo,
    /// o POST /authorize faz tudo como sempre fez. Não grava nada nem autoriza ninguém.
    /// </summary>
    [HttpPost("prepare")]
    [EnableRateLimiting("authorize-prepare")]
    [RequestSizeLimit(4 * 1024)]
    public async Task<IActionResult> Prepare(
        PrepareAuthorizeRequest objRequest, CancellationToken objCancellationToken)
    {
        if (string.IsNullOrWhiteSpace(objRequest.Mac)
            || (string.IsNullOrWhiteSpace(objRequest.Unit) && string.IsNullOrWhiteSpace(objRequest.Host)))
        {
            return BadRequest(new AuthorizeResponse(false, Error: "Unidade e MAC são obrigatórios."));
        }

        Unit? objUnit = await UnitResolver.FindAsync(
            _objDbContext.Units.AsNoTracking(), objRequest.Unit, objRequest.Host, objCancellationToken);
        if (objUnit is null || !objUnit.Active)
        {
            return NotFound(new AuthorizeResponse(false, Error: "Unidade não encontrada ou inativa."));
        }

        await _objUnifiClient.PrepareAsync(objUnit.Unifi, objRequest.Mac, objCancellationToken);
        return Accepted();
    }

    /// <summary>Grava o lead e autoriza o dispositivo do visitante na controladora UniFi da unidade.</summary>
    [HttpPost]
    [RequestSizeLimit(16 * 1024)]
    public async Task<ActionResult<AuthorizeResponse>> Post(
        AuthorizeRequest objRequest, CancellationToken objCancellationToken)
    {
        // A unidade vem pelo slug ou, quando a UniFi não pôde mandar a query string, pelo host
        // em que o portal foi aberto (ver UnitResolver).
        if (string.IsNullOrWhiteSpace(objRequest.Unit) && string.IsNullOrWhiteSpace(objRequest.Host))
        {
            return BadRequest(new AuthorizeResponse(false, Error: "Unidade não informada."));
        }

        Unit? objUnit = await UnitResolver.FindAsync(
            _objDbContext.Units, objRequest.Unit, objRequest.Host, objCancellationToken);
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

        bool bUnifiFalhou = false;
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
            bUnifiFalhou = true;
        }

        // No modo nuvem a primeira chamada descobre o SiteId da unidade e o grava na entidade;
        // persistir aqui evita repetir essa descoberta a cada visitante. Sem alteração pendente,
        // o SaveChanges não gera comando algum.
        await _objDbContext.SaveChangesAsync(objCancellationToken);

        if (bUnifiFalhou)
        {
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
