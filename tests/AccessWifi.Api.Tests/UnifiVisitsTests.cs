using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using AccessWifi.Api.Controllers;
using AccessWifi.Api.Features.Authorize;
using AccessWifi.Api.Features.Dashboard;
using AccessWifi.Api.Features.Units;
using AccessWifi.Api.Infrastructure.Unifi;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Models.DataBase;
using Models.Persistence;
using Models.Visits;

namespace AccessWifi.Api.Tests;

/// <summary>
/// Visitas e tempo na loja pelos avisos do Alarm Manager da UniFi (PROPOSTA_VISITAS_UNIFI.md, D1 a D9).
/// </summary>
public class UnifiVisitsTests
{
    private const string CaptureToken = "codigo-da-captura-de-teste-1234567890";
    private const string Phone = "93988881234";
    private const string AnaMac = "26:3e:04:44:81:cb";

    // Segunda, 26/10/2026 às 10:00 em Belém (13:00 UTC).
    private static readonly DateTime s_dtBase = new DateTime(2026, 10, 26, 13, 0, 0, DateTimeKind.Utc);

    private sealed record Scenario(Company Company, Unit Itaituba, Unit Castanhal);

    private static Scenario CreateScenario(AppDbContext objDb)
    {
        Company objCompany = new Company { Name = "Lojas Regional", Slug = "regional" };
        objDb.Companies.Add(objCompany);
        objDb.PortalSettings.Add(new PortalSettings { IDCompany = objCompany.Id, Ssid = "PIX REGIONAL" });
        Unit objItaituba = new Unit { IDCompany = objCompany.Id, Name = "Itaituba", Slug = "itaituba" };
        Unit objCastanhal = new Unit { IDCompany = objCompany.Id, Name = "Castanhal", Slug = "castanhal" };
        objDb.Units.AddRange(objItaituba, objCastanhal);
        objDb.SaveChanges();
        return new Scenario(objCompany, objItaituba, objCastanhal);
    }

    /// <summary>A Ana se cadastrou na Itaituba há uma semana (cadastro do aparelho + cliente).</summary>
    private static Customer AddAna(AppDbContext objDb, Scenario objScenario, Unit? objUnit = null)
    {
        Unit objStore = objUnit ?? objScenario.Itaituba;
        DateTime dtThen = s_dtBase.AddDays(-7);
        objDb.Leads.Add(new Lead
        {
            IDUnit = objStore.Id, Mac = AnaMac, Name = "Ana", Phone = "(93) 98888-1234", CreatedAt = dtThen, Timestamp = dtThen,
        });
        Customer objCustomer = new Customer
        {
            IDCompany = objScenario.Company.Id, Phone = Phone, Name = "Ana",
            FirstVisitAt = dtThen, FirstVisitDate = DateOnly.FromDateTime(dtThen), LastVisitAt = dtThen,
            LastVisitDate = DateOnly.FromDateTime(dtThen), VisitCount = 1, IDLastUnit = objStore.Id,
        };
        objDb.Customers.Add(objCustomer);
        objDb.CustomerUnits.Add(new CustomerUnit { IDCustomer = objCustomer.Id, IDUnit = objStore.Id, FirstVisitAt = dtThen, LastVisitAt = dtThen });
        objDb.SaveChanges();
        return objCustomer;
    }

    private static string Body(string sName, string sMac, string? sWifi, DateTime dtAt, string sHost = "Galaxy-A25-5G")
    {
        JsonObject objParameters = new JsonObject
        {
            ["UNIFIclientMac"] = sMac,
            ["UNIFIclientHostname"] = sHost,
            ["UNIFIconnectedToDeviceMac"] = "8C:30:66:4E:9B:58",
            ["UNIFIutcTime"] = dtAt.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
        };
        if (sWifi is not null)
        {
            objParameters["UNIFIwifiName"] = sWifi;
        }
        return new JsonObject { ["app"] = "network", ["name"] = sName, ["parameters"] = objParameters }.ToJsonString();
    }

    private static Task<UnifiVisitTracker.Outcome> SendAsync(
        AppDbContext objDb, Unit objUnit, string sName, string sMac, DateTime dtAt, string? sWifi = "PIX REGIONAL",
        DateTime? dtNow = null) =>
        UnifiVisitTracker.HandleAsync(objDb, objUnit, UnifiNotice.Parse(Body(sName, sMac, sWifi, dtAt))!, dtNow ?? dtAt);

    private const string Connected = "WiFi Client Connected";
    private const string Disconnected = "WiFi Client Disconnected";

    // ---------------------------------------------------------------- leitura do aviso

    [Fact]
    public void Parse_ReadsTheClientNotice_AndRefusesWhatIsNotOne()
    {
        UnifiNotice objNotice = UnifiNotice.Parse(Body(Connected, "26-3E-04-44-81-CB", "PIX REGIONAL", s_dtBase))!;
        Assert.Equal(UnifiNoticeKind.Connected, objNotice.Kind);
        Assert.Equal(AnaMac, objNotice.Mac);
        Assert.Equal("PIX REGIONAL", objNotice.WifiName);
        Assert.Equal("8c:30:66:4e:9b:58", objNotice.Ap);
        Assert.Equal(s_dtBase, objNotice.EventAtUtc);

        Assert.Equal(UnifiNoticeKind.Disconnected, UnifiNotice.Parse(Body(Disconnected, AnaMac, null, s_dtBase))!.Kind);
        Assert.Equal(UnifiNoticeKind.Other, UnifiNotice.Parse(Body("WiFi Client Roamed", AnaMac, null, s_dtBase))!.Kind);
        Assert.Null(UnifiNotice.Parse("não é json"));
        Assert.Null(UnifiNotice.Parse("""{"name":"Device Offline","parameters":{"UNIFIhost":"ITAITUBA"}}"""));
    }

    // ---------------------------------------------------------------- rede de clientes e aparelhos da loja

    [Fact]
    public async Task OnlyTheGuestNetworkCounts_AndStoreDevicesAreIgnored()
    {
        using AppDbContext objDb = TestHelpers.CreateDbContext();
        Scenario objScenario = CreateScenario(objDb);
        AddAna(objDb, objScenario);

        Assert.Equal(UnifiVisitTracker.Outcome.NotGuestNetwork, await SendAsync(objDb, objScenario.Itaituba, Connected, AnaMac, s_dtBase, "REGIONAL"));
        Assert.Equal(UnifiVisitTracker.Outcome.NotGuestNetwork, await SendAsync(objDb, objScenario.Itaituba, "Wired Client Connected", AnaMac, s_dtBase, null));
        // Desconexão de um aparelho que não foi visto na rede de clientes hoje.
        Assert.Equal(UnifiVisitTracker.Outcome.NotGuestNetwork, await SendAsync(objDb, objScenario.Itaituba, Disconnected, AnaMac, s_dtBase, null));

        objDb.UnitIgnoredDevices.Add(new UnitIgnoredDevice { IDUnit = objScenario.Itaituba.Id, Mac = AnaMac, Label = "TV da loja" });
        objDb.SaveChanges();
        Assert.Equal(UnifiVisitTracker.Outcome.IgnoredDevice, await SendAsync(objDb, objScenario.Itaituba, Connected, AnaMac, s_dtBase));

        Assert.Empty(objDb.WifiPresences);
        Assert.Empty(objDb.Visits);
    }

    // ---------------------------------------------------------------- visita de quem volta sem o portal

    [Fact]
    public async Task KnownCustomerConnects_VisitFromUnifi_OncePerDay_AndCustomerUpdatedLikeThePortal()
    {
        using AppDbContext objDb = TestHelpers.CreateDbContext();
        Scenario objScenario = CreateScenario(objDb);
        Customer objAna = AddAna(objDb, objScenario);

        Assert.Equal(UnifiVisitTracker.Outcome.VisitRegistered, await SendAsync(objDb, objScenario.Itaituba, Connected, AnaMac, s_dtBase));
        // O segundo aviso, 15 segundos depois (a UniFi repete), não cria outra visita.
        Assert.Equal(UnifiVisitTracker.Outcome.Seen, await SendAsync(objDb, objScenario.Itaituba, Connected, AnaMac, s_dtBase.AddSeconds(15)));

        Visit objVisit = Assert.Single(objDb.Visits);
        Assert.Equal(VisitSource.Unifi, objVisit.Source);
        Assert.Equal(objAna.Id, objVisit.IDCustomer);
        Assert.Equal(new DateOnly(2026, 10, 26), objVisit.LocalDate);
        Assert.Equal(10, objVisit.LocalHour);
        Assert.False(objVisit.NewInCompany);
        Assert.False(objVisit.NewInUnit);
        Assert.Equal("8c:30:66:4e:9b:58", objVisit.Ap);
        Assert.Null(objVisit.StaySeconds);

        Customer objUpdated = objDb.Customers.Single();
        Assert.Equal(2, objUpdated.VisitCount);
        Assert.Equal(s_dtBase, objUpdated.LastVisitAt);
        Assert.Equal(s_dtBase, objDb.Leads.Single().Timestamp);
        Assert.Equal(2, objDb.WifiPresences.Single().Connects);
    }

    [Fact]
    public async Task DeviceRegisteredInAnotherStore_IsRecognized_AndIsNewInThisUnit()
    {
        using AppDbContext objDb = TestHelpers.CreateDbContext();
        Scenario objScenario = CreateScenario(objDb);
        AddAna(objDb, objScenario, objScenario.Castanhal);

        Assert.Equal(UnifiVisitTracker.Outcome.VisitRegistered, await SendAsync(objDb, objScenario.Itaituba, Connected, AnaMac, s_dtBase));

        Visit objVisit = Assert.Single(objDb.Visits);
        Assert.Equal(objScenario.Itaituba.Id, objVisit.IDUnit);
        Assert.True(objVisit.NewInUnit);
        Assert.Equal(2, objDb.CustomerUnits.Count());
        // O cadastro da outra loja não muda (a "última conexão" é daquela loja).
        Assert.Equal(s_dtBase.AddDays(-7), objDb.Leads.Single().Timestamp);
    }

    [Fact]
    public async Task UnknownDevice_OnlyLeavesTheSignal_NoVisit()
    {
        using AppDbContext objDb = TestHelpers.CreateDbContext();
        Scenario objScenario = CreateScenario(objDb);

        Assert.Equal(UnifiVisitTracker.Outcome.Seen, await SendAsync(objDb, objScenario.Itaituba, Connected, "d2:2d:5b:ef:18:cd", s_dtBase));

        WifiPresence objPresence = Assert.Single(objDb.WifiPresences);
        Assert.Null(objPresence.IDCustomer);
        Assert.Equal("Galaxy-A25-5G", objPresence.Hostname);
        Assert.Empty(objDb.Visits);
    }

    // ---------------------------------------------------------------- tempo na loja

    [Fact]
    public async Task Stay_JoinsShortDrops_StartsANewStayAfter30Minutes_AndNeedsADisconnect()
    {
        using AppDbContext objDb = TestHelpers.CreateDbContext();
        Scenario objScenario = CreateScenario(objDb);
        AddAna(objDb, objScenario);
        Unit objUnit = objScenario.Itaituba;

        await SendAsync(objDb, objUnit, Connected, AnaMac, s_dtBase);
        await SendAsync(objDb, objUnit, Connected, AnaMac, s_dtBase.AddMinutes(4));
        // Só conexões: ainda sem tempo na visita.
        Assert.Null(objDb.Visits.Single().StaySeconds);

        // Queda de 15 minutos (o celular economizando bateria): continua a mesma estadia. Sem o nome da rede, o
        // aviso de desconexão vale porque o aparelho já está na rede de clientes hoje.
        await SendAsync(objDb, objUnit, Disconnected, AnaMac, s_dtBase.AddMinutes(5), null);
        await SendAsync(objDb, objUnit, Connected, AnaMac, s_dtBase.AddMinutes(20));
        await SendAsync(objDb, objUnit, Disconnected, AnaMac, s_dtBase.AddMinutes(45), null);
        Assert.Equal(45 * 60, objDb.Visits.Single().StaySeconds);

        // Volta às 12:00 (75 minutos depois): outra estadia; o intervalo fora não conta.
        await SendAsync(objDb, objUnit, Connected, AnaMac, s_dtBase.AddMinutes(120));
        await SendAsync(objDb, objUnit, Disconnected, AnaMac, s_dtBase.AddMinutes(130), null);

        Visit objVisit = Assert.Single(objDb.Visits);
        Assert.Equal(55 * 60, objVisit.StaySeconds);
        WifiPresence objPresence = objDb.WifiPresences.Single();
        Assert.Equal(4, objPresence.Connects);
        Assert.Equal(3, objPresence.Disconnects);
    }

    [Fact]
    public void Stay_OutOfOrderSignal_OnlyMovesTheStart()
    {
        WifiPresence objPresence = new WifiPresence { FirstSeenAt = s_dtBase, LastSeenAt = s_dtBase.AddMinutes(10), StayStartedAt = s_dtBase };
        UnifiVisitTracker.AddSignal(objPresence, s_dtBase.AddMinutes(-2));
        Assert.Equal(12 * 60, objPresence.StaySeconds);
        UnifiVisitTracker.AddSignal(objPresence, s_dtBase.AddMinutes(5));
        Assert.Equal(12 * 60, objPresence.StaySeconds);
    }

    // ---------------------------------------------------------------- junto com o portal

    private class FakeUnifiClient : IUnifiClient
    {
        public Task AuthorizeGuestAsync(CompanyUnifi objConfig, string sMac, int iAccessMinutes, CancellationToken objCancellationToken = default) =>
            Task.CompletedTask;

        public Task<string> TestConnectionAsync(CompanyUnifi objConfig, CancellationToken objCancellationToken = default) =>
            Task.FromResult("ok");
    }

    [Fact]
    public async Task NewCustomer_NoticeBeforeThePortal_OneVisit_AndTheStayIsTheirs()
    {
        using AppDbContext objDb = TestHelpers.CreateDbContext();
        Scenario objScenario = CreateScenario(objDb);
        DateTime dtNow = DateTime.UtcNow;
        const string sNewMac = "fe:68:1c:7c:3b:e5";

        // Conecta (aviso), sem cadastro ainda.
        await SendAsync(objDb, objScenario.Itaituba, Connected, sNewMac, dtNow.AddMinutes(-2), dtNow: dtNow);
        Assert.Empty(objDb.Visits);

        // Cadastra no portal 1 minuto depois.
        AuthorizeController objPortal = new AuthorizeController(objDb, new FakeUnifiClient(), NullLogger<AuthorizeController>.Instance);
        await objPortal.Post(new AuthorizeRequest(
            Name: "Bia", Instagram: "", Phone: "(93) 97777-0000", BirthDate: "", Consent: true, Unit: "itaituba",
            Mac: sNewMac, Ap: "8c:30:66:4e:9b:58", Ssid: "PIX REGIONAL", Url: null), CancellationToken.None);

        Visit objVisit = Assert.Single(objDb.Visits);
        Assert.Equal(VisitSource.Portal, objVisit.Source);
        Assert.True(objVisit.NewInCompany);
        Assert.Equal(objVisit.IDCustomer, objDb.WifiPresences.Single().IDCustomer);

        // Outro aviso no mesmo dia: não duplica; a desconexão leva o tempo para a visita do portal.
        Assert.Equal(UnifiVisitTracker.Outcome.Seen, await SendAsync(objDb, objScenario.Itaituba, Connected, sNewMac, dtNow, dtNow: dtNow));
        await SendAsync(objDb, objScenario.Itaituba, Disconnected, sNewMac, dtNow.AddMinutes(18), null, dtNow);
        Assert.Equal(20 * 60, Assert.Single(objDb.Visits).StaySeconds);
    }

    // ---------------------------------------------------------------- a rota

    private static UnifiWebhookController Webhook(AppDbContext objDb, string sBody, string sQuery = "")
    {
        IConfiguration objConfiguration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["UnifiWebhook:CaptureToken"] = CaptureToken })
            .Build();
        DefaultHttpContext objHttpContext = new DefaultHttpContext();
        objHttpContext.Request.Method = "POST";
        objHttpContext.Request.QueryString = new QueryString(sQuery);
        objHttpContext.Request.ContentType = "application/json";
        objHttpContext.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(sBody));
        objHttpContext.Connection.RemoteIpAddress = IPAddress.Parse("45.168.125.240");
        return new UnifiWebhookController(objDb, objConfiguration, NullLogger<UnifiWebhookController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = objHttpContext },
        };
    }

    private static UnitUnifiVisitsController Admin(AppDbContext objDb)
    {
        UnitUnifiVisitsController objController = new UnitUnifiVisitsController(objDb);
        TestHelpers.SetUser(objController, null, "root");
        objController.ControllerContext.HttpContext.Request.Scheme = "https";
        objController.ControllerContext.HttpContext.Request.Host = new HostString("vps11702.panel.icontainer.online");
        return objController;
    }

    [Fact]
    public async Task Route_UnitAddress_ShownOnce_ProcessesTheNotice_AndANewAddressReplacesTheOld()
    {
        using AppDbContext objDb = TestHelpers.CreateDbContext();
        Scenario objScenario = CreateScenario(objDb);
        AddAna(objDb, objScenario);

        UnifiAddressDto objAddress = Assert.IsType<UnifiAddressDto>(Assert.IsType<OkObjectResult>(
            (await Admin(objDb).CreateAddress(objScenario.Itaituba.Id, CancellationToken.None)).Result).Value);
        Assert.StartsWith("https://vps11702.panel.icontainer.online/webhooks/unifi/", objAddress.Url);
        string sToken = objAddress.Url.Split('/').Last();
        Assert.True(sToken.Length >= 40);
        // No banco, só a impressão do código.
        Unit objSaved = objDb.Units.Single(unit => unit.Id == objScenario.Itaituba.Id);
        Assert.Equal(UnifiWebhookController.HashToken(sToken), objSaved.UnifiWebhookHash);
        Assert.DoesNotContain(sToken, objSaved.UnifiWebhookHash);

        string sBody = Body(Connected, AnaMac, "PIX REGIONAL", DateTime.UtcNow);
        Assert.IsType<OkObjectResult>(await Webhook(objDb, sBody).Capture(sToken, null, CancellationToken.None));
        Assert.Equal(VisitSource.Unifi, Assert.Single(objDb.Visits).Source);
        Assert.NotNull(objDb.Units.Single(unit => unit.Id == objScenario.Itaituba.Id).UnifiWebhookLastAt);
        Assert.Equal("itaituba", objDb.UnifiWebhookCaptures.Single().Label);

        // Endereço novo: o antigo para de funcionar.
        await Admin(objDb).CreateAddress(objScenario.Itaituba.Id, CancellationToken.None);
        Assert.IsType<NotFoundResult>(await Webhook(objDb, sBody).Capture(sToken, null, CancellationToken.None));

        UnifiVisitsStatusDto objStatus = Assert.IsType<UnifiVisitsStatusDto>(Assert.IsType<OkObjectResult>(
            (await Admin(objDb).GetStatus(objScenario.Itaituba.Id, CancellationToken.None)).Result).Value);
        Assert.True(objStatus.HasAddress);
        Assert.Equal("PIX REGIONAL", objStatus.GuestNetwork);
    }

    [Fact]
    public async Task Route_CaptureToken_WithTheUnitSlug_StillWorks_WithoutIt_OnlyCaptures()
    {
        using AppDbContext objDb = TestHelpers.CreateDbContext();
        Scenario objScenario = CreateScenario(objDb);
        AddAna(objDb, objScenario);
        string sBody = Body(Connected, AnaMac, "PIX REGIONAL", DateTime.UtcNow);

        Assert.IsType<OkObjectResult>(await Webhook(objDb, sBody).Capture(CaptureToken, null, CancellationToken.None));
        Assert.Empty(objDb.Visits);
        Assert.IsType<OkObjectResult>(await Webhook(objDb, sBody, "?unit=itaituba").Capture(CaptureToken, "itaituba", CancellationToken.None));
        Assert.Single(objDb.Visits);
        Assert.Equal(2, objDb.UnifiWebhookCaptures.Count());

        Assert.IsType<NotFoundResult>(await Webhook(objDb, sBody).Capture("codigo-errado-codigo-errado-123", "itaituba", CancellationToken.None));
    }

    // ---------------------------------------------------------------- aparelhos da loja na tela

    [Fact]
    public async Task Devices_ListedForTheUnit_IgnoreAndCountAgain()
    {
        using AppDbContext objDb = TestHelpers.CreateDbContext();
        Scenario objScenario = CreateScenario(objDb);
        AddAna(objDb, objScenario);
        DateTime dtNow = DateTime.UtcNow;
        await SendAsync(objDb, objScenario.Itaituba, Connected, AnaMac, dtNow.AddMinutes(-30), dtNow: dtNow);
        await SendAsync(objDb, objScenario.Itaituba, Disconnected, AnaMac, dtNow.AddMinutes(-10), null, dtNow);
        await SendAsync(objDb, objScenario.Itaituba, Connected, "a0:b1:c2:d3:e4:f5", dtNow, dtNow: dtNow);

        UnifiDevicesDto objDevices = Assert.IsType<UnifiDevicesDto>(Assert.IsType<OkObjectResult>(
            (await Admin(objDb).GetDevices(objScenario.Itaituba.Id, 7, CancellationToken.None)).Result).Value);
        Assert.Equal(2, objDevices.Devices.Count);
        UnifiDeviceDto objAnaDevice = objDevices.Devices.Single(device => device.Mac == AnaMac);
        Assert.True(objAnaDevice.HasCustomer);
        Assert.Equal(20.0, objAnaDevice.AvgStayMinutes);
        Assert.Null(objDevices.Devices.Single(device => device.Mac != AnaMac).AvgStayMinutes);

        UnitUnifiVisitsController objAdmin = Admin(objDb);
        Assert.IsType<OkObjectResult>((await objAdmin.Ignore(
            objScenario.Itaituba.Id, new IgnoreDeviceRequest("A0-B1-C2-D3-E4-F5", "TV da loja"), CancellationToken.None)).Result);
        objDevices = Assert.IsType<UnifiDevicesDto>(Assert.IsType<OkObjectResult>(
            (await Admin(objDb).GetDevices(objScenario.Itaituba.Id, 7, CancellationToken.None)).Result).Value);
        Assert.Single(objDevices.Devices);
        UnitIgnoredDeviceDto objIgnored = Assert.Single(objDevices.Ignored);
        Assert.Equal("TV da loja", objIgnored.Label);
        Assert.Equal("root", objIgnored.Username);

        Assert.IsType<NoContentResult>(await Admin(objDb).Unignore(objScenario.Itaituba.Id, "a0:b1:c2:d3:e4:f5", CancellationToken.None));
        Assert.Empty(objDb.UnitIgnoredDevices);
        Assert.IsType<BadRequestObjectResult>((await Admin(objDb).Ignore(
            objScenario.Itaituba.Id, new IgnoreDeviceRequest("não é mac", null), CancellationToken.None)).Result);
    }

    // ---------------------------------------------------------------- dashboard

    [Fact]
    public async Task Dashboard_AverageStay_EachCustomerOnce_TheLastVisitCounts()
    {
        using AppDbContext objDb = TestHelpers.CreateDbContext();
        Scenario objScenario = CreateScenario(objDb);
        Guid objAna = Guid.NewGuid(), objBia = Guid.NewGuid(), objCaio = Guid.NewGuid(), objDani = Guid.NewGuid();
        void Add(Unit objUnit, Guid? objCustomer, int iDay, int? iMinutes) => objDb.Visits.Add(new Visit
        {
            IDUnit = objUnit.Id, IDCustomer = objCustomer, At = new DateTime(2026, 10, iDay, 13, 0, 0, DateTimeKind.Utc),
            LocalDate = new DateOnly(2026, 10, iDay), LocalHour = 10, StaySeconds = iMinutes * 60,
        });
        Add(objScenario.Itaituba, objAna, 21, 50);
        Add(objScenario.Castanhal, objAna, 22, 40);
        Add(objScenario.Itaituba, objAna, 26, 10);   // a última da Ana: é a que vale
        Add(objScenario.Itaituba, objBia, 23, 20);
        Add(objScenario.Itaituba, objCaio, 25, 300); // 5 horas: fora da média (D7)
        Add(objScenario.Itaituba, objDani, 24, null); // sem tempo medido
        Add(objScenario.Itaituba, null, 24, 30);     // sem cliente
        objDb.SaveChanges();

        DashboardDto objDashboard = await new DashboardBuilder(objDb).BuildAsync(
            objScenario.Company, [objScenario.Itaituba, objScenario.Castanhal], false,
            new DateOnly(2026, 10, 20), new DateOnly(2026, 10, 26), s_dtBase);

        // Empresa: Ana (10, a última) e Bia (20), uma vez cada.
        Assert.Equal(15.0, objDashboard.Kpis.AvgStayMinutes.Current);
        Assert.Equal(2, objDashboard.Kpis.AvgStayMinutes.Measured);
        Assert.Null(objDashboard.Kpis.AvgStayMinutes.Previous);
        // Por unidade: cada cliente com a última visita naquela unidade.
        Assert.Equal(15.0, objDashboard.Units.Single(row => row.Id == objScenario.Itaituba.Id).AvgStayMinutes);
        Assert.Equal(40.0, objDashboard.Units.Single(row => row.Id == objScenario.Castanhal.Id).AvgStayMinutes);
    }

    [Fact]
    public async Task Dashboard_ReturnRate_CountsTheReturnsSeenByUnifi()
    {
        using AppDbContext objDb = TestHelpers.CreateDbContext();
        Scenario objScenario = CreateScenario(objDb);
        AddAna(objDb, objScenario); // cadastrou há uma semana

        await SendAsync(objDb, objScenario.Itaituba, Connected, AnaMac, s_dtBase); // volta sem passar pelo portal
        DateOnly dtToday = new DateOnly(2026, 10, 26);
        DashboardDto objDashboard = await new DashboardBuilder(objDb).BuildAsync(
            objScenario.Company, [objScenario.Itaituba], true, dtToday, dtToday, s_dtBase);

        Assert.Equal(1, objDashboard.Kpis.Visitors.Current);
        Assert.Equal(1, objDashboard.Kpis.Returning.Current);
        Assert.Equal(100.0, objDashboard.Kpis.ReturnRate.Current);
    }
}
