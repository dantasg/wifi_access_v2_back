using System.Net;
using System.Text;
using AccessWifi.Api.Infrastructure.Unifi;
using Microsoft.Extensions.Logging.Abstractions;
using Models.DataBase;

namespace AccessWifi.Api.Tests;

/// <summary>
/// Cobre o caminho pela nuvem da Ubiquiti sem sair da máquina: a rede é substituída por um
/// handler falso que devolve exatamente os corpos observados na controladora real da Itaituba.
///
/// Caminho principal: API clássica, uma ida à loja, pelo MAC. Plano B: API oficial (achar o ID
/// do aparelho e autorizar). Cada ida à loja custa 0,5–1 s, então contar requisições aqui é contar
/// o tempo que o visitante espera.
/// </summary>
public class UnifiCloudClientTests
{
    private const string ConsoleId = "58D61F5E1531000000000A2C62C1000000006921:896725606";
    private const string SiteId = "88f7af54-98f8-306a-a1c7-c9349722b1f6";
    private const string ClientId = "6f3b6677-e61a-3057-8a4f-74ea11396064";
    private const string ApiKey = "chave-de-teste";

    private const string SitesJson =
        """{"offset":0,"limit":25,"count":1,"totalCount":1,"data":[{"id":"88f7af54-98f8-306a-a1c7-c9349722b1f6","internalReference":"default","name":"Default"}]}""";

    private const string ClientFoundJson =
        """{"offset":0,"limit":25,"count":1,"totalCount":1,"data":[{"id":"6f3b6677-e61a-3057-8a4f-74ea11396064","macAddress":"36:9d:94:1e:aa:10","type":"WIRELESS"}]}""";

    private const string ClientEmptyJson =
        """{"offset":0,"limit":25,"count":0,"totalCount":0,"data":[]}""";

    private const string AuthorizedJson =
        """{"action":"AUTHORIZE_GUEST_ACCESS","grantedAuthorization":{"authorizedAt":"2026-09-20T19:40:57Z","expiresAt":"2026-09-20T20:40:57Z"}}""";

    // Corpo real devolvido pela controladora da Itaituba em 21/09 (authorize-guest).
    private const string ClassicOkJson =
        """{"meta":{"rc":"ok"},"data":[{"authorized_by":"api","mac":"36:9d:94:1e:aa:10","minutes":1440}]}""";

    private const string ClassicErrorJson =
        """{"meta":{"rc":"error","msg":"api.err.Invalid"},"data":[]}""";

    /// <summary>Handler falso: guarda o que foi pedido e responde conforme a função informada.</summary>
    private class StubHandler : HttpMessageHandler
    {
        public List<HttpRequestMessage> ObjRequests { get; } = [];
        public List<string> ObjBodies { get; } = [];
        public required Func<HttpRequestMessage, HttpResponseMessage> ObjResponder { get; init; }

        public int IClassicas => ObjRequests.Count(objRequest => EhClassica(objRequest));
        public int IBuscas => ObjRequests.Count(objRequest => objRequest.RequestUri!.ToString().Contains("/clients?filter="));
        public int IAutorizacoesOficiais => ObjRequests.Count(objRequest => objRequest.RequestUri!.ToString().EndsWith("/actions"));

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage objRequest, CancellationToken objCancellationToken)
        {
            ObjBodies.Add(objRequest.Content is null
                ? ""
                : await objRequest.Content.ReadAsStringAsync(objCancellationToken));
            ObjRequests.Add(objRequest);
            return ObjResponder(objRequest);
        }

        public string SUrl(int iIndex) => ObjRequests[iIndex].RequestUri!.ToString();
    }

    private class StubHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _objHandler;

        public StubHttpClientFactory(HttpMessageHandler objHandler) => _objHandler = objHandler;

        public HttpClient CreateClient(string sName) =>
            new HttpClient(_objHandler, disposeHandler: false)
            {
                BaseAddress = new Uri("https://api.ui.com/"),
            };
    }

    private static bool EhClassica(HttpRequestMessage objRequest) =>
        objRequest.RequestUri!.ToString().EndsWith("/cmd/stamgr");

    private static HttpResponseMessage Json(string sBody, HttpStatusCode objStatus = HttpStatusCode.OK)
    {
        return new HttpResponseMessage(objStatus)
        {
            Content = new StringContent(sBody, Encoding.UTF8, "application/json"),
        };
    }

    /// <summary>A API oficial, respondendo como a controladora real (sites, aparelho, autorização).</summary>
    private static HttpResponseMessage RespondeOficial(HttpRequestMessage objRequest)
    {
        string sUrl = objRequest.RequestUri!.ToString();
        if (sUrl.EndsWith("/sites")) return Json(SitesJson);
        if (sUrl.Contains("/clients?filter=")) return Json(ClientFoundJson);
        return Json(AuthorizedJson);
    }

    /// <summary>Tudo funcionando: a clássica autoriza.</summary>
    private static HttpResponseMessage RespondeClassicaOk(HttpRequestMessage objRequest) =>
        EhClassica(objRequest) ? Json(ClassicOkJson) : RespondeOficial(objRequest);

    /// <summary>A Ubiquiti desligou a API clássica: só a oficial responde.</summary>
    private static HttpResponseMessage RespondeSemClassica(HttpRequestMessage objRequest) =>
        EhClassica(objRequest) ? Json("{}", HttpStatusCode.NotFound) : RespondeOficial(objRequest);

    private static CompanyUnifi CreateConfig(string sSiteId = "", string sSite = "default")
    {
        return new CompanyUnifi
        {
            Mode = UnifiMode.Cloud,
            ConsoleId = ConsoleId,
            ApiKey = TestHelpers.CreateEncryptor().Encrypt(ApiKey) ?? "",
            Site = sSite,
            SiteId = sSiteId,
        };
    }

    private static (UnifiCloudClient, StubHandler) CreateClient(
        Func<HttpRequestMessage, HttpResponseMessage> objResponder)
    {
        StubHandler objHandler = new StubHandler { ObjResponder = objResponder };
        UnifiCloudClient objClient = new UnifiCloudClient(
            new StubHttpClientFactory(objHandler), TestHelpers.CreateEncryptor(),
            NullLogger<UnifiCloudClient>.Instance);
        return (objClient, objHandler);
    }

    // ------------------------------------------------------------------ Caminho principal: API clássica

    [Fact]
    public async Task Autorizar_PelaApiClassica_UmaIdaSoPeloMac()
    {
        (UnifiCloudClient objClient, StubHandler objHandler) = CreateClient(RespondeClassicaOk);

        await objClient.AuthorizeGuestAsync(CreateConfig(SiteId), "36:9d:94:1e:aa:10", 1440);

        // O ganho de velocidade inteiro está aqui: uma requisição, não duas.
        HttpRequestMessage objRequest = Assert.Single(objHandler.ObjRequests);
        Assert.Equal(HttpMethod.Post, objRequest.Method);
        Assert.Equal(
            $"https://api.ui.com/v1/connector/consoles/{ConsoleId}/proxy/network/api/s/default/cmd/stamgr",
            objHandler.SUrl(0));
        Assert.Contains("\"cmd\":\"authorize-guest\"", objHandler.ObjBodies[0]);
        Assert.Contains("\"mac\":\"36:9d:94:1e:aa:10\"", objHandler.ObjBodies[0]);
        Assert.Contains("\"minutes\":1440", objHandler.ObjBodies[0]);
        Assert.Equal(ApiKey, objRequest.Headers.GetValues("X-API-KEY").Single());
    }

    [Fact]
    public async Task Autorizar_PelaApiClassica_NaoPrecisaDoSiteIdNemDaListaDeAparelhos()
    {
        // A clássica não depende de a controladora já listar o aparelho — exatamente o que fez
        // a busca adiantada falhar no teste real (o celular tinha 2 s de conexão).
        (UnifiCloudClient objClient, StubHandler objHandler) = CreateClient(RespondeClassicaOk);
        CompanyUnifi objConfig = CreateConfig();

        await objClient.AuthorizeGuestAsync(objConfig, "36:9d:94:1e:aa:10", 1440);

        Assert.Equal(1, objHandler.IClassicas);
        Assert.Equal(0, objHandler.IBuscas);
        Assert.Equal("", objConfig.SiteId);
    }

    [Theory]
    [InlineData("36-9D-94-1E-AA-10")]
    [InlineData("369D941EAA10")]
    [InlineData("36:9D:94:1E:AA:10")]
    public async Task Autorizar_MacEmQualquerFormato_VaiNormalizadoParaAClassica(string sMac)
    {
        (UnifiCloudClient objClient, StubHandler objHandler) = CreateClient(RespondeClassicaOk);

        await objClient.AuthorizeGuestAsync(CreateConfig(SiteId), sMac, 1440);

        Assert.Contains("\"mac\":\"36:9d:94:1e:aa:10\"", objHandler.ObjBodies[0]);
    }

    [Fact]
    public async Task Autorizar_UsaONomeDoSiteDaUnidadeNaClassica()
    {
        (UnifiCloudClient objClient, StubHandler objHandler) = CreateClient(RespondeClassicaOk);

        await objClient.AuthorizeGuestAsync(CreateConfig(SiteId, sSite: "loja2"), "36:9d:94:1e:aa:10", 1440);

        Assert.Contains("/proxy/network/api/s/loja2/cmd/stamgr", objHandler.SUrl(0));
    }

    // ------------------------------------------------------------------ Plano B: API oficial

    [Fact]
    public async Task Autorizar_ClassicaIndisponivel_CaiNaApiOficial()
    {
        (UnifiCloudClient objClient, StubHandler objHandler) = CreateClient(RespondeSemClassica);

        await objClient.AuthorizeGuestAsync(CreateConfig(SiteId), "36:9d:94:1e:aa:10", 1440);

        // Clássica recusou; a oficial achou o aparelho e autorizou.
        Assert.Equal(1, objHandler.IClassicas);
        Assert.Equal(1, objHandler.IBuscas);
        Assert.Equal(1, objHandler.IAutorizacoesOficiais);
        Assert.EndsWith($"/sites/{SiteId}/clients/{ClientId}/actions", objHandler.SUrl(2));
        Assert.Contains("\"action\":\"AUTHORIZE_GUEST_ACCESS\"", objHandler.ObjBodies[2]);
        Assert.Contains("\"timeLimitMinutes\":1440", objHandler.ObjBodies[2]);
    }

    [Fact]
    public async Task Autorizar_ClassicaRespondeErro_CaiNaApiOficial()
    {
        // HTTP 200 mas "rc: error" — a clássica sinaliza falha no corpo, não no status.
        (UnifiCloudClient objClient, StubHandler objHandler) = CreateClient(objRequest =>
            EhClassica(objRequest) ? Json(ClassicErrorJson) : RespondeOficial(objRequest));

        await objClient.AuthorizeGuestAsync(CreateConfig(SiteId), "36:9d:94:1e:aa:10", 1440);

        Assert.Equal(1, objHandler.IAutorizacoesOficiais);
    }

    [Fact]
    public async Task Autorizar_PelaOficialSemSiteId_DescobreOSiteEGravaParaQuemChamou()
    {
        (UnifiCloudClient objClient, StubHandler objHandler) = CreateClient(RespondeSemClassica);
        CompanyUnifi objConfig = CreateConfig();

        await objClient.AuthorizeGuestAsync(objConfig, "36:9d:94:1e:aa:10", 1440);

        Assert.Equal(
            $"https://api.ui.com/v1/connector/consoles/{ConsoleId}/proxy/network/integration/v1/sites",
            objHandler.SUrl(1));
        // D3: o site descoberto fica na entidade para quem chamou persistir.
        Assert.Equal(SiteId, objConfig.SiteId);
    }

    [Fact]
    public async Task Autorizar_PelaOficial_AparelhoSoApareceNaSegundaConsulta_Autoriza()
    {
        // D6: o aparelho acabou de conectar e a controladora ainda não o listou.
        int iBusca = 0;
        (UnifiCloudClient objClient, StubHandler objHandler) = CreateClient(objRequest =>
        {
            if (EhClassica(objRequest)) return Json("{}", HttpStatusCode.NotFound);
            if (objRequest.RequestUri!.ToString().Contains("/clients?filter="))
                return Json(++iBusca == 1 ? ClientEmptyJson : ClientFoundJson);
            return Json(AuthorizedJson);
        });

        await objClient.AuthorizeGuestAsync(CreateConfig(SiteId), "36:9d:94:1e:aa:10", 1440);

        Assert.Equal(2, objHandler.IBuscas);
        Assert.Equal(1, objHandler.IAutorizacoesOficiais);
    }

    [Fact]
    public async Task Autorizar_PelaOficial_AparelhoNuncaAparece_ErroClaroESemAutorizar()
    {
        (UnifiCloudClient objClient, StubHandler objHandler) = CreateClient(objRequest =>
            EhClassica(objRequest) ? Json("{}", HttpStatusCode.NotFound) : Json(ClientEmptyJson));

        UnifiException objException = await Assert.ThrowsAsync<UnifiException>(
            () => objClient.AuthorizeGuestAsync(CreateConfig(SiteId), "36:9d:94:1e:aa:10", 1440));

        Assert.Contains("Aparelho não encontrado", objException.Message);
        Assert.Equal(0, objHandler.IAutorizacoesOficiais);
    }

    [Fact]
    public async Task Autorizar_PelaOficial_RedeNaoEhDeVisitantes_MensagemExplicaACausa()
    {
        (UnifiCloudClient objClient, StubHandler objHandler) = CreateClient(objRequest =>
        {
            if (EhClassica(objRequest)) return Json("{}", HttpStatusCode.NotFound);
            if (objRequest.RequestUri!.ToString().Contains("/clients?filter=")) return Json(ClientFoundJson);
            return Json("""{"code":"api.client.not-guest","message":"Client is not a guest"}""",
                HttpStatusCode.UnprocessableEntity);
        });

        UnifiException objException = await Assert.ThrowsAsync<UnifiException>(
            () => objClient.AuthorizeGuestAsync(CreateConfig(SiteId), "36:9d:94:1e:aa:10", 1440));

        Assert.Contains("rede de visitantes", objException.Message);
    }

    // ------------------------------------------------------------------ Quando NÃO vale o plano B

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "inválida ou revogada")]
    [InlineData(HttpStatusCode.Forbidden, "não alcança este console")]
    [InlineData(HttpStatusCode.TooManyRequests, "Limite de chamadas")]
    public async Task Autorizar_ClassicaRecusaAChave_ErroNaHoraSemTentarAOficial(
        HttpStatusCode objStatus, string sTrechoEsperado)
    {
        // A oficial usa a mesma chave e o mesmo túnel: tentar de novo só dobraria a espera.
        (UnifiCloudClient objClient, StubHandler objHandler) = CreateClient(_ => Json("{}", objStatus));

        UnifiException objException = await Assert.ThrowsAsync<UnifiException>(
            () => objClient.AuthorizeGuestAsync(CreateConfig(SiteId), "36:9d:94:1e:aa:10", 1440));

        Assert.Contains(sTrechoEsperado, objException.Message);
        Assert.Single(objHandler.ObjRequests);
    }

    [Fact]
    public async Task Autorizar_NuvemFora_ErroNaHoraSemTentarAOficial()
    {
        (UnifiCloudClient objClient, StubHandler objHandler) = CreateClient(
            _ => throw new HttpRequestException("sem rota"));

        UnifiException objException = await Assert.ThrowsAsync<UnifiException>(
            () => objClient.AuthorizeGuestAsync(CreateConfig(SiteId), "36:9d:94:1e:aa:10", 1440));

        Assert.Contains("Não foi possível falar com a nuvem", objException.Message);
        Assert.Single(objHandler.ObjRequests);
    }

    // ------------------------------------------------------------------ Validação antes de sair para a rede

    [Fact]
    public async Task Autorizar_MacInvalido_NemChegaAFalarComARede()
    {
        (UnifiCloudClient objClient, StubHandler objHandler) = CreateClient(RespondeClassicaOk);

        UnifiException objException = await Assert.ThrowsAsync<UnifiException>(
            () => objClient.AuthorizeGuestAsync(CreateConfig(SiteId), "não-é-um-mac", 1440));

        Assert.Equal("MAC do aparelho em formato inválido.", objException.Message);
        Assert.Empty(objHandler.ObjRequests);
    }

    [Fact]
    public async Task Autorizar_SemChaveDeApi_NemChegaAFalarComARede()
    {
        (UnifiCloudClient objClient, StubHandler objHandler) = CreateClient(RespondeClassicaOk);
        CompanyUnifi objConfig = CreateConfig(SiteId);
        objConfig.ApiKey = "";

        UnifiException objException = await Assert.ThrowsAsync<UnifiException>(
            () => objClient.AuthorizeGuestAsync(objConfig, "36:9d:94:1e:aa:10", 1440));

        Assert.Contains("Chave de API", objException.Message);
        Assert.Empty(objHandler.ObjRequests);
    }

    [Fact]
    public async Task Autorizar_ConsoleIdInvalido_NemChegaAFalarComARede()
    {
        (UnifiCloudClient objClient, StubHandler objHandler) = CreateClient(RespondeClassicaOk);
        CompanyUnifi objConfig = CreateConfig(SiteId);
        // Barra no ConsoleId escaparia do caminho previsto na URL.
        objConfig.ConsoleId = "../../algum-outro-console";

        UnifiException objException = await Assert.ThrowsAsync<UnifiException>(
            () => objClient.AuthorizeGuestAsync(objConfig, "36:9d:94:1e:aa:10", 1440));

        Assert.Contains("Console da nuvem UniFi não configurado", objException.Message);
        Assert.Empty(objHandler.ObjRequests);
    }

    [Fact]
    public async Task Autorizar_NomeDoSiteInvalido_NaoMontaAUrlClassicaEVaiPelaOficial()
    {
        // O nome curto do site entra na URL da clássica; um valor estranho não pode escapar dela.
        (UnifiCloudClient objClient, StubHandler objHandler) = CreateClient(RespondeClassicaOk);

        await objClient.AuthorizeGuestAsync(CreateConfig(SiteId, sSite: "../../x"), "36:9d:94:1e:aa:10", 1440);

        Assert.Equal(0, objHandler.IClassicas);
        Assert.Equal(1, objHandler.IAutorizacoesOficiais);
    }

    // ------------------------------------------------------------------ Teste de conexão (painel)

    [Fact]
    public async Task TestConnectionAsync_ConsoleComUmSite_DescreveOSiteEGravaOId()
    {
        (UnifiCloudClient objClient, StubHandler objHandler) = CreateClient(_ => Json(SitesJson));
        CompanyUnifi objConfig = CreateConfig();

        string sDetalhe = await objClient.TestConnectionAsync(objConfig);

        Assert.Contains("Default", sDetalhe);
        Assert.Equal(SiteId, objConfig.SiteId);
    }

    [Fact]
    public async Task TestConnectionAsync_ConsoleComMaisDeUmSite_ExigeEscolhaManual()
    {
        const string sDoisSites =
            """{"totalCount":2,"data":[{"id":"88f7af54-98f8-306a-a1c7-c9349722b1f6","name":"Default"},{"id":"11111111-2222-3333-4444-555555555555","name":"Filial"}]}""";
        (UnifiCloudClient objClient, StubHandler objHandler) = CreateClient(_ => Json(sDoisSites));
        CompanyUnifi objConfig = CreateConfig();

        UnifiException objException = await Assert.ThrowsAsync<UnifiException>(
            () => objClient.TestConnectionAsync(objConfig));

        Assert.Contains("mais de um site", objException.Message);
        Assert.Contains("Filial", objException.Message);
        // Nada é adivinhado: a unidade continua sem site definido.
        Assert.Equal("", objConfig.SiteId);
    }
}
