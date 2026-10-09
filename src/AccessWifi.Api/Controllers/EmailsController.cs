using AccessWifi.Api.Features;
using AccessWifi.Api.Features.Emails;
using AccessWifi.Api.Infrastructure.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Models.Campaigns;
using Models.DataBase;
using Models.Persistence;
using Models.Reports;

namespace AccessWifi.Api.Controllers;

/// <summary>
/// Correio eletrônico: os e-mails que saíram para as unidades (relatório mensal e PDF das campanhas), para
/// quem perdeu um e-mail ver o que foi enviado. Mesmas regras de acesso das outras telas: super admin
/// escolhe a empresa (?company=slug), o admin vê a empresa dele e o usuário de unidade, só as unidades dele.
/// O anexo não é guardado: é remontado na hora com os dados que existem.
/// </summary>
[ApiController]
[Route("admin/emails")]
[Authorize]
public class EmailsController : ControllerBase
{
    private const int MaxPageSize = 100;

    private readonly AppDbContext _objDbContext;

    public EmailsController(AppDbContext objDbContext)
    {
        _objDbContext = objDbContext;
    }

    /// <summary>E-mails da empresa, mais novos primeiro. Filtros opcionais: unidade (slug) e tipo.</summary>
    [HttpGet]
    public async Task<ActionResult<SentEmailPageDto>> List(
        [FromQuery(Name = "company")] string? sCompanySlug,
        [FromQuery(Name = "unit")] string? sUnitSlug,
        [FromQuery(Name = "kind")] string? sKind,
        [FromQuery(Name = "page")] int iPage = 1,
        [FromQuery(Name = "pageSize")] int iPageSize = 25,
        CancellationToken objCancellationToken = default)
    {
        (IQueryable<SentEmail>? objQuery, ActionResult? objError) = await ScopedQueryAsync(sCompanySlug, objCancellationToken);
        if (objQuery is null)
        {
            return objError!;
        }

        if (!string.IsNullOrWhiteSpace(sUnitSlug))
        {
            Guid? objUnitId = await _objDbContext.Units.AsNoTracking()
                .Where(unit => unit.Slug == sUnitSlug)
                .Select(unit => (Guid?)unit.Id)
                .FirstOrDefaultAsync(objCancellationToken);
            if (objUnitId is null)
            {
                return Ok(new SentEmailPageDto([], 0));
            }
            objQuery = objQuery.Where(email => email.IDUnit == objUnitId);
        }
        if (sKind is SentEmailKind.Report or SentEmailKind.Campaign)
        {
            objQuery = objQuery.Where(email => email.Kind == sKind);
        }

        int iTamanho = Math.Clamp(iPageSize, 1, MaxPageSize);
        int iPagina = Math.Max(1, iPage);
        int iTotal = await objQuery.CountAsync(objCancellationToken);
        List<SentEmail> objEmails = await objQuery
            .OrderByDescending(email => email.SentAt)
            .ThenByDescending(email => email.Id)
            .Skip((iPagina - 1) * iTamanho)
            .Take(iTamanho)
            .ToListAsync(objCancellationToken);

        return Ok(new SentEmailPageDto(objEmails.Select(SentEmailListItemDto.FromEntity).ToList(), iTotal));
    }

    /// <summary>O e-mail inteiro, com o texto.</summary>
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<SentEmailDto>> Get(
        Guid id, [FromQuery(Name = "company")] string? sCompanySlug, CancellationToken objCancellationToken)
    {
        (SentEmail? objEmail, ActionResult? objError) = await FindAsync(id, sCompanySlug, objCancellationToken);
        return objEmail is null ? objError! : Ok(SentEmailDto.FromEntity(objEmail));
    }

    /// <summary>
    /// O anexo, remontado: o PDF da campanha a partir da execução (os mesmos clientes; o logo e as cores são
    /// os de hoje) ou o CSV do relatório a partir dos cadastros do mês. Some quando a LGPD apaga os dados.
    /// </summary>
    [HttpGet("{id:guid}/attachment")]
    public async Task<IActionResult> Attachment(
        Guid id, [FromQuery(Name = "company")] string? sCompanySlug, CancellationToken objCancellationToken)
    {
        (SentEmail? objEmail, ActionResult? objError) = await FindAsync(id, sCompanySlug, objCancellationToken);
        if (objEmail is null)
        {
            return objError!;
        }

        if (objEmail.Kind == SentEmailKind.Campaign && objEmail.IDCampaignRun is Guid objRunId)
        {
            CampaignRun? objRun = await _objDbContext.CampaignRuns.AsNoTracking()
                .FirstOrDefaultAsync(run => run.Id == objRunId && run.IDCompany == objEmail.IDCompany, objCancellationToken);
            if (objRun is null)
            {
                return NotFound(new ErrorResponse("A execução desta campanha não existe mais."));
            }
            CampaignPdfData objDados = await CampaignDeliveryDocument.LoadAsync(
                _objDbContext, objRun, objEmail.IDUnit, CampaignDeliveryDocument.HistoryStatuses, objCancellationToken);
            if (objDados.Rows.Count == 0)
            {
                return NotFound(new ErrorResponse(
                    "A lista de clientes deste envio já foi apagada pela regra de retenção (LGPD), então o PDF não pode ser remontado."));
            }
            // A unidade pode ter mudado de nome: o PDF mostra o nome de quando saiu.
            objDados = objDados with { UnitName = objEmail.UnitName };
            return File(CampaignPdf.Build(objDados), "application/pdf", objEmail.AttachmentName);
        }

        if (objEmail.Kind == SentEmailKind.Report && objEmail.IDUnit is Guid objUnitId
            && objEmail.PeriodStart is DateTime dtInicio && objEmail.PeriodEnd is DateTime dtFim)
        {
            List<LeadReportRow> objRows = await _objDbContext.Leads.AsNoTracking()
                .Where(lead => lead.IDUnit == objUnitId && lead.CreatedAt >= dtInicio && lead.CreatedAt < dtFim)
                .OrderBy(lead => lead.CreatedAt)
                .Select(lead => new LeadReportRow(lead, objEmail.UnitName))
                .ToListAsync(objCancellationToken);
            return File(LeadsCsv.Build(objRows), "text/csv", objEmail.AttachmentName);
        }

        return NotFound(new ErrorResponse("O anexo deste e-mail não pode ser remontado."));
    }

    /// <summary>Os e-mails que o usuário pode ver na empresa (a do token ou, para o super admin, a do slug).</summary>
    private async Task<(IQueryable<SentEmail>?, ActionResult?)> ScopedQueryAsync(
        string? sCompanySlug, CancellationToken objCancellationToken)
    {
        AccessScope objScope = await AccessScope.LoadAsync(_objDbContext, User, objCancellationToken);
        Guid objCompanyId;
        if (objScope.IDCompany is Guid objTokenCompany)
        {
            objCompanyId = objTokenCompany;
        }
        else
        {
            if (string.IsNullOrWhiteSpace(sCompanySlug))
            {
                return (null, BadRequest(new ErrorResponse("Informe a empresa (?company=slug).")));
            }
            Guid? objId = await _objDbContext.Companies.AsNoTracking()
                .Where(company => company.Slug == sCompanySlug)
                .Select(company => (Guid?)company.Id)
                .FirstOrDefaultAsync(objCancellationToken);
            if (objId is null)
            {
                return (null, NotFound(new ErrorResponse("Empresa não encontrada.")));
            }
            objCompanyId = objId.Value;
        }

        IQueryable<SentEmail> objQuery = _objDbContext.SentEmails.AsNoTracking()
            .Where(email => email.IDCompany == objCompanyId);
        if (objScope.IsUnitRestricted)
        {
            Guid[] arrUnidades = objScope.UnitIds;
            objQuery = objQuery.Where(email => email.IDUnit != null && arrUnidades.Contains(email.IDUnit.Value));
        }
        return (objQuery, null);
    }

    private async Task<(SentEmail?, ActionResult?)> FindAsync(
        Guid id, string? sCompanySlug, CancellationToken objCancellationToken)
    {
        (IQueryable<SentEmail>? objQuery, ActionResult? objError) = await ScopedQueryAsync(sCompanySlug, objCancellationToken);
        if (objQuery is null)
        {
            return (null, objError);
        }
        SentEmail? objEmail = await objQuery.FirstOrDefaultAsync(email => email.Id == id, objCancellationToken);
        return objEmail is null
            ? (null, NotFound(new ErrorResponse("E-mail não encontrado.")))
            : (objEmail, null);
    }
}
