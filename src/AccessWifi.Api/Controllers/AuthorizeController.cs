using AccessWifi.Api.Features.Authorize;
using AccessWifi.Api.Features.Units;
using Models.Persistence;
using AccessWifi.Api.Infrastructure.Unifi;
using Models.Campaigns;
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
    private readonly UnitLocator _objUnitLocator;

    public AuthorizeController(
        AppDbContext objDbContext,
        IUnifiClient objUnifiClient,
        ILogger<AuthorizeController> objLogger,
        UnitLocator? objUnitLocator = null)
    {
        _objDbContext = objDbContext;
        _objUnifiClient = objUnifiClient;
        _objLogger = objLogger;
        _objUnitLocator = objUnitLocator ?? new UnitLocator(objDbContext);
    }

    /// <summary>Grava o lead e autoriza o dispositivo do visitante na controladora UniFi da unidade.</summary>
    [HttpPost]
    [RequestSizeLimit(16 * 1024)]
    public async Task<ActionResult<AuthorizeResponse>> Post(
        AuthorizeRequest objRequest, CancellationToken objCancellationToken)
    {
        // A unidade vem pelo slug ou, quando a UniFi não pôde mandar a query string, pelo ponto de
        // acesso (Ap) e pelo host em que o portal foi aberto (ver UnitLocator).
        if (string.IsNullOrWhiteSpace(objRequest.Unit) && string.IsNullOrWhiteSpace(objRequest.Host))
        {
            return BadRequest(new AuthorizeResponse(false, Error: "Unidade não informada."));
        }

        Unit? objUnit = await _objUnitLocator.FindAsync(
            _objDbContext.Units, objRequest.Unit, objRequest.Host, objRequest.Ap, objCancellationToken);
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
        bool bAparelhoNovo = objLead is null;
        if (objLead is null)
        {
            objLead = new Lead { IDUnit = objUnit.Id, Mac = objRequest.Mac };
            _objDbContext.Leads.Add(objLead);
        }

        objLead.Nome = objRequest.Nome;
        objLead.Instagram = InstagramHandle.ProfileUrl(objRequest.Instagram);
        objLead.Telefone = objRequest.Telefone;
        objLead.Nascimento = objRequest.Nascimento;
        objLead.Ap = objRequest.Ap;
        objLead.Ssid = objRequest.Ssid;
        objLead.Timestamp = DateTime.UtcNow;
        await _objDbContext.SaveChangesAsync(objCancellationToken);

        // Configurações da empresa dona da unidade (tempo de liberação + URL "Geral" de
        // redirecionamento, usada quando a unidade não tem a sua).
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

        // Base de clientes das campanhas (D2). Fica depois da UniFi para não atrasar a liberação.
        ClienteRegistrado objCliente = await RegistrarClienteAsync(
            objUnit, objRequest, bAparelhoNovo, objCancellationToken);

        // Conexão do dashboard (PROPOSTA_DASHBOARD.md, D3): só a que a UniFi liberou.
        if (!bUnifiFalhou)
        {
            await RegistrarConexaoAsync(objUnit, objRequest.Ap, objCliente, objCancellationToken);
        }

        if (bUnifiFalhou)
        {
            return StatusCode(
                StatusCodes.Status502BadGateway,
                new AuthorizeResponse(false, Error: "Falha ao autorizar na UniFi."));
        }

        string sRedirect = EscolherRedirect(
            objUnit.RedirectUrl, objCompanySettings?.RedirectUrl, objRequest.Url);
        return Ok(new AuthorizeResponse(true, Redirect: sRedirect));
    }

    /// <summary>O que a conexão do dashboard precisa saber do cliente desta visita.</summary>
    private record ClienteRegistrado(
        Guid? IDCustomer, bool NovoNaEmpresa, bool NovoNaUnidade, TimeZoneInfo Fuso, DateTime AtUtc);

    /// <summary>
    /// Atualiza o cliente da empresa (um por telefone) com esta conexão. É invisível para o visitante e
    /// nunca pode derrubar a liberação do Wi-Fi: qualquer falha aqui só fica no log.
    /// Sem cliente (telefone que não identifica ninguém, ou gravação que falhou), "novo" vale pelo aparelho.
    /// </summary>
    private async Task<ClienteRegistrado> RegistrarClienteAsync(
        Unit objUnit, AuthorizeRequest objRequest, bool bAparelhoNovo, CancellationToken objCancellationToken)
    {
        DateTime dtNowUtc = DateTime.UtcNow;
        TimeZoneInfo objFuso = CompanyTimeZone.Resolve(null);
        try
        {
            string? sTimeZone = await _objDbContext.Companies.AsNoTracking()
                .Where(company => company.Id == objUnit.IDCompany)
                .Select(company => company.TimeZone)
                .FirstOrDefaultAsync(objCancellationToken);
            objFuso = CompanyTimeZone.Resolve(sTimeZone);
            Customer? objCustomer = await CustomerDirectory.RegisterVisitAsync(
                _objDbContext, objUnit.IDCompany, objFuso, objUnit.Id,
                objRequest.Nome, InstagramHandle.ProfileUrl(objRequest.Instagram), objRequest.Telefone, objRequest.Nascimento,
                dtNowUtc, objCancellationToken);

            // "Novo" sai do que esta visita acabou de criar — depois do SaveChanges já não dá para saber.
            bool bNovoNaEmpresa = objCustomer is null
                ? bAparelhoNovo
                : _objDbContext.Entry(objCustomer).State == EntityState.Added;
            bool bNovoNaUnidade = objCustomer is null
                ? bAparelhoNovo
                : _objDbContext.ChangeTracker.Entries<CustomerUnit>().Any(entry =>
                    entry.State == EntityState.Added
                    && entry.Entity.IDCustomer == objCustomer.Id
                    && entry.Entity.IDUnit == objUnit.Id);

            await _objDbContext.SaveChangesAsync(objCancellationToken);
            return new ClienteRegistrado(objCustomer?.Id, bNovoNaEmpresa, bNovoNaUnidade, objFuso, dtNowUtc);
        }
        catch (Exception objException) when (objException is not OperationCanceledException)
        {
            // Ex.: o mesmo telefone conectando em dois aparelhos no mesmo instante (a segunda gravação
            // esbarra na chave única). O cliente se acerta na próxima conexão. O que não foi gravado sai do
            // contexto, para não voltar junto com a conexão.
            _objLogger.LogWarning(objException, "Cliente não atualizado na unidade {Slug}.", objUnit.Slug);
            _objDbContext.ChangeTracker.Clear();
            return new ClienteRegistrado(null, bAparelhoNovo, bAparelhoNovo, objFuso, dtNowUtc);
        }
    }

    /// <summary>
    /// Registra a conexão liberada para o dashboard (PROPOSTA_DASHBOARD.md, D3). Gravação própria, depois do
    /// cliente: se falhar, o cliente já está salvo e o visitante segue para o redirecionamento — só fica no log.
    /// </summary>
    private async Task RegistrarConexaoAsync(
        Unit objUnit, string? sAp, ClienteRegistrado objCliente, CancellationToken objCancellationToken)
    {
        try
        {
            DateTime dtLocal = TimeZoneInfo.ConvertTimeFromUtc(
                DateTime.SpecifyKind(objCliente.AtUtc, DateTimeKind.Utc), objCliente.Fuso);
            _objDbContext.Visits.Add(new Visit
            {
                IDUnit = objUnit.Id,
                IDCustomer = objCliente.IDCustomer,
                At = objCliente.AtUtc,
                LocalDate = DateOnly.FromDateTime(dtLocal),
                LocalHour = dtLocal.Hour,
                NewInCompany = objCliente.NovoNaEmpresa,
                NewInUnit = objCliente.NovoNaUnidade,
                Ap = MacAddress.Normalize(sAp),
            });
            await _objDbContext.SaveChangesAsync(objCancellationToken);
        }
        catch (Exception objException) when (objException is not OperationCanceledException)
        {
            _objLogger.LogWarning(objException, "Conexão não registrada para o dashboard na unidade {Slug}.", objUnit.Slug);
            _objDbContext.ChangeTracker.Clear();
        }
    }

    /// <summary>
    /// Para onde o visitante vai depois de liberado, na ordem: a URL da unidade (ex.: o Instagram
    /// da loja); senão a "Geral" da empresa; senão a URL que a UniFi enviou; por fim, o Google.
    /// </summary>
    private static string EscolherRedirect(string? sUnitUrl, string? sCompanyUrl, string? sUnifiUrl)
    {
        if (!string.IsNullOrWhiteSpace(sUnitUrl))
        {
            return sUnitUrl;
        }

        if (!string.IsNullOrWhiteSpace(sCompanyUrl))
        {
            return sCompanyUrl;
        }

        return string.IsNullOrWhiteSpace(sUnifiUrl) ? "https://www.google.com" : sUnifiUrl;
    }
}
