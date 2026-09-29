using System.Text;
using AccessWifi.Api.Features;
using AccessWifi.Api.Features.Campaigns;
using AccessWifi.Api.Infrastructure.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Models.Campaigns;
using Models.DataBase;
using Models.Persistence;

namespace AccessWifi.Api.Controllers;

/// <summary>
/// Campanhas da empresa (D7): o admin da empresa gerencia as da própria empresa; o super admin indica
/// a empresa por ?company=slug. Só os tipos liberados para a empresa podem ser criados (D6).
/// </summary>
[ApiController]
[Route("admin/campaigns")]
[Authorize]
public class CampaignsController : ControllerBase
{
    private const int MaxNameChars = 120;
    private const int MaxPageSize = 200;

    private readonly AppDbContext _objDbContext;

    public CampaignsController(AppDbContext objDbContext)
    {
        _objDbContext = objDbContext;
    }

    // ------------------------------------------------------------------ Leitura

    /// <summary>Tipos de campanha: quais estão liberados para a empresa e quais de sistema já existem.</summary>
    [HttpGet("catalog")]
    public async Task<ActionResult<List<CampaignCatalogItemDto>>> Catalog(
        [FromQuery(Name = "company")] string? sCompanySlug, CancellationToken objCancellationToken)
    {
        (Company? objCompany, ActionResult? objError) = await ResolveCompanyAsync(sCompanySlug, objCancellationToken);
        if (objCompany is null)
        {
            return objError!;
        }

        HashSet<string> objLigados = await EnabledKindsAsync(objCompany.Id, objCancellationToken);
        Dictionary<string, Guid> objSistema = (await _objDbContext.Campaigns.AsNoTracking()
                .Where(campaign => campaign.IDCompany == objCompany.Id && campaign.Kind != CampaignKind.Filtered)
                .Select(campaign => new { campaign.Kind, campaign.Id })
                .ToListAsync(objCancellationToken))
            .GroupBy(item => item.Kind)
            .ToDictionary(group => group.Key, group => group.First().Id);

        return Ok(CampaignKind.All
            .Select(sKind => new CampaignCatalogItemDto(
                sKind, CampaignKind.Label(sKind), CampaignKind.IsSystem(sKind), objLigados.Contains(sKind),
                objSistema.TryGetValue(sKind, out Guid objId) ? objId : null))
            .ToList());
    }

    /// <summary>Campanhas da empresa, com o próximo disparo e o resultado da última execução.</summary>
    [HttpGet]
    public async Task<ActionResult<List<CampaignSummaryDto>>> GetAll(
        [FromQuery(Name = "company")] string? sCompanySlug, CancellationToken objCancellationToken)
    {
        (Company? objCompany, ActionResult? objError) = await ResolveCompanyAsync(sCompanySlug, objCancellationToken);
        if (objCompany is null)
        {
            return objError!;
        }

        HashSet<string> objLigados = await EnabledKindsAsync(objCompany.Id, objCancellationToken);
        // Cada campanha com a sua última execução (sem as "perdidas", que não rodaram).
        var objCampaigns = await _objDbContext.Campaigns.AsNoTracking()
            .Where(campaign => campaign.IDCompany == objCompany.Id)
            .Select(campaign => new
            {
                Campaign = campaign,
                LastRun = _objDbContext.CampaignRuns
                    .Where(run => run.IDCampaign == campaign.Id && run.Status != CampaignRunStatus.Missed)
                    .OrderByDescending(run => run.ScheduledFor)
                    .FirstOrDefault(),
            })
            .ToListAsync(objCancellationToken);

        return Ok(objCampaigns
            .OrderBy(item => CampaignKind.Priority(item.Campaign.Kind))
            .ThenBy(item => item.Campaign.Name)
            .Select(item =>
            {
                Campaign objCampaign = item.Campaign;
                CampaignConfig objConfig = CampaignConfig.FromJson(objCampaign.ConfigJson);
                return new CampaignSummaryDto(
                    objCampaign.Id, objCampaign.Kind, CampaignKind.Label(objCampaign.Kind), objCampaign.Name,
                    objCampaign.Status, objLigados.Contains(objCampaign.Kind), objConfig.Channel, objConfig.SendTime,
                    objCampaign.CurrentVersion, objCampaign.NextRunAt,
                    item.LastRun is null ? null : CampaignRunDto.FromEntity(item.LastRun),
                    objCampaign.UpdatedAt);
            })
            .ToList());
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<CampaignDetailDto>> Get(
        Guid id, [FromQuery(Name = "company")] string? sCompanySlug, CancellationToken objCancellationToken)
    {
        (Campaign? objCampaign, ActionResult? objError) = await FindCampaignAsync(id, sCompanySlug, objCancellationToken);
        if (objCampaign is null)
        {
            return objError!;
        }
        HashSet<string> objLigados = await EnabledKindsAsync(objCampaign.IDCompany, objCancellationToken);
        return Ok(CampaignDetailDto.FromEntity(objCampaign, objLigados.Contains(objCampaign.Kind)));
    }

    // ------------------------------------------------------------ Criar e editar

    [HttpPost]
    public async Task<ActionResult<CampaignDetailDto>> Create(
        SaveCampaignRequest objRequest, [FromQuery(Name = "company")] string? sCompanySlug,
        CancellationToken objCancellationToken)
    {
        (Company? objCompany, ActionResult? objError) = await ResolveCompanyAsync(sCompanySlug, objCancellationToken);
        if (objCompany is null)
        {
            return objError!;
        }

        string sKind = objRequest.Kind ?? "";
        if (!CampaignKind.IsValid(sKind))
        {
            return BadRequest(new ErrorResponse("Tipo de campanha inválido."));
        }
        HashSet<string> objLigados = await EnabledKindsAsync(objCompany.Id, objCancellationToken);
        if (!objLigados.Contains(sKind))
        {
            return BadRequest(new ErrorResponse(
                $"A campanha \"{CampaignKind.Label(sKind)}\" não está liberada para esta empresa."));
        }
        if (CampaignKind.IsSystem(sKind) && await _objDbContext.Campaigns.AnyAsync(
                campaign => campaign.IDCompany == objCompany.Id && campaign.Kind == sKind, objCancellationToken))
        {
            return BadRequest(new ErrorResponse(
                $"A empresa já tem a campanha \"{CampaignKind.Label(sKind)}\" — edite a existente."));
        }

        string sName = string.IsNullOrWhiteSpace(objRequest.Name) ? CampaignKind.Label(sKind) : objRequest.Name.Trim();
        string? sInvalida = ValidateSave(sKind, sName, objRequest.Config);
        if (sInvalida is not null)
        {
            return BadRequest(new ErrorResponse(sInvalida));
        }

        DateTime dtNowUtc = DateTime.UtcNow;
        Campaign objCampaign = new Campaign
        {
            IDCompany = objCompany.Id,
            Kind = sKind,
            Name = sName,
            Status = CampaignStatus.Active,
            ConfigJson = objRequest.Config.ToJson(),
            CurrentVersion = 1,
            CreatedAt = dtNowUtc,
            UpdatedAt = dtNowUtc,
        };
        CampaignScheduling.RefreshNextRun(objCampaign, CompanyTimeZone.Resolve(objCompany.TimeZone), dtNowUtc, true);
        if (objCampaign.NextRunAt is null)
        {
            return BadRequest(new ErrorResponse(SemDisparoFuturo));
        }

        (Guid? objUserId, string sUsername) = await CurrentUserAsync(objCancellationToken);
        _objDbContext.Campaigns.Add(objCampaign);
        _objDbContext.CampaignVersions.Add(new CampaignVersion
        {
            IDCampaign = objCampaign.Id,
            Number = 1,
            Name = sName,
            ConfigJson = objCampaign.ConfigJson,
            Changes = "Campanha criada",
            CreatedAt = dtNowUtc,
            IDUser = objUserId,
            Username = sUsername,
        });
        AddEvent(objCampaign.Id, null, CampaignEventAction.Created, objUserId, sUsername, dtNowUtc);
        await _objDbContext.SaveChangesAsync(objCancellationToken);

        return Ok(CampaignDetailDto.FromEntity(objCampaign, true));
    }

    /// <summary>
    /// Salva uma nova versão (D13): a execução que já começou continua com a versão dela. Sem nenhuma
    /// mudança, não cria versão.
    /// </summary>
    [HttpPut("{id:guid}")]
    public async Task<ActionResult<CampaignDetailDto>> Update(
        Guid id, SaveCampaignRequest objRequest, [FromQuery(Name = "company")] string? sCompanySlug,
        CancellationToken objCancellationToken)
    {
        (Campaign? objCampaign, ActionResult? objError) =
            await FindCampaignAsync(id, sCompanySlug, objCancellationToken, bTracking: true);
        if (objCampaign is null)
        {
            return objError!;
        }

        string sName = string.IsNullOrWhiteSpace(objRequest.Name) ? objCampaign.Name : objRequest.Name.Trim();
        string? sInvalida = ValidateSave(objCampaign.Kind, sName, objRequest.Config);
        if (sInvalida is not null)
        {
            return BadRequest(new ErrorResponse(sInvalida));
        }

        HashSet<string> objLigados = await EnabledKindsAsync(objCampaign.IDCompany, objCancellationToken);
        bool bLigado = objLigados.Contains(objCampaign.Kind);
        CampaignConfig objAntiga = CampaignConfig.FromJson(objCampaign.ConfigJson);
        string sMudancas = CampaignChanges.Describe(objCampaign.Name, objAntiga, sName, objRequest.Config);
        if (sMudancas.Length == 0)
        {
            return Ok(CampaignDetailDto.FromEntity(objCampaign, bLigado));
        }

        DateTime dtNowUtc = DateTime.UtcNow;
        string sTimeZone = await _objDbContext.Companies.AsNoTracking()
            .Where(company => company.Id == objCampaign.IDCompany)
            .Select(company => company.TimeZone)
            .FirstAsync(objCancellationToken);

        objCampaign.Name = sName;
        objCampaign.ConfigJson = objRequest.Config.ToJson();
        if (objCampaign.Status == CampaignStatus.Finished)
        {
            // Encerrada com agenda nova (ex.: outra data): volta a valer.
            objCampaign.Status = CampaignStatus.Active;
        }
        CampaignScheduling.RefreshNextRun(objCampaign, CompanyTimeZone.Resolve(sTimeZone), dtNowUtc, bLigado);
        if (objCampaign.Status == CampaignStatus.Finished)
        {
            return BadRequest(new ErrorResponse(SemDisparoFuturo));
        }

        objCampaign.CurrentVersion++;
        objCampaign.UpdatedAt = dtNowUtc;
        (Guid? objUserId, string sUsername) = await CurrentUserAsync(objCancellationToken);
        _objDbContext.CampaignVersions.Add(new CampaignVersion
        {
            IDCampaign = objCampaign.Id,
            Number = objCampaign.CurrentVersion,
            Name = sName,
            ConfigJson = objCampaign.ConfigJson,
            Changes = sMudancas.Length <= 2000 ? sMudancas : sMudancas[..2000],
            CreatedAt = dtNowUtc,
            IDUser = objUserId,
            Username = sUsername,
        });
        AddEvent(objCampaign.Id, null, CampaignEventAction.Edited, objUserId, sUsername, dtNowUtc);
        await _objDbContext.SaveChangesAsync(objCancellationToken);

        return Ok(CampaignDetailDto.FromEntity(objCampaign, bLigado));
    }

    /// <summary>Pausar a campanha impede novos disparos; uma execução em andamento segue (pause-a à parte).</summary>
    [HttpPost("{id:guid}/pause")]
    public Task<ActionResult<CampaignDetailDto>> Pause(
        Guid id, [FromQuery(Name = "company")] string? sCompanySlug, CancellationToken objCancellationToken) =>
        SetCampaignStatusAsync(id, sCompanySlug, CampaignStatus.Paused, objCancellationToken);

    [HttpPost("{id:guid}/resume")]
    public Task<ActionResult<CampaignDetailDto>> Resume(
        Guid id, [FromQuery(Name = "company")] string? sCompanySlug, CancellationToken objCancellationToken) =>
        SetCampaignStatusAsync(id, sCompanySlug, CampaignStatus.Active, objCancellationToken);

    /// <summary>Prévia de alcance: quantos clientes a campanha pegaria hoje e uma mensagem de exemplo.</summary>
    [HttpPost("preview")]
    public async Task<ActionResult<AudiencePreviewDto>> Preview(
        AudiencePreviewRequest objRequest, [FromQuery(Name = "company")] string? sCompanySlug,
        CancellationToken objCancellationToken)
    {
        (Company? objCompany, ActionResult? objError) = await ResolveCompanyAsync(sCompanySlug, objCancellationToken);
        if (objCompany is null)
        {
            return objError!;
        }
        if (!CampaignKind.IsValid(objRequest.Kind))
        {
            return BadRequest(new ErrorResponse("Tipo de campanha inválido."));
        }

        DateTime dtNowUtc = DateTime.UtcNow;
        DateOnly dtHoje = CompanyTimeZone.Today(CompanyTimeZone.Resolve(objCompany.TimeZone), dtNowUtc);
        Guid? objCampaignId = objRequest.CampaignId is Guid objId && await _objDbContext.Campaigns.AnyAsync(
            campaign => campaign.Id == objId && campaign.IDCompany == objCompany.Id, objCancellationToken)
            ? objId
            : null;

        IQueryable<Customer> objAudience = CampaignAudience.Query(
            _objDbContext, objCompany.Id, objCampaignId, objRequest.Kind, objRequest.Config, dtHoje, dtNowUtc);
        int iCount = await objAudience.CountAsync(objCancellationToken);
        Customer? objExemplo = await objAudience.AsNoTracking()
            .OrderByDescending(customer => customer.LastVisitAt)
            .FirstOrDefaultAsync(objCancellationToken);

        string? sMensagem = null;
        if (objExemplo is not null)
        {
            string sUnidade = objExemplo.IDLastUnit is Guid objUnitId
                ? await _objDbContext.Units.AsNoTracking()
                    .Where(unit => unit.Id == objUnitId).Select(unit => unit.Name)
                    .FirstOrDefaultAsync(objCancellationToken) ?? ""
                : "";
            sMensagem = CampaignMessage.Render(objRequest.Config.Message ?? "", new CampaignMessageData(
                objExemplo.Name, objCompany.Name, sUnidade,
                CampaignMessage.AgeOn(objExemplo.BirthDate, dtHoje),
                CampaignMessage.FullYearsBetween(objExemplo.FirstVisitDate, dtHoje)));
        }

        return Ok(new AudiencePreviewDto(iCount, dtHoje, objExemplo?.Name, sMensagem));
    }

    // -------------------------------------------------------------- Histórico

    /// <summary>Versões da mais nova para a mais antiga, com quem salvou e o que mudou.</summary>
    [HttpGet("{id:guid}/versions")]
    public async Task<ActionResult<List<CampaignVersionDto>>> Versions(
        Guid id, [FromQuery(Name = "company")] string? sCompanySlug, CancellationToken objCancellationToken)
    {
        (Campaign? objCampaign, ActionResult? objError) = await FindCampaignAsync(id, sCompanySlug, objCancellationToken);
        if (objCampaign is null)
        {
            return objError!;
        }

        List<CampaignVersion> objVersions = await _objDbContext.CampaignVersions.AsNoTracking()
            .Where(version => version.IDCampaign == id)
            .OrderByDescending(version => version.Number)
            .ToListAsync(objCancellationToken);
        return Ok(objVersions
            .Select(version => new CampaignVersionDto(
                version.Number, version.Name, version.Changes, version.CreatedAt, version.Username,
                CampaignConfig.FromJson(version.ConfigJson)))
            .ToList());
    }

    /// <summary>Ações (criar, editar, pausar, retomar, cancelar execução), da mais nova para a mais antiga.</summary>
    [HttpGet("{id:guid}/events")]
    public async Task<ActionResult<List<CampaignEventDto>>> Events(
        Guid id, [FromQuery(Name = "company")] string? sCompanySlug, CancellationToken objCancellationToken)
    {
        (Campaign? objCampaign, ActionResult? objError) = await FindCampaignAsync(id, sCompanySlug, objCancellationToken);
        if (objCampaign is null)
        {
            return objError!;
        }

        return Ok(await _objDbContext.CampaignEvents.AsNoTracking()
            .Where(evt => evt.IDCampaign == id)
            .OrderByDescending(evt => evt.CreatedAt)
            .Take(500)
            .Select(evt => new CampaignEventDto(evt.Action, evt.CreatedAt, evt.Username, evt.IDRun))
            .ToListAsync(objCancellationToken));
    }

    // -------------------------------------------------------------- Execuções

    [HttpGet("{id:guid}/runs")]
    public async Task<ActionResult<List<CampaignRunDto>>> Runs(
        Guid id, [FromQuery(Name = "company")] string? sCompanySlug, CancellationToken objCancellationToken)
    {
        (Campaign? objCampaign, ActionResult? objError) = await FindCampaignAsync(id, sCompanySlug, objCancellationToken);
        if (objCampaign is null)
        {
            return objError!;
        }

        List<CampaignRun> objRuns = await _objDbContext.CampaignRuns.AsNoTracking()
            .Where(run => run.IDCampaign == id)
            .OrderByDescending(run => run.ScheduledFor)
            .Take(200)
            .ToListAsync(objCancellationToken);
        return Ok(objRuns.Select(CampaignRunDto.FromEntity).ToList());
    }

    /// <summary>Uma execução — a tela consulta de poucos em poucos segundos para a barra de progresso.</summary>
    [HttpGet("runs/{runId:guid}")]
    public async Task<ActionResult<CampaignRunDto>> Run(
        Guid runId, [FromQuery(Name = "company")] string? sCompanySlug, CancellationToken objCancellationToken)
    {
        (CampaignRun? objRun, ActionResult? objError) = await FindRunAsync(runId, sCompanySlug, objCancellationToken);
        return objRun is null ? objError! : Ok(CampaignRunDto.FromEntity(objRun));
    }

    [HttpGet("runs/{runId:guid}/recipients")]
    public async Task<ActionResult<PagedDto<CampaignRecipientDto>>> Recipients(
        Guid runId,
        [FromQuery(Name = "company")] string? sCompanySlug,
        [FromQuery(Name = "status")] string? sStatus,
        [FromQuery(Name = "page")] int iPage,
        [FromQuery(Name = "pageSize")] int iPageSize,
        CancellationToken objCancellationToken)
    {
        (CampaignRun? objRun, ActionResult? objError) = await FindRunAsync(runId, sCompanySlug, objCancellationToken);
        if (objRun is null)
        {
            return objError!;
        }

        iPage = Math.Max(1, iPage);
        iPageSize = iPageSize <= 0 ? 50 : Math.Min(iPageSize, MaxPageSize);
        IQueryable<CampaignRecipient> objQuery = _objDbContext.CampaignRecipients.AsNoTracking()
            .Where(recipient => recipient.IDRun == runId);
        if (!string.IsNullOrWhiteSpace(sStatus))
        {
            objQuery = objQuery.Where(recipient => recipient.Status == sStatus);
        }

        int iTotal = await objQuery.CountAsync(objCancellationToken);
        List<CampaignRecipientDto> objItems = await objQuery
            .OrderBy(recipient => recipient.Id)
            .Skip((iPage - 1) * iPageSize)
            .Take(iPageSize)
            .Select(recipient => new CampaignRecipientDto(
                recipient.Id, recipient.Phone, recipient.Name, recipient.Message, recipient.Status,
                recipient.Reason, recipient.Milestone, recipient.ProcessedAt))
            .ToListAsync(objCancellationToken);
        return Ok(new PagedDto<CampaignRecipientDto>(objItems, iTotal, iPage, iPageSize));
    }

    /// <summary>Todos os destinatários da execução em CSV (abre no Excel: BOM + CRLF).</summary>
    [HttpGet("runs/{runId:guid}/recipients.csv")]
    public async Task<IActionResult> RecipientsCsv(
        Guid runId, [FromQuery(Name = "company")] string? sCompanySlug, CancellationToken objCancellationToken)
    {
        (CampaignRun? objRun, ActionResult? objError) = await FindRunAsync(runId, sCompanySlug, objCancellationToken);
        if (objRun is null)
        {
            return objError!;
        }

        List<CampaignRecipient> objRecipients = await _objDbContext.CampaignRecipients.AsNoTracking()
            .Where(recipient => recipient.IDRun == runId)
            .OrderBy(recipient => recipient.Id)
            .ToListAsync(objCancellationToken);

        StringBuilder objCsv = new StringBuilder();
        objCsv.Append("telefone,nome,situacao,motivo,processado_em,mensagem\r\n");
        foreach (CampaignRecipient objRecipient in objRecipients)
        {
            objCsv.Append(string.Join(',',
                Csv(objRecipient.Phone), Csv(objRecipient.Name), Csv(StatusLabel(objRecipient.Status)),
                Csv(objRecipient.Reason ?? ""), Csv(objRecipient.ProcessedAt?.ToString("yyyy-MM-dd HH:mm:ss") ?? ""),
                Csv(objRecipient.Message)));
            objCsv.Append("\r\n");
        }

        byte[] arrBytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(objCsv.ToString())).ToArray();
        return File(arrBytes, "text/csv; charset=utf-8", $"campanha-execucao-{objRun.LocalDate:yyyy-MM-dd}.csv");
    }

    /// <summary>Pausa no fim do lote atual; retomar continua dos pendentes.</summary>
    [HttpPost("runs/{runId:guid}/pause")]
    public Task<ActionResult<CampaignRunDto>> PauseRun(
        Guid runId, [FromQuery(Name = "company")] string? sCompanySlug, CancellationToken objCancellationToken) =>
        SetRunStatusAsync(runId, sCompanySlug, CampaignRunStatus.Paused, objCancellationToken);

    [HttpPost("runs/{runId:guid}/resume")]
    public Task<ActionResult<CampaignRunDto>> ResumeRun(
        Guid runId, [FromQuery(Name = "company")] string? sCompanySlug, CancellationToken objCancellationToken) =>
        SetRunStatusAsync(runId, sCompanySlug, CampaignRunStatus.Running, objCancellationToken);

    /// <summary>Cancela: os pendentes viram "cancelado" (o serviço conclui em segundos).</summary>
    [HttpPost("runs/{runId:guid}/cancel")]
    public Task<ActionResult<CampaignRunDto>> CancelRun(
        Guid runId, [FromQuery(Name = "company")] string? sCompanySlug, CancellationToken objCancellationToken) =>
        SetRunStatusAsync(runId, sCompanySlug, CampaignRunStatus.Cancelled, objCancellationToken);

    // ---------------------------------------------------------------- Apoio

    private const string SemDisparoFuturo =
        "Essa agenda não tem nenhum disparo daqui para frente — confira a data de início, a de fim e o horário.";

    private static string? ValidateSave(string sKind, string sName, CampaignConfig? objConfig)
    {
        if (sName.Length > MaxNameChars)
        {
            return $"O nome pode ter no máximo {MaxNameChars} caracteres.";
        }
        if (objConfig is null)
        {
            return "Configuração da campanha ausente.";
        }
        return CampaignConfigValidator.Validate(sKind, objConfig);
    }

    private async Task<ActionResult<CampaignDetailDto>> SetCampaignStatusAsync(
        Guid id, string? sCompanySlug, string sStatus, CancellationToken objCancellationToken)
    {
        (Campaign? objCampaign, ActionResult? objError) =
            await FindCampaignAsync(id, sCompanySlug, objCancellationToken, bTracking: true);
        if (objCampaign is null)
        {
            return objError!;
        }

        HashSet<string> objLigados = await EnabledKindsAsync(objCampaign.IDCompany, objCancellationToken);
        bool bLigado = objLigados.Contains(objCampaign.Kind);
        bool bPausar = sStatus == CampaignStatus.Paused;
        if (bPausar ? objCampaign.Status != CampaignStatus.Active : objCampaign.Status != CampaignStatus.Paused)
        {
            return BadRequest(new ErrorResponse(bPausar
                ? "Só dá para pausar uma campanha ativa."
                : "Só dá para retomar uma campanha pausada."));
        }

        DateTime dtNowUtc = DateTime.UtcNow;
        string sTimeZone = await _objDbContext.Companies.AsNoTracking()
            .Where(company => company.Id == objCampaign.IDCompany)
            .Select(company => company.TimeZone)
            .FirstAsync(objCancellationToken);
        objCampaign.Status = sStatus;
        objCampaign.UpdatedAt = dtNowUtc;
        // Retomar recalcula a partir de agora: o que venceu enquanto estava pausada não dispara.
        CampaignScheduling.RefreshNextRun(objCampaign, CompanyTimeZone.Resolve(sTimeZone), dtNowUtc, bLigado);

        (Guid? objUserId, string sUsername) = await CurrentUserAsync(objCancellationToken);
        AddEvent(objCampaign.Id, null, bPausar ? CampaignEventAction.Paused : CampaignEventAction.Resumed,
            objUserId, sUsername, dtNowUtc);
        await _objDbContext.SaveChangesAsync(objCancellationToken);
        return Ok(CampaignDetailDto.FromEntity(objCampaign, bLigado));
    }

    private async Task<ActionResult<CampaignRunDto>> SetRunStatusAsync(
        Guid runId, string? sCompanySlug, string sStatus, CancellationToken objCancellationToken)
    {
        (CampaignRun? objRun, ActionResult? objError) =
            await FindRunAsync(runId, sCompanySlug, objCancellationToken, bTracking: true);
        if (objRun is null)
        {
            return objError!;
        }

        string? sProibido = (sStatus, objRun.Status) switch
        {
            (CampaignRunStatus.Paused, CampaignRunStatus.Running) => null,
            (CampaignRunStatus.Paused, _) => "Só dá para pausar uma execução em andamento.",
            (CampaignRunStatus.Running, CampaignRunStatus.Paused) => null,
            (CampaignRunStatus.Running, _) => "Só dá para retomar uma execução pausada.",
            (CampaignRunStatus.Cancelled, CampaignRunStatus.Selecting or CampaignRunStatus.Running or CampaignRunStatus.Paused) => null,
            (CampaignRunStatus.Cancelled, _) => "Esta execução já terminou.",
            _ => "Ação inválida.",
        };
        if (sProibido is not null)
        {
            return BadRequest(new ErrorResponse(sProibido));
        }

        DateTime dtNowUtc = DateTime.UtcNow;
        objRun.Status = sStatus;
        if (sStatus == CampaignRunStatus.Cancelled)
        {
            objRun.FinishedAt = dtNowUtc;
        }

        (Guid? objUserId, string sUsername) = await CurrentUserAsync(objCancellationToken);
        string sAction = sStatus switch
        {
            CampaignRunStatus.Paused => CampaignEventAction.RunPaused,
            CampaignRunStatus.Running => CampaignEventAction.RunResumed,
            _ => CampaignEventAction.RunCancelled,
        };
        AddEvent(objRun.IDCampaign, objRun.Id, sAction, objUserId, sUsername, dtNowUtc);
        await _objDbContext.SaveChangesAsync(objCancellationToken);
        return Ok(CampaignRunDto.FromEntity(objRun));
    }

    /// <summary>Empresa em contexto: a do token (admin) ou a de ?company=slug (super admin).</summary>
    private async Task<(Company?, ActionResult?)> ResolveCompanyAsync(
        string? sCompanySlug, CancellationToken objCancellationToken)
    {
        Guid? objTokenCompanyId = User.GetCompanyId();
        Company? objCompany;
        if (objTokenCompanyId is not null)
        {
            objCompany = await _objDbContext.Companies.AsNoTracking()
                .FirstOrDefaultAsync(company => company.Id == objTokenCompanyId, objCancellationToken);
        }
        else
        {
            if (string.IsNullOrWhiteSpace(sCompanySlug))
            {
                return (null, BadRequest(new ErrorResponse("Informe a empresa (?company=slug).")));
            }
            objCompany = await _objDbContext.Companies.AsNoTracking()
                .FirstOrDefaultAsync(company => company.Slug == sCompanySlug, objCancellationToken);
        }

        return objCompany is null
            ? (null, NotFound(new ErrorResponse("Empresa não encontrada.")))
            : (objCompany, null);
    }

    private async Task<(Campaign?, ActionResult?)> FindCampaignAsync(
        Guid id, string? sCompanySlug, CancellationToken objCancellationToken, bool bTracking = false)
    {
        (Company? objCompany, ActionResult? objError) = await ResolveCompanyAsync(sCompanySlug, objCancellationToken);
        if (objCompany is null)
        {
            return (null, objError);
        }

        IQueryable<Campaign> objQuery = bTracking ? _objDbContext.Campaigns : _objDbContext.Campaigns.AsNoTracking();
        Campaign? objCampaign = await objQuery.FirstOrDefaultAsync(
            campaign => campaign.Id == id && campaign.IDCompany == objCompany.Id, objCancellationToken);
        return objCampaign is null
            ? (null, NotFound(new ErrorResponse("Campanha não encontrada.")))
            : (objCampaign, null);
    }

    private async Task<(CampaignRun?, ActionResult?)> FindRunAsync(
        Guid runId, string? sCompanySlug, CancellationToken objCancellationToken, bool bTracking = false)
    {
        (Company? objCompany, ActionResult? objError) = await ResolveCompanyAsync(sCompanySlug, objCancellationToken);
        if (objCompany is null)
        {
            return (null, objError);
        }

        IQueryable<CampaignRun> objQuery = bTracking ? _objDbContext.CampaignRuns : _objDbContext.CampaignRuns.AsNoTracking();
        CampaignRun? objRun = await objQuery.FirstOrDefaultAsync(
            run => run.Id == runId && run.IDCompany == objCompany.Id, objCancellationToken);
        return objRun is null
            ? (null, NotFound(new ErrorResponse("Execução não encontrada.")))
            : (objRun, null);
    }

    private async Task<HashSet<string>> EnabledKindsAsync(Guid objCompanyId, CancellationToken objCancellationToken) =>
        (await _objDbContext.CompanyCampaignKinds.AsNoTracking()
            .Where(kind => kind.IDCompany == objCompanyId)
            .Select(kind => kind.Kind)
            .ToListAsync(objCancellationToken))
        .ToHashSet();

    /// <summary>Quem está fazendo a ação, para o histórico (o token traz o usuário; o id vem do banco).</summary>
    private async Task<(Guid?, string)> CurrentUserAsync(CancellationToken objCancellationToken)
    {
        string sUsername = User.GetUsername() ?? "";
        Guid? objUserId = await _objDbContext.Users.AsNoTracking()
            .Where(user => user.Username == sUsername)
            .Select(user => (Guid?)user.Id)
            .FirstOrDefaultAsync(objCancellationToken);
        return (objUserId, sUsername);
    }

    private void AddEvent(
        Guid objCampaignId, Guid? objRunId, string sAction, Guid? objUserId, string sUsername, DateTime dtNowUtc)
    {
        _objDbContext.CampaignEvents.Add(new CampaignEvent
        {
            IDCampaign = objCampaignId,
            IDRun = objRunId,
            Action = sAction,
            CreatedAt = dtNowUtc,
            IDUser = objUserId,
            Username = sUsername,
        });
    }

    private static string Csv(string sValue) => "\"" + sValue.Replace("\"", "\"\"") + "\"";

    private static string StatusLabel(string sStatus) => sStatus switch
    {
        CampaignRecipientStatus.Pending => "pendente",
        CampaignRecipientStatus.Sent => "enviado",
        CampaignRecipientStatus.Simulated => "simulado",
        CampaignRecipientStatus.Failed => "falhou",
        CampaignRecipientStatus.Ignored => "ignorado",
        CampaignRecipientStatus.Cancelled => "cancelado",
        _ => sStatus,
    };
}
