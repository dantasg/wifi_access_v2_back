using AccessWifi.Api.Features;
using AccessWifi.Api.Features.WhatsApp;
using AccessWifi.Api.Infrastructure.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Models.Campaigns;
using Models.DataBase;
using Models.Persistence;
using Models.Security;
using Models.WhatsApp;

namespace AccessWifi.Api.Controllers;

/// <summary>
/// O número de WhatsApp da empresa (PROPOSTA_WHATSAPP.md, partes 1 e 2): credenciais, teste de conexão, o
/// interruptor do envio automático, o WhatsApp de atendimento das unidades e os modelos de mensagem. Admin da empresa e super admin (D3); o usuário de unidade não entra.
///
/// Os segredos (token, chave secreta do app, código de verificação) são só de ida: entram cifrados e nunca
/// saem, nem para a tela nem para o registro da conta.
/// </summary>
[ApiController]
[Route("admin/whatsapp")]
[Authorize]
public class CompanyWhatsAppController : ControllerBase
{
    private readonly AppDbContext _objDbContext;
    private readonly IEncryptor _objEncryptor;
    private readonly WhatsAppCloudClient _objWhatsAppClient;
    private readonly ILogger<CompanyWhatsAppController> _objLogger;

    public CompanyWhatsAppController(
        AppDbContext objDbContext,
        IEncryptor objEncryptor,
        WhatsAppCloudClient objWhatsAppClient,
        ILogger<CompanyWhatsAppController> objLogger)
    {
        _objDbContext = objDbContext;
        _objEncryptor = objEncryptor;
        _objWhatsAppClient = objWhatsAppClient;
        _objLogger = objLogger;
    }

    /// <summary>A situação do WhatsApp da empresa, sem nenhum segredo.</summary>
    [HttpGet]
    public async Task<ActionResult<WhatsAppSettingsDto>> Get(
        [FromQuery(Name = "company")] string? sCompanySlug, CancellationToken objCancellationToken)
    {
        (Company? objCompany, ActionResult? objError) = await LoadCompanyAsync(sCompanySlug, objCancellationToken);
        if (objCompany is null)
        {
            return objError!;
        }
        return Ok(await SettingsAsync(objCompany, objCancellationToken));
    }

    /// <summary>
    /// Grava as credenciais. Trocou alguma coisa: o teste anterior deixa de valer e, se o envio automático
    /// estava ligado, ele é desligado até um novo teste passar.
    /// </summary>
    [HttpPut]
    public async Task<ActionResult<WhatsAppSettingsDto>> Put(
        WhatsAppSettingsRequest objRequest,
        [FromQuery(Name = "company")] string? sCompanySlug,
        CancellationToken objCancellationToken)
    {
        (Company? objCompany, ActionResult? objError) = await LoadCompanyAsync(sCompanySlug, objCancellationToken);
        if (objCompany is null)
        {
            return objError!;
        }

        string sPhoneNumberId = objRequest.PhoneNumberId?.Trim() ?? "";
        string sBusinessAccountId = objRequest.BusinessAccountId?.Trim() ?? "";
        if (sPhoneNumberId.Length > 0 && !WhatsAppCloudClient.IsValidMetaId(sPhoneNumberId))
        {
            return BadRequest(new ErrorResponse("O id do número tem só números, como aparece no painel da Meta."));
        }
        if (sBusinessAccountId.Length > 0 && !WhatsAppCloudClient.IsValidMetaId(sBusinessAccountId))
        {
            return BadRequest(new ErrorResponse("O id da conta do WhatsApp tem só números, como aparece no painel da Meta."));
        }
        string? sSecretError = ValidateSecret(objRequest.AccessToken, "O token", 1500)
            ?? ValidateSecret(objRequest.AppSecret, "A chave secreta do app", 200)
            ?? ValidateSecret(objRequest.VerifyToken, "O código de verificação", 200);
        if (sSecretError is not null)
        {
            return BadRequest(new ErrorResponse(sSecretError));
        }

        CompanyWhatsApp objWhatsApp = objCompany.WhatsApp;
        List<string> objChanged = [];
        if (sPhoneNumberId != objWhatsApp.PhoneNumberId)
        {
            objWhatsApp.PhoneNumberId = sPhoneNumberId;
            objChanged.Add("id do número");
        }
        if (sBusinessAccountId != objWhatsApp.BusinessAccountId)
        {
            objWhatsApp.BusinessAccountId = sBusinessAccountId;
            objChanged.Add("id da conta do WhatsApp");
        }
        // Segredos: vazio = manter. Só se sabe que trocou, nunca o valor.
        if (!string.IsNullOrWhiteSpace(objRequest.AccessToken))
        {
            objWhatsApp.AccessToken = _objEncryptor.Encrypt(objRequest.AccessToken.Trim()) ?? "";
            objChanged.Add("token");
        }
        if (!string.IsNullOrWhiteSpace(objRequest.AppSecret))
        {
            objWhatsApp.AppSecret = _objEncryptor.Encrypt(objRequest.AppSecret.Trim()) ?? "";
            objChanged.Add("chave secreta do app");
        }
        if (!string.IsNullOrWhiteSpace(objRequest.VerifyToken))
        {
            objWhatsApp.VerifyToken = _objEncryptor.Encrypt(objRequest.VerifyToken.Trim()) ?? "";
            objChanged.Add("código de verificação");
        }

        if (objChanged.Count == 0)
        {
            return Ok(await SettingsAsync(objCompany, objCancellationToken));
        }

        string sUsername = User.GetUsername() ?? "";
        DateTime dtNow = DateTime.UtcNow;
        // Credencial nova = conexão ainda não testada com ela.
        objWhatsApp.CheckPassed = false;
        objWhatsApp.LastError = "";
        AddEvent(objCompany, WhatsAppAccountEventKind.CredentialsChanged, sUsername,
            "Trocou: " + string.Join(", ", objChanged) + ".", dtNow);
        if (objCompany.WhatsAppEnabled)
        {
            objCompany.WhatsAppEnabled = false;
            objCompany.WhatsAppChangedAt = dtNow;
            objCompany.WhatsAppChangedBy = sUsername;
            AddEvent(objCompany, WhatsAppAccountEventKind.Disabled, sUsername,
                "Envio automático desligado porque as credenciais mudaram. Teste a conexão e ligue de novo.", dtNow);
        }

        await _objDbContext.SaveChangesAsync(objCancellationToken);
        return Ok(await SettingsAsync(objCompany, objCancellationToken));
    }

    /// <summary>
    /// Testa as credenciais gravadas na Meta: lê o número e confere a conta. Não envia mensagem. Falha volta
    /// como 200 com checkPassed=false e o motivo em lastError: é o resultado do teste, não erro do pedido.
    /// </summary>
    [HttpPost("test")]
    public async Task<ActionResult<WhatsAppSettingsDto>> Test(
        [FromQuery(Name = "company")] string? sCompanySlug, CancellationToken objCancellationToken)
    {
        (Company? objCompany, ActionResult? objError) = await LoadCompanyAsync(sCompanySlug, objCancellationToken);
        if (objCompany is null)
        {
            return objError!;
        }

        CompanyWhatsApp objWhatsApp = objCompany.WhatsApp;
        string sAccessToken = _objEncryptor.Decrypt(objWhatsApp.AccessToken) ?? "";
        if (objWhatsApp.PhoneNumberId.Length == 0 || objWhatsApp.BusinessAccountId.Length == 0
            || sAccessToken.Length == 0)
        {
            return BadRequest(new ErrorResponse(
                "Preencha o id do número, o id da conta do WhatsApp e o token, e salve antes de testar."));
        }

        string sUsername = User.GetUsername() ?? "";
        DateTime dtNow = DateTime.UtcNow;
        objWhatsApp.CheckedAt = dtNow;
        try
        {
            (WhatsAppPhoneInfo objPhone, List<WhatsAppCall> objCalls) = await _objWhatsAppClient.CheckConnectionAsync(
                objWhatsApp.PhoneNumberId, objWhatsApp.BusinessAccountId, sAccessToken, objCancellationToken);

            objWhatsApp.DisplayPhoneNumber = Truncate(objPhone.DisplayPhoneNumber, 40);
            objWhatsApp.DisplayName = Truncate(objPhone.VerifiedName, 120);
            objWhatsApp.QualityRating = Truncate(objPhone.QualityRating, 20);
            objWhatsApp.MessagingLimit = Truncate(objPhone.MessagingLimit, 30);
            objWhatsApp.CheckPassed = true;
            objWhatsApp.LastError = "";

            // Liga a conta ao app, para a Meta mandar os avisos dela. Se falhar, a conexão continua aprovada: só fica
            // anotado (o envio funciona; os avisos de entrega é que não chegariam).
            string sSubscribe;
            try
            {
                objCalls.AddRange(await _objWhatsAppClient.SubscribeAppAsync(
                    objWhatsApp.BusinessAccountId, sAccessToken, objCancellationToken));
                sSubscribe = " Avisos: conta ligada ao app.";
            }
            catch (WhatsAppApiException objSubscribeException)
            {
                objCalls.AddRange(objSubscribeException.Calls);
                sSubscribe = " Avisos: não foi possível ligar a conta ao app (" + objSubscribeException.Message + ").";
            }
            AddEvent(objCompany, WhatsAppAccountEventKind.ConnectionTested, sUsername,
                $"Conexão OK: {objWhatsApp.DisplayPhoneNumber} ({objWhatsApp.DisplayName}), qualidade "
                + $"{objWhatsApp.QualityRating}, limite {objWhatsApp.MessagingLimit}." + sSubscribe, dtNow, objCalls);
        }
        catch (WhatsAppApiException objException)
        {
            // Sem segredo no log: só a empresa e o motivo.
            _objLogger.LogWarning("Teste do WhatsApp falhou na empresa {Slug}: {Motivo}", objCompany.Slug, objException.Message);
            objWhatsApp.CheckPassed = false;
            objWhatsApp.LastError = Truncate(objException.Message, 500);
            AddEvent(objCompany, WhatsAppAccountEventKind.ConnectionTested, sUsername,
                "Falhou: " + objException.Message, dtNow, objException.Calls);
        }

        await _objDbContext.SaveChangesAsync(objCancellationToken);
        return Ok(await SettingsAsync(objCompany, objCancellationToken));
    }

    /// <summary>Liga ou desliga o envio automático. Ligar exige o último teste de conexão aprovado.</summary>
    [HttpPut("enabled")]
    public async Task<ActionResult<WhatsAppSettingsDto>> SetEnabled(
        WhatsAppEnabledRequest objRequest,
        [FromQuery(Name = "company")] string? sCompanySlug,
        CancellationToken objCancellationToken)
    {
        (Company? objCompany, ActionResult? objError) = await LoadCompanyAsync(sCompanySlug, objCancellationToken);
        if (objCompany is null)
        {
            return objError!;
        }
        if (objCompany.WhatsAppEnabled == objRequest.Enabled)
        {
            return Ok(await SettingsAsync(objCompany, objCancellationToken));
        }
        if (objRequest.Enabled && !objCompany.WhatsApp.CheckPassed)
        {
            return BadRequest(new ErrorResponse("Teste a conexão com a Meta antes de ligar o envio automático."));
        }

        string sUsername = User.GetUsername() ?? "";
        DateTime dtNow = DateTime.UtcNow;
        objCompany.WhatsAppEnabled = objRequest.Enabled;
        objCompany.WhatsAppChangedAt = dtNow;
        objCompany.WhatsAppChangedBy = sUsername;
        AddEvent(objCompany,
            objRequest.Enabled ? WhatsAppAccountEventKind.Enabled : WhatsAppAccountEventKind.Disabled,
            sUsername,
            objRequest.Enabled ? "Envio automático pelo WhatsApp ligado." : "Envio automático pelo WhatsApp desligado.",
            dtNow);

        await _objDbContext.SaveChangesAsync(objCancellationToken);
        return Ok(await SettingsAsync(objCompany, objCancellationToken));
    }

    /// <summary>O WhatsApp de atendimento de cada unidade da empresa (D10), que vai no fim das mensagens.</summary>
    [HttpGet("units")]
    public async Task<ActionResult<List<WhatsAppUnitDto>>> GetUnits(
        [FromQuery(Name = "company")] string? sCompanySlug, CancellationToken objCancellationToken)
    {
        (Company? objCompany, ActionResult? objError) = await LoadCompanyAsync(sCompanySlug, objCancellationToken);
        if (objCompany is null)
        {
            return objError!;
        }
        return Ok(await UnitDtosAsync(objCompany.Id, objCancellationToken));
    }

    /// <summary>Grava o WhatsApp de atendimento das unidades informadas (vazio apaga). Vai para o registro da conta.</summary>
    [HttpPut("units")]
    public async Task<ActionResult<List<WhatsAppUnitDto>>> SaveUnits(
        List<WhatsAppUnitPhoneRequest> objRequest,
        [FromQuery(Name = "company")] string? sCompanySlug,
        CancellationToken objCancellationToken)
    {
        (Company? objCompany, ActionResult? objError) = await LoadCompanyAsync(sCompanySlug, objCancellationToken);
        if (objCompany is null)
        {
            return objError!;
        }

        Dictionary<Guid, Unit> objUnits = await _objDbContext.Units
            .Where(unit => unit.IDCompany == objCompany.Id)
            .ToDictionaryAsync(unit => unit.Id, objCancellationToken);
        List<string> objChanged = [];
        foreach (WhatsAppUnitPhoneRequest objItem in objRequest ?? [])
        {
            if (!objUnits.TryGetValue(objItem.UnitId, out Unit? objUnit))
            {
                return BadRequest(new ErrorResponse("Unidade não encontrada nesta empresa."));
            }
            string? sPhone = NormalizeStorePhone(objItem.StorePhone);
            if (sPhone is null)
            {
                return BadRequest(new ErrorResponse(
                    $"WhatsApp da unidade {objUnit.Name} inválido: use DDD + número, como (93) 99123-4567."));
            }
            if (sPhone != objUnit.StorePhone)
            {
                objUnit.StorePhone = sPhone;
                objChanged.Add($"{objUnit.Name}: {(sPhone.Length > 0 ? CampaignContact.FormatPhone(sPhone) : "apagado")}");
            }
        }

        if (objChanged.Count > 0)
        {
            AddEvent(objCompany, WhatsAppAccountEventKind.StorePhonesChanged, User.GetUsername() ?? "",
                "WhatsApp de atendimento: " + string.Join("; ", objChanged) + ".", DateTime.UtcNow);
            await _objDbContext.SaveChangesAsync(objCancellationToken);
        }
        return Ok(await UnitDtosAsync(objCompany.Id, objCancellationToken));
    }

    /// <summary>
    /// Os modelos de mensagem da empresa na Meta (parte 2), do mais novo para o mais antigo, com a situação e o
    /// texto como o cliente vê. Quem cria e manda para a Meta é o serviço.
    /// </summary>
    [HttpGet("templates")]
    public async Task<ActionResult<List<WhatsAppTemplateDto>>> GetTemplates(
        [FromQuery(Name = "company")] string? sCompanySlug, CancellationToken objCancellationToken)
    {
        (Company? objCompany, ActionResult? objError) = await LoadCompanyAsync(sCompanySlug, objCancellationToken);
        if (objCompany is null)
        {
            return objError!;
        }

        List<WhatsAppTemplate> objTemplates = await _objDbContext.WhatsAppTemplates.AsNoTracking()
            .Where(template => template.IDCompany == objCompany.Id)
            .OrderByDescending(template => template.CreatedAt)
            .ToListAsync(objCancellationToken);
        var objCampaigns = await _objDbContext.Campaigns.AsNoTracking()
            .Where(campaign => campaign.IDCompany == objCompany.Id)
            .Select(campaign => new { campaign.Id, campaign.Name, campaign.ConfigJson })
            .ToListAsync(objCancellationToken);

        // O modelo atual de cada campanha é o do texto de agora.
        HashSet<Guid> objCurrent = objCampaigns
            .Select(campaign => WhatsAppTemplateLookup.Match(
                WhatsAppTemplateBuilder.Build(CampaignConfig.FromJson(campaign.ConfigJson).Message, objCompany.Name),
                objTemplates))
            .OfType<WhatsAppTemplate>()
            .Select(template => template.Id)
            .ToHashSet();
        Dictionary<Guid, string> objNames = objCampaigns.ToDictionary(campaign => campaign.Id, campaign => campaign.Name);
        WhatsAppTemplateSamples objSamples =
            await WhatsAppTemplateSamples.LoadAsync(_objDbContext, objCompany.Id, objCancellationToken);

        return Ok(objTemplates
            .Select(template => new WhatsAppTemplateDto(
                template.Id, template.Name, template.IDCampaign, objNames.GetValueOrDefault(template.IDCampaign, ""),
                template.VersionNumber, objCurrent.Contains(template.Id), template.Status, template.MetaStatus,
                template.Category, template.Reason, template.LastError,
                WhatsAppTemplateBuilder.Preview(
                    template.BodyText, template.FooterText,
                    WhatsAppTemplateBuilder.ParametersFromJson(template.ParametersJson), objSamples),
                template.CreatedAt, template.SubmittedAt, template.StatusChangedAt, template.CheckedAt))
            .ToList());
    }

    /// <summary>
    /// Manda um texto aprovado para um número informado, para testar com o próprio celular antes de ligar o envio
    /// automático (§5.1). Os campos vão com exemplos ("Maria" e o WhatsApp de uma unidade). Fica no registro das
    /// mensagens como teste, com quem mandou. Recusa da Meta volta como 400 com o motivo.
    /// </summary>
    [HttpPost("test-message")]
    public async Task<ActionResult<WhatsAppTestMessageDto>> TestMessage(
        WhatsAppTestMessageRequest objRequest,
        [FromQuery(Name = "company")] string? sCompanySlug,
        CancellationToken objCancellationToken)
    {
        (Company? objCompany, ActionResult? objError) = await LoadCompanyAsync(sCompanySlug, objCancellationToken);
        if (objCompany is null)
        {
            return objError!;
        }

        string? sPhone = NormalizeStorePhone(objRequest.Phone);
        if (string.IsNullOrEmpty(sPhone))
        {
            return BadRequest(new ErrorResponse("Informe o celular com DDD, como (93) 99123-4567."));
        }
        CompanyWhatsApp objWhatsApp = objCompany.WhatsApp;
        string sAccessToken = _objEncryptor.Decrypt(objWhatsApp.AccessToken) ?? "";
        if (!objWhatsApp.CheckPassed || sAccessToken.Length == 0)
        {
            return BadRequest(new ErrorResponse("Teste a conexão com a Meta antes de enviar um teste."));
        }
        WhatsAppTemplate? objTemplate = await _objDbContext.WhatsAppTemplates.AsNoTracking()
            .FirstOrDefaultAsync(template => template.Id == objRequest.TemplateId && template.IDCompany == objCompany.Id,
                objCancellationToken);
        if (objTemplate is null)
        {
            return NotFound(new ErrorResponse("Texto não encontrado."));
        }
        if (objTemplate.Status != WhatsAppTemplateStatus.Approved)
        {
            return BadRequest(new ErrorResponse("Só dá para testar um texto aprovado pela Meta."));
        }

        List<string> objParameters = WhatsAppTemplateBuilder.ParametersFromJson(objTemplate.ParametersJson);
        WhatsAppTemplateSamples objSamples =
            await WhatsAppTemplateSamples.LoadAsync(_objDbContext, objCompany.Id, objCancellationToken);
        List<string> objValues = WhatsAppTemplateBuilder.Examples(objParameters, objSamples);
        string sToPhone = CampaignContact.WhatsAppNumber(sPhone);
        DateTime dtNow = DateTime.UtcNow;
        WhatsAppMessage objMessage = new WhatsAppMessage
        {
            IDCompany = objCompany.Id,
            IDCampaign = objTemplate.IDCampaign,
            IsTest = true,
            Username = User.GetUsername() ?? "",
            ToPhone = sToPhone,
            FromPhoneNumberId = objWhatsApp.PhoneNumberId,
            FromDisplayPhone = objWhatsApp.DisplayPhoneNumber,
            IDWhatsAppTemplate = objTemplate.Id,
            TemplateName = objTemplate.Name,
            TemplateLanguage = objTemplate.Language,
            VersionNumber = objTemplate.VersionNumber,
            ParametersJson = WhatsAppTemplateBuilder.ParametersToJson(objValues),
            Text = Truncate(
                WhatsAppTemplateBuilder.Preview(objTemplate.BodyText, objTemplate.FooterText, objParameters, objSamples), 4000),
            CreatedAt = dtNow,
        };

        string? sFailure = null;
        IReadOnlyList<WhatsAppCall> objCalls;
        try
        {
            (WhatsAppSendResult objResult, List<WhatsAppCall> objSendCalls) = await _objWhatsAppClient.SendTemplateAsync(
                objWhatsApp.PhoneNumberId, sAccessToken, sToPhone, objTemplate.Name, objTemplate.Language, objValues,
                objCancellationToken);
            objCalls = objSendCalls;
            objMessage.MetaMessageId = Truncate(objResult.MessageId, 200);
            objMessage.WaId = Truncate(objResult.WaId, 20);
            objMessage.MetaMessageStatus = Truncate(objResult.MessageStatus, 40);
            objMessage.Status = WhatsAppMessageStatus.Accepted;
            objMessage.AcceptedAt = dtNow;
        }
        catch (WhatsAppApiException objException)
        {
            objCalls = objException.Calls;
            sFailure = objException.Message;
            objMessage.Status = WhatsAppMessageStatus.Failed;
            objMessage.ErrorCode = objException.MetaCode;
            objMessage.ErrorMessage = Truncate(objException.Message, 1000);
            objMessage.FailedAt = dtNow;
        }
        if (objCalls.Count > 0)
        {
            objMessage.RequestJson = WhatsAppCallLog.RequestsJson(objCalls);
            objMessage.ResponseJson = WhatsAppCallLog.ResponsesJson(objCalls);
            objMessage.HttpStatus = objCalls[^1].HttpStatus;
            objMessage.DurationMs = objCalls[^1].DurationMs;
        }
        _objDbContext.WhatsAppMessages.Add(objMessage);
        await _objDbContext.SaveChangesAsync(objCancellationToken);

        return sFailure is null
            ? Ok(new WhatsAppTestMessageDto(CampaignContact.FormatPhone(sPhone), objMessage.Text))
            : BadRequest(new ErrorResponse(sFailure));
    }

    /// <summary>
    /// A empresa do pedido: a do token para o admin da empresa; a do ?company=slug para o super admin. Usuário
    /// de unidade não acessa (D3).
    /// </summary>
    private async Task<(Company? objCompany, ActionResult? objError)> LoadCompanyAsync(
        string? sCompanySlug, CancellationToken objCancellationToken)
    {
        AccessScope objScope = await AccessScope.LoadAsync(_objDbContext, User, objCancellationToken);
        if (objScope.IsUnitRestricted)
        {
            return (null, Forbid());
        }

        Company? objCompany;
        if (objScope.IDCompany is Guid objCompanyId)
        {
            objCompany = await _objDbContext.Companies
                .FirstOrDefaultAsync(company => company.Id == objCompanyId, objCancellationToken);
        }
        else
        {
            if (string.IsNullOrWhiteSpace(sCompanySlug))
            {
                return (null, BadRequest(new ErrorResponse("Informe a empresa (?company=slug).")));
            }
            objCompany = await _objDbContext.Companies
                .FirstOrDefaultAsync(company => company.Slug == sCompanySlug, objCancellationToken);
        }
        return objCompany is null
            ? (null, NotFound(new ErrorResponse("Empresa não encontrada.")))
            : (objCompany, null);
    }

    /// <summary>
    /// A situação para a tela, com o endereço dos avisos (para colar no app da Meta; não é segredo), quando a Meta
    /// conferiu o endereço e quando chegou o último aviso com assinatura válida.
    /// </summary>
    private async Task<WhatsAppSettingsDto> SettingsAsync(Company objCompany, CancellationToken objCancellationToken)
    {
        DateTime? dtVerified = await _objDbContext.WhatsAppAccountEvents.AsNoTracking()
            .Where(evt => evt.IDCompany == objCompany.Id && evt.Kind == WhatsAppAccountEventKind.WebhookVerified)
            .OrderByDescending(evt => evt.CreatedAt)
            .Select(evt => (DateTime?)evt.CreatedAt)
            .FirstOrDefaultAsync(objCancellationToken);
        DateTime? dtLastWebhook = await _objDbContext.WhatsAppWebhookRequests.AsNoTracking()
            .Where(request => request.IDCompany == objCompany.Id && request.SignatureValid)
            .OrderByDescending(request => request.ReceivedAt)
            .Select(request => (DateTime?)request.ReceivedAt)
            .FirstOrDefaultAsync(objCancellationToken);
        return WhatsAppSettingsDto.From(objCompany) with
        {
            WebhookUrl = $"{Request.Scheme}://{Request.Host}/webhooks/whatsapp/{objCompany.Slug}",
            WebhookVerifiedAt = dtVerified,
            LastWebhookAt = dtLastWebhook,
        };
    }

    private async Task<List<WhatsAppUnitDto>> UnitDtosAsync(Guid objCompanyId, CancellationToken objCancellationToken) =>
        await _objDbContext.Units.AsNoTracking()
            .Where(unit => unit.IDCompany == objCompanyId)
            .OrderBy(unit => unit.Name)
            .Select(unit => new WhatsAppUnitDto(unit.Id, unit.Name, unit.Active, unit.StorePhone))
            .ToListAsync(objCancellationToken);

    /// <summary>
    /// "(93) 99123-4567", "+55 93 99123-4567" → "93991234567". Vazio = apagar (""). Nulo = inválido: precisa de
    /// DDD válido e 8 ou 9 dígitos (fixo ou celular, os dois podem ter WhatsApp).
    /// </summary>
    private static string? NormalizeStorePhone(string? sValue)
    {
        string sDigits = new string((sValue ?? "").Where(char.IsAsciiDigit).ToArray());
        if (sDigits.Length == 0)
        {
            return "";
        }
        if (sDigits.StartsWith("55", StringComparison.Ordinal) && sDigits.Length is 12 or 13)
        {
            sDigits = sDigits[2..];
        }
        if (sDigits.Length is not (10 or 11) || AreaCodeRules.Validate(sDigits[..2]) is not null)
        {
            return null;
        }
        return sDigits;
    }

    private static string? ValidateSecret(string? sValue, string sLabel, int iMaxLength)
    {
        if (string.IsNullOrWhiteSpace(sValue))
        {
            return null;
        }
        string sTrimmed = sValue.Trim();
        if (sTrimmed.Length > iMaxLength)
        {
            return $"{sLabel} está grande demais.";
        }
        return sTrimmed.Any(char.IsWhiteSpace) ? $"{sLabel} não pode ter espaços no meio." : null;
    }

    /// <summary>Registro da conta (§15): o que aconteceu, quem fez e, nas idas à Meta, o pedido e a resposta.</summary>
    private void AddEvent(
        Company objCompany, string sKind, string sUsername, string sDetails, DateTime dtNow,
        IReadOnlyList<WhatsAppCall>? objCalls = null)
    {
        _objDbContext.WhatsAppAccountEvents.Add(new WhatsAppAccountEvent
        {
            IDCompany = objCompany.Id,
            Kind = sKind,
            Username = sUsername,
            Details = Truncate(sDetails, 1000),
            RequestJson = objCalls is null ? null : WhatsAppCallLog.RequestsJson(objCalls),
            ResponseJson = objCalls is null ? null : WhatsAppCallLog.ResponsesJson(objCalls),
            CreatedAt = dtNow,
        });
    }

    private static string Truncate(string sValue, int iMax) => sValue.Length <= iMax ? sValue : sValue[..iMax];
}
