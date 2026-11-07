using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using AccessWifi.Api.Controllers;
using AccessWifiService.WhatsApp;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Models.DataBase;
using Models.Email;
using Models.Persistence;
using Models.Security;
using Models.WhatsApp;

namespace AccessWifi.Api.Tests;

/// <summary>
/// WhatsApp, parte 4 (PROPOSTA_WHATSAPP.md): os avisos da Meta (verificação, assinatura, situação das mensagens,
/// respostas e "SAIR", situação dos textos) e os alertas por e-mail ao super admin.
/// </summary>
public class WhatsAppWebhookTests
{
    private const string Waba = "102290129340398";
    private const string PhoneNumberId = "106540352242922";
    private const string AppSecret = "segredo-do-app-abc";
    private const string VerifyToken = "codigo-escolhido-xyz";
    private static readonly DateTime s_dtNow = new DateTime(2026, 10, 22, 12, 0, 0, DateTimeKind.Utc);

    private sealed class Scenario
    {
        public required AppDbContext Db { get; init; }
        public required Company Company { get; init; }
        public required Customer Ana { get; init; }
        public required CampaignRecipient AnaRecipient { get; init; }
        public required WhatsAppMessage AnaMessage { get; init; }
        public required IEncryptor Encryptor { get; init; }
    }

    private static Scenario CreateScenario(AppDbContext objDbContext)
    {
        IEncryptor objEncryptor = TestHelpers.CreateEncryptor();
        Company objCompany = new Company { Name = "Lojas Regional", Slug = "regional", WhatsAppEnabled = true };
        objCompany.WhatsApp.PhoneNumberId = PhoneNumberId;
        objCompany.WhatsApp.BusinessAccountId = Waba;
        objCompany.WhatsApp.AppSecret = objEncryptor.Encrypt(AppSecret)!;
        objCompany.WhatsApp.VerifyToken = objEncryptor.Encrypt(VerifyToken)!;
        objCompany.WhatsApp.MessagingLimit = "TIER_250";
        Customer objAna = new Customer { IDCompany = objCompany.Id, Phone = "93991110001", Name = "Ana" };
        Campaign objCampaign = new Campaign { IDCompany = objCompany.Id, Kind = CampaignKind.Birthday, Name = "Aniversário" };
        CampaignRun objRun = new CampaignRun
        {
            IDCampaign = objCampaign.Id, IDCompany = objCompany.Id, WhatsAppEnabled = true, Status = CampaignRunStatus.Running,
        };
        objDbContext.AddRange(objCompany, objAna, objCampaign, objRun);
        objDbContext.SaveChanges();
        CampaignRecipient objRecipient = new CampaignRecipient
        {
            IDRun = objRun.Id, IDCustomer = objAna.Id, Phone = objAna.Phone, Name = objAna.Name,
            WhatsAppStatus = CampaignWhatsAppStatus.Accepted,
        };
        objDbContext.CampaignRecipients.Add(objRecipient);
        objDbContext.SaveChanges();
        WhatsAppMessage objMessage = new WhatsAppMessage
        {
            IDCompany = objCompany.Id, IDCustomer = objAna.Id, IDCampaign = objCampaign.Id, IDCampaignRun = objRun.Id,
            IDCampaignRecipient = objRecipient.Id, ToPhone = "5593991110001", WaId = "5593991110001",
            MetaMessageId = "wamid.ANA", Status = WhatsAppMessageStatus.Accepted, CreatedAt = s_dtNow.AddMinutes(-30),
        };
        objDbContext.WhatsAppMessages.Add(objMessage);
        objDbContext.SaveChanges();
        return new Scenario
        {
            Db = objDbContext, Company = objCompany, Ana = objAna, AnaRecipient = objRecipient, AnaMessage = objMessage,
            Encryptor = objEncryptor,
        };
    }

    private static WhatsAppWebhookController Controller(Scenario objScenario, string sBody, string? sSignature)
    {
        WhatsAppWebhookController objController = new WhatsAppWebhookController(
            objScenario.Db, objScenario.Encryptor, NullLogger<WhatsAppWebhookController>.Instance);
        DefaultHttpContext objHttpContext = new DefaultHttpContext();
        objHttpContext.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(sBody));
        if (sSignature is not null)
        {
            objHttpContext.Request.Headers["X-Hub-Signature-256"] = sSignature;
        }
        objController.ControllerContext = new ControllerContext { HttpContext = objHttpContext };
        return objController;
    }

    private static string Sign(string sBody, string sSecret = AppSecret) =>
        "sha256=" + Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(sSecret), Encoding.UTF8.GetBytes(sBody)));

    private static async Task<int> PostAsync(Scenario objScenario, string sBody, string? sSignature = null)
    {
        IActionResult objResult = await Controller(objScenario, sBody, sSignature ?? Sign(sBody))
            .Receive("regional", CancellationToken.None);
        return objResult switch
        {
            OkResult => 200,
            UnauthorizedResult => 401,
            StatusCodeResult objStatus => objStatus.StatusCode,
            _ => -1,
        };
    }

    /// <summary>Um aviso "messages" com o valor informado, como a Meta manda (webhooks/reference/messages).</summary>
    private static string MessagesPayload(JsonObject objValue, string sWaba = Waba)
    {
        objValue["messaging_product"] = "whatsapp";
        objValue["metadata"] = new JsonObject { ["display_phone_number"] = "559198888777", ["phone_number_id"] = PhoneNumberId };
        return new JsonObject
        {
            ["object"] = "whatsapp_business_account",
            ["entry"] = new JsonArray(new JsonObject
            {
                ["id"] = sWaba,
                ["changes"] = new JsonArray(new JsonObject { ["value"] = objValue, ["field"] = "messages" }),
            }),
        }.ToJsonString();
    }

    private static JsonObject Status(string sWamid, string sStatus, long lTime, int? iError = null) => new JsonObject
    {
        ["id"] = sWamid,
        ["status"] = sStatus,
        ["timestamp"] = lTime.ToString(),
        ["recipient_id"] = "5593991110001",
        ["pricing"] = new JsonObject { ["billable"] = true, ["pricing_model"] = "PMP", ["category"] = "marketing", ["type"] = "regular" },
        ["errors"] = iError is int iCode
            ? new JsonArray(new JsonObject { ["code"] = iCode, ["title"] = "Falhou", ["message"] = "Falhou" })
            : null,
    };

    private static JsonObject TextMessage(string sFrom, string sId, string sText, long lTime, string sType = "text") =>
        new JsonObject
        {
            ["contacts"] = new JsonArray(new JsonObject { ["profile"] = new JsonObject { ["name"] = "Ana S." }, ["wa_id"] = sFrom }),
            ["messages"] = new JsonArray(new JsonObject
            {
                ["from"] = sFrom, ["id"] = sId, ["timestamp"] = lTime.ToString(), ["type"] = sType,
                ["text"] = new JsonObject { ["body"] = sText },
            }),
        };

    private static long Unix(DateTime dtValue) => new DateTimeOffset(dtValue).ToUnixTimeSeconds();

    [Fact]
    public async Task Verification_ReturnsTheChallengeOnlyWithTheRightCode()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Scenario objScenario = CreateScenario(objDbContext);

        IActionResult objOk = await Controller(objScenario, "", null)
            .Verify("regional", "subscribe", VerifyToken, "1158201444", CancellationToken.None);
        ContentResult objContent = Assert.IsType<ContentResult>(objOk);
        Assert.Equal("1158201444", objContent.Content);
        Assert.Equal(WhatsAppAccountEventKind.WebhookVerified, Assert.Single(objDbContext.WhatsAppAccountEvents).Kind);

        IActionResult objWrong = await Controller(objScenario, "", null)
            .Verify("regional", "subscribe", "outro-codigo", "1158201444", CancellationToken.None);
        Assert.Equal(403, Assert.IsType<StatusCodeResult>(objWrong).StatusCode);
        Assert.IsType<NotFoundResult>(await Controller(objScenario, "", null)
            .Verify("nao-existe", "subscribe", VerifyToken, "1", CancellationToken.None));
    }

    [Fact]
    public async Task WithoutAValidSignature_IsRefused_AndNotProcessed()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Scenario objScenario = CreateScenario(objDbContext);
        string sBody = MessagesPayload(new JsonObject { ["statuses"] = new JsonArray(Status("wamid.ANA", "delivered", Unix(s_dtNow))) });

        Assert.Equal(401, await PostAsync(objScenario, sBody, Sign(sBody, "outra-chave")));
        Assert.Equal(401, await PostAsync(objScenario, sBody, "sha256=zz"));

        Assert.Equal(WhatsAppMessageStatus.Accepted, objDbContext.WhatsAppMessages.Single().Status);
        Assert.Empty(objDbContext.WhatsAppMessageEvents);
        Assert.All(objDbContext.WhatsAppWebhookRequests, request =>
        {
            Assert.False(request.SignatureValid);
            Assert.Null(request.ProcessedAt);
        });
    }

    [Fact]
    public async Task Statuses_UpdateTheMessageAndTheCampaignCustomer_InAnyOrder_AndOnlyOnce()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Scenario objScenario = CreateScenario(objDbContext);
        // "lida" chega antes de "entregue" (a Meta não garante a ordem), e o pacote se repete.
        string sBody = MessagesPayload(new JsonObject
        {
            ["statuses"] = new JsonArray(
                Status("wamid.ANA", "sent", Unix(s_dtNow.AddMinutes(-29))),
                Status("wamid.ANA", "read", Unix(s_dtNow.AddMinutes(-20))),
                Status("wamid.ANA", "delivered", Unix(s_dtNow.AddMinutes(-25))),
                Status("wamid.DESCONHECIDO", "delivered", Unix(s_dtNow))),
        });

        Assert.Equal(200, await PostAsync(objScenario, sBody));
        Assert.Equal(200, await PostAsync(objScenario, sBody));

        WhatsAppMessage objMessage = objDbContext.WhatsAppMessages.Single();
        Assert.Equal(WhatsAppMessageStatus.Read, objMessage.Status);
        Assert.Equal(s_dtNow.AddMinutes(-25), objMessage.DeliveredAt);
        Assert.Equal(s_dtNow.AddMinutes(-20), objMessage.ReadAt);
        Assert.Equal("marketing", objMessage.PricingCategory);
        Assert.True(objMessage.Billable);
        Assert.Equal(3, objDbContext.WhatsAppMessageEvents.Count()); // repetido não duplica
        Assert.Equal(CampaignWhatsAppStatus.Read, objDbContext.CampaignRecipients.Single().WhatsAppStatus);
        Assert.Equal(2, objDbContext.WhatsAppWebhookRequests.Count(request => request.SignatureValid && request.ProcessedAt != null));
        Assert.Equal("messages", objDbContext.WhatsAppWebhookRequests.First().Fields);
    }

    [Fact]
    public async Task FailedStatus_GivesTheReasonInPortuguese()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Scenario objScenario = CreateScenario(objDbContext);
        string sBody = MessagesPayload(new JsonObject
        {
            ["statuses"] = new JsonArray(Status("wamid.ANA", "failed", Unix(s_dtNow), iError: 131050)),
        });

        Assert.Equal(200, await PostAsync(objScenario, sBody));

        WhatsAppMessage objMessage = objDbContext.WhatsAppMessages.Single();
        Assert.Equal(WhatsAppMessageStatus.Failed, objMessage.Status);
        Assert.Equal(131050, objMessage.ErrorCode);
        CampaignRecipient objRecipient = objDbContext.CampaignRecipients.Single();
        Assert.Equal(CampaignWhatsAppStatus.Failed, objRecipient.WhatsAppStatus);
        Assert.Equal("O cliente desligou as promoções da empresa no WhatsApp.", objRecipient.WhatsAppError);

        // Aviso de outra conta do WhatsApp não mexe em nada.
        string sOther = MessagesPayload(new JsonObject
        {
            ["statuses"] = new JsonArray(Status("wamid.ANA", "read", Unix(s_dtNow))),
        }, sWaba: "999999999999");
        Assert.Equal(200, await PostAsync(objScenario, sOther));
        Assert.Equal(WhatsAppMessageStatus.Failed, objDbContext.WhatsAppMessages.Single().Status);
    }

    [Fact]
    public async Task Replies_AreRecordedWithTheirCampaign_AndSairOptsOut()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Scenario objScenario = CreateScenario(objDbContext);

        string sReply = MessagesPayload(TextMessage("5593991110001", "wamid.R1", "Obrigada! 😊", Unix(s_dtNow)));
        Assert.Equal(200, await PostAsync(objScenario, sReply));
        Assert.Equal(200, await PostAsync(objScenario, sReply)); // repetido

        WhatsAppInboundMessage objInbound = Assert.Single(objDbContext.WhatsAppInboundMessages);
        Assert.Equal(objScenario.Ana.Id, objInbound.IDCustomer);
        Assert.Equal("Obrigada! 😊", objInbound.Text);
        Assert.Equal("Ana S.", objInbound.ProfileName);
        Assert.Equal(objScenario.AnaMessage.IDCampaign, objInbound.IDCampaign); // D14
        Assert.Equal(objScenario.AnaMessage.Id, objInbound.IDWhatsAppMessage);
        Assert.False(objInbound.IsOptOut);
        Assert.Null(objDbContext.Customers.Single(customer => customer.Id == objScenario.Ana.Id).WhatsAppOptOutAt);

        // "Sair." vale; vem do número antigo sem o 9 (o WhatsApp de números antigos vem assim).
        Assert.Equal(200, await PostAsync(objScenario,
            MessagesPayload(TextMessage("559391110001", "wamid.R2", " Sair. ", Unix(s_dtNow.AddMinutes(5))))));
        Customer objAna = objDbContext.Customers.Single(customer => customer.Id == objScenario.Ana.Id);
        Assert.Equal(s_dtNow.AddMinutes(5), objAna.WhatsAppOptOutAt);
        WhatsAppInboundMessage objSair = objDbContext.WhatsAppInboundMessages.Single(inbound => inbound.MetaMessageId == "wamid.R2");
        Assert.True(objSair.IsOptOut);
        Assert.Equal(objSair.Id, objAna.IDWhatsAppOptOutMessage);

        // D13: número que não é cliente não é registrado; D11: só texto.
        Assert.Equal(200, await PostAsync(objScenario,
            MessagesPayload(TextMessage("5511988887777", "wamid.R3", "Oi", Unix(s_dtNow)))));
        Assert.Equal(200, await PostAsync(objScenario,
            MessagesPayload(TextMessage("5593991110001", "wamid.R4", "", Unix(s_dtNow), sType: "image"))));
        Assert.Equal(2, objDbContext.WhatsAppInboundMessages.Count());

        Assert.True(WhatsAppWebhookProcessor.IsOptOut("SAIR"));
        Assert.True(WhatsAppWebhookProcessor.IsOptOut("sair!"));
        Assert.False(WhatsAppWebhookProcessor.IsOptOut("quero sair"));
    }

    [Fact]
    public async Task Part5_RepliesShowInTheRunAndInTheList_LeadsMarkWhoLeft_AndThePortalNamesTheCompany()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Scenario objScenario = CreateScenario(objDbContext);
        Unit objUnit = new Unit { IDCompany = objScenario.Company.Id, Name = "Itaituba", Slug = "itaituba" };
        objDbContext.Units.Add(objUnit);
        objScenario.Ana.IDLastUnit = objUnit.Id;
        objScenario.AnaRecipient.IDUnit = objUnit.Id;
        objDbContext.Leads.Add(new Lead { IDUnit = objUnit.Id, Name = "Ana", Phone = "(93) 99111-0001" });
        objDbContext.Leads.Add(new Lead { IDUnit = objUnit.Id, Name = "Bia", Phone = "(93) 99111-0002" });
        objDbContext.SaveChanges();

        Assert.Equal(200, await PostAsync(objScenario,
            MessagesPayload(TextMessage("5593991110001", "wamid.R1", "Obrigada!", Unix(s_dtNow)))));
        Assert.Equal(200, await PostAsync(objScenario,
            MessagesPayload(TextMessage("5593991110001", "wamid.R2", "SAIR", Unix(s_dtNow.AddMinutes(2))))));

        // Detalhe da execução: a última resposta junto do cliente.
        CampaignsController objCampaigns = new CampaignsController(objDbContext);
        TestHelpers.SetCompanyUser(objCampaigns, objDbContext, objScenario.Company.Id, "gerente");
        AccessWifi.Api.Features.Campaigns.PagedDto<AccessWifi.Api.Features.Campaigns.CampaignRecipientDto> objRecipients =
            Assert.IsType<AccessWifi.Api.Features.Campaigns.PagedDto<AccessWifi.Api.Features.Campaigns.CampaignRecipientDto>>(
                Assert.IsType<OkObjectResult>((await objCampaigns.Recipients(
                    objScenario.AnaRecipient.IDRun, null, null, 1, 50, null, CancellationToken.None)).Result).Value);
        AccessWifi.Api.Features.Campaigns.CampaignRecipientDto objAna = Assert.Single(objRecipients.Items);
        Assert.Equal("SAIR", objAna.LastReply!.Text);
        Assert.True(objAna.LastReply.IsOptOut);
        Assert.Equal(2, objAna.ReplyCount);

        // Registro, só as respostas: a mais nova primeiro, com a campanha e o link do WhatsApp para a loja responder.
        WhatsAppHistoryController objHistory = new WhatsAppHistoryController(objDbContext);
        TestHelpers.SetCompanyUser(objHistory, objDbContext, objScenario.Company.Id, "gerente");
        AccessWifi.Api.Features.Campaigns.PagedDto<AccessWifi.Api.Features.WhatsApp.WhatsAppLogItemDto> objReplies =
            Assert.IsType<AccessWifi.Api.Features.Campaigns.PagedDto<AccessWifi.Api.Features.WhatsApp.WhatsAppLogItemDto>>(
                Assert.IsType<OkObjectResult>((await objHistory.GetLog(null, new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31),
                    "in", null, null, null, null, 1, 20, CancellationToken.None)).Result).Value);
        Assert.Equal(2, objReplies.Total);
        Assert.Equal(["SAIR", "Obrigada!"], objReplies.Items.Select(reply => reply.Text));
        Assert.Equal("Aniversário", objReplies.Items[0].CampaignName);
        Assert.Equal("Itaituba", objReplies.Items[0].Unit);
        Assert.Equal("https://wa.me/5593991110001", objReplies.Items[0].WhatsAppUrl);

        // Leads: a marca de quem saiu, pelo telefone (com máscara no cadastro).
        AdminController objAdmin = new AdminController(objDbContext, new AccessWifi.Api.Infrastructure.Security.TokenService(
            Microsoft.Extensions.Options.Options.Create(new AccessWifi.Api.Infrastructure.Security.JwtOptions
            {
                Secret = "segredo-de-teste-3f9a1c7e5b2d8046a1e9c3b7d5f20486",
            })));
        TestHelpers.SetCompanyUser(objAdmin, objDbContext, objScenario.Company.Id, "gerente");
        List<AccessWifi.Api.Features.Leads.LeadDto> objLeads = Assert.IsType<List<AccessWifi.Api.Features.Leads.LeadDto>>(
            Assert.IsType<OkObjectResult>((await objAdmin.GetLeads(null, null, CancellationToken.None)).Result).Value);
        Assert.Equal(s_dtNow.AddMinutes(2), objLeads.Single(lead => lead.Name == "Ana").WhatsAppOptOutAt);
        Assert.Null(objLeads.Single(lead => lead.Name == "Bia").WhatsAppOptOutAt);

        // Portal: o nome da empresa vai para o texto da autorização.
        AccessWifi.Api.Features.Settings.SettingsDto objPortal = Assert.IsType<AccessWifi.Api.Features.Settings.SettingsDto>(
            Assert.IsType<OkObjectResult>((await new SettingsController(objDbContext)
                .Get("itaituba", null, null, CancellationToken.None)).Result).Value);
        Assert.Equal("Lojas Regional", objPortal.CompanyName);
    }

    [Fact]
    public async Task Part7_ConversationsLogMessageDetailAndAccount_RespectTheUnitOfTheUser()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Scenario objScenario = CreateScenario(objDbContext);
        Unit objItaituba = new Unit { IDCompany = objScenario.Company.Id, Name = "Itaituba", Slug = "itaituba" };
        Unit objCentro = new Unit { IDCompany = objScenario.Company.Id, Name = "Centro", Slug = "centro" };
        objDbContext.Units.AddRange(objItaituba, objCentro);
        objScenario.Ana.IDLastUnit = objItaituba.Id;
        objDbContext.WhatsAppAccountEvents.Add(new WhatsAppAccountEvent
        {
            IDCompany = objScenario.Company.Id, Kind = WhatsAppAccountEventKind.Enabled, Username = "gerente",
            Details = "Envio automático pelo WhatsApp ligado.", CreatedAt = s_dtNow.AddDays(-1),
        });
        objDbContext.SaveChanges();
        // Entregue, lida e uma resposta.
        Assert.Equal(200, await PostAsync(objScenario, MessagesPayload(new JsonObject
        {
            ["statuses"] = new JsonArray(
                Status("wamid.ANA", "delivered", Unix(s_dtNow.AddMinutes(-29))),
                Status("wamid.ANA", "read", Unix(s_dtNow.AddMinutes(-28)))),
        })));
        Assert.Equal(200, await PostAsync(objScenario,
            MessagesPayload(TextMessage("5593991110001", "wamid.R1", "Que presente?", Unix(s_dtNow.AddMinutes(-10))))));

        WhatsAppHistoryController objHistory = new WhatsAppHistoryController(objDbContext);
        TestHelpers.SetCompanyUser(objHistory, objDbContext, objScenario.Company.Id, "gerente");

        // Conversas: a Ana, com a última mensagem dela.
        var objConversations = Assert.IsType<AccessWifi.Api.Features.Campaigns.PagedDto<AccessWifi.Api.Features.WhatsApp.WhatsAppConversationDto>>(
            Assert.IsType<OkObjectResult>((await objHistory.GetConversations(null, null, 1, 30, CancellationToken.None)).Result).Value);
        AccessWifi.Api.Features.WhatsApp.WhatsAppConversationDto objAna = Assert.Single(objConversations.Items);
        Assert.Equal("in", objAna.LastDirection);
        Assert.Equal("Que presente?", objAna.LastText);
        Assert.Equal(1, objAna.SentCount);
        Assert.Equal(1, objAna.ReplyCount);
        Assert.Equal("Itaituba", objAna.Unit);
        Assert.Empty(Assert.IsType<AccessWifi.Api.Features.Campaigns.PagedDto<AccessWifi.Api.Features.WhatsApp.WhatsAppConversationDto>>(
            Assert.IsType<OkObjectResult>((await objHistory.GetConversations(null, "ninguem", 1, 30, CancellationToken.None)).Result).Value).Items);

        // O histórico dela: a mensagem da campanha, lida, e a resposta embaixo (D14).
        var objDetail = Assert.IsType<AccessWifi.Api.Features.WhatsApp.WhatsAppConversationDetailDto>(
            Assert.IsType<OkObjectResult>((await objHistory.GetConversation(objScenario.Ana.Id, null, CancellationToken.None)).Result).Value);
        AccessWifi.Api.Features.WhatsApp.WhatsAppConversationGroupDto objGroup = Assert.Single(objDetail.Groups);
        Assert.Equal("Aniversário", objGroup.Message!.CampaignName);
        Assert.Equal(WhatsAppMessageStatus.Read, objGroup.Message.Status);
        Assert.Equal("Que presente?", Assert.Single(objGroup.Replies).Text);

        // Registro: a enviada e a recebida; o filtro de situação deixa só a enviada.
        var objLog = Assert.IsType<AccessWifi.Api.Features.Campaigns.PagedDto<AccessWifi.Api.Features.WhatsApp.WhatsAppLogItemDto>>(
            Assert.IsType<OkObjectResult>((await objHistory.GetLog(null, new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31),
                null, null, null, null, null, 1, 50, CancellationToken.None)).Result).Value);
        Assert.Equal(["in", "out"], objLog.Items.Select(item => item.Direction));
        var objRead = Assert.IsType<AccessWifi.Api.Features.Campaigns.PagedDto<AccessWifi.Api.Features.WhatsApp.WhatsAppLogItemDto>>(
            Assert.IsType<OkObjectResult>((await objHistory.GetLog(null, new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31),
                null, WhatsAppMessageStatus.Read, null, null, "91110001", 1, 50, CancellationToken.None)).Result).Value);
        Assert.Equal("out", Assert.Single(objRead.Items).Direction);

        // O detalhe da mensagem: as situações na ordem da Meta.
        var objMessage = Assert.IsType<AccessWifi.Api.Features.WhatsApp.WhatsAppMessageDetailDto>(
            Assert.IsType<OkObjectResult>((await objHistory.GetMessage(objScenario.AnaMessage.Id, null, CancellationToken.None)).Result).Value);
        Assert.Equal([WhatsAppMessageStatus.Delivered, WhatsAppMessageStatus.Read], objMessage.Events.Select(evt => evt.Status));
        Assert.Equal("Ana", objMessage.CustomerName);

        // Conta: o histórico da empresa.
        var objAccount = Assert.IsType<AccessWifi.Api.Features.Campaigns.PagedDto<AccessWifi.Api.Features.WhatsApp.WhatsAppAccountEventDto>>(
            Assert.IsType<OkObjectResult>((await objHistory.GetAccount(null, 1, 30, CancellationToken.None)).Result).Value);
        Assert.Contains(objAccount.Items, evt => evt.Kind == WhatsAppAccountEventKind.Enabled);

        // Usuário da unidade Centro: não vê a Ana (Itaituba), nem a Conta.
        WhatsAppHistoryController objCentroUser = new WhatsAppHistoryController(objDbContext);
        TestHelpers.SetCompanyUser(objCentroUser, objDbContext, objScenario.Company.Id, "loja.centro", objCentro.Id);
        Assert.Empty(Assert.IsType<AccessWifi.Api.Features.Campaigns.PagedDto<AccessWifi.Api.Features.WhatsApp.WhatsAppConversationDto>>(
            Assert.IsType<OkObjectResult>((await objCentroUser.GetConversations(null, null, 1, 30, CancellationToken.None)).Result).Value).Items);
        Assert.Equal(0, Assert.IsType<AccessWifi.Api.Features.Campaigns.PagedDto<AccessWifi.Api.Features.WhatsApp.WhatsAppLogItemDto>>(
            Assert.IsType<OkObjectResult>((await objCentroUser.GetLog(null, new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31),
                null, null, null, null, null, 1, 50, CancellationToken.None)).Result).Value).Total);
        Assert.IsType<NotFoundObjectResult>((await objCentroUser.GetMessage(objScenario.AnaMessage.Id, null, CancellationToken.None)).Result);
        Assert.IsType<ForbidResult>((await objCentroUser.GetAccount(null, 1, 30, CancellationToken.None)).Result);

        // Usuário da Itaituba vê a Ana.
        WhatsAppHistoryController objItaitubaUser = new WhatsAppHistoryController(objDbContext);
        TestHelpers.SetCompanyUser(objItaitubaUser, objDbContext, objScenario.Company.Id, "loja.itaituba", objItaituba.Id);
        Assert.Single(Assert.IsType<AccessWifi.Api.Features.Campaigns.PagedDto<AccessWifi.Api.Features.WhatsApp.WhatsAppConversationDto>>(
            Assert.IsType<OkObjectResult>((await objItaitubaUser.GetConversations(null, null, 1, 30, CancellationToken.None)).Result).Value).Items);
    }

    [Fact]
    public async Task TemplateRejected_UpdatesTheText_AndAlertsOnce()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Scenario objScenario = CreateScenario(objDbContext);
        objDbContext.WhatsAppTemplates.Add(new WhatsAppTemplate
        {
            IDCompany = objScenario.Company.Id, Name = "aw_birthday_x_v1", MetaTemplateId = "1689556908129832",
            Status = WhatsAppTemplateStatus.Pending, MetaStatus = "PENDING",
        });
        objDbContext.SaveChanges();
        string sBody = new JsonObject
        {
            ["object"] = "whatsapp_business_account",
            ["entry"] = new JsonArray(new JsonObject
            {
                ["id"] = Waba,
                ["time"] = Unix(s_dtNow),
                ["changes"] = new JsonArray(new JsonObject
                {
                    ["value"] = new JsonObject
                    {
                        ["event"] = "REJECTED", ["message_template_id"] = 1689556908129832,
                        ["message_template_name"] = "aw_birthday_x_v1", ["message_template_language"] = "pt_BR",
                        ["reason"] = "INVALID_FORMAT", ["message_template_category"] = "MARKETING",
                    },
                    ["field"] = "message_template_status_update",
                }),
            }),
        }.ToJsonString();

        Assert.Equal(200, await PostAsync(objScenario, sBody));
        Assert.Equal(200, await PostAsync(objScenario, sBody));

        WhatsAppTemplate objTemplate = objDbContext.WhatsAppTemplates.Single();
        Assert.Equal(WhatsAppTemplateStatus.Rejected, objTemplate.Status);
        Assert.Contains("formato inválido", objTemplate.Reason);
        WhatsAppAlert objAlert = Assert.Single(objDbContext.WhatsAppAlerts);
        Assert.Contains("recusado", objAlert.Subject);
        Assert.Single(objDbContext.WhatsAppAccountEvents, evt => evt.Kind == WhatsAppAccountEventKind.TemplateStatusChanged);
    }

    [Fact]
    public async Task Alerts_GoToTheConfiguredEmails_OrStayInTheRecord()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Scenario objScenario = CreateScenario(objDbContext);
        FakeEmailSender objEmail = new FakeEmailSender();
        WhatsAppAlertService objService = new WhatsAppAlertService(objDbContext, objEmail,
            new ConfigurationReader(objDbContext, objScenario.Encryptor), NullLogger<WhatsAppAlertService>.Instance);

        await WhatsAppAlerts.EnqueueAsync(objDbContext, objScenario.Company.Id, "token", "Token", "Token vencido.", s_dtNow);
        objDbContext.SaveChanges();
        await objService.SendPendingAsync(s_dtNow, CancellationToken.None);
        Assert.Empty(objEmail.Sent);
        Assert.StartsWith("Sem e-mail para alertas", objDbContext.WhatsAppAlerts.Single().Error);

        objDbContext.Configurations.Add(new Configuration
        {
            IDConfiguration = ConfigurationKeys.WhatsAppAlertEmails, Value = "ti@regional.com.br, dono@regional.com.br",
        });
        // O mesmo assunto em menos de 12 horas não volta; depois disso, sim.
        Assert.False(await WhatsAppAlerts.EnqueueAsync(objDbContext, objScenario.Company.Id, "token", "Token", "x", s_dtNow.AddHours(1)));
        Assert.True(await WhatsAppAlerts.EnqueueAsync(objDbContext, objScenario.Company.Id, "token", "Token", "Token vencido.", s_dtNow.AddHours(13)));
        objDbContext.SaveChanges();
        await objService.SendPendingAsync(s_dtNow.AddHours(13), CancellationToken.None);

        Assert.Equal(["ti@regional.com.br", "dono@regional.com.br"], objEmail.Sent.Select(email => email.To));
        Assert.Equal("[AccessWifi] Token", objEmail.Sent[0].Subject);
        Assert.Contains("Empresa: Lojas Regional", objEmail.Sent[0].Body);
    }
}
