using System.Net;
using System.Text;
using System.Text.Json;
using AccessWifi.Api.Controllers;
using AccessWifi.Api.Features;
using AccessWifi.Api.Features.WhatsApp;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Models.DataBase;
using Models.Persistence;
using Models.Security;
using Models.WhatsApp;

namespace AccessWifi.Api.Tests;

/// <summary>
/// WhatsApp, parte 1 (PROPOSTA_WHATSAPP.md): credenciais cifradas e só de ida, teste de conexão com a Meta
/// (simulada aqui) e o interruptor do envio automático, com tudo no registro da conta.
/// </summary>
public class CompanyWhatsAppTests
{
    private const string PhoneNumberId = "106540352242922";
    private const string BusinessAccountId = "102290129340398";
    private const string AccessToken = "EAAG-token-secreto-de-teste-123";
    private const string AppSecret = "segredo-do-app-abc";
    private const string VerifyToken = "codigo-de-verificacao-xyz";

    private const string PhoneJson =
        """{"display_phone_number":"+55 91 98888-7777","verified_name":"Lojão dos Plásticos","quality_rating":"GREEN","whatsapp_business_manager_messaging_limit":"TIER_250","id":"106540352242922"}""";
    private const string NumbersJson = """{"data":[{"id":"106540352242922"}]}""";
    private const string OtherNumbersJson = """{"data":[{"id":"999999999999999"}]}""";
    private const string TokenErrorJson =
        """{"error":{"message":"Error validating access token: Session has expired","type":"OAuthException","code":190,"error_subcode":463,"fbtrace_id":"A1b2"}}""";

    /// <summary>Meta falsa: guarda os pedidos e responde conforme a função informada.</summary>
    private sealed class StubHandler : HttpMessageHandler
    {
        public List<HttpRequestMessage> ObjRequests { get; } = [];
        public required Func<HttpRequestMessage, HttpResponseMessage> ObjResponder { get; init; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage objRequest, CancellationToken objCancellationToken)
        {
            ObjRequests.Add(objRequest);
            return Task.FromResult(ObjResponder(objRequest));
        }
    }

    private static HttpResponseMessage Json(string sBody, HttpStatusCode objStatus = HttpStatusCode.OK) =>
        new HttpResponseMessage(objStatus) { Content = new StringContent(sBody, Encoding.UTF8, "application/json") };

    private static HttpResponseMessage MetaOk(HttpRequestMessage objRequest) =>
        objRequest.RequestUri!.AbsolutePath.EndsWith("/phone_numbers") ? Json(NumbersJson) : Json(PhoneJson);

    private static WhatsAppCloudClient CreateClient(StubHandler objHandler) =>
        new WhatsAppCloudClient(new HttpClient(objHandler), new WhatsAppOptions());

    private static (Company objCompany, CompanyWhatsAppController objController) Create(
        AppDbContext objDbContext, StubHandler objHandler, IEncryptor objEncryptor)
    {
        Company objCompany = new Company { Name = "Lojão dos Plásticos", Slug = "lojao" };
        objDbContext.Companies.Add(objCompany);
        objDbContext.SaveChanges();
        CompanyWhatsAppController objController = new CompanyWhatsAppController(
            objDbContext, objEncryptor, CreateClient(objHandler), NullLogger<CompanyWhatsAppController>.Instance);
        TestHelpers.SetCompanyUser(objController, objDbContext, objCompany.Id, "gerente.lojao");
        return (objCompany, objController);
    }

    private static T Ok<T>(ActionResult<T> objResult) =>
        Assert.IsType<T>(Assert.IsType<OkObjectResult>(objResult.Result).Value);

    private static string Error<T>(ActionResult<T> objResult) =>
        Assert.IsType<ErrorResponse>(Assert.IsAssignableFrom<ObjectResult>(objResult.Result).Value).Error;

    private static WhatsAppSettingsRequest FullRequest() =>
        new WhatsAppSettingsRequest(PhoneNumberId, BusinessAccountId, AccessToken, AppSecret, VerifyToken);

    [Fact]
    public async Task Client_ReadsTheNumber_AndChecksTheAccount_WithoutPuttingTheTokenInTheRecord()
    {
        StubHandler objHandler = new StubHandler { ObjResponder = MetaOk };
        (WhatsAppPhoneInfo objPhone, List<WhatsAppCall> objCalls) = await CreateClient(objHandler)
            .CheckConnectionAsync(PhoneNumberId, BusinessAccountId, AccessToken);

        Assert.Equal("+55 91 98888-7777", objPhone.DisplayPhoneNumber);
        Assert.Equal("Lojão dos Plásticos", objPhone.VerifiedName);
        Assert.Equal("GREEN", objPhone.QualityRating);
        Assert.Equal("TIER_250", objPhone.MessagingLimit);

        Assert.Equal(2, objHandler.ObjRequests.Count);
        Assert.All(objHandler.ObjRequests, objRequest =>
        {
            Assert.Equal("Bearer", objRequest.Headers.Authorization!.Scheme);
            Assert.Equal(AccessToken, objRequest.Headers.Authorization.Parameter);
            Assert.StartsWith("https://graph.facebook.com/v26.0/", objRequest.RequestUri!.ToString());
        });
        Assert.Contains("whatsapp_business_manager_messaging_limit", objHandler.ObjRequests[0].RequestUri!.Query);
        Assert.EndsWith($"/{BusinessAccountId}/phone_numbers", objHandler.ObjRequests[1].RequestUri!.AbsolutePath);
        Assert.All(objCalls, objCall => Assert.DoesNotContain(AccessToken, objCall.Request + objCall.Response));
    }

    [Fact]
    public async Task Client_TranslatesTheMetaErrors()
    {
        WhatsAppCloudClient objExpired = CreateClient(new StubHandler
        {
            ObjResponder = _ => Json(TokenErrorJson, HttpStatusCode.Unauthorized),
        });
        WhatsAppApiException objToken = await Assert.ThrowsAsync<WhatsAppApiException>(
            () => objExpired.CheckConnectionAsync(PhoneNumberId, BusinessAccountId, AccessToken));
        Assert.Equal(190, objToken.MetaCode);
        Assert.StartsWith("Token inválido ou vencido.", objToken.Message);
        Assert.Single(objToken.Calls);

        WhatsAppCloudClient objOtherAccount = CreateClient(new StubHandler
        {
            ObjResponder = objRequest => objRequest.RequestUri!.AbsolutePath.EndsWith("/phone_numbers")
                ? Json(OtherNumbersJson)
                : Json(PhoneJson),
        });
        WhatsAppApiException objAccount = await Assert.ThrowsAsync<WhatsAppApiException>(
            () => objOtherAccount.CheckConnectionAsync(PhoneNumberId, BusinessAccountId, AccessToken));
        Assert.Contains("não aparece nessa conta", objAccount.Message);

        WhatsAppCloudClient objOffline = CreateClient(new StubHandler
        {
            ObjResponder = _ => throw new HttpRequestException("sem rede"),
        });
        WhatsAppApiException objNoAnswer = await Assert.ThrowsAsync<WhatsAppApiException>(
            () => objOffline.CheckConnectionAsync(PhoneNumberId, BusinessAccountId, AccessToken));
        Assert.Contains("Não foi possível falar com a Meta", objNoAnswer.Message);

        // Id com letra ou barra nem chega a sair para a Meta.
        StubHandler objNever = new StubHandler { ObjResponder = MetaOk };
        await Assert.ThrowsAsync<WhatsAppApiException>(
            () => CreateClient(objNever).CheckConnectionAsync("123/../x", BusinessAccountId, AccessToken));
        Assert.Empty(objNever.ObjRequests);
    }

    [Fact]
    public async Task Credentials_AreEncrypted_WriteOnly_AndNeverComeBack()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        IEncryptor objEncryptor = TestHelpers.CreateEncryptor();
        (Company objCompany, CompanyWhatsAppController objController) =
            Create(objDbContext, new StubHandler { ObjResponder = MetaOk }, objEncryptor);

        WhatsAppSettingsDto objSaved = Ok(await objController.Put(FullRequest(), null, CancellationToken.None));
        Assert.True(objSaved.HasAccessToken && objSaved.HasAppSecret && objSaved.HasVerifyToken);
        Assert.Equal(PhoneNumberId, objSaved.PhoneNumberId);
        Assert.False(objSaved.CheckPassed);

        // No banco: cifrados, e decifram para o valor certo.
        CompanyWhatsApp objStored = objDbContext.Companies.Single().WhatsApp;
        foreach ((string sStored, string sPlain) in new[]
        {
            (objStored.AccessToken, AccessToken), (objStored.AppSecret, AppSecret), (objStored.VerifyToken, VerifyToken),
        })
        {
            Assert.StartsWith("enc:v1:", sStored);
            Assert.Equal(sPlain, objEncryptor.Decrypt(sStored));
        }

        // Nada do que a tela recebe tem os segredos, nem cifrados.
        string sJson = JsonSerializer.Serialize(Ok(await objController.Get(null, CancellationToken.None)));
        foreach (string sSecret in new[] { AccessToken, AppSecret, VerifyToken, objStored.AccessToken, "enc:v1:" })
        {
            Assert.DoesNotContain(sSecret, sJson);
        }

        // Segredo vazio = manter; o registro diz o que trocou, nunca o valor.
        string sTokenBefore = objStored.AccessToken;
        Ok(await objController.Put(
            new WhatsAppSettingsRequest(PhoneNumberId, BusinessAccountId, "", null, " "), null, CancellationToken.None));
        Assert.Equal(sTokenBefore, objDbContext.Companies.Single().WhatsApp.AccessToken);
        WhatsAppAccountEvent objEvent = Assert.Single(objDbContext.WhatsAppAccountEvents);
        Assert.Equal(WhatsAppAccountEventKind.CredentialsChanged, objEvent.Kind);
        Assert.Equal("gerente.lojao", objEvent.Username);
        Assert.Equal("Trocou: id do número, id da conta do WhatsApp, token, chave secreta do app, código de verificação.",
            objEvent.Details);
        Assert.DoesNotContain(AccessToken, objEvent.Details);

        // Validações.
        Assert.Contains("só números", Error(await objController.Put(
            new WhatsAppSettingsRequest("12ab", BusinessAccountId, null, null, null), null, CancellationToken.None)));
        Assert.Contains("espaços", Error(await objController.Put(
            new WhatsAppSettingsRequest(PhoneNumberId, BusinessAccountId, "abc def", null, null), null, CancellationToken.None)));
        Assert.Equal(objCompany.Id, objEvent.IDCompany);
    }

    [Fact]
    public async Task Test_Enable_AndCredentialChanges_AreRecorded()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        bool bMetaOk = true;
        StubHandler objHandler = new StubHandler
        {
            ObjResponder = objRequest => bMetaOk ? MetaOk(objRequest) : Json(TokenErrorJson, HttpStatusCode.Unauthorized),
        };
        (_, CompanyWhatsAppController objController) = Create(objDbContext, objHandler, TestHelpers.CreateEncryptor());

        // Sem credenciais: nem testa, nem liga.
        Assert.Contains("salve antes de testar", Error(await objController.Test(null, CancellationToken.None)));
        Assert.Contains("Teste a conexão", Error(await objController.SetEnabled(
            new WhatsAppEnabledRequest(true), null, CancellationToken.None)));

        Ok(await objController.Put(FullRequest(), null, CancellationToken.None));

        // Meta recusando: o teste volta 200 com o motivo, e o envio continua sem poder ligar.
        bMetaOk = false;
        WhatsAppSettingsDto objFailed = Ok(await objController.Test(null, CancellationToken.None));
        Assert.False(objFailed.CheckPassed);
        Assert.StartsWith("Token inválido ou vencido.", objFailed.LastError);
        Assert.NotNull(objFailed.CheckedAt);

        bMetaOk = true;
        WhatsAppSettingsDto objPassed = Ok(await objController.Test(null, CancellationToken.None));
        Assert.True(objPassed.CheckPassed);
        Assert.Equal("", objPassed.LastError);
        Assert.Equal("+55 91 98888-7777", objPassed.DisplayPhoneNumber);
        Assert.Equal("Lojão dos Plásticos", objPassed.DisplayName);
        Assert.Equal("TIER_250", objPassed.MessagingLimit);

        WhatsAppSettingsDto objOn = Ok(await objController.SetEnabled(
            new WhatsAppEnabledRequest(true), null, CancellationToken.None));
        Assert.True(objOn.Enabled);
        Assert.Equal("gerente.lojao", objOn.ChangedBy);
        Assert.NotNull(objOn.ChangedAt);

        // Trocar o token desliga o envio até um novo teste.
        WhatsAppSettingsDto objChanged = Ok(await objController.Put(
            new WhatsAppSettingsRequest(PhoneNumberId, BusinessAccountId, "token-novo-456", null, null), null,
            CancellationToken.None));
        Assert.False(objChanged.Enabled);
        Assert.False(objChanged.CheckPassed);

        List<WhatsAppAccountEvent> objEvents = objDbContext.WhatsAppAccountEvents.OrderBy(evt => evt.Id).ToList();
        Assert.Equal(
            [
                WhatsAppAccountEventKind.CredentialsChanged, WhatsAppAccountEventKind.ConnectionTested,
                WhatsAppAccountEventKind.ConnectionTested, WhatsAppAccountEventKind.Enabled,
                WhatsAppAccountEventKind.CredentialsChanged, WhatsAppAccountEventKind.Disabled,
            ],
            objEvents.Select(evt => evt.Kind));
        Assert.StartsWith("Falhou: Token inválido", objEvents[1].Details);
        Assert.StartsWith("Conexão OK: +55 91 98888-7777", objEvents[2].Details);
        Assert.Equal("Trocou: token.", objEvents[4].Details);

        // O registro das idas à Meta é JSON válido e não tem o token (nem o novo).
        foreach (WhatsAppAccountEvent objEvent in objEvents.Where(evt => evt.RequestJson is not null))
        {
            JsonDocument.Parse(objEvent.RequestJson!).Dispose();
            JsonDocument.Parse(objEvent.ResponseJson!).Dispose();
            Assert.DoesNotContain(AccessToken, objEvent.RequestJson + objEvent.ResponseJson);
            Assert.Contains($"GET /v26.0/{PhoneNumberId}?fields=", objEvent.RequestJson);
        }
        // O teste que passou registrou as duas idas: o número e a conta.
        Assert.Contains($"/{BusinessAccountId}/phone_numbers", objEvents[2].RequestJson);
    }

    [Fact]
    public async Task Access_CompanyAdminOnlyOwnCompany_UnitUserForbidden_SuperAdminByTopSelector()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        StubHandler objHandler = new StubHandler { ObjResponder = MetaOk };
        IEncryptor objEncryptor = TestHelpers.CreateEncryptor();
        (Company objLojao, CompanyWhatsAppController objController) = Create(objDbContext, objHandler, objEncryptor);
        Company objOther = new Company { Name = "Outra", Slug = "outra" };
        Unit objUnit = new Unit { IDCompany = objLojao.Id, Name = "Coronel Leal", Slug = "lojao-coronel-leal" };
        objDbContext.AddRange(objOther, objUnit);
        objDbContext.SaveChanges();

        // Admin da empresa: o ?company= de outra empresa é ignorado, ele só mexe na dele.
        Ok(await objController.Put(FullRequest(), "outra", CancellationToken.None));
        Assert.Equal(PhoneNumberId, objDbContext.Companies.Single(company => company.Slug == "lojao").WhatsApp.PhoneNumberId);
        Assert.Equal("", objDbContext.Companies.Single(company => company.Slug == "outra").WhatsApp.PhoneNumberId);

        // Usuário de unidade: não entra.
        CompanyWhatsAppController objUnitUser = new CompanyWhatsAppController(
            objDbContext, objEncryptor, CreateClient(objHandler), NullLogger<CompanyWhatsAppController>.Instance);
        TestHelpers.SetCompanyUser(objUnitUser, objDbContext, objLojao.Id, "equipe.coronel", objUnit.Id);
        Assert.IsType<ForbidResult>((await objUnitUser.Get(null, CancellationToken.None)).Result);
        Assert.IsType<ForbidResult>((await objUnitUser.SetEnabled(
            new WhatsAppEnabledRequest(false), null, CancellationToken.None)).Result);

        // Super admin: pela empresa do topo; sem ela, avisa.
        CompanyWhatsAppController objRoot = new CompanyWhatsAppController(
            objDbContext, objEncryptor, CreateClient(objHandler), NullLogger<CompanyWhatsAppController>.Instance);
        TestHelpers.SetUser(objRoot, null, "root");
        Assert.Contains("Informe a empresa", Error(await objRoot.Get(null, CancellationToken.None)));
        Assert.Equal(PhoneNumberId, Ok(await objRoot.Get("lojao", CancellationToken.None)).PhoneNumberId);
        Assert.IsType<NotFoundObjectResult>((await objRoot.Get("nao-existe", CancellationToken.None)).Result);
    }
}
