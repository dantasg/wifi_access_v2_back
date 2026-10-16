using AccessWifi.Api.Features;
using AccessWifi.Api.Features.Campaigns;
using AccessWifi.Api.Infrastructure.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Models.Campaigns;
using Models.DataBase;
using Models.Email;
using Models.Persistence;

namespace AccessWifi.Api.Controllers;

/// <summary>
/// Demonstração de campanha (PROPOSTA_DEMONSTRACAO_CAMPANHA.md), só do super admin, pela empresa escolhida no
/// topo (?company=slug): manda para o e-mail de uma unidade o PDF de campanha com todos os clientes dela e uma
/// mensagem padrão, na hora. O e-mail sai igual ao de uma campanha; no sistema fica no Correio eletrônico como
/// "Demonstração". Não cria execução, então não mexe nas campanhas reais nem nos números.
/// </summary>
[ApiController]
[Route("admin/campaigns/demo")]
[Authorize(Roles = ClaimsExtensions.RoleSuperAdmin)]
public class CampaignDemoController : ControllerBase
{
    private readonly AppDbContext _objDbContext;
    private readonly IEmailSender _objEmailSender;
    private readonly ILogger<CampaignDemoController> _objLogger;

    public CampaignDemoController(
        AppDbContext objDbContext, IEmailSender objEmailSender, ILogger<CampaignDemoController> objLogger)
    {
        _objDbContext = objDbContext;
        _objEmailSender = objEmailSender;
        _objLogger = objLogger;
    }

    /// <summary>O que a confirmação mostra: a mensagem padrão e cada unidade ativa, com o e-mail e quantos clientes.</summary>
    [HttpGet]
    public async Task<ActionResult<CampaignDemoInfoDto>> Get(
        [FromQuery(Name = "company")] string? sCompanySlug, CancellationToken objCancellationToken)
    {
        (Company? objCompany, ActionResult? objError) = await FindCompanyAsync(sCompanySlug, objCancellationToken);
        if (objCompany is null)
        {
            return objError!;
        }

        List<CampaignDemoUnitDto> objUnits = await _objDbContext.Units.AsNoTracking()
            .Where(unit => unit.IDCompany == objCompany.Id && unit.Active)
            .OrderBy(unit => unit.Name)
            .Select(unit => new CampaignDemoUnitDto(
                unit.Id, unit.Name, unit.Email.Trim(),
                _objDbContext.CustomerUnits.Count(link => link.IDUnit == unit.Id)))
            .ToListAsync(objCancellationToken);

        return Ok(new CampaignDemoInfoDto(
            CampaignKind.Label(CampaignKind.Demo),
            CampaignMessage.RenderShared(CampaignDemo.Message, objCompany.Name, ""),
            objUnits));
    }

    /// <summary>Manda a demonstração para o e-mail da unidade, na hora, e registra no Correio eletrônico.</summary>
    [HttpPost("{unitId:guid}")]
    public async Task<ActionResult<CampaignDemoResultDto>> Send(
        Guid unitId, [FromQuery(Name = "company")] string? sCompanySlug, CancellationToken objCancellationToken)
    {
        (Company? objCompany, ActionResult? objError) = await FindCompanyAsync(sCompanySlug, objCancellationToken);
        if (objCompany is null)
        {
            return objError!;
        }

        Unit? objUnit = await _objDbContext.Units.AsNoTracking()
            .FirstOrDefaultAsync(unit => unit.Id == unitId && unit.IDCompany == objCompany.Id, objCancellationToken);
        if (objUnit is null)
        {
            return NotFound(new ErrorResponse("Unidade não encontrada nesta empresa."));
        }
        if (!objUnit.Active)
        {
            return BadRequest(new ErrorResponse($"A unidade {objUnit.Name} está inativa."));
        }
        string sEmail = objUnit.Email.Trim();
        if (sEmail.Length == 0)
        {
            return BadRequest(new ErrorResponse(
                $"A unidade {objUnit.Name} não tem e-mail cadastrado (Unidades → editar → e-mail)."));
        }

        DateOnly dtToday = CompanyTimeZone.Today(CompanyTimeZone.Resolve(objCompany.TimeZone), DateTime.UtcNow);
        CampaignPdfData objData = await CampaignDemo.LoadAsync(_objDbContext, objUnit, dtToday, objCancellationToken);
        if (objData.Rows.Count == 0)
        {
            return BadRequest(new ErrorResponse($"A unidade {objUnit.Name} ainda não tem clientes cadastrados."));
        }

        string sSubject = CampaignEmailText.Subject(objData);
        string sBody = CampaignEmailText.Body(objData);
        string sFileName = CampaignDeliveryDocument.FileName(CampaignKind.Demo, objUnit.Slug, dtToday);
        try
        {
            await _objEmailSender.SendAsync(
                sEmail, sSubject, sBody, CampaignPdf.Build(objData), sFileName, objCancellationToken);
        }
        catch (Exception objException) when (objException is not OperationCanceledException)
        {
            _objLogger.LogWarning(objException, "Demonstração para {Email} (unidade {Unidade}) não saiu.", sEmail, objUnit.Name);
            return BadRequest(new ErrorResponse($"O e-mail para {sEmail} não saiu: {objException.Message}"));
        }

        _objDbContext.SentEmails.Add(new SentEmail
        {
            IDCompany = objCompany.Id,
            IDUnit = objUnit.Id,
            UnitName = objUnit.Name,
            Kind = SentEmailKind.CampaignDemo,
            ToEmail = sEmail,
            Subject = sSubject,
            Body = sBody,
            AttachmentName = sFileName,
            SentAt = DateTime.UtcNow,
        });
        await _objDbContext.SaveChangesAsync(objCancellationToken);
        _objLogger.LogInformation(
            "Demonstração de campanha pedida por {Usuario}: {Total} cliente(s) para {Email} (unidade {Unidade}).",
            User.GetUsername(), objData.Rows.Count, sEmail, objUnit.Name);

        return Ok(new CampaignDemoResultDto(objUnit.Name, sEmail, objData.Rows.Count, sSubject));
    }

    private async Task<(Company?, ActionResult?)> FindCompanyAsync(
        string? sCompanySlug, CancellationToken objCancellationToken)
    {
        if (string.IsNullOrWhiteSpace(sCompanySlug))
        {
            return (null, BadRequest(new ErrorResponse("Escolha a empresa no topo do painel (?company=slug).")));
        }
        Company? objCompany = await _objDbContext.Companies.AsNoTracking()
            .FirstOrDefaultAsync(company => company.Slug == sCompanySlug, objCancellationToken);
        return objCompany is null
            ? (null, NotFound(new ErrorResponse("Empresa não encontrada.")))
            : (objCompany, null);
    }
}
