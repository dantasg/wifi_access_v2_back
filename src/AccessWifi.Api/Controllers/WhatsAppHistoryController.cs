using AccessWifi.Api.Features;
using AccessWifi.Api.Features.Campaigns;
using AccessWifi.Api.Features.WhatsApp;
using AccessWifi.Api.Infrastructure.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Models.Campaigns;
using Models.DataBase;
using Models.Persistence;

namespace AccessWifi.Api.Controllers;

/// <summary>
/// O registro de tudo do WhatsApp na tela (PROPOSTA_WHATSAPP.md §15.3, D15), só leitura:
/// <list type="bullet">
/// <item>Conversas: os clientes com a última mensagem e, de cada um, o histórico separado por campanha (D14);</item>
/// <item>Registro: tudo, enviado e recebido, com filtros; e o detalhe de uma mensagem enviada;</item>
/// <item>Conta: o histórico da conta da empresa (só admin da empresa e super admin, como na D3).</item>
/// </list>
/// O usuário de unidade vê Conversas e Registro só dos clientes das unidades dele (a da última visita).
/// </summary>
[ApiController]
[Route("admin/whatsapp")]
[Authorize]
public class WhatsAppHistoryController : ControllerBase
{
    private const int MaxPageSize = 100;

    private readonly AppDbContext _objDbContext;

    public WhatsAppHistoryController(AppDbContext objDbContext)
    {
        _objDbContext = objDbContext;
    }

    // -------------------------------------------------------------------- Conversas

    /// <summary>Clientes com mensagens no WhatsApp, da conversa mais recente para a mais antiga. Busca por nome ou telefone.</summary>
    [HttpGet("conversations")]
    public async Task<ActionResult<PagedDto<WhatsAppConversationDto>>> GetConversations(
        [FromQuery(Name = "company")] string? sCompanySlug,
        [FromQuery(Name = "search")] string? sSearch,
        [FromQuery(Name = "page")] int iPage,
        [FromQuery(Name = "pageSize")] int iPageSize,
        CancellationToken objCancellationToken)
    {
        (Company? objCompany, AccessScope? objScope, ActionResult? objError) =
            await LoadAsync(sCompanySlug, bAdminOnly: false, objCancellationToken);
        if (objCompany is null || objScope is null)
        {
            return objError!;
        }
        (iPage, iPageSize) = Paging(iPage, iPageSize, 30);

        var objSent = await _objDbContext.WhatsAppMessages.AsNoTracking()
            .Where(message => message.IDCompany == objCompany.Id && message.IDCustomer != null)
            .GroupBy(message => message.IDCustomer)
            .Select(group => new { Id = group.Key!.Value, Last = group.Max(message => message.CreatedAt), Count = group.Count() })
            .ToListAsync(objCancellationToken);
        var objReceived = await _objDbContext.WhatsAppInboundMessages.AsNoTracking()
            .Where(inbound => inbound.IDCompany == objCompany.Id)
            .GroupBy(inbound => inbound.IDCustomer)
            .Select(group => new { Id = group.Key, Last = group.Max(inbound => inbound.SentAt), Count = group.Count() })
            .ToListAsync(objCancellationToken);
        Dictionary<Guid, (DateTime dtLast, int iSent, int iReplies)> objActivity = [];
        foreach (var objItem in objSent)
        {
            objActivity[objItem.Id] = (objItem.Last, objItem.Count, 0);
        }
        foreach (var objItem in objReceived)
        {
            (DateTime dtLast, int iSent, int iReplies) = objActivity.GetValueOrDefault(objItem.Id);
            objActivity[objItem.Id] = (objItem.Last > dtLast ? objItem.Last : dtLast, iSent, objItem.Count);
        }

        List<Guid> objIds = [.. objActivity.Keys];
        IQueryable<Customer> objCustomers = CustomersQuery(objCompany, objScope).Where(customer => objIds.Contains(customer.Id));
        string sTerm = (sSearch ?? "").Trim();
        if (sTerm.Length > 0)
        {
            string sDigits = new string(sTerm.Where(char.IsAsciiDigit).ToArray());
            string sLower = sTerm.ToLower();
            objCustomers = objCustomers.Where(customer => customer.Name.ToLower().Contains(sLower)
                || (sDigits.Length >= 3 && customer.Phone.Contains(sDigits)));
        }
        var objRows = (await objCustomers
                .Select(customer => new { customer.Id, customer.Name, customer.Phone, customer.IDLastUnit, customer.WhatsAppOptOutAt })
                .ToListAsync(objCancellationToken))
            .OrderByDescending(customer => objActivity[customer.Id].dtLast)
            .ToList();
        var objPage = objRows.Skip((iPage - 1) * iPageSize).Take(iPageSize).ToList();

        // A última mensagem de cada cliente da página, de qualquer lado.
        List<Guid> objPageIds = objPage.Select(customer => customer.Id).ToList();
        Dictionary<Guid, WhatsAppMessage> objLastSent = (await _objDbContext.WhatsAppMessages.AsNoTracking()
                .Where(message => message.IDCompany == objCompany.Id && message.IDCustomer != null
                    && objPageIds.Contains(message.IDCustomer.Value))
                .ToListAsync(objCancellationToken))
            .GroupBy(message => message.IDCustomer!.Value)
            .ToDictionary(group => group.Key, group => group.OrderByDescending(message => message.CreatedAt).First());
        Dictionary<Guid, WhatsAppInboundMessage> objLastReceived = (await _objDbContext.WhatsAppInboundMessages.AsNoTracking()
                .Where(inbound => inbound.IDCompany == objCompany.Id && objPageIds.Contains(inbound.IDCustomer))
                .ToListAsync(objCancellationToken))
            .GroupBy(inbound => inbound.IDCustomer)
            .ToDictionary(group => group.Key, group => group.OrderByDescending(inbound => inbound.SentAt).First());
        Dictionary<Guid, string> objUnits = await UnitNamesAsync(objCompany.Id, objCancellationToken);

        List<WhatsAppConversationDto> objItems = objPage
            .Select(customer =>
            {
                WhatsAppMessage? objOut = objLastSent.GetValueOrDefault(customer.Id);
                WhatsAppInboundMessage? objIn = objLastReceived.GetValueOrDefault(customer.Id);
                bool bLastIn = objIn is not null && (objOut is null || objIn.SentAt >= objOut.CreatedAt);
                (DateTime dtLast, int iSent, int iReplies) = objActivity[customer.Id];
                return new WhatsAppConversationDto(
                    customer.Id, customer.Name, customer.Phone, UnitName(objUnits, customer.IDLastUnit), dtLast,
                    bLastIn ? "in" : "out",
                    Truncate(bLastIn ? objIn!.Text : objOut?.Text ?? "", 160),
                    bLastIn ? "" : objOut?.Status ?? "",
                    customer.WhatsAppOptOutAt, iSent, iReplies, CampaignContact.WhatsAppUrl(customer.Phone, null));
            })
            .ToList();
        return Ok(new PagedDto<WhatsAppConversationDto>(objItems, objRows.Count, iPage, iPageSize));
    }

    /// <summary>
    /// O histórico completo de um cliente: cada mensagem enviada, com a situação, e embaixo as respostas que vieram até
    /// a mensagem seguinte (D14).
    /// </summary>
    [HttpGet("conversations/{customerId:guid}")]
    public async Task<ActionResult<WhatsAppConversationDetailDto>> GetConversation(
        Guid customerId, [FromQuery(Name = "company")] string? sCompanySlug, CancellationToken objCancellationToken)
    {
        (Company? objCompany, AccessScope? objScope, ActionResult? objError) =
            await LoadAsync(sCompanySlug, bAdminOnly: false, objCancellationToken);
        if (objCompany is null || objScope is null)
        {
            return objError!;
        }
        Customer? objCustomer = await CustomersQuery(objCompany, objScope)
            .FirstOrDefaultAsync(customer => customer.Id == customerId, objCancellationToken);
        if (objCustomer is null)
        {
            return NotFound(new ErrorResponse("Cliente não encontrado."));
        }

        List<WhatsAppMessage> objSent = await _objDbContext.WhatsAppMessages.AsNoTracking()
            .Where(message => message.IDCompany == objCompany.Id && message.IDCustomer == customerId)
            .OrderBy(message => message.CreatedAt)
            .ToListAsync(objCancellationToken);
        List<WhatsAppInboundMessage> objReceived = await _objDbContext.WhatsAppInboundMessages.AsNoTracking()
            .Where(inbound => inbound.IDCompany == objCompany.Id && inbound.IDCustomer == customerId)
            .OrderBy(inbound => inbound.SentAt)
            .ToListAsync(objCancellationToken);
        (Dictionary<Guid, string> objCampaigns, Dictionary<Guid, DateOnly> objRuns) =
            await CampaignNamesAsync(objSent.Select(message => (message.IDCampaign, message.IDCampaignRun)), objCancellationToken);

        static WhatsAppReplyItemDto Reply(WhatsAppInboundMessage objInbound) => new WhatsAppReplyItemDto(
            objInbound.Id, objInbound.Text, objInbound.SentAt, objInbound.IsOptOut, objInbound.ProfileName);

        HashSet<long> objSentIds = objSent.Select(message => message.Id).ToHashSet();
        List<WhatsAppConversationGroupDto> objGroups = [];
        // Respostas sem campanha antes (o cliente escreveu antes de receber qualquer campanha).
        List<WhatsAppReplyItemDto> objLoose = objReceived
            .Where(inbound => inbound.IDWhatsAppMessage is not long lId || !objSentIds.Contains(lId))
            .Select(Reply)
            .ToList();
        if (objLoose.Count > 0)
        {
            objGroups.Add(new WhatsAppConversationGroupDto(null, objLoose));
        }
        foreach (WhatsAppMessage objMessage in objSent)
        {
            objGroups.Add(new WhatsAppConversationGroupDto(
                Sent(objMessage, objCampaigns, objRuns),
                objReceived.Where(inbound => inbound.IDWhatsAppMessage == objMessage.Id).Select(Reply).ToList()));
        }

        Dictionary<Guid, string> objUnits = await UnitNamesAsync(objCompany.Id, objCancellationToken);
        return Ok(new WhatsAppConversationDetailDto(
            objCustomer.Id, objCustomer.Name, objCustomer.Phone, UnitName(objUnits, objCustomer.IDLastUnit),
            objCustomer.WhatsAppOptOutAt, CampaignContact.WhatsAppUrl(objCustomer.Phone, null), objGroups));
    }

    // -------------------------------------------------------------------- Registro

    /// <summary>
    /// Tudo, enviado e recebido, do mais novo para o mais antigo, com filtros: período (dias no fuso da empresa; sem
    /// período, os últimos 30 dias), direção (out/in), situação (a da mensagem enviada, ou OptOut para os "SAIR"),
    /// campanha, unidade (a da última visita do cliente) e telefone.
    /// </summary>
    [HttpGet("log")]
    public async Task<ActionResult<PagedDto<WhatsAppLogItemDto>>> GetLog(
        [FromQuery(Name = "company")] string? sCompanySlug,
        [FromQuery(Name = "from")] DateOnly? dtFrom,
        [FromQuery(Name = "to")] DateOnly? dtTo,
        [FromQuery(Name = "direction")] string? sDirection,
        [FromQuery(Name = "status")] string? sStatus,
        [FromQuery(Name = "campaign")] Guid? objCampaignId,
        [FromQuery(Name = "unit")] Guid? objUnitId,
        [FromQuery(Name = "phone")] string? sPhone,
        [FromQuery(Name = "page")] int iPage,
        [FromQuery(Name = "pageSize")] int iPageSize,
        CancellationToken objCancellationToken)
    {
        (Company? objCompany, AccessScope? objScope, ActionResult? objError) =
            await LoadAsync(sCompanySlug, bAdminOnly: false, objCancellationToken);
        if (objCompany is null || objScope is null)
        {
            return objError!;
        }
        (iPage, iPageSize) = Paging(iPage, iPageSize, 50);

        TimeZoneInfo objZone = CompanyTimeZone.Resolve(objCompany.TimeZone);
        DateOnly dtToday = CompanyTimeZone.Today(objZone, DateTime.UtcNow);
        DateOnly dtStart = dtFrom ?? dtToday.AddDays(-30);
        DateOnly dtEnd = dtTo ?? dtToday;
        if (dtEnd < dtStart)
        {
            return BadRequest(new ErrorResponse("O fim do período não pode ser antes do começo."));
        }
        DateTime dtStartUtc = TimeZoneInfo.ConvertTimeToUtc(dtStart.ToDateTime(TimeOnly.MinValue), objZone);
        DateTime dtEndUtc = TimeZoneInfo.ConvertTimeToUtc(dtEnd.AddDays(1).ToDateTime(TimeOnly.MinValue), objZone);
        string sDigits = new string((sPhone ?? "").Where(char.IsAsciiDigit).ToArray());

        // Unidade (filtro ou restrição do usuário): pelos clientes dela.
        bool bByCustomer = objScope.IsUnitRestricted || objUnitId is not null;
        IQueryable<Guid> objCustomerIds = CustomersQuery(objCompany, objScope)
            .Where(customer => objUnitId == null || customer.IDLastUnit == objUnitId)
            .Select(customer => customer.Id);

        bool bSentStatus = sStatus is WhatsAppMessageStatus.Accepted or WhatsAppMessageStatus.Sent
            or WhatsAppMessageStatus.Delivered or WhatsAppMessageStatus.Read or WhatsAppMessageStatus.Failed;
        bool bWithSent = sDirection != "in" && sStatus != "OptOut";
        bool bWithReceived = sDirection != "out" && !bSentStatus;

        IQueryable<WhatsAppMessage> objSentQuery = _objDbContext.WhatsAppMessages.AsNoTracking()
            .Where(message => message.IDCompany == objCompany.Id
                && message.CreatedAt >= dtStartUtc && message.CreatedAt < dtEndUtc);
        if (bByCustomer)
        {
            objSentQuery = objSentQuery.Where(message => message.IDCustomer != null && objCustomerIds.Contains(message.IDCustomer.Value));
        }
        if (objCampaignId is not null)
        {
            objSentQuery = objSentQuery.Where(message => message.IDCampaign == objCampaignId);
        }
        if (bSentStatus)
        {
            objSentQuery = objSentQuery.Where(message => message.Status == sStatus);
        }
        if (sDigits.Length > 0)
        {
            objSentQuery = objSentQuery.Where(message => message.ToPhone.Contains(sDigits));
        }

        IQueryable<WhatsAppInboundMessage> objReceivedQuery = _objDbContext.WhatsAppInboundMessages.AsNoTracking()
            .Where(inbound => inbound.IDCompany == objCompany.Id
                && inbound.SentAt >= dtStartUtc && inbound.SentAt < dtEndUtc);
        if (bByCustomer)
        {
            objReceivedQuery = objReceivedQuery.Where(inbound => objCustomerIds.Contains(inbound.IDCustomer));
        }
        if (objCampaignId is not null)
        {
            objReceivedQuery = objReceivedQuery.Where(inbound => inbound.IDCampaign == objCampaignId);
        }
        if (sStatus == "OptOut")
        {
            objReceivedQuery = objReceivedQuery.Where(inbound => inbound.IsOptOut);
        }
        if (sDigits.Length > 0)
        {
            objReceivedQuery = objReceivedQuery.Where(inbound => inbound.FromPhone.Contains(sDigits));
        }

        // Os mais novos de cada lado até o fim da página, juntos e na ordem.
        int iNeeded = iPage * iPageSize;
        int iTotal = 0;
        List<WhatsAppMessage> objSent = [];
        List<WhatsAppInboundMessage> objReceived = [];
        if (bWithSent)
        {
            iTotal += await objSentQuery.CountAsync(objCancellationToken);
            objSent = await objSentQuery.OrderByDescending(message => message.CreatedAt).Take(iNeeded)
                .ToListAsync(objCancellationToken);
        }
        if (bWithReceived)
        {
            iTotal += await objReceivedQuery.CountAsync(objCancellationToken);
            objReceived = await objReceivedQuery.OrderByDescending(inbound => inbound.SentAt).Take(iNeeded)
                .ToListAsync(objCancellationToken);
        }

        List<Guid> objPeople = objSent.Select(message => message.IDCustomer).OfType<Guid>()
            .Concat(objReceived.Select(inbound => inbound.IDCustomer)).Distinct().ToList();
        Dictionary<Guid, Customer> objCustomers = await _objDbContext.Customers.AsNoTracking()
            .Where(customer => objPeople.Contains(customer.Id))
            .ToDictionaryAsync(customer => customer.Id, objCancellationToken);
        (Dictionary<Guid, string> objCampaigns, _) = await CampaignNamesAsync(
            objSent.Select(message => (message.IDCampaign, (Guid?)null))
                .Concat(objReceived.Select(inbound => (inbound.IDCampaign, (Guid?)null))), objCancellationToken);
        Dictionary<Guid, string> objUnits = await UnitNamesAsync(objCompany.Id, objCancellationToken);

        List<WhatsAppLogItemDto> objItems = objSent
            .Select(message =>
            {
                Customer? objCustomer = message.IDCustomer is Guid objId ? objCustomers.GetValueOrDefault(objId) : null;
                return new WhatsAppLogItemDto(
                    "out", message.Id, message.CreatedAt, message.IDCustomer, objCustomer?.Name ?? (message.IsTest ? "Teste" : ""),
                    objCustomer?.Phone ?? message.ToPhone, UnitName(objUnits, objCustomer?.IDLastUnit),
                    message.IDCampaign is Guid objCampaign ? objCampaigns.GetValueOrDefault(objCampaign, "") : "",
                    message.Text, message.Status, message.ErrorMessage, message.IsTest, false,
                    CampaignContact.WhatsAppUrl(objCustomer?.Phone ?? message.ToPhone, null));
            })
            .Concat(objReceived.Select(inbound =>
            {
                Customer? objCustomer = objCustomers.GetValueOrDefault(inbound.IDCustomer);
                return new WhatsAppLogItemDto(
                    "in", inbound.Id, inbound.SentAt, inbound.IDCustomer, objCustomer?.Name ?? inbound.ProfileName,
                    objCustomer?.Phone ?? inbound.FromPhone, UnitName(objUnits, objCustomer?.IDLastUnit),
                    inbound.IDCampaign is Guid objCampaign ? objCampaigns.GetValueOrDefault(objCampaign, "") : "",
                    inbound.Text, "", "", false, inbound.IsOptOut,
                    CampaignContact.WhatsAppUrl(objCustomer?.Phone ?? inbound.FromPhone, null));
            }))
            .OrderByDescending(item => item.At)
            .Skip((iPage - 1) * iPageSize)
            .Take(iPageSize)
            .ToList();
        return Ok(new PagedDto<WhatsAppLogItemDto>(objItems, iTotal, iPage, iPageSize));
    }

    /// <summary>O detalhe de uma mensagem enviada: situações, cobrança, erro, pedido e resposta da Meta (sem o token).</summary>
    [HttpGet("messages/{id:long}")]
    public async Task<ActionResult<WhatsAppMessageDetailDto>> GetMessage(
        long id, [FromQuery(Name = "company")] string? sCompanySlug, CancellationToken objCancellationToken)
    {
        (Company? objCompany, AccessScope? objScope, ActionResult? objError) =
            await LoadAsync(sCompanySlug, bAdminOnly: false, objCancellationToken);
        if (objCompany is null || objScope is null)
        {
            return objError!;
        }
        WhatsAppMessage? objMessage = await _objDbContext.WhatsAppMessages.AsNoTracking()
            .FirstOrDefaultAsync(message => message.Id == id && message.IDCompany == objCompany.Id, objCancellationToken);
        Customer? objCustomer = objMessage?.IDCustomer is Guid objCustomerId
            ? await CustomersQuery(objCompany, objScope).FirstOrDefaultAsync(customer => customer.Id == objCustomerId,
                objCancellationToken)
            : null;
        // Usuário de unidade: só mensagens de clientes das unidades dele (os testes ficam de fora).
        if (objMessage is null || (objScope.IsUnitRestricted && objCustomer is null))
        {
            return NotFound(new ErrorResponse("Mensagem não encontrada."));
        }

        List<WhatsAppMessageEventDto> objEvents = await _objDbContext.WhatsAppMessageEvents.AsNoTracking()
            .Where(evt => evt.IDWhatsAppMessage == id)
            .OrderBy(evt => evt.MetaTimestamp)
            .ThenBy(evt => evt.Id)
            .Select(evt => new WhatsAppMessageEventDto(
                evt.Status, evt.MetaTimestamp, evt.ErrorCode, evt.ErrorMessage, evt.PricingCategory, evt.Billable,
                evt.CreatedAt))
            .ToListAsync(objCancellationToken);
        (Dictionary<Guid, string> objCampaigns, Dictionary<Guid, DateOnly> objRuns) =
            await CampaignNamesAsync([(objMessage.IDCampaign, objMessage.IDCampaignRun)], objCancellationToken);

        return Ok(new WhatsAppMessageDetailDto(
            Sent(objMessage, objCampaigns, objRuns), objMessage.IDCustomer, objCustomer?.Name ?? "", objMessage.ToPhone,
            objMessage.FromDisplayPhone, objMessage.TemplateName, objMessage.ParametersJson, objMessage.MetaMessageId,
            objMessage.HttpStatus, objMessage.DurationMs, objMessage.PricingCategory, objMessage.PricingType,
            objMessage.Billable, objMessage.RequestJson, objMessage.ResponseJson, objEvents));
    }

    // ------------------------------------------------------------------------ Conta

    /// <summary>O histórico da conta da empresa, do mais novo para o mais antigo. Só admin da empresa e super admin.</summary>
    [HttpGet("account")]
    public async Task<ActionResult<PagedDto<WhatsAppAccountEventDto>>> GetAccount(
        [FromQuery(Name = "company")] string? sCompanySlug,
        [FromQuery(Name = "page")] int iPage,
        [FromQuery(Name = "pageSize")] int iPageSize,
        CancellationToken objCancellationToken)
    {
        (Company? objCompany, _, ActionResult? objError) = await LoadAsync(sCompanySlug, bAdminOnly: true, objCancellationToken);
        if (objCompany is null)
        {
            return objError!;
        }
        (iPage, iPageSize) = Paging(iPage, iPageSize, 30);

        IQueryable<WhatsAppAccountEvent> objQuery = _objDbContext.WhatsAppAccountEvents.AsNoTracking()
            .Where(evt => evt.IDCompany == objCompany.Id);
        int iTotal = await objQuery.CountAsync(objCancellationToken);
        List<WhatsAppAccountEventDto> objItems = await objQuery
            .OrderByDescending(evt => evt.CreatedAt)
            .ThenByDescending(evt => evt.Id)
            .Skip((iPage - 1) * iPageSize)
            .Take(iPageSize)
            .Select(evt => new WhatsAppAccountEventDto(
                evt.Id, evt.Kind, evt.Username, evt.Details, evt.RequestJson, evt.ResponseJson, evt.CreatedAt))
            .ToListAsync(objCancellationToken);
        return Ok(new PagedDto<WhatsAppAccountEventDto>(objItems, iTotal, iPage, iPageSize));
    }

    // ------------------------------------------------------------------------ ajudas

    /// <summary>
    /// A empresa e o que o usuário pode ver. Admin da empresa: a dele; super admin: a do ?company=slug. Usuário de
    /// unidade só quando bAdminOnly = false (Conversas e Registro), restrito às unidades dele.
    /// </summary>
    private async Task<(Company? objCompany, AccessScope? objScope, ActionResult? objError)> LoadAsync(
        string? sCompanySlug, bool bAdminOnly, CancellationToken objCancellationToken)
    {
        AccessScope objScope = await AccessScope.LoadAsync(_objDbContext, User, objCancellationToken);
        if (bAdminOnly && objScope.IsUnitRestricted)
        {
            return (null, null, Forbid());
        }
        Company? objCompany;
        if (objScope.IDCompany is Guid objCompanyId)
        {
            objCompany = await _objDbContext.Companies.AsNoTracking()
                .FirstOrDefaultAsync(company => company.Id == objCompanyId, objCancellationToken);
        }
        else
        {
            if (string.IsNullOrWhiteSpace(sCompanySlug))
            {
                return (null, null, BadRequest(new ErrorResponse("Informe a empresa (?company=slug).")));
            }
            objCompany = await _objDbContext.Companies.AsNoTracking()
                .FirstOrDefaultAsync(company => company.Slug == sCompanySlug, objCancellationToken);
        }
        return objCompany is null
            ? (null, null, NotFound(new ErrorResponse("Empresa não encontrada.")))
            : (objCompany, objScope, null);
    }

    /// <summary>Os clientes da empresa que o usuário pode ver (usuário de unidade: os da última visita nas unidades dele).</summary>
    private IQueryable<Customer> CustomersQuery(Company objCompany, AccessScope objScope)
    {
        IQueryable<Customer> objQuery = _objDbContext.Customers.AsNoTracking()
            .Where(customer => customer.IDCompany == objCompany.Id);
        if (objScope.IsUnitRestricted)
        {
            Guid[] arrUnits = objScope.UnitIds;
            objQuery = objQuery.Where(customer => customer.IDLastUnit != null && arrUnits.Contains(customer.IDLastUnit.Value));
        }
        return objQuery;
    }

    private async Task<(Dictionary<Guid, string> objCampaigns, Dictionary<Guid, DateOnly> objRuns)> CampaignNamesAsync(
        IEnumerable<(Guid? objCampaignId, Guid? objRunId)> objRefs, CancellationToken objCancellationToken)
    {
        List<(Guid? objCampaignId, Guid? objRunId)> objList = objRefs.ToList();
        List<Guid> objCampaignIds = objList.Select(item => item.objCampaignId).OfType<Guid>().Distinct().ToList();
        List<Guid> objRunIds = objList.Select(item => item.objRunId).OfType<Guid>().Distinct().ToList();
        Dictionary<Guid, string> objCampaigns = await _objDbContext.Campaigns.AsNoTracking()
            .Where(campaign => objCampaignIds.Contains(campaign.Id))
            .ToDictionaryAsync(campaign => campaign.Id, campaign => campaign.Name, objCancellationToken);
        Dictionary<Guid, DateOnly> objRuns = await _objDbContext.CampaignRuns.AsNoTracking()
            .Where(run => objRunIds.Contains(run.Id))
            .ToDictionaryAsync(run => run.Id, run => run.LocalDate, objCancellationToken);
        return (objCampaigns, objRuns);
    }

    private Task<Dictionary<Guid, string>> UnitNamesAsync(Guid objCompanyId, CancellationToken objCancellationToken) =>
        _objDbContext.Units.AsNoTracking()
            .Where(unit => unit.IDCompany == objCompanyId)
            .ToDictionaryAsync(unit => unit.Id, unit => unit.Name, objCancellationToken);

    private static WhatsAppSentDto Sent(
        WhatsAppMessage objMessage, IReadOnlyDictionary<Guid, string> objCampaigns, IReadOnlyDictionary<Guid, DateOnly> objRuns) =>
        new WhatsAppSentDto(
            objMessage.Id,
            objMessage.IDCampaign is Guid objCampaignId ? objCampaigns.GetValueOrDefault(objCampaignId, "") : "",
            objMessage.IDCampaignRun is Guid objRunId && objRuns.TryGetValue(objRunId, out DateOnly dtRun) ? dtRun : null,
            objMessage.VersionNumber, objMessage.Text, objMessage.Status, objMessage.ErrorMessage, objMessage.CreatedAt,
            objMessage.SentAt, objMessage.DeliveredAt, objMessage.ReadAt, objMessage.FailedAt, objMessage.IsTest,
            objMessage.Username);

    private static string UnitName(IReadOnlyDictionary<Guid, string> objUnits, Guid? objUnitId) =>
        objUnitId is Guid objId ? objUnits.GetValueOrDefault(objId, "") : "";

    private static (int iPage, int iPageSize) Paging(int iPage, int iPageSize, int iDefault) =>
        (Math.Max(1, iPage), iPageSize <= 0 ? iDefault : Math.Min(iPageSize, MaxPageSize));

    private static string Truncate(string sValue, int iMax) => sValue.Length <= iMax ? sValue : sValue[..iMax];
}
