using System.Net;
using System.Text;
using AccessWifi.Api.Infrastructure.Unifi;
using Microsoft.Extensions.Caching.Memory;
using Models.DataBase;

namespace AccessWifi.Api.Tests;

/// <summary>
/// Cobre o caminho pela nuvem da Ubiquiti sem sair da máquina: a rede é substituída por um
/// handler falso que devolve exatamente os corpos observados na controladora real da Itaituba.
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

    /// <summary>Handler falso: guarda o que foi pedido e responde conforme a função informada.</summary>
    private class StubHandler : HttpMessageHandler
    {
        public List<HttpRequestMessage> ObjRequests { get; } = [];
        public List<string> ObjBodies { get; } = [];
        public required Func<HttpRequestMessage, int, HttpResponseMessage> ObjResponder { get; init; }

        /// <summary>Se definido, a busca do aparelho só responde quando o portão abrir.</summary>
        public TaskCompletionSource? ObjPortao { get; set; }

        public int IBuscas => ObjRequests.Count(objRequest => objRequest.RequestUri!.ToString().Contains("/clients?filter="));
        public int IAutorizacoes => ObjRequests.Count(objRequest => objRequest.Method == HttpMethod.Post);

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage objRequest, CancellationToken objCancellationToken)
        {
            if (ObjPortao is not null && objRequest.RequestUri!.ToString().Contains("/clients?filter="))
            {
                await ObjPortao.Task.WaitAsync(objCancellationToken);
            }
            ObjBodies.Add(objRequest.Content is null
                ? ""
                : await objRequest.Content.ReadAsStringAsync(objCancellationToken));
            ObjRequests.Add(objRequest);
            return ObjResponder(objRequest, ObjRequests.Count - 1);
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

    private static HttpResponseMessage Json(string sBody, HttpStatusCode objStatus = HttpStatusCode.OK)
    {
        return new HttpResponseMessage(objStatus)
        {
            Content = new StringContent(sBody, Encoding.UTF8, "application/json"),
        };
    }

    /// <summary>Responde na ordem do fluxo real: sites, clients, actions.</summary>
    private static HttpResponseMessage RespondeFluxoFeliz(HttpRequestMessage objRequest, int iIndex)
    {
        string sUrl = objRequest.RequestUri!.ToString();
        if (sUrl.EndsWith("/sites")) return Json(SitesJson);
        if (sUrl.Contains("/clients?filter=")) return Json(ClientFoundJson);
        return Json(AuthorizedJson);
    }

    private static CompanyUnifi CreateConfig(string sSiteId = "")
    {
        return new CompanyUnifi
        {
            Mode = UnifiMode.Cloud,
            ConsoleId = ConsoleId,
            ApiKey = TestHelpers.CreateEncryptor().Encrypt(ApiKey) ?? "",
            SiteId = sSiteId,
        };
    }

    private static (UnifiCloudClient, StubHandler) CreateClient(
        Func<HttpRequestMessage, int, HttpResponseMessage> objResponder)
    {
        StubHandler objHandler = new StubHandler { ObjResponder = objResponder };
        UnifiCloudClient objClient = new UnifiCloudClient(
            new StubHttpClientFactory(objHandler), TestHelpers.CreateEncryptor(),
            new MemoryCache(new MemoryCacheOptions()));
        return (objClient, objHandler);
    }

    [Fact]
    public async Task AuthorizeGuestAsync_FluxoCompleto_DescobreSiteAchaOAparelhoEAutoriza()
    {
        (UnifiCloudClient objClient, StubHandler objHandler) = CreateClient(RespondeFluxoFeliz);
        CompanyUnifi objConfig = CreateConfig();

        await objClient.AuthorizeGuestAsync(objConfig, "36:9d:94:1e:aa:10", 1440);

        Assert.Equal(3, objHandler.ObjRequests.Count);

        // 1) Descoberta do site, no caminho do connector proxy.
        Assert.Equal(
            $"https://api.ui.com/v1/connector/consoles/{ConsoleId}/proxy/network/integration/v1/sites",
            objHandler.SUrl(0));

        // 2) Busca do aparelho pelo MAC, com o filtro da API do Network.
        Assert.Contains($"/sites/{SiteId}/clients?filter=", objHandler.SUrl(1));
        Assert.Contains(Uri.EscapeDataString("macAddress.eq('36:9d:94:1e:aa:10')"), objHandler.SUrl(1));

        // 3) Autorização propriamente dita.
        Assert.Equal(HttpMethod.Post, objHandler.ObjRequests[2].Method);
        Assert.EndsWith($"/sites/{SiteId}/clients/{ClientId}/actions", objHandler.SUrl(2));
        Assert.Contains("\"action\":\"AUTHORIZE_GUEST_ACCESS\"", objHandler.ObjBodies[2]);
        Assert.Contains("\"timeLimitMinutes\":1440", objHandler.ObjBodies[2]);

        // A chave vai em todas as chamadas, decifrada.
        Assert.All(objHandler.ObjRequests, objRequest =>
            Assert.Equal(ApiKey, objRequest.Headers.GetValues("X-API-KEY").Single()));

        // D3: o site descoberto fica na entidade para quem chamou persistir.
        Assert.Equal(SiteId, objConfig.SiteId);
    }

    [Fact]
    public async Task AuthorizeGuestAsync_ComSiteIdJaGravado_NaoConsultaOsSitesDeNovo()
    {
        (UnifiCloudClient objClient, StubHandler objHandler) = CreateClient(RespondeFluxoFeliz);

        await objClient.AuthorizeGuestAsync(CreateConfig(SiteId), "36:9d:94:1e:aa:10", 1440);

        // Só a busca do aparelho e a autorização: a descoberta do site é feita uma vez só.
        Assert.Equal(2, objHandler.ObjRequests.Count);
        Assert.DoesNotContain(objHandler.ObjRequests, objRequest =>
            objRequest.RequestUri!.ToString().EndsWith("/sites"));
    }

    [Theory]
    [InlineData("36-9D-94-1E-AA-10")]
    [InlineData("369D941EAA10")]
    [InlineData("36:9D:94:1E:AA:10")]
    public async Task AuthorizeGuestAsync_MacEmQualquerFormato_NormalizaParaOFormatoDaUnifi(string sMac)
    {
        (UnifiCloudClient objClient, StubHandler objHandler) = CreateClient(RespondeFluxoFeliz);

        await objClient.AuthorizeGuestAsync(CreateConfig(SiteId), sMac, 1440);

        Assert.Contains(Uri.EscapeDataString("macAddress.eq('36:9d:94:1e:aa:10')"), objHandler.SUrl(0));
    }

    [Fact]
    public async Task AuthorizeGuestAsync_MacInvalido_NemChegaAFalarComARede()
    {
        (UnifiCloudClient objClient, StubHandler objHandler) = CreateClient(RespondeFluxoFeliz);

        UnifiException objException = await Assert.ThrowsAsync<UnifiException>(
            () => objClient.AuthorizeGuestAsync(CreateConfig(SiteId), "não-é-um-mac", 1440));

        Assert.Equal("MAC do aparelho em formato inválido.", objException.Message);
        Assert.Empty(objHandler.ObjRequests);
    }

    [Fact]
    public async Task AuthorizeGuestAsync_AparelhoSoApareceNaSegundaConsulta_Autoriza()
    {
        // D6: o aparelho acabou de conectar e a controladora ainda não o listou.
        int iConsultasDeCliente = 0;
        (UnifiCloudClient objClient, StubHandler objHandler) = CreateClient((objRequest, iIndex) =>
        {
            string sUrl = objRequest.RequestUri!.ToString();
            if (sUrl.Contains("/clients?filter="))
            {
                iConsultasDeCliente++;
                return Json(iConsultasDeCliente == 1 ? ClientEmptyJson : ClientFoundJson);
            }
            return Json(AuthorizedJson);
        });

        await objClient.AuthorizeGuestAsync(CreateConfig(SiteId), "36:9d:94:1e:aa:10", 1440);

        Assert.Equal(2, iConsultasDeCliente);
        Assert.EndsWith("/actions", objHandler.SUrl(2));
    }

    [Fact]
    public async Task AuthorizeGuestAsync_AparelhoNuncaAparece_ErroClaroESemAutorizar()
    {
        (UnifiCloudClient objClient, StubHandler objHandler) =
            CreateClient((objRequest, iIndex) => Json(ClientEmptyJson));

        UnifiException objException = await Assert.ThrowsAsync<UnifiException>(
            () => objClient.AuthorizeGuestAsync(CreateConfig(SiteId), "36:9d:94:1e:aa:10", 1440));

        Assert.Contains("Aparelho não encontrado", objException.Message);
        // Duas tentativas de busca e nenhum POST de autorização.
        Assert.Equal(2, objHandler.ObjRequests.Count);
        Assert.DoesNotContain(objHandler.ObjRequests, objRequest => objRequest.Method == HttpMethod.Post);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "{}", "inválida ou revogada")]
    [InlineData(HttpStatusCode.Forbidden, "{}", "não alcança este console")]
    [InlineData(HttpStatusCode.TooManyRequests, "{}", "Limite de chamadas")]
    [InlineData(HttpStatusCode.UnprocessableEntity,
        """{"code":"api.client.not-guest","message":"Client is not a guest"}""",
        "rede de visitantes")]
    public async Task AuthorizeGuestAsync_ErroDaNuvem_MensagemExplicaACausa(
        HttpStatusCode objStatus, string sBody, string sTrechoEsperado)
    {
        // D11: 401, 403, 429 e 422 têm causas e soluções diferentes — todos vistos em campo.
        (UnifiCloudClient objClient, StubHandler objHandler) = CreateClient((objRequest, iIndex) =>
        {
            string sUrl = objRequest.RequestUri!.ToString();
            if (sUrl.Contains("/clients?filter=")) return Json(ClientFoundJson);
            return Json(sBody, objStatus);
        });

        UnifiException objException = await Assert.ThrowsAsync<UnifiException>(
            () => objClient.AuthorizeGuestAsync(CreateConfig(SiteId), "36:9d:94:1e:aa:10", 1440));

        Assert.Contains(sTrechoEsperado, objException.Message);
    }

    [Fact]
    public async Task AuthorizeGuestAsync_SemChaveDeApi_NemChegaAFalarComARede()
    {
        (UnifiCloudClient objClient, StubHandler objHandler) = CreateClient(RespondeFluxoFeliz);
        CompanyUnifi objConfig = CreateConfig(SiteId);
        objConfig.ApiKey = "";

        UnifiException objException = await Assert.ThrowsAsync<UnifiException>(
            () => objClient.AuthorizeGuestAsync(objConfig, "36:9d:94:1e:aa:10", 1440));

        Assert.Contains("Chave de API", objException.Message);
        Assert.Empty(objHandler.ObjRequests);
    }

    [Fact]
    public async Task AuthorizeGuestAsync_ConsoleIdInvalido_NemChegaAFalarComARede()
    {
        (UnifiCloudClient objClient, StubHandler objHandler) = CreateClient(RespondeFluxoFeliz);
        CompanyUnifi objConfig = CreateConfig(SiteId);
        // Barra no ConsoleId escaparia do caminho previsto na URL.
        objConfig.ConsoleId = "../../algum-outro-console";

        UnifiException objException = await Assert.ThrowsAsync<UnifiException>(
            () => objClient.AuthorizeGuestAsync(objConfig, "36:9d:94:1e:aa:10", 1440));

        Assert.Contains("Console da nuvem UniFi não configurado", objException.Message);
        Assert.Empty(objHandler.ObjRequests);
    }

    [Fact]
    public async Task TestConnectionAsync_ConsoleComUmSite_DescreveOSiteEGravaOId()
    {
        (UnifiCloudClient objClient, StubHandler objHandler) =
            CreateClient((objRequest, iIndex) => Json(SitesJson));
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
        (UnifiCloudClient objClient, StubHandler objHandler) =
            CreateClient((objRequest, iIndex) => Json(sDoisSites));
        CompanyUnifi objConfig = CreateConfig();

        UnifiException objException = await Assert.ThrowsAsync<UnifiException>(
            () => objClient.TestConnectionAsync(objConfig));

        Assert.Contains("mais de um site", objException.Message);
        Assert.Contains("Filial", objException.Message);
        // Nada é adivinhado: a unidade continua sem site definido.
        Assert.Equal("", objConfig.SiteId);
    }

    // ------------------------------------------------------------------ Preparo (velocidade no caixa)

    [Fact]
    public async Task Prepare_DepoisAutorizar_NaHoraDoToqueSoAutoriza()
    {
        // O coração da otimização: a busca do aparelho sai do caminho do toque em "Conectar".
        (UnifiCloudClient objClient, StubHandler objHandler) = CreateClient(RespondeFluxoFeliz);
        CompanyUnifi objConfig = CreateConfig(SiteId);

        await objClient.PrepareAsync(objConfig, "36:9d:94:1e:aa:10");
        await objClient.AuthorizeGuestAsync(objConfig, "36:9d:94:1e:aa:10", 1440);

        // Uma busca (a do preparo) e uma autorização — o /authorize não buscou de novo.
        Assert.Equal(1, objHandler.IBuscas);
        Assert.Equal(1, objHandler.IAutorizacoes);
        Assert.EndsWith($"/clients/{ClientId}/actions", objHandler.SUrl(1));
    }

    [Fact]
    public async Task Autorizar_ComPreparoAindaEmAndamento_EsperaAMesmaBuscaEmVezDeComecarOutra()
    {
        // Cliente rápido: tocou em "Conectar" antes de a busca adiantada terminar.
        (UnifiCloudClient objClient, StubHandler objHandler) = CreateClient(RespondeFluxoFeliz);
        objHandler.ObjPortao = new TaskCompletionSource();
        CompanyUnifi objConfig = CreateConfig(SiteId);

        await objClient.PrepareAsync(objConfig, "36:9d:94:1e:aa:10");
        Task objAutorizacao = objClient.AuthorizeGuestAsync(objConfig, "36:9d:94:1e:aa:10", 1440);
        await Task.Delay(50);
        Assert.False(objAutorizacao.IsCompleted); // esperando a busca que já está em andamento

        objHandler.ObjPortao.SetResult();
        await objAutorizacao;

        Assert.Equal(1, objHandler.IBuscas);
        Assert.Equal(1, objHandler.IAutorizacoes);
    }

    [Fact]
    public async Task Prepare_AparelhoAindaNaoApareceu_AutorizarBuscaDeNovoEFunciona()
    {
        // O preparo não achou (aparelho acabou de conectar); o /authorize não pode herdar o "não achei".
        int iBusca = 0;
        (UnifiCloudClient objClient, StubHandler objHandler) = CreateClient((objRequest, iIndex) =>
            objRequest.RequestUri!.ToString().Contains("/clients?filter=")
                ? Json(++iBusca == 1 ? ClientEmptyJson : ClientFoundJson)
                : Json(AuthorizedJson));
        CompanyUnifi objConfig = CreateConfig(SiteId);

        await objClient.PrepareAsync(objConfig, "36:9d:94:1e:aa:10");
        await objClient.AuthorizeGuestAsync(objConfig, "36:9d:94:1e:aa:10", 1440);

        Assert.Equal(2, objHandler.IBuscas);
        Assert.Equal(1, objHandler.IAutorizacoes);
    }

    [Fact]
    public async Task Autorizar_IdPreparadoNaoValeMais_BuscaDeNovoEAutoriza()
    {
        // Entre o preparo e o toque o aparelho saiu e voltou; a controladora não reconhece o ID antigo.
        const string sIdNovo = "11111111-2222-3333-8444-555555555555";
        int iBusca = 0;
        (UnifiCloudClient objClient, StubHandler objHandler) = CreateClient((objRequest, iIndex) =>
        {
            string sUrl = objRequest.RequestUri!.ToString();
            if (sUrl.Contains("/clients?filter="))
            {
                return Json(++iBusca == 1 ? ClientFoundJson : ClientFoundJson.Replace(ClientId, sIdNovo));
            }
            return sUrl.Contains(ClientId)
                ? Json("{\"code\":\"api.client.not-found\"}", HttpStatusCode.NotFound)
                : Json(AuthorizedJson);
        });
        CompanyUnifi objConfig = CreateConfig(SiteId);

        await objClient.PrepareAsync(objConfig, "36:9d:94:1e:aa:10");
        await objClient.AuthorizeGuestAsync(objConfig, "36:9d:94:1e:aa:10", 1440);

        Assert.Equal(2, objHandler.IBuscas);
        Assert.Equal(2, objHandler.IAutorizacoes);
        Assert.EndsWith($"/clients/{sIdNovo}/actions", objHandler.SUrl(3));
    }

    [Fact]
    public async Task Prepare_DuasVezesSeguidas_FazUmaBuscaSo()
    {
        // O StrictMode do React em dev, ou um recarregar de página, não podem dobrar as idas à loja.
        (UnifiCloudClient objClient, StubHandler objHandler) = CreateClient(RespondeFluxoFeliz);
        CompanyUnifi objConfig = CreateConfig(SiteId);

        await objClient.PrepareAsync(objConfig, "36:9d:94:1e:aa:10");
        await objClient.PrepareAsync(objConfig, "36-9D-94-1E-AA-10"); // mesmo aparelho, outro formato
        await objClient.AuthorizeGuestAsync(objConfig, "36:9d:94:1e:aa:10", 1440);

        Assert.Equal(1, objHandler.IBuscas);
    }

    [Fact]
    public async Task Prepare_SemSiteIdConhecido_NaoFazNada()
    {
        // Descobrir o site grava na entidade; em segundo plano não há quem persista.
        (UnifiCloudClient objClient, StubHandler objHandler) = CreateClient(RespondeFluxoFeliz);

        await objClient.PrepareAsync(CreateConfig(), "36:9d:94:1e:aa:10");

        Assert.Empty(objHandler.ObjRequests);
    }

    [Theory]
    [InlineData("nao-e-mac")]
    [InlineData("")]
    public async Task Prepare_MacInvalido_NaoLancaNemChamaARede(string sMac)
    {
        (UnifiCloudClient objClient, StubHandler objHandler) = CreateClient(RespondeFluxoFeliz);

        await objClient.PrepareAsync(CreateConfig(SiteId), sMac);

        Assert.Empty(objHandler.ObjRequests);
    }

    [Fact]
    public async Task Prepare_SemChaveDeApi_NaoLancaNemChamaARede()
    {
        (UnifiCloudClient objClient, StubHandler objHandler) = CreateClient(RespondeFluxoFeliz);
        CompanyUnifi objConfig = CreateConfig(SiteId);
        objConfig.ApiKey = "";

        await objClient.PrepareAsync(objConfig, "36:9d:94:1e:aa:10");

        Assert.Empty(objHandler.ObjRequests);
    }
}
