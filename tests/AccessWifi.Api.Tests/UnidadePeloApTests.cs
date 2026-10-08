using System.Net;
using System.Text;
using AccessWifi.Api.Controllers;
using AccessWifi.Api.Features.Authorize;
using AccessWifi.Api.Features.Settings;
using AccessWifi.Api.Features.Units;
using AccessWifi.Api.Infrastructure.Unifi;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Models.DataBase;
using Models.Persistence;
using Models.Security;

namespace AccessWifi.Api.Tests;

/// <summary>
/// Unidade pelo ponto de acesso (PROPOSTA_UNIDADE_PELO_AP.md): todas as lojas no mesmo endereço, e o MAC do
/// AP (que a UniFi manda em toda visita) diz de qual loja é. A Itaituba, que já usa o portal pelo endereço,
/// não pode parar em nenhum passo — vários testes abaixo existem só para isso.
/// </summary>
public class UnidadePeloApTests
{
    private const string HostCompartilhado = "vps11702.panel.icontainer.online";
    private const string ConsoleItaituba = "58D61F5E1531000000000A2C62C1000000006921:896725606";
    private const string ConsoleCameta = "70A7413F0E9A00000000079D4F6A000000000841:1281399421";
    private const string ApItaituba = "8c:30:66:4e:9b:58";
    private const string ApCameta = "d0:21:f9:aa:bb:01";
    private const string ApiKey = "chave-de-teste";

    // ---------------------------------------------------------------- nuvem falsa (GET v1/devices)

    private class FakeCloud : HttpMessageHandler
    {
        public int IChamadas { get; private set; }
        public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;
        public TimeSpan Atraso { get; set; } = TimeSpan.Zero;

        /// <summary>hostId → aparelhos (mac, nome, modelo).</summary>
        public Dictionary<string, List<(string Mac, string Name, string Model)>> ObjConsoles { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage objRequest, CancellationToken objCancellationToken)
        {
            IChamadas++;
            if (Atraso > TimeSpan.Zero)
            {
                await Task.Delay(Atraso, objCancellationToken);
            }
            Assert.Equal(ApiKey, objRequest.Headers.GetValues("X-API-KEY").Single());
            Assert.StartsWith("https://api.ui.com/v1/devices", objRequest.RequestUri!.ToString());
            if (Status != HttpStatusCode.OK)
            {
                return new HttpResponseMessage(Status) { Content = new StringContent("{}") };
            }

            // Mesmo formato da resposta real: um grupo por console, com os aparelhos dentro.
            string sData = string.Join(',', ObjConsoles.Select(console =>
                $$"""{"hostId":"{{console.Key}}","updatedAt":"2026-10-08T12:00:00Z","devices":[{{string.Join(',', console.Value.Select(device =>
                    $$"""{"id":"x","mac":"{{device.Mac}}","name":"{{device.Name}}","model":"{{device.Model}}","shortname":"U6L","status":"online"}"""))}}]}"""));
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent($$"""{"data":[{{sData}}],"httpStatusCode":200,"traceId":"t"}""", Encoding.UTF8, "application/json"),
            };
        }
    }

    private class FakeHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _objHandler;
        public FakeHttpClientFactory(HttpMessageHandler objHandler) => _objHandler = objHandler;
        public HttpClient CreateClient(string sName) =>
            new HttpClient(_objHandler, disposeHandler: false) { BaseAddress = new Uri("https://api.ui.com/") };
    }

    private class FakeUnifiClient : IUnifiClient
    {
        public CompanyUnifi? ObjConfigRecebida { get; private set; }

        public Task AuthorizeGuestAsync(
            CompanyUnifi objConfig, string sMac, int iAccessMinutes, CancellationToken objCancellationToken = default)
        {
            ObjConfigRecebida = objConfig;
            return Task.CompletedTask;
        }

        public Task<string> TestConnectionAsync(CompanyUnifi objConfig, CancellationToken objCancellationToken = default) =>
            Task.FromResult("ok");
    }

    /// <summary>Banco compartilhado entre o "request" do teste e o escopo próprio da leitura na hora.</summary>
    private sealed class Ambiente : IDisposable
    {
        public FakeCloud ObjCloud { get; } = new FakeCloud();
        public ServiceProvider ObjServices { get; }
        public AppDbContext ObjDb { get; }
        public IMemoryCache ObjCache { get; } = new MemoryCache(new MemoryCacheOptions());

        public Ambiente()
        {
            string sDbName = Guid.NewGuid().ToString();
            InMemoryDatabaseRoot objRoot = new InMemoryDatabaseRoot();
            ServiceCollection objServices = new ServiceCollection();
            objServices.AddLogging();
            objServices.AddDbContext<AppDbContext>(objOptions => objOptions.UseInMemoryDatabase(sDbName, objRoot));
            objServices.AddSingleton(TestHelpers.CreateEncryptor());
            objServices.AddSingleton<IHttpClientFactory>(new FakeHttpClientFactory(ObjCloud));
            objServices.AddSingleton<UnifiCloudClient>();
            objServices.AddScoped<UnitDeviceSync>();
            ObjServices = objServices.BuildServiceProvider();
            ObjDb = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(sDbName, objRoot).Options);
        }

        public UnitDeviceSync Sync() =>
            new UnitDeviceSync(
                ObjDb, ObjServices.GetRequiredService<UnifiCloudClient>(), TestHelpers.CreateEncryptor(),
                NullLogger<UnitDeviceSync>.Instance);

        /// <summary>Locator como no Program.cs: com leitura na hora.</summary>
        public UnitLocator Locator() =>
            new UnitLocator(ObjDb, ObjServices.GetRequiredService<IServiceScopeFactory>(), ObjCache, NullLogger<UnitLocator>.Instance);

        public void Dispose()
        {
            ObjDb.Dispose();
            ObjServices.Dispose();
        }
    }

    private static Company CreateCompany(AppDbContext objDb, string sSlug = "regional")
    {
        Company objCompany = new Company { Name = "Lojas Regional", Slug = sSlug };
        objDb.Companies.Add(objCompany);
        objDb.SaveChanges();
        return objCompany;
    }

    private static Unit CreateCloudUnit(
        AppDbContext objDb, Company objCompany, string sSlug, string sConsoleId, string sPortalHost = "",
        string sApiKey = ApiKey)
    {
        IEncryptor objEncryptor = TestHelpers.CreateEncryptor();
        Unit objUnit = new Unit
        {
            IDCompany = objCompany.Id,
            Name = sSlug,
            Slug = sSlug,
            PortalHost = sPortalHost,
            Unifi = new CompanyUnifi
            {
                Mode = UnifiMode.Cloud,
                ConsoleId = sConsoleId,
                ApiKey = objEncryptor.Encrypt(sApiKey) ?? "",
            },
        };
        objDb.Units.Add(objUnit);
        objDb.SaveChanges();
        return objUnit;
    }

    private static void AddDevice(AppDbContext objDb, Unit objUnit, string sMac)
    {
        objDb.UnitDevices.Add(new UnitDevice { IDUnit = objUnit.Id, Mac = sMac, Name = "AP", Model = "U6L", SyncedAt = DateTime.UtcNow });
        objDb.SaveChanges();
    }

    /// <summary>Itaituba como está em produção: nuvem, endereço do portal = o endereço compartilhado.</summary>
    private static (Unit Itaituba, Unit Cameta) CreateLojas(AppDbContext objDb)
    {
        Company objCompany = CreateCompany(objDb);
        Unit objItaituba = CreateCloudUnit(objDb, objCompany, "itaituba", ConsoleItaituba, HostCompartilhado);
        Unit objCameta = CreateCloudUnit(objDb, objCompany, "cameta-04", ConsoleCameta);
        return (objItaituba, objCameta);
    }

    // ---------------------------------------------------------------- MAC

    [Theory]
    [InlineData("8c:30:66:4e:9b:58", "8c:30:66:4e:9b:58")]
    [InlineData("8C-30-66-4E-9B-58", "8c:30:66:4e:9b:58")]
    [InlineData("8c30664e9b58", "8c:30:66:4e:9b:58")]
    [InlineData(" 8C:30:66:4E:9B:58 ", "8c:30:66:4e:9b:58")]
    [InlineData("", "")]
    [InlineData(null, "")]
    [InlineData("8c:30:66:4e:9b", "")]
    [InlineData("8c:30:66:4e:9b:58:00", "")]
    [InlineData("zz:30:66:4e:9b:58", "")]
    [InlineData("8c:30:66:4e:9b:58-lixo-que-nao-e-mac", "")]
    public void MacAddress_Normaliza(string? sEntrada, string sEsperado)
    {
        Assert.Equal(sEsperado, MacAddress.Normalize(sEntrada));
    }

    // ---------------------------------------------------------------- Locator (só banco)

    [Fact]
    public async Task Locator_ApConhecido_AchaALojaDoAp_MesmoNoEnderecoDeOutra()
    {
        using AppDbContext objDb = TestHelpers.CreateDbContext();
        (Unit objItaituba, Unit objCameta) = CreateLojas(objDb);
        AddDevice(objDb, objItaituba, ApItaituba);
        AddDevice(objDb, objCameta, ApCameta);

        Unit? objUnit = await new UnitLocator(objDb).FindAsync(objDb.Units, null, HostCompartilhado, "D0-21-F9-AA-BB-01");

        Assert.Equal(objCameta.Id, objUnit?.Id);
    }

    [Fact]
    public async Task Locator_ItaitubaPeloAp()
    {
        using AppDbContext objDb = TestHelpers.CreateDbContext();
        (Unit objItaituba, Unit objCameta) = CreateLojas(objDb);
        AddDevice(objDb, objItaituba, ApItaituba);
        AddDevice(objDb, objCameta, ApCameta);

        Unit? objUnit = await new UnitLocator(objDb).FindAsync(objDb.Units, null, HostCompartilhado, ApItaituba);

        Assert.Equal(objItaituba.Id, objUnit?.Id);
    }

    [Fact]
    public async Task Locator_SlugTemPrioridadeSobreOAp()
    {
        using AppDbContext objDb = TestHelpers.CreateDbContext();
        (Unit objItaituba, Unit objCameta) = CreateLojas(objDb);
        AddDevice(objDb, objCameta, ApCameta);

        Unit? objUnit = await new UnitLocator(objDb).FindAsync(objDb.Units, "itaituba", HostCompartilhado, ApCameta);

        Assert.Equal(objItaituba.Id, objUnit?.Id);
    }

    [Fact]
    public async Task Locator_SemAp_ContinuaPeloEndereco()
    {
        using AppDbContext objDb = TestHelpers.CreateDbContext();
        (Unit objItaituba, Unit objCameta) = CreateLojas(objDb);
        AddDevice(objDb, objItaituba, ApItaituba);

        Unit? objUnit = await new UnitLocator(objDb).FindAsync(objDb.Units, null, HostCompartilhado, null);

        Assert.Equal(objItaituba.Id, objUnit?.Id);
    }

    [Fact]
    public async Task Locator_AntesDaPrimeiraLeitura_ItaitubaContinuaPeloEndereco()
    {
        // Logo depois do deploy ainda não há aparelho gravado: nada pode mudar para a Itaituba.
        using AppDbContext objDb = TestHelpers.CreateDbContext();
        (Unit objItaituba, _) = CreateLojas(objDb);

        Unit? objUnit = await new UnitLocator(objDb).FindAsync(objDb.Units, null, HostCompartilhado, ApItaituba);

        Assert.Equal(objItaituba.Id, objUnit?.Id);
    }

    [Fact]
    public async Task Locator_ApDesconhecido_ComListaNaUnidadeDoEndereco_NaoChuta()
    {
        // O endereço é de todas as lojas: AP que ninguém conhece não pode virar cadastro da Itaituba.
        using AppDbContext objDb = TestHelpers.CreateDbContext();
        (Unit objItaituba, _) = CreateLojas(objDb);
        AddDevice(objDb, objItaituba, ApItaituba);

        Unit? objUnit = await new UnitLocator(objDb).FindAsync(objDb.Units, null, HostCompartilhado, "aa:aa:aa:aa:aa:aa");

        Assert.Null(objUnit);
    }

    [Fact]
    public async Task Locator_ApLixo_ValeComoSemAp()
    {
        using AppDbContext objDb = TestHelpers.CreateDbContext();
        (Unit objItaituba, _) = CreateLojas(objDb);
        AddDevice(objDb, objItaituba, ApItaituba);

        Unit? objUnit = await new UnitLocator(objDb).FindAsync(objDb.Units, null, HostCompartilhado, "nao-e-mac");

        Assert.Equal(objItaituba.Id, objUnit?.Id);
    }

    [Fact]
    public async Task Locator_ApEmDuasUnidades_SoAceitaSeAUnidadeDoEnderecoForUmaDelas()
    {
        using AppDbContext objDb = TestHelpers.CreateDbContext();
        (Unit objItaituba, Unit objCameta) = CreateLojas(objDb);
        Unit objOutra = CreateCloudUnit(objDb, objDb.Companies.Single(), "santarem", ConsoleCameta);
        AddDevice(objDb, objItaituba, ApItaituba);
        AddDevice(objDb, objCameta, ApItaituba);
        AddDevice(objDb, objCameta, ApCameta);
        AddDevice(objDb, objOutra, ApCameta);

        UnitLocator objLocator = new UnitLocator(objDb);
        Assert.Equal(objItaituba.Id, (await objLocator.FindAsync(objDb.Units, null, HostCompartilhado, ApItaituba))?.Id);
        Assert.Null(await objLocator.FindAsync(objDb.Units, null, HostCompartilhado, ApCameta));
    }

    [Fact]
    public async Task Locator_SemSlugSemHostSemAp_Nulo()
    {
        using AppDbContext objDb = TestHelpers.CreateDbContext();
        CreateLojas(objDb);

        Assert.Null(await new UnitLocator(objDb).FindAsync(objDb.Units, null, null, null));
        Assert.Null(await new UnitLocator(objDb).FindAsync(objDb.Units, null, "  ", ""));
    }

    // ---------------------------------------------------------------- leitura da nuvem

    [Fact]
    public async Task Sync_GravaOsApsDeCadaConsole_UmaChamadaPorChave()
    {
        using Ambiente objAmb = new Ambiente();
        (Unit objItaituba, Unit objCameta) = CreateLojas(objAmb.ObjDb);
        objAmb.ObjCloud.ObjConsoles[ConsoleItaituba] =
        [
            ("8C:30:66:4E:9B:58", "AP Salão", "U6 Lite"),
            ("8c:30:66:4e:9b:59", "AP Caixa", "U6 Lite"),
            ("70:a7:41:00:00:01", "UDR", "UDR"),
        ];
        objAmb.ObjCloud.ObjConsoles[ConsoleCameta] = [(ApCameta, "AP", "U6 Lite")];
        objAmb.ObjCloud.ObjConsoles["console-de-outra-empresa"] = [("11:11:11:11:11:11", "AP", "U6 Lite")];

        UnitDeviceSync.Result objResult = await objAmb.Sync().SyncAsync();

        Assert.Equal(new UnitDeviceSync.Result(2, 4, 0), objResult);
        Assert.Equal(1, objAmb.ObjCloud.IChamadas);
        List<string> objMacsItaituba = objAmb.ObjDb.UnitDevices
            .Where(device => device.IDUnit == objItaituba.Id).Select(device => device.Mac).OrderBy(sMac => sMac).ToList();
        Assert.Equal(new List<string> { "70:a7:41:00:00:01", "8c:30:66:4e:9b:58", "8c:30:66:4e:9b:59" }, objMacsItaituba);
        Assert.False(objAmb.ObjDb.UnitDevices.Any(device => device.Mac == "11:11:11:11:11:11"));
        Assert.Equal("AP Salão", objAmb.ObjDb.UnitDevices.Single(device => device.Mac == ApItaituba).Name);
        Assert.NotNull(objAmb.ObjDb.Units.Single(unit => unit.Id == objCameta.Id).DevicesSyncedAt);
    }

    [Fact]
    public async Task Sync_ApQueSaiuDoConsole_DeixaDeContar_EONovoEntra()
    {
        using Ambiente objAmb = new Ambiente();
        (Unit objItaituba, _) = CreateLojas(objAmb.ObjDb);
        AddDevice(objAmb.ObjDb, objItaituba, "8c:30:66:00:00:99");
        AddDevice(objAmb.ObjDb, objItaituba, ApItaituba);
        objAmb.ObjCloud.ObjConsoles[ConsoleItaituba] = [(ApItaituba, "AP Salão", "U6 Lite"), ("8c:30:66:00:00:01", "AP Novo", "U7")];

        await objAmb.Sync().SyncAsync();

        List<string> objMacs = objAmb.ObjDb.UnitDevices.AsNoTracking()
            .Where(device => device.IDUnit == objItaituba.Id).Select(device => device.Mac).OrderBy(sMac => sMac).ToList();
        Assert.Equal(new List<string> { "8c:30:66:00:00:01", ApItaituba }, objMacs);
    }

    [Fact]
    public async Task Sync_NuvemFalhou_MantemOsApsJaGravados_EAnotaOMotivo()
    {
        using Ambiente objAmb = new Ambiente();
        (Unit objItaituba, _) = CreateLojas(objAmb.ObjDb);
        AddDevice(objAmb.ObjDb, objItaituba, ApItaituba);
        objAmb.ObjCloud.Status = HttpStatusCode.Unauthorized;

        UnitDeviceSync.Result objResult = await objAmb.Sync().SyncAsync();

        Assert.Equal(2, objResult.Failures);
        Assert.True(objAmb.ObjDb.UnitDevices.Any(device => device.Mac == ApItaituba));
        Assert.Contains("inválida", objAmb.ObjDb.Units.Single(unit => unit.Id == objItaituba.Id).DevicesSyncError);
    }

    [Fact]
    public async Task Sync_ConsoleSemAparelhoNaNuvem_MantemOsGravados()
    {
        using Ambiente objAmb = new Ambiente();
        (Unit objItaituba, _) = CreateLojas(objAmb.ObjDb);
        AddDevice(objAmb.ObjDb, objItaituba, ApItaituba);
        objAmb.ObjCloud.ObjConsoles[ConsoleCameta] = [(ApCameta, "AP", "U6 Lite")];

        await objAmb.Sync().SyncAsync();

        Assert.True(objAmb.ObjDb.UnitDevices.Any(device => device.Mac == ApItaituba));
        Assert.StartsWith("Nenhum aparelho", objAmb.ObjDb.Units.Single(unit => unit.Id == objItaituba.Id).DevicesSyncError);
    }

    [Fact]
    public async Task Sync_IgnoraUnidadeLocalEInativa()
    {
        using Ambiente objAmb = new Ambiente();
        (Unit objItaituba, Unit objCameta) = CreateLojas(objAmb.ObjDb);
        objItaituba.Unifi.Mode = UnifiMode.Local;
        objCameta.Active = false;
        objAmb.ObjDb.SaveChanges();

        UnitDeviceSync.Result objResult = await objAmb.Sync().SyncAsync();

        Assert.Equal(new UnitDeviceSync.Result(0, 0, 0), objResult);
        Assert.Equal(0, objAmb.ObjCloud.IChamadas);
    }

    // ---------------------------------------------------------------- leitura na hora (AP ainda desconhecido)

    [Fact]
    public async Task Locator_ApNovo_LeANuvemNaHora_EAchaALoja()
    {
        using Ambiente objAmb = new Ambiente();
        (Unit objItaituba, Unit objCameta) = CreateLojas(objAmb.ObjDb);
        AddDevice(objAmb.ObjDb, objItaituba, ApItaituba);
        objAmb.ObjCloud.ObjConsoles[ConsoleItaituba] = [(ApItaituba, "AP", "U6 Lite")];
        objAmb.ObjCloud.ObjConsoles[ConsoleCameta] = [(ApCameta, "AP", "U6 Lite")];

        Unit? objUnit = await objAmb.Locator().FindAsync(objAmb.ObjDb.Units, null, HostCompartilhado, ApCameta);

        Assert.Equal(objCameta.Id, objUnit?.Id);
        Assert.Equal(1, objAmb.ObjCloud.IChamadas);
    }

    [Fact]
    public async Task Locator_ApConhecido_NaoChamaANuvem()
    {
        using Ambiente objAmb = new Ambiente();
        (Unit objItaituba, _) = CreateLojas(objAmb.ObjDb);
        AddDevice(objAmb.ObjDb, objItaituba, ApItaituba);

        for (int i = 0; i < 5; i++)
        {
            Assert.Equal(objItaituba.Id, (await objAmb.Locator().FindAsync(objAmb.ObjDb.Units, null, HostCompartilhado, ApItaituba))?.Id);
        }
        Assert.Equal(0, objAmb.ObjCloud.IChamadas);
    }

    [Fact]
    public async Task Locator_ApInventado_NaoViraEnxurradaNaNuvem()
    {
        using Ambiente objAmb = new Ambiente();
        (Unit objItaituba, _) = CreateLojas(objAmb.ObjDb);
        AddDevice(objAmb.ObjDb, objItaituba, ApItaituba);
        objAmb.ObjCloud.ObjConsoles[ConsoleItaituba] = [(ApItaituba, "AP", "U6 Lite")];

        for (int i = 0; i < 10; i++)
        {
            string sMac = $"aa:aa:aa:aa:aa:{i:x2}";
            Assert.Null(await objAmb.Locator().FindAsync(objAmb.ObjDb.Units, null, HostCompartilhado, sMac));
            Assert.Null(await objAmb.Locator().FindAsync(objAmb.ObjDb.Units, null, HostCompartilhado, sMac));
        }

        // Uma leitura só: as outras caem no intervalo mínimo de 30 s ou no "já sei que não existe".
        Assert.Equal(1, objAmb.ObjCloud.IChamadas);
    }

    [Fact]
    public async Task Locator_NuvemLenta_DesisteEmQuatroSegundos_ESegueSemOAp()
    {
        using Ambiente objAmb = new Ambiente();
        (Unit objItaituba, _) = CreateLojas(objAmb.ObjDb);
        objAmb.ObjCloud.Atraso = TimeSpan.FromSeconds(30);

        System.Diagnostics.Stopwatch objRelogio = System.Diagnostics.Stopwatch.StartNew();
        Unit? objUnit = await objAmb.Locator().FindAsync(objAmb.ObjDb.Units, null, HostCompartilhado, ApItaituba);
        objRelogio.Stop();

        // Itaituba sem lista ainda: segue pelo endereço, como hoje.
        Assert.Equal(objItaituba.Id, objUnit?.Id);
        Assert.True(objRelogio.Elapsed < TimeSpan.FromSeconds(8), $"Demorou {objRelogio.Elapsed}.");
    }

    [Fact]
    public async Task Locator_NuvemForaDoAr_ItaitubaComListaContinua()
    {
        using Ambiente objAmb = new Ambiente();
        (Unit objItaituba, _) = CreateLojas(objAmb.ObjDb);
        AddDevice(objAmb.ObjDb, objItaituba, ApItaituba);
        objAmb.ObjCloud.Status = HttpStatusCode.InternalServerError;

        await objAmb.Sync().SyncAsync();
        Unit? objUnit = await objAmb.Locator().FindAsync(objAmb.ObjDb.Units, null, HostCompartilhado, ApItaituba);

        Assert.Equal(objItaituba.Id, objUnit?.Id);
    }

    // ---------------------------------------------------------------- portal (GET /settings e POST /authorize)

    [Fact]
    public async Task Settings_MesmoEndereco_TemaDaLojaDoAp()
    {
        using AppDbContext objDb = TestHelpers.CreateDbContext();
        Company objRegional = CreateCompany(objDb, "regional");
        Company objOutra = CreateCompany(objDb, "outra-rede");
        Unit objItaituba = CreateCloudUnit(objDb, objRegional, "itaituba", ConsoleItaituba, HostCompartilhado);
        Unit objLojaOutra = CreateCloudUnit(objDb, objOutra, "outra-loja", ConsoleCameta);
        AddDevice(objDb, objItaituba, ApItaituba);
        AddDevice(objDb, objLojaOutra, ApCameta);
        SettingsController objController = new SettingsController(objDb, new UnitLocator(objDb));

        ActionResult<SettingsDto> objResult = await objController.Get(null, HostCompartilhado, ApCameta, CancellationToken.None);

        SettingsDto objDto = Assert.IsType<SettingsDto>(Assert.IsType<OkObjectResult>(objResult.Result).Value);
        Assert.Equal("outra-loja", objDto.Unit);
    }

    [Fact]
    public async Task Settings_ApDesconhecido_404()
    {
        using AppDbContext objDb = TestHelpers.CreateDbContext();
        (Unit objItaituba, _) = CreateLojas(objDb);
        AddDevice(objDb, objItaituba, ApItaituba);
        SettingsController objController = new SettingsController(objDb, new UnitLocator(objDb));

        ActionResult<SettingsDto> objResult = await objController.Get(null, HostCompartilhado, "aa:aa:aa:aa:aa:aa", CancellationToken.None);

        Assert.IsType<NotFoundObjectResult>(objResult.Result);
    }

    [Fact]
    public async Task Authorize_PeloAp_GravaNaLojaCerta_ELiberaNaControladoraDela()
    {
        using AppDbContext objDb = TestHelpers.CreateDbContext();
        (Unit objItaituba, Unit objCameta) = CreateLojas(objDb);
        AddDevice(objDb, objItaituba, ApItaituba);
        AddDevice(objDb, objCameta, ApCameta);
        FakeUnifiClient objUnifi = new FakeUnifiClient();
        AuthorizeController objController = new AuthorizeController(
            objDb, objUnifi, NullLogger<AuthorizeController>.Instance, new UnitLocator(objDb));

        ActionResult<AuthorizeResponse> objResult = await objController.Post(
            new AuthorizeRequest("Ana", "@ana", "(93) 98888-1234", "10/05/1990", true, null,
                "aa:bb:cc:dd:ee:ff", ApCameta, "PIX REGIONAL", null, HostCompartilhado),
            CancellationToken.None);

        Assert.IsType<OkObjectResult>(objResult.Result);
        Assert.Equal(ConsoleCameta, objUnifi.ObjConfigRecebida?.ConsoleId);
        Assert.Equal(objCameta.Id, objDb.Leads.Single().IDUnit);
    }

    [Fact]
    public async Task Authorize_ItaitubaComoHoje_SemListaAinda_Libera()
    {
        using AppDbContext objDb = TestHelpers.CreateDbContext();
        (Unit objItaituba, _) = CreateLojas(objDb);
        FakeUnifiClient objUnifi = new FakeUnifiClient();
        AuthorizeController objController = new AuthorizeController(
            objDb, objUnifi, NullLogger<AuthorizeController>.Instance, new UnitLocator(objDb));

        ActionResult<AuthorizeResponse> objResult = await objController.Post(
            new AuthorizeRequest("Ana", "@ana", "(93) 98888-1234", "10/05/1990", true, null,
                "aa:bb:cc:dd:ee:ff", ApItaituba, "PIX REGIONAL", null, HostCompartilhado),
            CancellationToken.None);

        Assert.IsType<OkObjectResult>(objResult.Result);
        Assert.Equal(objItaituba.Id, objDb.Leads.Single().IDUnit);
    }

    // ---------------------------------------------------------------- painel

    [Fact]
    public async Task Units_ListaMostraQuantosApsCadaUnidadeTem()
    {
        using AppDbContext objDb = TestHelpers.CreateDbContext();
        (Unit objItaituba, Unit objCameta) = CreateLojas(objDb);
        AddDevice(objDb, objItaituba, ApItaituba);
        AddDevice(objDb, objItaituba, "8c:30:66:4e:9b:59");
        UnitsController objController = new UnitsController(
            objDb, TestHelpers.CreateEncryptor(), new FakeUnifiClient(), NullLogger<UnitsController>.Instance);
        TestHelpers.SetUser(objController, null, "root");

        ActionResult<List<UnitDto>> objResult = await objController.GetAll(null, CancellationToken.None);

        List<UnitDto> objUnits = Assert.IsType<List<UnitDto>>(Assert.IsType<OkObjectResult>(objResult.Result).Value);
        Assert.Equal(2, objUnits.Single(unit => unit.Id == objItaituba.Id).DeviceCount);
        Assert.Equal(0, objUnits.Single(unit => unit.Id == objCameta.Id).DeviceCount);
    }

    [Fact]
    public async Task Units_TrocarOConsole_LimpaOsApsAntigos()
    {
        using AppDbContext objDb = TestHelpers.CreateDbContext();
        (Unit objItaituba, _) = CreateLojas(objDb);
        AddDevice(objDb, objItaituba, ApItaituba);
        UnitsController objController = new UnitsController(
            objDb, TestHelpers.CreateEncryptor(), new FakeUnifiClient(), NullLogger<UnitsController>.Instance);
        TestHelpers.SetUser(objController, null, "root");

        await objController.Update(objItaituba.Id, new UpdateUnitRequest("itaituba", true,
            new UnitUnifiRequest("", "default", "", null, false, false, UnifiMode.Cloud, "NOVOCONSOLE0000000000000000000000000000:123")),
            CancellationToken.None);

        Assert.False(objDb.UnitDevices.Any());
    }

    [Fact]
    public async Task Units_SalvarSemMexerNoConsole_MantemOsAps()
    {
        using AppDbContext objDb = TestHelpers.CreateDbContext();
        (Unit objItaituba, _) = CreateLojas(objDb);
        AddDevice(objDb, objItaituba, ApItaituba);
        UnitsController objController = new UnitsController(
            objDb, TestHelpers.CreateEncryptor(), new FakeUnifiClient(), NullLogger<UnitsController>.Instance);
        TestHelpers.SetUser(objController, null, "root");

        await objController.Update(objItaituba.Id, new UpdateUnitRequest("Itaituba", true,
            new UnitUnifiRequest("", "default", "", null, false, false, UnifiMode.Cloud, ConsoleItaituba)),
            CancellationToken.None);

        Assert.Equal(1, objDb.UnitDevices.Count());
    }
}
