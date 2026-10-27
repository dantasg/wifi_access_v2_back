using AccessWifi.Api.Features;
using AccessWifi.Api.Features.SiteContacts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Models.DataBase;
using Models.Persistence;

namespace AccessWifi.Api.Controllers;

/// <summary>
/// Formulário de contato do site institucional (PROPOSTA_SITE.md §4). Público: grava o pedido com a origem do
/// visitante e responde na hora; o aviso por e-mail é do serviço (SiteContactNotifier), para a página não esperar o
/// servidor de e-mail.
///
/// Enquanto o site estiver em /site/ do mesmo endereço, não há CORS. Com o domínio próprio, a origem do site entra em
/// "SiteContact:AllowedOrigins" (política <see cref="CorsPolicy"/>).
/// </summary>
[ApiController]
[Route("site-contact")]
[AllowAnonymous]
[EnableRateLimiting("site-contact")]
[EnableCors(CorsPolicy)]
public class SiteContactController : ControllerBase
{
    public const string CorsPolicy = "site";

    private const int MaxBodyBytes = 16 * 1024;

    private readonly AppDbContext _objDbContext;
    private readonly ILogger<SiteContactController> _objLogger;

    public SiteContactController(AppDbContext objDbContext, ILogger<SiteContactController> objLogger)
    {
        _objDbContext = objDbContext;
        _objLogger = objLogger;
    }

    [HttpPost]
    [RequestSizeLimit(MaxBodyBytes)]
    public async Task<IActionResult> Create(SiteContactRequest objRequest, CancellationToken objCancellationToken)
    {
        // Robô (campo-isca preenchido): responde como se tivesse dado certo, para ele não tentar de outro jeito.
        if (!string.IsNullOrEmpty(objRequest.Website))
        {
            _objLogger.LogInformation("Contato do site recusado: campo-isca preenchido.");
            return Ok(new { ok = true });
        }

        string? sError = SiteContactRules.TryBuild(objRequest, DateTime.UtcNow, out SiteContact? objContact);
        if (sError is not null || objContact is null)
        {
            return BadRequest(new ErrorResponse(sError ?? "Confira os dados do formulário."));
        }

        _objDbContext.SiteContacts.Add(objContact);
        await _objDbContext.SaveChangesAsync(objCancellationToken);
        // Sem dado pessoal no log: só a origem, para acompanhar os anúncios.
        _objLogger.LogInformation(
            "Contato do site recebido (origem: {Source}/{Medium}).",
            objContact.UtmSource.Length > 0 ? objContact.UtmSource : "direto", objContact.UtmMedium);
        return Ok(new { ok = true });
    }
}
