using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using AccessWifi.Api.Controllers;
using AccessWifi.Api.Features.Campaigns;
using AccessWifiService.Campaigns;
using AccessWifiService.WhatsApp;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Models.Campaigns;
using Models.DataBase;
using Models.Persistence;
using Models.Security;
using Models.WhatsApp;

namespace AccessWifi.Api.Tests;

/// <summary>
/// WhatsApp, parte 3 (PROPOSTA_WHATSAPP.md): com o interruptor ligado, a execução manda o texto aprovado para cada
/// cliente, espera os avisos e só então manda o e-mail ao gestor, com quem recebeu e quem não recebeu.
/// </summary>
public class CampaignWhatsAppTests
{
    // Terça, 29/09/2026 às 09:00 em Belém = 12:00 UTC.
    private static readonly DateTime s_dtNine = new DateTime(2026, 9, 29, 12, 0, 0, DateTimeKind.Utc);
    private const string AccessToken = "EAAG-token-secreto-de-teste-123";
    private const string PhoneNumberId = "106540352242922";
    private const string Message = "Feliz aniversário, {primeiro_nome}! Um abraço da {empresa}.";

    private sealed class StubHandler : HttpMessageHandler
    {
        public List<(HttpRequestMessage objRequest, string sBody)> ObjRequests { get; } = [];
        public required Func<string, HttpResponseMessage> ObjResponder { get; set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage objRequest, CancellationToken objCancellationToken)
        {
            string sBody = objRequest.Content is null ? "" : await objRequest.Content.ReadAsStringAsync(objCancellationToken);
            ObjRequests.Add((objRequest, sBody));
            return ObjResponder(sBody);
        }
    }

    private static HttpResponseMessage Json(string sBody, HttpStatusCode objStatus = HttpStatusCode.OK) =>
        new HttpResponseMessage(objStatus) { Content = new StringContent(sBody, Encoding.UTF8, "application/json") };

    private static HttpResponseMessage Accepted(string sRequest)
    {
        string sTo = JsonNode.Parse(sRequest)!["to"]!.GetValue<string>();
        return Json(new JsonObject
        {
            ["messaging_product"] = "whatsapp",
            ["contacts"] = new JsonArray(new JsonObject { ["input"] = sTo, ["wa_id"] = sTo.TrimStart('+') }),
            ["messages"] = new JsonArray(new JsonObject { ["id"] = "wamid." + sTo.TrimStart('+'), ["message_status"] = "accepted" }),
        }.ToJsonString());
    }

    private static HttpResponseMessage MetaError(int iCode, string sMessage, HttpStatusCode objStatus = HttpStatusCode.BadRequest) =>
        Json(new JsonObject
        {
            ["error"] = new JsonObject { ["message"] = sMessage, ["type"] = "OAuthException", ["code"] = iCode },
        }.ToJsonString(), objStatus);

    private sealed class Scenario
    {
        public required AppDbContext Db { get; init; }
        public required Company Company { get; init; }
        public required Unit Itaituba { get; init; }
        public required Unit Centro { get; init; }
        public required StubHandler Meta { get; init; }
        public FakeEmailSender Email { get; } = new FakeEmailSender();

        public CampaignEngine Engine() => new CampaignEngine(
            Db, Email,
            new CampaignWhatsAppSender(Db, TestHelpers.CreateEncryptor(),
                new WhatsAppCloudClient(new HttpClient(Meta), new WhatsAppOptions()),
                new CampaignWhatsAppOptions(), NullLogger<CampaignWhatsAppSender>.Instance),
            Options.Create(new CampaignEngineOptions { SelectionChunkSize = 100 }),
            NullLogger<CampaignEngine>.Instance);
    }

    private static Scenario CreateScenario(AppDbContext objDbContext, string sMessagingLimit = "TIER_250")
    {
        IEncryptor objEncryptor = TestHelpers.CreateEncryptor();
        Company objCompany = new Company
        {
            Name = "Lojas Regional", Slug = "regional", TimeZone = "America/Belem", WhatsAppEnabled = true,
        };
        objCompany.WhatsApp.PhoneNumberId = PhoneNumberId;
        objCompany.WhatsApp.BusinessAccountId = "102290129340398";
        objCompany.WhatsApp.AccessToken = objEncryptor.Encrypt(AccessToken)!;
        objCompany.WhatsApp.CheckPassed = true;
        objCompany.WhatsApp.MessagingLimit = sMessagingLimit;
        objCompany.WhatsApp.DisplayPhoneNumber = "+55 91 98888-7777";
        Unit objItaituba = new Unit
        {
            IDCompany = objCompany.Id, Name = "Itaituba", Slug = "itaituba", Email = "itaituba@regional.com.br",
            StorePhone = "93991234567",
        };
        // Sem o WhatsApp de atendimento (D16): os clientes dela ficam só no PDF.
        Unit objCentro = new Unit { IDCompany = objCompany.Id, Name = "Centro", Slug = "centro", Email = "centro@regional.com.br" };
        objDbContext.AddRange(objCompany, objItaituba, objCentro);
        objDbContext.CompanyCampaignKinds.Add(new CompanyCampaignKind { IDCompany = objCompany.Id, Kind = CampaignKind.Birthday });
        objDbContext.SaveChanges();
        return new Scenario
        {
            Db = objDbContext, Company = objCompany, Itaituba = objItaituba, Centro = objCentro,
            Meta = new StubHandler { ObjResponder = Accepted },
        };
    }

    /// <summary>Aniversariante de hoje (29/09) na unidade informada.</summary>
    private static Customer AddCustomer(Scenario objScenario, string sPhone, string sName, Unit objUnit, DateTime? dtOptOut = null)
    {
        DateTime dtLast = new DateTime(2026, 9, 1, 15, 0, 0, DateTimeKind.Utc);
        Customer objCustomer = new Customer
        {
            IDCompany = objScenario.Company.Id, Phone = sPhone, Name = sName, BirthDate = new DateOnly(1990, 9, 29),
            FirstVisitDate = new DateOnly(2025, 1, 10), FirstVisitAt = new DateTime(2025, 1, 10, 15, 0, 0, DateTimeKind.Utc),
            LastVisitAt = dtLast, LastVisitDate = DateOnly.FromDateTime(dtLast), IDLastUnit = objUnit.Id,
            WhatsAppOptOutAt = dtOptOut,
        };
        objScenario.Db.Customers.Add(objCustomer);
        objScenario.Db.CustomerUnits.Add(new CustomerUnit
        {
            IDCustomer = objCustomer.Id, IDUnit = objUnit.Id, FirstVisitAt = dtLast, LastVisitAt = dtLast,
        });
        objScenario.Db.SaveChanges();
        return objCustomer;
    }

    /// <summary>A campanha com as versões informadas (a última é a atual) e, se pedido, o texto aprovado de uma delas.</summary>
    private static Campaign AddCampaign(Scenario objScenario, string[] arrVersions, int? iApprovedVersion)
    {
        Campaign objCampaign = new Campaign
        {
            IDCompany = objScenario.Company.Id, Kind = CampaignKind.Birthday, Name = "Aniversário",
            ConfigJson = new CampaignConfig { Message = arrVersions[^1], SendTime = "09:00" }.ToJson(),
            CurrentVersion = arrVersions.Length, NextRunAt = s_dtNine,
        };
        objScenario.Db.Campaigns.Add(objCampaign);
        for (int iIndex = 0; iIndex < arrVersions.Length; iIndex++)
        {
            objScenario.Db.CampaignVersions.Add(new CampaignVersion
            {
                IDCampaign = objCampaign.Id, Number = iIndex + 1, Name = objCampaign.Name,
                ConfigJson = new CampaignConfig { Message = arrVersions[iIndex], SendTime = "09:00" }.ToJson(),
            });
        }
        if (iApprovedVersion is int iVersion)
        {
            WhatsAppTemplateContent objContent = WhatsAppTemplateBuilder.Build(arrVersions[iVersion - 1], objScenario.Company.Name);
            objScenario.Db.WhatsAppTemplates.Add(new WhatsAppTemplate
            {
                IDCompany = objScenario.Company.Id, IDCampaign = objCampaign.Id, VersionNumber = iVersion,
                Name = WhatsAppTemplateBuilder.NameFor(objCampaign.Kind, objCampaign.Id, iVersion),
                BodyText = objContent.BodyText, FooterText = objContent.FooterText,
                ParametersJson = WhatsAppTemplateBuilder.ParametersToJson(objContent.Parameters),
                Status = WhatsAppTemplateStatus.Approved, MetaStatus = "APPROVED", MetaTemplateId = "1689556908129832",
            });
        }
        objScenario.Db.SaveChanges();
        return objCampaign;
    }

    private static CampaignRecipient Recipient(AppDbContext objDbContext, Customer objCustomer) =>
        objDbContext.CampaignRecipients.Single(recipient => recipient.IDCustomer == objCustomer.Id);

    [Fact]
    public async Task SendsByWhatsApp_WaitsForTheNotices_ThenEmailsWhoReceivedAndWhoNot()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Scenario objScenario = CreateScenario(objDbContext);
        objScenario.Meta.ObjResponder = sBody => JsonNode.Parse(sBody)!["to"]!.GetValue<string>() == "+5593991110004"
            ? MetaError(131026, "(#131026) Message Undeliverable.")
            : Accepted(sBody);
        Customer objAna = AddCustomer(objScenario, "93991110001", "Ana Souza", objScenario.Itaituba);
        Customer objBia = AddCustomer(objScenario, "93991110002", "Bia", objScenario.Itaituba,
            dtOptOut: new DateTime(2026, 9, 10, 15, 0, 0, DateTimeKind.Utc));
        Customer objCaio = AddCustomer(objScenario, "93991110003", "Caio", objScenario.Centro);
        Customer objDavi = AddCustomer(objScenario, "93991110004", "Davi", objScenario.Itaituba);
        Campaign objCampaign = AddCampaign(objScenario, [Message], iApprovedVersion: 1);

        await objScenario.Engine().TickAsync(s_dtNine.AddSeconds(3));

        CampaignRun objRun = Assert.Single(objDbContext.CampaignRuns);
        Assert.True(objRun.WhatsAppEnabled);
        Assert.Equal(1, objRun.WhatsAppVersionNumber);
        Assert.Equal(s_dtNine.AddSeconds(3).AddMinutes(10), objRun.WhatsAppWaitUntil);
        Assert.Equal(CampaignWhatsAppStatus.Accepted, Recipient(objDbContext, objAna).WhatsAppStatus);
        Assert.Equal(CampaignWhatsAppStatus.Skipped, Recipient(objDbContext, objBia).WhatsAppStatus);
        Assert.Equal("Pediu para sair do WhatsApp automático (respondeu SAIR) em 10/09/2026.",
            Recipient(objDbContext, objBia).WhatsAppError);
        Assert.Equal("A unidade não tem WhatsApp de atendimento cadastrado.", Recipient(objDbContext, objCaio).WhatsAppError);
        Assert.Equal(CampaignWhatsAppStatus.Failed, Recipient(objDbContext, objDavi).WhatsAppStatus);
        Assert.Equal("Este número não tem WhatsApp (ou não recebe mensagens de empresas).",
            Recipient(objDbContext, objDavi).WhatsAppError);
        // Só Ana e Davi foram à Meta; ainda nenhum e-mail (esperando os avisos).
        Assert.Equal(2, objScenario.Meta.ObjRequests.Count);
        Assert.Empty(objScenario.Email.Sent);

        // O pedido: o texto aprovado, o número com +55 e os valores na ordem; o token só no cabeçalho.
        (HttpRequestMessage objRequest, string sBody) = objScenario.Meta.ObjRequests[0];
        Assert.Equal($"/v26.0/{PhoneNumberId}/messages", objRequest.RequestUri!.AbsolutePath);
        Assert.Equal(AccessToken, objRequest.Headers.Authorization!.Parameter);
        JsonNode objJson = JsonNode.Parse(sBody)!;
        Assert.Equal("+5593991110001", objJson["to"]!.GetValue<string>());
        Assert.Equal("template", objJson["type"]!.GetValue<string>());
        Assert.Equal("pt_BR", objJson["template"]!["language"]!["code"]!.GetValue<string>());
        Assert.Equal("""[{"type":"text","text":"Ana"},{"type":"text","text":"(93) 99123-4567"}]""",
            objJson["template"]!["components"]![0]!["parameters"]!.ToJsonString());

        // O registro de cada mensagem, sem o token.
        WhatsAppMessage objSent = objDbContext.WhatsAppMessages.Single(message => message.IDCustomer == objAna.Id);
        Assert.Equal(WhatsAppMessageStatus.Accepted, objSent.Status);
        Assert.Equal("wamid.5593991110001", objSent.MetaMessageId);
        Assert.Equal(objCampaign.Id, objSent.IDCampaign);
        Assert.Equal(
            "Feliz aniversário, Ana! Um abraço da Lojas Regional.\n\nFale com a loja pelo WhatsApp (93) 99123-4567 "
            + "sempre que precisar.\n\nPara não receber mais, responda SAIR.",
            objSent.Text);
        WhatsAppMessage objFailed = objDbContext.WhatsAppMessages.Single(message => message.IDCustomer == objDavi.Id);
        Assert.Equal(WhatsAppMessageStatus.Failed, objFailed.Status);
        Assert.Equal(131026, objFailed.ErrorCode);
        Assert.All(objDbContext.WhatsAppMessages, message =>
            Assert.DoesNotContain(AccessToken, message.RequestJson + message.ResponseJson));

        // Dentro dos 10 minutos, o e-mail espera; passado o prazo, sai.
        await objScenario.Engine().TickAsync(s_dtNine.AddMinutes(5));
        Assert.Empty(objScenario.Email.Sent);
        await objScenario.Engine().TickAsync(s_dtNine.AddMinutes(11));

        FakeEmailSender.Email objItaituba = objScenario.Email.Sent.Single(email => email.To == "itaituba@regional.com.br");
        Assert.Equal("Campanha Aniversário — Itaituba — 29/09/2026 (3 clientes: 1 pelo WhatsApp)", objItaituba.Subject);
        Assert.Contains("1 cliente já recebeu a mensagem pelo WhatsApp.", objItaituba.Body);
        Assert.Contains("2 clientes não receberam e estão no PDF em anexo", objItaituba.Body);
        Assert.Contains("Mensagem para quem não recebeu:", objItaituba.Body);
        Assert.NotEmpty(objItaituba.Attachment!);
        FakeEmailSender.Email objCentro = objScenario.Email.Sent.Single(email => email.To == "centro@regional.com.br");
        Assert.EndsWith("(1 cliente: nenhum pelo WhatsApp)", objCentro.Subject);
        Assert.Contains("Nenhum cliente recebeu a mensagem pelo WhatsApp.", objCentro.Body);
        Assert.Equal(CampaignRunStatus.Completed, objDbContext.CampaignRuns.Single().Status);

        // A tela: os números do WhatsApp na execução e o filtro de quem não recebeu.
        CampaignsController objController = new CampaignsController(objDbContext);
        TestHelpers.SetCompanyUser(objController, objDbContext, objScenario.Company.Id, "gerente");
        CampaignRunDto objDto = Assert.IsType<CampaignRunDto>(
            Assert.IsType<OkObjectResult>((await objController.Run(objRun.Id, null, CancellationToken.None)).Result).Value);
        Assert.Equal(1, objDto.WhatsApp!.Counts[CampaignWhatsAppStatus.Accepted]);
        Assert.Equal(2, objDto.WhatsApp.Counts[CampaignWhatsAppStatus.Skipped]);
        Assert.Equal(1, objDto.WhatsApp.Counts[CampaignWhatsAppStatus.Failed]);
        PagedDto<CampaignRecipientDto> objNotReceived = Assert.IsType<PagedDto<CampaignRecipientDto>>(
            Assert.IsType<OkObjectResult>((await objController.Recipients(
                objRun.Id, null, null, 1, 50, "NotReceived", CancellationToken.None)).Result).Value);
        Assert.Equal(3, objNotReceived.Total);
        Assert.DoesNotContain(objNotReceived.Items, item => item.Name == "Ana Souza");
    }

    [Fact]
    public async Task WhenTheMetaConfirmsEveryDelivery_TheEmailDoesNotWaitTheWholeTenMinutes()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Scenario objScenario = CreateScenario(objDbContext);
        AddCustomer(objScenario, "93991110001", "Ana", objScenario.Itaituba);
        AddCampaign(objScenario, [Message], iApprovedVersion: 1);
        await objScenario.Engine().TickAsync(s_dtNine.AddSeconds(3));
        Assert.Empty(objScenario.Email.Sent);

        // O aviso de entrega da Meta (parte 4) chega um minuto depois.
        JsonNode objPayload = JsonNode.Parse(new JsonObject
        {
            ["object"] = "whatsapp_business_account",
            ["entry"] = new JsonArray(new JsonObject
            {
                ["id"] = "102290129340398",
                ["changes"] = new JsonArray(new JsonObject
                {
                    ["field"] = "messages",
                    ["value"] = new JsonObject
                    {
                        ["metadata"] = new JsonObject { ["phone_number_id"] = PhoneNumberId },
                        ["statuses"] = new JsonArray(new JsonObject
                        {
                            ["id"] = "wamid.5593991110001", ["status"] = "delivered",
                            ["timestamp"] = new DateTimeOffset(s_dtNine.AddMinutes(1)).ToUnixTimeSeconds().ToString(),
                        }),
                    },
                }),
            }),
        }.ToJsonString())!;
        await new WhatsAppWebhookProcessor(objDbContext, objDbContext.Companies.Single(), s_dtNine.AddMinutes(1))
            .ProcessAsync(objPayload);
        objDbContext.SaveChanges();

        await objScenario.Engine().TickAsync(s_dtNine.AddMinutes(2));
        FakeEmailSender.Email objEmail = Assert.Single(objScenario.Email.Sent);
        Assert.EndsWith("(1 cliente: 1 pelo WhatsApp)", objEmail.Subject);
        Assert.Equal(CampaignWhatsAppStatus.Delivered, objDbContext.CampaignRecipients.Single().WhatsAppStatus);
    }

    [Fact]
    public async Task WithoutAnApprovedText_EveryoneGoesToThePdf_AndTheEmailDoesNotWait()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Scenario objScenario = CreateScenario(objDbContext);
        Customer objAna = AddCustomer(objScenario, "93991110001", "Ana", objScenario.Itaituba);
        AddCampaign(objScenario, [Message], iApprovedVersion: null);

        await objScenario.Engine().TickAsync(s_dtNine.AddSeconds(3));

        Assert.Empty(objScenario.Meta.ObjRequests);
        Assert.Equal(CampaignWhatsAppStatus.Skipped, Recipient(objDbContext, objAna).WhatsAppStatus);
        Assert.StartsWith("Nenhum texto desta campanha foi aprovado", Recipient(objDbContext, objAna).WhatsAppError);
        Assert.StartsWith("Nenhum texto desta campanha foi aprovado", objDbContext.CampaignRuns.Single().WhatsAppError);
        FakeEmailSender.Email objEmail = Assert.Single(objScenario.Email.Sent);
        Assert.EndsWith("(1 cliente: nenhum pelo WhatsApp)", objEmail.Subject);
    }

    [Fact]
    public async Task InvalidToken_StopsTheWhatsApp_AndTheRestGoesToThePdf()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Scenario objScenario = CreateScenario(objDbContext);
        objScenario.Meta.ObjResponder = _ => MetaError(190, "Error validating access token", HttpStatusCode.Unauthorized);
        AddCustomer(objScenario, "93991110001", "Ana", objScenario.Itaituba);
        AddCustomer(objScenario, "93991110002", "Bia", objScenario.Itaituba);
        AddCampaign(objScenario, [Message], iApprovedVersion: 1);

        await objScenario.Engine().TickAsync(s_dtNine.AddSeconds(3));

        Assert.Single(objScenario.Meta.ObjRequests); // o segundo nem foi
        // O primeiro foi à Meta e falhou; o outro nem foi. Os dois vão para o PDF com o motivo.
        Assert.Equal(
            [CampaignWhatsAppStatus.Failed, CampaignWhatsAppStatus.Skipped],
            objDbContext.CampaignRecipients.OrderBy(recipient => recipient.Id).Select(recipient => recipient.WhatsAppStatus));
        Assert.All(objDbContext.CampaignRecipients, recipient =>
            Assert.StartsWith("Token inválido ou vencido", recipient.WhatsAppError));
        Assert.StartsWith("Token inválido ou vencido", objDbContext.CampaignRuns.Single().WhatsAppError);
        Assert.Single(objScenario.Email.Sent);
    }

    [Fact]
    public async Task MetaBusy_TriesAgainLater()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Scenario objScenario = CreateScenario(objDbContext);
        int iCalls = 0;
        objScenario.Meta.ObjResponder = sBody => ++iCalls == 1
            ? MetaError(130429, "(#130429) Rate limit hit", HttpStatusCode.TooManyRequests)
            : Accepted(sBody);
        Customer objAna = AddCustomer(objScenario, "93991110001", "Ana", objScenario.Itaituba);
        AddCampaign(objScenario, [Message], iApprovedVersion: 1);

        await objScenario.Engine().TickAsync(s_dtNine.AddSeconds(3));
        Assert.Equal(CampaignWhatsAppStatus.Pending, Recipient(objDbContext, objAna).WhatsAppStatus);
        Assert.Empty(objDbContext.WhatsAppMessages); // a tentativa passageira não vira mensagem

        await objScenario.Engine().TickAsync(s_dtNine.AddSeconds(30));
        Assert.Equal(1, iCalls); // ainda esperando a hora de tentar de novo

        await objScenario.Engine().TickAsync(s_dtNine.AddSeconds(70));
        Assert.Equal(CampaignWhatsAppStatus.Accepted, Recipient(objDbContext, objAna).WhatsAppStatus);
        Assert.Empty(objScenario.Email.Sent); // agora espera os avisos
    }

    [Fact]
    public async Task NewTextInReview_UsesTheLastApprovedOne_AndRespectsTheDailyLimit()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Scenario objScenario = CreateScenario(objDbContext, sMessagingLimit: "TIER_1");
        Customer objAna = AddCustomer(objScenario, "93991110001", "Ana", objScenario.Itaituba);
        Customer objBia = AddCustomer(objScenario, "93991110002", "Bia", objScenario.Itaituba);
        AddCampaign(objScenario, [Message, "Parabéns, {primeiro_nome}! Texto novo em análise."], iApprovedVersion: 1);

        await objScenario.Engine().TickAsync(s_dtNine.AddSeconds(3));

        CampaignRun objRun = objDbContext.CampaignRuns.Single();
        Assert.Equal(2, objRun.VersionNumber);
        Assert.Equal(1, objRun.WhatsAppVersionNumber); // saiu o texto aprovado da v1
        WhatsAppMessage objSent = Assert.Single(objDbContext.WhatsAppMessages);
        Assert.Equal(1, objSent.VersionNumber);
        Assert.StartsWith("Feliz aniversário, ", objSent.Text);
        // Limite de 1 cliente por dia: o segundo fica para o PDF.
        string[] arrStatuses = [Recipient(objDbContext, objAna).WhatsAppStatus, Recipient(objDbContext, objBia).WhatsAppStatus];
        Assert.Contains(CampaignWhatsAppStatus.Accepted, arrStatuses);
        Assert.Contains(CampaignWhatsAppStatus.Skipped, arrStatuses);
        Assert.Single(objDbContext.CampaignRecipients, recipient => recipient.WhatsAppError != null
            && recipient.WhatsAppError.StartsWith("Limite do dia da Meta atingido"));
    }

    [Fact]
    public async Task CancelledBeforeSending_NobodyReceives()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Scenario objScenario = CreateScenario(objDbContext);
        Customer objAna = AddCustomer(objScenario, "93991110001", "Ana", objScenario.Itaituba);
        AddCampaign(objScenario, [Message], iApprovedVersion: 1);
        CampaignEngine objEngine = objScenario.Engine();
        await objEngine.ScheduleDueAsync(s_dtNine.AddSeconds(3));
        await objEngine.SelectRecipientsAsync(objDbContext.CampaignRuns.Single().Id, s_dtNine.AddSeconds(3));
        Assert.Equal(CampaignWhatsAppStatus.Pending, Recipient(objDbContext, objAna).WhatsAppStatus);

        objDbContext.CampaignRuns.Single().Status = CampaignRunStatus.Cancelled;
        objDbContext.SaveChanges();
        await objEngine.TickAsync(s_dtNine.AddSeconds(8));

        Assert.Empty(objScenario.Meta.ObjRequests);
        Assert.Equal(CampaignWhatsAppStatus.Skipped, Recipient(objDbContext, objAna).WhatsAppStatus);
        Assert.Equal("Execução cancelada antes do envio.", Recipient(objDbContext, objAna).WhatsAppError);
    }

    [Fact]
    public void Pdf_WithWhatsApp_ListsWhoDidNotReceiveFirst_AndTheEmailSaysWhenEveryoneReceived()
    {
        CampaignPdfData objData = new CampaignPdfData(
            "Lojas Regional", "Itaituba", "Aniversário", CampaignKind.Birthday, new DateOnly(2026, 9, 29), Message,
            null, new ThemeColors(),
            [
                new CampaignPdfRow("Ana", "93991110001", "", "ter, 29/09 · 36 anos", true, "Feliz aniversário, Ana!",
                    CampaignWhatsAppStatus.Delivered),
                new CampaignPdfRow("Bia", "93991110002", "", "ter, 29/09 · 36 anos", true, "Feliz aniversário, Bia!",
                    CampaignWhatsAppStatus.Skipped, "Pediu para sair do WhatsApp automático (respondeu SAIR) em 10/09/2026."),
            ],
            WhatsApp: true);

        Assert.Equal(["Bia"], objData.NotReceivedRows.Select(row => row.Name));
        Assert.Equal(["Ana"], objData.ReceivedRows.Select(row => row.Name));
        Assert.NotEmpty(CampaignPdf.Build(objData));

        CampaignPdfData objAll = objData with { Rows = [objData.Rows[0]] };
        Assert.Contains("Todos receberam: o PDF em anexo é só para conferência.", CampaignEmailText.Body(objAll));
        Assert.DoesNotContain("Mensagem para", CampaignEmailText.Body(objAll));
        Assert.Equal(250, CampaignWhatsAppSender.DailyLimit("TIER_250"));
        Assert.Equal(2000, CampaignWhatsAppSender.DailyLimit("TIER_2K"));
        Assert.Equal(int.MaxValue, CampaignWhatsAppSender.DailyLimit("TIER_UNLIMITED"));
        Assert.Equal(250, CampaignWhatsAppSender.DailyLimit(""));
    }
}
