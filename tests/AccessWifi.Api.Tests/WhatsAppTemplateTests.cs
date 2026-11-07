using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using AccessWifi.Api.Controllers;
using AccessWifi.Api.Features;
using AccessWifi.Api.Features.Campaigns;
using AccessWifi.Api.Features.WhatsApp;
using AccessWifiService.WhatsApp;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Models.Campaigns;
using Models.DataBase;
using Models.Persistence;
using Models.Security;
using Models.WhatsApp;

namespace AccessWifi.Api.Tests;

/// <summary>
/// WhatsApp, parte 2 (PROPOSTA_WHATSAPP.md, D8 e D10): o texto da campanha vira modelo da Meta, o serviço manda
/// para a análise e acompanha a situação, e a tela mostra o modelo de cada campanha e o WhatsApp das unidades.
/// </summary>
public class WhatsAppTemplateTests
{
    private const string BusinessAccountId = "102290129340398";
    private const string AccessToken = "EAAG-token-secreto-de-teste-123";
    private const string TemplateId = "1689556908129832";

    // Terça, 22/10/2026 às 10:00 UTC.
    private static readonly DateTime s_dtNow = new DateTime(2026, 10, 22, 10, 0, 0, DateTimeKind.Utc);

    /// <summary>Meta falsa: guarda os pedidos (com o corpo) e responde conforme a função informada.</summary>
    private sealed class StubHandler : HttpMessageHandler
    {
        public List<(HttpRequestMessage objRequest, string sBody)> ObjRequests { get; } = [];
        public required Func<HttpRequestMessage, HttpResponseMessage> ObjResponder { get; set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage objRequest, CancellationToken objCancellationToken)
        {
            string sBody = objRequest.Content is null ? "" : await objRequest.Content.ReadAsStringAsync(objCancellationToken);
            ObjRequests.Add((objRequest, sBody));
            return ObjResponder(objRequest);
        }
    }

    private static HttpResponseMessage Json(string sBody, HttpStatusCode objStatus = HttpStatusCode.OK) =>
        new HttpResponseMessage(objStatus) { Content = new StringContent(sBody, Encoding.UTF8, "application/json") };

    /// <summary>A Meta de sempre: cria em análise e, na consulta, devolve a situação informada.</summary>
    private static Func<HttpRequestMessage, HttpResponseMessage> Meta(Func<string> objStatus) => objRequest =>
        objRequest.Method == HttpMethod.Post
            ? Json($$"""{"id":"{{TemplateId}}","status":"PENDING","category":"MARKETING"}""")
            : Json($$"""{"id":"{{TemplateId}}","name":"x","status":"{{objStatus()}}","category":"MARKETING","rejected_reason":"{{(objStatus() == "REJECTED" ? "INVALID_FORMAT" : "NONE")}}"}""");

    private sealed class Scenario
    {
        public required AppDbContext Db { get; init; }
        public required Company Company { get; init; }
        public required Unit Unit { get; init; }
        public required StubHandler Handler { get; init; }
        public required IEncryptor Encryptor { get; init; }

        public WhatsAppTemplateService Service() => new WhatsAppTemplateService(
            Db, Encryptor, new WhatsAppCloudClient(new HttpClient(Handler), new WhatsAppOptions()),
            new WhatsAppTemplateOptions(), NullLogger<WhatsAppTemplateService>.Instance);
    }

    private static Scenario CreateScenario(AppDbContext objDbContext, bool bCheckPassed = true)
    {
        IEncryptor objEncryptor = TestHelpers.CreateEncryptor();
        Company objCompany = new Company { Name = "Lojas Regional", Slug = "regional", TimeZone = "America/Belem" };
        objCompany.WhatsApp.PhoneNumberId = "106540352242922";
        objCompany.WhatsApp.BusinessAccountId = BusinessAccountId;
        objCompany.WhatsApp.AccessToken = objEncryptor.Encrypt(AccessToken)!;
        objCompany.WhatsApp.CheckPassed = bCheckPassed;
        // Número conferido agora: a conferência de 6 em 6 horas não entra nestes testes (tem o dela).
        objCompany.WhatsApp.CheckedAt = s_dtNow;
        Unit objUnit = new Unit { IDCompany = objCompany.Id, Name = "Itaituba", Slug = "itaituba", StorePhone = "93991234567" };
        objDbContext.AddRange(objCompany, objUnit);
        objDbContext.SaveChanges();
        return new Scenario
        {
            Db = objDbContext,
            Company = objCompany,
            Unit = objUnit,
            Handler = new StubHandler { ObjResponder = Meta(() => "PENDING") },
            Encryptor = objEncryptor,
        };
    }

    private static Campaign AddCampaign(
        Scenario objScenario, string sMessage, string sKind = CampaignKind.Birthday, string sStatus = CampaignStatus.Active)
    {
        Campaign objCampaign = new Campaign
        {
            IDCompany = objScenario.Company.Id,
            Kind = sKind,
            Name = CampaignKind.Label(sKind),
            Status = sStatus,
            ConfigJson = new CampaignConfig { Message = sMessage }.ToJson(),
            CurrentVersion = 1,
            CreatedAt = s_dtNow.AddDays(-1),
        };
        objScenario.Db.Campaigns.Add(objCampaign);
        objScenario.Db.CampaignVersions.Add(new CampaignVersion
        {
            IDCampaign = objCampaign.Id, Number = 1, Name = objCampaign.Name, ConfigJson = objCampaign.ConfigJson,
        });
        objScenario.Db.SaveChanges();
        return objCampaign;
    }

    /// <summary>Edição da campanha: versão nova com outro texto (como o CampaignsController faz).</summary>
    private static void Edit(Scenario objScenario, Campaign objCampaign, string sMessage)
    {
        objCampaign.ConfigJson = new CampaignConfig { Message = sMessage }.ToJson();
        objCampaign.CurrentVersion++;
        objScenario.Db.CampaignVersions.Add(new CampaignVersion
        {
            IDCampaign = objCampaign.Id, Number = objCampaign.CurrentVersion, Name = objCampaign.Name,
            ConfigJson = objCampaign.ConfigJson,
        });
        objScenario.Db.SaveChanges();
    }

    private static T Ok<T>(ActionResult<T> objResult) =>
        Assert.IsType<T>(Assert.IsType<OkObjectResult>(objResult.Result).Value);

    private static string Error<T>(ActionResult<T> objResult) =>
        Assert.IsType<ErrorResponse>(Assert.IsAssignableFrom<ObjectResult>(objResult.Result).Value).Error;

    // ------------------------------------------------------------------ Montagem do modelo

    [Fact]
    public void Builder_TurnsFieldsIntoVariables_WritesTheCompany_AndEndsWithTheStorePhoneAndSair()
    {
        WhatsAppTemplateContent objContent = WhatsAppTemplateBuilder.Build(
            "Feliz aniversário, {primeiro_nome}! A {empresa} preparou um presente na {unidade}. 🎉", "Lojas Regional");

        Assert.Null(objContent.Invalid);
        Assert.Equal(
            "Feliz aniversário, {{1}}! A Lojas Regional preparou um presente na {{2}}. 🎉\n\n"
            + "Fale com a loja pelo WhatsApp {{3}} sempre que precisar.",
            objContent.BodyText);
        Assert.Equal("Para não receber mais, responda SAIR.", objContent.FooterText);
        Assert.Equal(
            [WhatsAppTemplateField.FirstName, WhatsAppTemplateField.Unit, WhatsAppTemplateField.StorePhone],
            objContent.Parameters);

        string sPreview = WhatsAppTemplateBuilder.Preview(objContent.BodyText, objContent.FooterText,
            objContent.Parameters, new WhatsAppTemplateSamples("Itaituba", "(93) 99123-4567"));
        Assert.Equal(
            "Feliz aniversário, Maria! A Lojas Regional preparou um presente na Itaituba. 🎉\n\n"
            + "Fale com a loja pelo WhatsApp (93) 99123-4567 sempre que precisar.\n\n"
            + "Para não receber mais, responda SAIR.",
            sPreview);
    }

    [Fact]
    public void Builder_FollowsTheMetaRules()
    {
        // Começa com campo: a Meta recusa, então ganha um "Olá, ".
        WhatsAppTemplateContent objStart = WhatsAppTemplateBuilder.Build(
            "{primeiro_nome}, hoje faz {anos_de_cadastro} ano(s) com a gente!", "Lojão");
        Assert.StartsWith("Olá, {{1}}, hoje faz {{2}} ano(s) com a gente!", objStart.BodyText);
        Assert.Null(objStart.Invalid);

        // O mesmo campo duas vezes: duas variáveis, em sequência.
        WhatsAppTemplateContent objTwice = WhatsAppTemplateBuilder.Build("Oi {nome}! Tudo bem, {nome}?", "Lojão");
        Assert.StartsWith("Oi {{1}}! Tudo bem, {{2}}?", objTwice.BodyText);
        Assert.Equal(3, objTwice.Parameters.Count);

        // Grande demais com o final automático.
        WhatsAppTemplateContent objLong = WhatsAppTemplateBuilder.Build("Oi " + new string('a', 990), "Lojão");
        Assert.Contains("Tire pelo menos", objLong.Invalid);

        // Chaves duplas confundem a Meta.
        Assert.Contains("chaves duplas", WhatsAppTemplateBuilder.Build("Oi {{amigo}}", "Lojão").Invalid);

        // {{10}} não vira "{{1}}0" na troca.
        Assert.Equal("a J b", WhatsAppTemplateBuilder.Render("a {{10}} b",
            ["1", "2", "3", "4", "5", "6", "7", "8", "9", "J"]));

        Assert.Matches("^[a-z0-9_]+$", WhatsAppTemplateBuilder.NameFor(
            CampaignKind.SignupAnniversary, Guid.NewGuid(), 12));
    }

    [Fact]
    public void Lookup_UsesTheLastApprovedTextWhileTheNewOneIsInReview()
    {
        WhatsAppTemplate objOld = new WhatsAppTemplate
        {
            BodyText = WhatsAppTemplateBuilder.Build("Parabéns, {primeiro_nome}!", "Lojão").BodyText,
            FooterText = WhatsAppTemplateBuilder.Footer,
            Status = WhatsAppTemplateStatus.Approved,
        };
        WhatsAppTemplate objNew = new WhatsAppTemplate
        {
            BodyText = WhatsAppTemplateBuilder.Build("Feliz aniversário, {primeiro_nome}!", "Lojão").BodyText,
            FooterText = WhatsAppTemplateBuilder.Footer,
            Status = WhatsAppTemplateStatus.Pending,
        };

        WhatsAppCampaignTemplate objFound = WhatsAppTemplateLookup.ForCampaign(
            "Lojão", [(2, "Feliz aniversário, {primeiro_nome}!"), (1, "Parabéns, {primeiro_nome}!")], [objOld, objNew]);
        Assert.Same(objNew, objFound.Current);
        Assert.Same(objOld, objFound.Fallback);
        Assert.Equal(1, objFound.FallbackVersion);

        objNew.Status = WhatsAppTemplateStatus.Approved;
        objFound = WhatsAppTemplateLookup.ForCampaign(
            "Lojão", [(2, "Feliz aniversário, {primeiro_nome}!"), (1, "Parabéns, {primeiro_nome}!")], [objOld, objNew]);
        Assert.Same(objNew, objFound.Current);
        Assert.Null(objFound.Fallback);
    }

    // ------------------------------------------------------------------ Serviço

    [Fact]
    public async Task Service_SubmitsTheCampaignText_ThenFollowsTheReview()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Scenario objScenario = CreateScenario(objDbContext);
        string sMetaStatus = "PENDING";
        objScenario.Handler.ObjResponder = Meta(() => sMetaStatus);
        Campaign objBirthday = AddCampaign(objScenario, "Feliz aniversário, {primeiro_nome}! Um abraço da {empresa}.");
        // Mesmo texto em outra campanha: um modelo só. Encerrada: nenhum.
        AddCampaign(objScenario, "Feliz aniversário, {primeiro_nome}! Um abraço da {empresa}.", CampaignKind.WeMissYou);
        AddCampaign(objScenario, "Outro texto, {primeiro_nome}.", CampaignKind.FrequentCustomer, CampaignStatus.Finished);

        await objScenario.Service().TickAsync(s_dtNow, CancellationToken.None);

        WhatsAppTemplate objTemplate = Assert.Single(objDbContext.WhatsAppTemplates);
        Assert.Equal(objBirthday.Id, objTemplate.IDCampaign);
        Assert.Equal(WhatsAppTemplateStatus.Pending, objTemplate.Status);
        Assert.Equal("PENDING", objTemplate.MetaStatus);
        Assert.Equal(TemplateId, objTemplate.MetaTemplateId);
        Assert.Equal(1, objTemplate.Attempts);
        Assert.StartsWith("aw_birthday_", objTemplate.Name);

        // O pedido: marketing, pt_BR, corpo com exemplos reais da unidade e rodapé do SAIR; o token só no cabeçalho.
        (HttpRequestMessage objRequest, string sBody) = Assert.Single(objScenario.Handler.ObjRequests);
        Assert.Equal(HttpMethod.Post, objRequest.Method);
        Assert.Equal($"/v26.0/{BusinessAccountId}/message_templates", objRequest.RequestUri!.AbsolutePath);
        Assert.Equal(AccessToken, objRequest.Headers.Authorization!.Parameter);
        JsonNode objJson = JsonNode.Parse(sBody)!;
        Assert.Equal(objTemplate.Name, objJson["name"]!.GetValue<string>());
        Assert.Equal("pt_BR", objJson["language"]!.GetValue<string>());
        Assert.Equal("MARKETING", objJson["category"]!.GetValue<string>());
        Assert.Equal("BODY", objJson["components"]![0]!["type"]!.GetValue<string>());
        Assert.Equal(
            "Feliz aniversário, {{1}}! Um abraço da Lojas Regional.\n\nFale com a loja pelo WhatsApp {{2}} sempre que precisar.",
            objJson["components"]![0]!["text"]!.GetValue<string>());
        Assert.Equal("""[["Maria","(93) 99123-4567"]]""", objJson["components"]![0]!["example"]!["body_text"]!.ToJsonString());
        Assert.Equal("FOOTER", objJson["components"]![1]!["type"]!.GetValue<string>());
        Assert.Equal("Para não receber mais, responda SAIR.", objJson["components"]![1]!["text"]!.GetValue<string>());

        WhatsAppAccountEvent objSubmitted = Assert.Single(objDbContext.WhatsAppAccountEvents);
        Assert.Equal(WhatsAppAccountEventKind.TemplateSubmitted, objSubmitted.Kind);
        Assert.Contains("em análise", objSubmitted.Details);
        Assert.Contains("message_templates", objSubmitted.RequestJson);
        Assert.Contains("Lojas Regional", objSubmitted.RequestJson);
        Assert.DoesNotContain(AccessToken, objSubmitted.RequestJson + objSubmitted.ResponseJson);

        // Em análise: só confere de novo depois de 15 minutos.
        await objScenario.Service().TickAsync(s_dtNow.AddMinutes(5), CancellationToken.None);
        Assert.Single(objScenario.Handler.ObjRequests);

        sMetaStatus = "APPROVED";
        await objScenario.Service().TickAsync(s_dtNow.AddMinutes(16), CancellationToken.None);
        Assert.Equal(2, objScenario.Handler.ObjRequests.Count);
        Assert.Equal($"/v26.0/{TemplateId}", objScenario.Handler.ObjRequests[1].objRequest.RequestUri!.AbsolutePath);
        objTemplate = Assert.Single(objDbContext.WhatsAppTemplates);
        Assert.Equal(WhatsAppTemplateStatus.Approved, objTemplate.Status);
        WhatsAppAccountEvent objChanged = objDbContext.WhatsAppAccountEvents.OrderBy(evt => evt.Id).Last();
        Assert.Equal(WhatsAppAccountEventKind.TemplateStatusChanged, objChanged.Kind);
        Assert.Contains("em análise → aprovado", objChanged.Details);

        // Aprovado: confere só a cada 6 horas.
        await objScenario.Service().TickAsync(s_dtNow.AddHours(1), CancellationToken.None);
        Assert.Equal(2, objScenario.Handler.ObjRequests.Count);

        // Texto editado: modelo novo para a v2; o aprovado da v1 continua valendo enquanto isso.
        Edit(objScenario, objBirthday, "Parabéns, {primeiro_nome}! Passe na {unidade} para pegar seu presente.");
        await objScenario.Service().TickAsync(s_dtNow.AddHours(2), CancellationToken.None);
        WhatsAppTemplate objV2 = objDbContext.WhatsAppTemplates.Single(template => template.VersionNumber == 2);
        Assert.Equal(WhatsAppTemplateStatus.Pending, objV2.Status);
        Assert.EndsWith("_v2", objV2.Name);
    }

    [Fact]
    public async Task Service_MetaFailures_RetryLater_OrAskToFixTheText()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Scenario objScenario = CreateScenario(objDbContext);
        objScenario.Handler.ObjResponder = _ => Json(
            """{"error":{"message":"Error validating access token","type":"OAuthException","code":190}}""",
            HttpStatusCode.Unauthorized);
        AddCampaign(objScenario, "Parabéns, {primeiro_nome}!");
        AddCampaign(objScenario, "Sentimos sua falta, {primeiro_nome}!", CampaignKind.WeMissYou);

        await objScenario.Service().TickAsync(s_dtNow, CancellationToken.None);

        // Token inválido: o primeiro falha e o outro nem vai (falharia igual).
        Assert.Single(objScenario.Handler.ObjRequests);
        WhatsAppTemplate objFailed = objDbContext.WhatsAppTemplates.Single(template => template.Attempts == 1);
        Assert.Equal(WhatsAppTemplateStatus.Error, objFailed.Status);
        Assert.StartsWith("Token inválido ou vencido.", objFailed.LastError);
        Assert.Equal(s_dtNow.AddMinutes(5), objFailed.NextAttemptAt);
        Assert.Contains("falhou", Assert.Single(objDbContext.WhatsAppAccountEvents).Details);

        // A Meta recusa o conteúdo (parâmetro inválido): não adianta tentar de novo, fica para ajustar o texto.
        objScenario.Handler.ObjResponder = _ => Json(
            """{"error":{"message":"Invalid parameter","type":"OAuthException","code":100,"error_subcode":2388040,"error_user_msg":"O corpo tem variáveis demais para o tamanho."}}""",
            HttpStatusCode.BadRequest);
        await objScenario.Service().TickAsync(s_dtNow.AddMinutes(1), CancellationToken.None);
        WhatsAppTemplate objInvalid = objDbContext.WhatsAppTemplates.Single(template => template.Id != objFailed.Id);
        Assert.Equal(WhatsAppTemplateStatus.Invalid, objInvalid.Status);
        Assert.Contains("variáveis demais", objInvalid.Reason);
        // O que falhou por token ainda espera os 5 minutos.
        Assert.Equal(2, objScenario.Handler.ObjRequests.Count);

        // Passados os 5 minutos, tenta de novo e agora vai.
        objScenario.Handler.ObjResponder = Meta(() => "PENDING");
        await objScenario.Service().TickAsync(s_dtNow.AddMinutes(6), CancellationToken.None);
        Assert.Equal(WhatsAppTemplateStatus.Pending, objDbContext.WhatsAppTemplates.Single(t => t.Id == objFailed.Id).Status);
        Assert.Equal(3, objScenario.Handler.ObjRequests.Count);
    }

    [Fact]
    public async Task Service_AdoptsATemplateThatAlreadyExists_AndSkipsWhatCannotGo()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Scenario objScenario = CreateScenario(objDbContext);
        string sName = "";
        objScenario.Handler.ObjResponder = objRequest => objRequest.Method == HttpMethod.Post
            ? Json("""{"error":{"message":"Content in this language already exists","code":100,"error_subcode":2388024}}""",
                HttpStatusCode.BadRequest)
            : Json(new JsonObject
            {
                ["data"] = new JsonArray(
                    new JsonObject { ["id"] = "111111111", ["name"] = "outro", ["language"] = "pt_BR", ["status"] = "APPROVED" },
                    new JsonObject
                    {
                        ["id"] = TemplateId, ["name"] = sName, ["language"] = "pt_BR", ["status"] = "APPROVED",
                        ["category"] = "MARKETING",
                    }),
                ["paging"] = new JsonObject { ["cursors"] = new JsonObject { ["after"] = "x" } },
            }.ToJsonString());
        Campaign objCampaign = AddCampaign(objScenario, "Parabéns, {primeiro_nome}!");
        sName = WhatsAppTemplateBuilder.NameFor(objCampaign.Kind, objCampaign.Id, 1);
        // Texto grande demais: fica para ajustar e não vai para a Meta.
        AddCampaign(objScenario, "Oi " + new string('a', 1000), CampaignKind.WeMissYou);

        await objScenario.Service().TickAsync(s_dtNow, CancellationToken.None);

        WhatsAppTemplate objAdopted = objDbContext.WhatsAppTemplates.Single(template => template.Name == sName);
        Assert.Equal(WhatsAppTemplateStatus.Approved, objAdopted.Status);
        Assert.Equal(TemplateId, objAdopted.MetaTemplateId);
        WhatsAppTemplate objTooLong = objDbContext.WhatsAppTemplates.Single(template => template.Name != sName);
        Assert.Equal(WhatsAppTemplateStatus.Invalid, objTooLong.Status);
        Assert.Contains("Tire pelo menos", objTooLong.Reason);
        Assert.Equal(2, objScenario.Handler.ObjRequests.Count); // criar + procurar; o grande não foi
        Assert.Contains("já existia na Meta", Assert.Single(objDbContext.WhatsAppAccountEvents).Details);
    }

    [Fact]
    public async Task Service_ChecksTheNumberEvery6Hours_AndDropsTheConnectionWhenTheTokenDies()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Scenario objScenario = CreateScenario(objDbContext);
        objScenario.Company.WhatsApp.QualityRating = "GREEN";
        objScenario.Company.WhatsApp.CheckedAt = s_dtNow.AddHours(-7);
        objDbContext.SaveChanges();
        objScenario.Handler.ObjResponder = objRequest => objRequest.RequestUri!.AbsolutePath.EndsWith("/phone_numbers")
            ? Json("""{"data":[{"id":"106540352242922"}]}""")
            : Json("""{"display_phone_number":"+55 91 98888-7777","verified_name":"Lojas Regional","quality_rating":"RED","whatsapp_business_manager_messaging_limit":"TIER_2K","id":"106540352242922"}""");

        await objScenario.Service().TickAsync(s_dtNow, CancellationToken.None);

        CompanyWhatsApp objWhatsApp = objDbContext.Companies.Single().WhatsApp;
        Assert.Equal("RED", objWhatsApp.QualityRating);
        Assert.Equal("TIER_2K", objWhatsApp.MessagingLimit); // o limite do dia do envio vem daqui
        Assert.Contains("qualidade baixa", Assert.Single(objDbContext.WhatsAppAlerts).Subject);

        // Seis horas depois, o token venceu: a conexão cai e o super admin fica sabendo.
        objScenario.Handler.ObjResponder = _ => Json(
            """{"error":{"message":"Error validating access token","type":"OAuthException","code":190}}""",
            HttpStatusCode.Unauthorized);
        await objScenario.Service().TickAsync(s_dtNow.AddHours(7), CancellationToken.None);

        objWhatsApp = objDbContext.Companies.Single().WhatsApp;
        Assert.False(objWhatsApp.CheckPassed);
        Assert.StartsWith("Token inválido ou vencido.", objWhatsApp.LastError);
        Assert.Single(objDbContext.WhatsAppAlerts, alert => alert.Key == "token");
    }

    [Fact]
    public async Task Service_DoesNothing_WithoutATestedConnection()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Scenario objScenario = CreateScenario(objDbContext, bCheckPassed: false);
        AddCampaign(objScenario, "Parabéns, {primeiro_nome}!");

        await objScenario.Service().TickAsync(s_dtNow, CancellationToken.None);

        Assert.Empty(objDbContext.WhatsAppTemplates);
        Assert.Empty(objScenario.Handler.ObjRequests);
    }

    // ------------------------------------------------------------------ Telas

    [Fact]
    public async Task Units_StorePhone_IsNormalized_Validated_AndRecorded()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Scenario objScenario = CreateScenario(objDbContext);
        Company objOther = new Company { Name = "Outra", Slug = "outra" };
        Unit objOtherUnit = new Unit { IDCompany = objOther.Id, Name = "Fora", Slug = "fora" };
        objDbContext.AddRange(objOther, objOtherUnit);
        objDbContext.SaveChanges();
        CompanyWhatsAppController objController = new CompanyWhatsAppController(
            objDbContext, objScenario.Encryptor,
            new WhatsAppCloudClient(new HttpClient(objScenario.Handler), new WhatsAppOptions()),
            NullLogger<CompanyWhatsAppController>.Instance);
        TestHelpers.SetCompanyUser(objController, objDbContext, objScenario.Company.Id, "gerente.regional");

        List<WhatsAppUnitDto> objSaved = Ok(await objController.SaveUnits(
            [new WhatsAppUnitPhoneRequest(objScenario.Unit.Id, "+55 (93) 98888-1234")], null, CancellationToken.None));
        Assert.Equal("93988881234", Assert.Single(objSaved).StorePhone);
        WhatsAppAccountEvent objEvent = Assert.Single(objDbContext.WhatsAppAccountEvents);
        Assert.Equal(WhatsAppAccountEventKind.StorePhonesChanged, objEvent.Kind);
        Assert.Equal("WhatsApp de atendimento: Itaituba: (93) 98888-1234.", objEvent.Details);
        Assert.Equal("gerente.regional", objEvent.Username);

        Assert.Contains("inválido", Error(await objController.SaveUnits(
            [new WhatsAppUnitPhoneRequest(objScenario.Unit.Id, "(00) 98888-1234")], null, CancellationToken.None)));
        Assert.Contains("inválido", Error(await objController.SaveUnits(
            [new WhatsAppUnitPhoneRequest(objScenario.Unit.Id, "9888")], null, CancellationToken.None)));
        Assert.Contains("não encontrada", Error(await objController.SaveUnits(
            [new WhatsAppUnitPhoneRequest(objOtherUnit.Id, "93988881234")], null, CancellationToken.None)));

        // Vazio apaga.
        Assert.Equal("", Assert.Single(Ok(await objController.SaveUnits(
            [new WhatsAppUnitPhoneRequest(objScenario.Unit.Id, "")], null, CancellationToken.None))).StorePhone);
    }

    [Fact]
    public async Task TestMessage_SendsAnApprovedTextWithExamples_AndRecordsIt()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Scenario objScenario = CreateScenario(objDbContext);
        AddCampaign(objScenario, "Parabéns, {primeiro_nome}!");
        objScenario.Handler.ObjResponder = Meta(() => "APPROVED");
        await objScenario.Service().TickAsync(s_dtNow, CancellationToken.None);
        await objScenario.Service().TickAsync(s_dtNow.AddMinutes(20), CancellationToken.None);
        WhatsAppTemplate objTemplate = Assert.Single(objDbContext.WhatsAppTemplates);
        Assert.Equal(WhatsAppTemplateStatus.Approved, objTemplate.Status);

        objScenario.Handler.ObjResponder = _ => Json(
            """{"messaging_product":"whatsapp","contacts":[{"input":"+5593988881234","wa_id":"5593988881234"}],"messages":[{"id":"wamid.TESTE","message_status":"accepted"}]}""");
        CompanyWhatsAppController objController = new CompanyWhatsAppController(
            objDbContext, objScenario.Encryptor,
            new WhatsAppCloudClient(new HttpClient(objScenario.Handler), new WhatsAppOptions()),
            NullLogger<CompanyWhatsAppController>.Instance);
        TestHelpers.SetCompanyUser(objController, objDbContext, objScenario.Company.Id, "gerente.regional");

        WhatsAppTestMessageDto objSent = Ok(await objController.TestMessage(
            new WhatsAppTestMessageRequest(objTemplate.Id, "(93) 98888-1234"), null, CancellationToken.None));
        Assert.Equal("(93) 98888-1234", objSent.Phone);
        Assert.StartsWith("Parabéns, Maria!", objSent.Text);

        JsonNode objBody = JsonNode.Parse(objScenario.Handler.ObjRequests[^1].sBody)!;
        Assert.Equal("+5593988881234", objBody["to"]!.GetValue<string>());
        Assert.Equal(objTemplate.Name, objBody["template"]!["name"]!.GetValue<string>());
        WhatsAppMessage objMessage = Assert.Single(objDbContext.WhatsAppMessages);
        Assert.True(objMessage.IsTest);
        Assert.Equal("gerente.regional", objMessage.Username);
        Assert.Equal("wamid.TESTE", objMessage.MetaMessageId);
        Assert.Null(objMessage.IDCustomer);

        Assert.Contains("celular com DDD", Error(await objController.TestMessage(
            new WhatsAppTestMessageRequest(objTemplate.Id, "123"), null, CancellationToken.None)));
        objDbContext.WhatsAppTemplates.Single().Status = WhatsAppTemplateStatus.Pending;
        objDbContext.SaveChanges();
        Assert.Contains("aprovado", Error(await objController.TestMessage(
            new WhatsAppTestMessageRequest(objTemplate.Id, "(93) 98888-1234"), null, CancellationToken.None)));
    }

    [Fact]
    public async Task Screens_ShowTheTemplateOfEachCampaign()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Scenario objScenario = CreateScenario(objDbContext);
        Campaign objCampaign = AddCampaign(objScenario, "Parabéns, {primeiro_nome}!");
        CampaignsController objCampaigns = new CampaignsController(objDbContext);
        TestHelpers.SetCompanyUser(objCampaigns, objDbContext, objScenario.Company.Id, "gerente.regional");

        // Ainda sem modelo: na fila.
        CampaignWhatsAppDto objQueued = Assert.Single(Ok(await objCampaigns.GetAll(null, CancellationToken.None))).WhatsApp!;
        Assert.Equal(WhatsAppTemplateStatus.Queued, objQueued.Status);
        Assert.Contains("em até 1 minuto", objQueued.Reason);

        objScenario.Handler.ObjResponder = Meta(() => "APPROVED");
        await objScenario.Service().TickAsync(s_dtNow, CancellationToken.None);
        await objScenario.Service().TickAsync(s_dtNow.AddMinutes(20), CancellationToken.None);
        Edit(objScenario, objCampaign, "Feliz aniversário, {primeiro_nome}!");

        // Texto novo ainda sem modelo: sai o aprovado da v1 enquanto isso.
        CampaignWhatsAppDto objEdited = Assert.Single(Ok(await objCampaigns.GetAll(null, CancellationToken.None))).WhatsApp!;
        Assert.Equal(WhatsAppTemplateStatus.Queued, objEdited.Status);
        Assert.Equal(1, objEdited.FallbackVersion);

        CompanyWhatsAppController objWhatsApp = new CompanyWhatsAppController(
            objDbContext, objScenario.Encryptor,
            new WhatsAppCloudClient(new HttpClient(objScenario.Handler), new WhatsAppOptions()),
            NullLogger<CompanyWhatsAppController>.Instance);
        TestHelpers.SetCompanyUser(objWhatsApp, objDbContext, objScenario.Company.Id, "gerente.regional");
        WhatsAppTemplateDto objListed = Assert.Single(Ok(await objWhatsApp.GetTemplates(null, CancellationToken.None)));
        Assert.Equal(WhatsAppTemplateStatus.Approved, objListed.Status);
        Assert.False(objListed.IsCurrent); // é da v1; o texto atual é outro
        Assert.Equal("Aniversário", objListed.CampaignName);
        Assert.Equal(
            "Parabéns, Maria!\n\nFale com a loja pelo WhatsApp (93) 99123-4567 sempre que precisar.\n\n"
            + "Para não receber mais, responda SAIR.",
            objListed.Preview);
        Assert.DoesNotContain(AccessToken, JsonSerializer.Serialize(objListed));

        // Empresa sem WhatsApp configurado: a lista de campanhas não mostra nada do WhatsApp.
        objScenario.Company.WhatsApp.AccessToken = "";
        objDbContext.SaveChanges();
        Assert.Null(Assert.Single(Ok(await objCampaigns.GetAll(null, CancellationToken.None))).WhatsApp);
    }
}
