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
public class UnitByApTests
{
    private const string SharedHost = "vps11702.panel.icontainer.online";
    private const string ConsoleItaituba = "58D61F5E1531000000000A2C62C1000000006921:896725606";
    private const string ConsoleCameta = "70A7413F0E9A00000000079D4F6A000000000841:1281399421";
    private const string ApItaituba = "8c:30:66:4e:9b:58";
    private const string ApCameta = "d0:21:f9:aa:bb:01";
    private const string ApiKey = "chave-de-teste";

    // ---------------------------------------------------------------- nuvem falsa (GET v1/devices)

    private class FakeCloud : HttpMessageHandler
    {
        public int ICalls { get; private set; }
        public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;
        public TimeSpan Delay { get; set; } = TimeSpan.Zero;

        /// <summary>hostId → aparelhos (mac, nome, modelo).</summary>
        public Dictionary<string, List<(string Mac, string Name, string Model)>> ObjConsoles { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage objRequest, CancellationToken objCancellationToken)
        {
            ICalls++;
            if (Delay > TimeSpan.Zero)
            {
                await Task.Delay(Delay, objCancellationToken);
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
        public CompanyUnifi? ObjReceivedConfig { get; private set; }

        public Task AuthorizeGuestAsync(
            CompanyUnifi objConfig, string sMac, int iAccessMinutes, CancellationToken objCancellationToken = default)
        {
            ObjReceivedConfig = objConfig;
            return Task.CompletedTask;
        }

        public Task<string> TestConnectionAsync(CompanyUnifi objConfig, CancellationToken objCancellationToken = default) =>
            Task.FromResult("ok");
    }

    /// <summary>Banco compartilhado entre o "request" do teste e o escopo próprio da leitura na hora.</summary>
    private sealed class TestEnvironment : IDisposable
    {
        public FakeCloud ObjCloud { get; } = new FakeCloud();
        public ServiceProvider ObjServices { get; }
        public AppDbContext ObjDb { get; }
        public IMemoryCache ObjCache { get; } = new MemoryCache(new MemoryCacheOptions());

        public TestEnvironment()
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
    private static (Unit Itaituba, Unit Cameta) CreateStores(AppDbContext objDb)
    {
        Company objCompany = CreateCompany(objDb);
        Unit objItaituba = CreateCloudUnit(objDb, objCompany, "itaituba", ConsoleItaituba, SharedHost);
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
    public void MacAddress_Normalizes(string? sInput, string sExpected)
    {
        Assert.Equal(sExpected, MacAddress.Normalize(sInput));
    }

    // ---------------------------------------------------------------- Locator (só banco)

    [Fact]
    public async Task Locator_KnownAp_FindsApStore_EvenOnAnotherAddress()
    {
        using AppDbContext objDb = TestHelpers.CreateDbContext();
        (Unit objItaituba, Unit objCameta) = CreateStores(objDb);
        AddDevice(objDb, objItaituba, ApItaituba);
        AddDevice(objDb, objCameta, ApCameta);

        Unit? objUnit = await new UnitLocator(objDb).FindAsync(objDb.Units, null, SharedHost, "D0-21-F9-AA-BB-01");

        Assert.Equal(objCameta.Id, objUnit?.Id);
    }

    [Fact]
    public async Task Locator_ItaitubaByAp()
    {
        using AppDbContext objDb = TestHelpers.CreateDbContext();
        (Unit objItaituba, Unit objCameta) = CreateStores(objDb);
        AddDevice(objDb, objItaituba, ApItaituba);
        AddDevice(objDb, objCameta, ApCameta);

        Unit? objUnit = await new UnitLocator(objDb).FindAsync(objDb.Units, null, SharedHost, ApItaituba);

        Assert.Equal(objItaituba.Id, objUnit?.Id);
    }

    [Fact]
    public async Task Locator_SlugTakesPriorityOverAp()
    {
        using AppDbContext objDb = TestHelpers.CreateDbContext();
        (Unit objItaituba, Unit objCameta) = CreateStores(objDb);
        AddDevice(objDb, objCameta, ApCameta);

        Unit? objUnit = await new UnitLocator(objDb).FindAsync(objDb.Units, "itaituba", SharedHost, ApCameta);

        Assert.Equal(objItaituba.Id, objUnit?.Id);
    }

    [Fact]
    public async Task Locator_NoAp_KeepsWorkingByAddress()
    {
        using AppDbContext objDb = TestHelpers.CreateDbContext();
        (Unit objItaituba, Unit objCameta) = CreateStores(objDb);
        AddDevice(objDb, objItaituba, ApItaituba);

        Unit? objUnit = await new UnitLocator(objDb).FindAsync(objDb.Units, null, SharedHost, null);

        Assert.Equal(objItaituba.Id, objUnit?.Id);
    }

    [Fact]
    public async Task Locator_BeforeFirstRead_ItaitubaKeepsWorkingByAddress()
    {
        // Logo depois do deploy ainda não há aparelho gravado: nada pode mudar para a Itaituba.
        using AppDbContext objDb = TestHelpers.CreateDbContext();
        (Unit objItaituba, _) = CreateStores(objDb);

        Unit? objUnit = await new UnitLocator(objDb).FindAsync(objDb.Units, null, SharedHost, ApItaituba);

        Assert.Equal(objItaituba.Id, objUnit?.Id);
    }

    [Fact]
    public async Task Locator_UnknownAp_WithListOnAddressUnit_DoesNotGuess()
    {
        // O endereço é de todas as lojas: AP que ninguém conhece não pode virar cadastro da Itaituba.
        using AppDbContext objDb = TestHelpers.CreateDbContext();
        (Unit objItaituba, _) = CreateStores(objDb);
        AddDevice(objDb, objItaituba, ApItaituba);

        Unit? objUnit = await new UnitLocator(objDb).FindAsync(objDb.Units, null, SharedHost, "aa:aa:aa:aa:aa:aa");

        Assert.Null(objUnit);
    }

    [Fact]
    public async Task Locator_GarbageAp_CountsAsNoAp()
    {
        using AppDbContext objDb = TestHelpers.CreateDbContext();
        (Unit objItaituba, _) = CreateStores(objDb);
        AddDevice(objDb, objItaituba, ApItaituba);

        Unit? objUnit = await new UnitLocator(objDb).FindAsync(objDb.Units, null, SharedHost, "nao-e-mac");

        Assert.Equal(objItaituba.Id, objUnit?.Id);
    }

    [Fact]
    public async Task Locator_ApInTwoUnits_AcceptsOnlyIfAddressUnitIsOneOfThem()
    {
        using AppDbContext objDb = TestHelpers.CreateDbContext();
        (Unit objItaituba, Unit objCameta) = CreateStores(objDb);
        Unit objOther = CreateCloudUnit(objDb, objDb.Companies.Single(), "santarem", ConsoleCameta);
        AddDevice(objDb, objItaituba, ApItaituba);
        AddDevice(objDb, objCameta, ApItaituba);
        AddDevice(objDb, objCameta, ApCameta);
        AddDevice(objDb, objOther, ApCameta);

        UnitLocator objLocator = new UnitLocator(objDb);
        Assert.Equal(objItaituba.Id, (await objLocator.FindAsync(objDb.Units, null, SharedHost, ApItaituba))?.Id);
        Assert.Null(await objLocator.FindAsync(objDb.Units, null, SharedHost, ApCameta));
    }

    [Fact]
    public async Task Locator_NoSlugNoHostNoAp_Null()
    {
        using AppDbContext objDb = TestHelpers.CreateDbContext();
        CreateStores(objDb);

        Assert.Null(await new UnitLocator(objDb).FindAsync(objDb.Units, null, null, null));
        Assert.Null(await new UnitLocator(objDb).FindAsync(objDb.Units, null, "  ", ""));
    }

    // ---------------------------------------------------------------- leitura da nuvem

    [Fact]
    public async Task Sync_SavesApsOfEachConsole_OneCallPerKey()
    {
        using TestEnvironment objEnv = new TestEnvironment();
        (Unit objItaituba, Unit objCameta) = CreateStores(objEnv.ObjDb);
        objEnv.ObjCloud.ObjConsoles[ConsoleItaituba] =
        [
            ("8C:30:66:4E:9B:58", "AP Salão", "U6 Lite"),
            ("8c:30:66:4e:9b:59", "AP Caixa", "U6 Lite"),
            ("70:a7:41:00:00:01", "UDR", "UDR"),
        ];
        objEnv.ObjCloud.ObjConsoles[ConsoleCameta] = [(ApCameta, "AP", "U6 Lite")];
        objEnv.ObjCloud.ObjConsoles["console-de-outra-empresa"] = [("11:11:11:11:11:11", "AP", "U6 Lite")];

        UnitDeviceSync.Result objResult = await objEnv.Sync().SyncAsync();

        Assert.Equal(new UnitDeviceSync.Result(2, 4, 0), objResult);
        Assert.Equal(1, objEnv.ObjCloud.ICalls);
        List<string> objMacsItaituba = objEnv.ObjDb.UnitDevices
            .Where(device => device.IDUnit == objItaituba.Id).Select(device => device.Mac).OrderBy(sMac => sMac).ToList();
        Assert.Equal(new List<string> { "70:a7:41:00:00:01", "8c:30:66:4e:9b:58", "8c:30:66:4e:9b:59" }, objMacsItaituba);
        Assert.False(objEnv.ObjDb.UnitDevices.Any(device => device.Mac == "11:11:11:11:11:11"));
        Assert.Equal("AP Salão", objEnv.ObjDb.UnitDevices.Single(device => device.Mac == ApItaituba).Name);
        Assert.NotNull(objEnv.ObjDb.Units.Single(unit => unit.Id == objCameta.Id).DevicesSyncedAt);
    }

    [Fact]
    public async Task Sync_ApRemovedFromConsole_StopsCounting_AndNewOneEnters()
    {
        using TestEnvironment objEnv = new TestEnvironment();
        (Unit objItaituba, _) = CreateStores(objEnv.ObjDb);
        AddDevice(objEnv.ObjDb, objItaituba, "8c:30:66:00:00:99");
        AddDevice(objEnv.ObjDb, objItaituba, ApItaituba);
        objEnv.ObjCloud.ObjConsoles[ConsoleItaituba] = [(ApItaituba, "AP Salão", "U6 Lite"), ("8c:30:66:00:00:01", "AP Novo", "U7")];

        await objEnv.Sync().SyncAsync();

        List<string> objMacs = objEnv.ObjDb.UnitDevices.AsNoTracking()
            .Where(device => device.IDUnit == objItaituba.Id).Select(device => device.Mac).OrderBy(sMac => sMac).ToList();
        Assert.Equal(new List<string> { "8c:30:66:00:00:01", ApItaituba }, objMacs);
    }

    [Fact]
    public async Task Sync_CloudFailed_KeepsSavedAps_AndRecordsReason()
    {
        using TestEnvironment objEnv = new TestEnvironment();
        (Unit objItaituba, _) = CreateStores(objEnv.ObjDb);
        AddDevice(objEnv.ObjDb, objItaituba, ApItaituba);
        objEnv.ObjCloud.Status = HttpStatusCode.Unauthorized;

        UnitDeviceSync.Result objResult = await objEnv.Sync().SyncAsync();

        Assert.Equal(2, objResult.Failures);
        Assert.True(objEnv.ObjDb.UnitDevices.Any(device => device.Mac == ApItaituba));
        Assert.Contains("inválida", objEnv.ObjDb.Units.Single(unit => unit.Id == objItaituba.Id).DevicesSyncError);
    }

    [Fact]
    public async Task Sync_ConsoleWithoutCloudDevices_KeepsSavedOnes()
    {
        using TestEnvironment objEnv = new TestEnvironment();
        (Unit objItaituba, _) = CreateStores(objEnv.ObjDb);
        AddDevice(objEnv.ObjDb, objItaituba, ApItaituba);
        objEnv.ObjCloud.ObjConsoles[ConsoleCameta] = [(ApCameta, "AP", "U6 Lite")];

        await objEnv.Sync().SyncAsync();

        Assert.True(objEnv.ObjDb.UnitDevices.Any(device => device.Mac == ApItaituba));
        Assert.StartsWith("Nenhum aparelho", objEnv.ObjDb.Units.Single(unit => unit.Id == objItaituba.Id).DevicesSyncError);
    }

    [Fact]
    public async Task SyncButton_ReadsOnlyTheCompanyChosenInThePanel()
    {
        using TestEnvironment objEnv = new TestEnvironment();
        (Unit objItaituba, Unit objCameta) = CreateStores(objEnv.ObjDb);
        Company objOther = CreateCompany(objEnv.ObjDb, "lojao-dos-plasticos");
        Company objEmpty = CreateCompany(objEnv.ObjDb, "guara-acqua-park");
        Unit objOtherUnit = CreateCloudUnit(objEnv.ObjDb, objOther, "lojao-matriz", "console-do-lojao");
        objEnv.ObjCloud.ObjConsoles[ConsoleItaituba] = [(ApItaituba, "AP Salão", "U6 Lite")];
        objEnv.ObjCloud.ObjConsoles[ConsoleCameta] = [(ApCameta, "AP", "U6 Lite")];
        objEnv.ObjCloud.ObjConsoles["console-do-lojao"] = [("11:11:11:11:11:11", "AP", "U6 Lite")];
        UnitsController objController = new UnitsController(
            objEnv.ObjDb, TestHelpers.CreateEncryptor(), new FakeUnifiClient(), NullLogger<UnitsController>.Instance,
            objEnv.Sync());

        ActionResult<UnitDeviceSyncResponse> objResult = await objController.SyncDevices(objOther.Id, CancellationToken.None);

        Assert.Equal(new UnitDeviceSyncResponse(1, 1, 0), Assert.IsType<OkObjectResult>(objResult.Result).Value);
        Assert.True(objEnv.ObjDb.UnitDevices.Any(device => device.IDUnit == objOtherUnit.Id));
        Assert.False(objEnv.ObjDb.UnitDevices.Any(device => device.IDUnit == objItaituba.Id || device.IDUnit == objCameta.Id));

        // Empresa sem unidade: nada é lido na nuvem.
        int iCallsBefore = objEnv.ObjCloud.ICalls;
        ActionResult<UnitDeviceSyncResponse> objEmptyResult = await objController.SyncDevices(objEmpty.Id, CancellationToken.None);

        Assert.Equal(new UnitDeviceSyncResponse(0, 0, 0), Assert.IsType<OkObjectResult>(objEmptyResult.Result).Value);
        Assert.Equal(iCallsBefore, objEnv.ObjCloud.ICalls);
    }

    [Fact]
    public async Task Sync_IgnoresLocalAndInactiveUnits()
    {
        using TestEnvironment objEnv = new TestEnvironment();
        (Unit objItaituba, Unit objCameta) = CreateStores(objEnv.ObjDb);
        objItaituba.Unifi.Mode = UnifiMode.Local;
        objCameta.Active = false;
        objEnv.ObjDb.SaveChanges();

        UnitDeviceSync.Result objResult = await objEnv.Sync().SyncAsync();

        Assert.Equal(new UnitDeviceSync.Result(0, 0, 0), objResult);
        Assert.Equal(0, objEnv.ObjCloud.ICalls);
    }

    // ---------------------------------------------------------------- leitura na hora (AP ainda desconhecido)

    [Fact]
    public async Task Locator_NewAp_ReadsCloudAtOnce_AndFindsStore()
    {
        using TestEnvironment objEnv = new TestEnvironment();
        (Unit objItaituba, Unit objCameta) = CreateStores(objEnv.ObjDb);
        AddDevice(objEnv.ObjDb, objItaituba, ApItaituba);
        objEnv.ObjCloud.ObjConsoles[ConsoleItaituba] = [(ApItaituba, "AP", "U6 Lite")];
        objEnv.ObjCloud.ObjConsoles[ConsoleCameta] = [(ApCameta, "AP", "U6 Lite")];

        Unit? objUnit = await objEnv.Locator().FindAsync(objEnv.ObjDb.Units, null, SharedHost, ApCameta);

        Assert.Equal(objCameta.Id, objUnit?.Id);
        Assert.Equal(1, objEnv.ObjCloud.ICalls);
    }

    [Fact]
    public async Task Locator_KnownAp_DoesNotCallCloud()
    {
        using TestEnvironment objEnv = new TestEnvironment();
        (Unit objItaituba, _) = CreateStores(objEnv.ObjDb);
        AddDevice(objEnv.ObjDb, objItaituba, ApItaituba);

        for (int i = 0; i < 5; i++)
        {
            Assert.Equal(objItaituba.Id, (await objEnv.Locator().FindAsync(objEnv.ObjDb.Units, null, SharedHost, ApItaituba))?.Id);
        }
        Assert.Equal(0, objEnv.ObjCloud.ICalls);
    }

    [Fact]
    public async Task Locator_MadeUpAp_DoesNotFloodCloud()
    {
        using TestEnvironment objEnv = new TestEnvironment();
        (Unit objItaituba, _) = CreateStores(objEnv.ObjDb);
        AddDevice(objEnv.ObjDb, objItaituba, ApItaituba);
        objEnv.ObjCloud.ObjConsoles[ConsoleItaituba] = [(ApItaituba, "AP", "U6 Lite")];

        for (int i = 0; i < 10; i++)
        {
            string sMac = $"aa:aa:aa:aa:aa:{i:x2}";
            Assert.Null(await objEnv.Locator().FindAsync(objEnv.ObjDb.Units, null, SharedHost, sMac));
            Assert.Null(await objEnv.Locator().FindAsync(objEnv.ObjDb.Units, null, SharedHost, sMac));
        }

        // Uma leitura só: as outras caem no intervalo mínimo de 30 s ou no "já sei que não existe".
        Assert.Equal(1, objEnv.ObjCloud.ICalls);
    }

    [Fact]
    public async Task Locator_SlowCloud_GivesUpAfterFourSeconds_AndContinuesWithoutAp()
    {
        using TestEnvironment objEnv = new TestEnvironment();
        (Unit objItaituba, _) = CreateStores(objEnv.ObjDb);
        objEnv.ObjCloud.Delay = TimeSpan.FromSeconds(30);

        System.Diagnostics.Stopwatch objClock = System.Diagnostics.Stopwatch.StartNew();
        Unit? objUnit = await objEnv.Locator().FindAsync(objEnv.ObjDb.Units, null, SharedHost, ApItaituba);
        objClock.Stop();

        // Itaituba sem lista ainda: segue pelo endereço, como hoje.
        Assert.Equal(objItaituba.Id, objUnit?.Id);
        Assert.True(objClock.Elapsed < TimeSpan.FromSeconds(8), $"Demorou {objClock.Elapsed}.");
    }

    [Fact]
    public async Task Locator_CloudDown_ItaitubaWithListKeepsWorking()
    {
        using TestEnvironment objEnv = new TestEnvironment();
        (Unit objItaituba, _) = CreateStores(objEnv.ObjDb);
        AddDevice(objEnv.ObjDb, objItaituba, ApItaituba);
        objEnv.ObjCloud.Status = HttpStatusCode.InternalServerError;

        await objEnv.Sync().SyncAsync();
        Unit? objUnit = await objEnv.Locator().FindAsync(objEnv.ObjDb.Units, null, SharedHost, ApItaituba);

        Assert.Equal(objItaituba.Id, objUnit?.Id);
    }

    // ---------------------------------------------------------------- portal (GET /settings e POST /authorize)

    [Fact]
    public async Task Settings_SameAddress_ApStoreTheme()
    {
        using AppDbContext objDb = TestHelpers.CreateDbContext();
        Company objRegional = CreateCompany(objDb, "regional");
        Company objOther = CreateCompany(objDb, "outra-rede");
        Unit objItaituba = CreateCloudUnit(objDb, objRegional, "itaituba", ConsoleItaituba, SharedHost);
        Unit objOtherStore = CreateCloudUnit(objDb, objOther, "outra-loja", ConsoleCameta);
        AddDevice(objDb, objItaituba, ApItaituba);
        AddDevice(objDb, objOtherStore, ApCameta);
        SettingsController objController = new SettingsController(objDb, new UnitLocator(objDb));

        ActionResult<SettingsDto> objResult = await objController.Get(null, SharedHost, ApCameta, CancellationToken.None);

        SettingsDto objDto = Assert.IsType<SettingsDto>(Assert.IsType<OkObjectResult>(objResult.Result).Value);
        Assert.Equal("outra-loja", objDto.Unit);
    }

    [Fact]
    public async Task Settings_UnknownAp_404()
    {
        using AppDbContext objDb = TestHelpers.CreateDbContext();
        (Unit objItaituba, _) = CreateStores(objDb);
        AddDevice(objDb, objItaituba, ApItaituba);
        SettingsController objController = new SettingsController(objDb, new UnitLocator(objDb));

        ActionResult<SettingsDto> objResult = await objController.Get(null, SharedHost, "aa:aa:aa:aa:aa:aa", CancellationToken.None);

        Assert.IsType<NotFoundObjectResult>(objResult.Result);
    }

    [Fact]
    public async Task Authorize_ByAp_SavesInRightStore_AndAllowsOnItsController()
    {
        using AppDbContext objDb = TestHelpers.CreateDbContext();
        (Unit objItaituba, Unit objCameta) = CreateStores(objDb);
        AddDevice(objDb, objItaituba, ApItaituba);
        AddDevice(objDb, objCameta, ApCameta);
        FakeUnifiClient objUnifi = new FakeUnifiClient();
        AuthorizeController objController = new AuthorizeController(
            objDb, objUnifi, NullLogger<AuthorizeController>.Instance, new UnitLocator(objDb));

        ActionResult<AuthorizeResponse> objResult = await objController.Post(
            new AuthorizeRequest("Ana", "@ana", "(93) 98888-1234", "10/05/1990", true, null,
                "aa:bb:cc:dd:ee:ff", ApCameta, "PIX REGIONAL", null, SharedHost),
            CancellationToken.None);

        Assert.IsType<OkObjectResult>(objResult.Result);
        Assert.Equal(ConsoleCameta, objUnifi.ObjReceivedConfig?.ConsoleId);
        Assert.Equal(objCameta.Id, objDb.Leads.Single().IDUnit);
    }

    [Fact]
    public async Task Authorize_ItaitubaAsToday_NoListYet_Allows()
    {
        using AppDbContext objDb = TestHelpers.CreateDbContext();
        (Unit objItaituba, _) = CreateStores(objDb);
        FakeUnifiClient objUnifi = new FakeUnifiClient();
        AuthorizeController objController = new AuthorizeController(
            objDb, objUnifi, NullLogger<AuthorizeController>.Instance, new UnitLocator(objDb));

        ActionResult<AuthorizeResponse> objResult = await objController.Post(
            new AuthorizeRequest("Ana", "@ana", "(93) 98888-1234", "10/05/1990", true, null,
                "aa:bb:cc:dd:ee:ff", ApItaituba, "PIX REGIONAL", null, SharedHost),
            CancellationToken.None);

        Assert.IsType<OkObjectResult>(objResult.Result);
        Assert.Equal(objItaituba.Id, objDb.Leads.Single().IDUnit);
    }

    // ---------------------------------------------------------------- painel

    [Fact]
    public async Task Units_ListShowsHowManyApsEachUnitHas()
    {
        using AppDbContext objDb = TestHelpers.CreateDbContext();
        (Unit objItaituba, Unit objCameta) = CreateStores(objDb);
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
    public async Task Units_ChangeConsole_ClearsOldAps()
    {
        using AppDbContext objDb = TestHelpers.CreateDbContext();
        (Unit objItaituba, _) = CreateStores(objDb);
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
    public async Task Units_SaveWithoutChangingConsole_KeepsAps()
    {
        using AppDbContext objDb = TestHelpers.CreateDbContext();
        (Unit objItaituba, _) = CreateStores(objDb);
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
