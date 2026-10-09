using Models.DataBase;
using AccessWifi.Api.Controllers;
using AccessWifi.Api.Features.Authorize;
using Models.Persistence;
using AccessWifi.Api.Infrastructure.Unifi;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;

namespace AccessWifi.Api.Tests;

public class AuthorizeControllerTests
{
    /// <summary>Dublê da controladora: registra a chamada ou simula falha.</summary>
    private class FakeUnifiClient : IUnifiClient
    {
        public CompanyUnifi? ObjReceivedConfig { get; private set; }
        public string? SAuthorizedMac { get; private set; }
        public int? IReceivedMinutes { get; private set; }
        public bool Fail { get; set; }

        public Task AuthorizeGuestAsync(
            CompanyUnifi objConfig, string sMac, int iAccessMinutes,
            CancellationToken objCancellationToken = default)
        {
            if (Fail)
            {
                throw new UnifiException("Simulação de falha.");
            }
            ObjReceivedConfig = objConfig;
            SAuthorizedMac = sMac;
            IReceivedMinutes = iAccessMinutes;
            return Task.CompletedTask;
        }

        public Task<string> TestConnectionAsync(
            CompanyUnifi objConfig, CancellationToken objCancellationToken = default)
        {
            return Task.FromResult("ok");
        }
    }

    /// <summary>Cria empresa + unidade (com a controladora) e devolve a unidade.</summary>
    private static Unit CreateUnit(AppDbContext objDbContext, string sUnitSlug = "exemplo-matriz")
    {
        Company objCompany = new Company { Name = "Loja Exemplo", Slug = "exemplo" };
        objDbContext.Companies.Add(objCompany);
        Unit objUnit = new Unit
        {
            IDCompany = objCompany.Id,
            Name = "Matriz",
            Slug = sUnitSlug,
            Unifi = new CompanyUnifi { Host = "https://192.168.1.1" },
        };
        objDbContext.Units.Add(objUnit);
        objDbContext.SaveChanges();
        return objUnit;
    }

    private static AuthorizeRequest CreateRequest(
        string? sUnit = "exemplo-matriz", string? sMac = "AA:BB:CC:DD:EE:FF", bool consent = true)
    {
        return new AuthorizeRequest(
            Name: "Ana Beatriz Souza",
            Instagram: "@anabsouza",
            Phone: "(91) 98888-1234",
            BirthDate: "12/03/1998",
            Consent: consent,
            Unit: sUnit,
            Mac: sMac,
            Ap: "11:22:33:44:55:66",
            Ssid: "Exemplo",
            Url: "https://www.exemplo.com.br");
    }

    private static AuthorizeController CreateController(AppDbContext objDbContext, IUnifiClient objUnifiClient)
    {
        return new AuthorizeController(
            objDbContext, objUnifiClient, NullLogger<AuthorizeController>.Instance);
    }

    [Fact]
    public async Task Post_NoUnit_Returns400()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        AuthorizeController objController = CreateController(objDbContext, new FakeUnifiClient());

        ActionResult<AuthorizeResponse> objResult =
            await objController.Post(CreateRequest(sUnit: null), CancellationToken.None);

        BadRequestObjectResult objBadRequest = Assert.IsType<BadRequestObjectResult>(objResult.Result);
        AuthorizeResponse objResponse = Assert.IsType<AuthorizeResponse>(objBadRequest.Value);
        Assert.Equal("Unidade não informada.", objResponse.Error);
    }

    [Fact]
    public async Task Post_UnknownUnit_Returns400()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        CreateUnit(objDbContext);
        AuthorizeController objController = CreateController(objDbContext, new FakeUnifiClient());

        ActionResult<AuthorizeResponse> objResult =
            await objController.Post(CreateRequest(sUnit: "outra"), CancellationToken.None);

        BadRequestObjectResult objBadRequest = Assert.IsType<BadRequestObjectResult>(objResult.Result);
        AuthorizeResponse objResponse = Assert.IsType<AuthorizeResponse>(objBadRequest.Value);
        Assert.Equal("Unidade não encontrada ou inativa.", objResponse.Error);
        Assert.Empty(objDbContext.Leads);
    }

    [Fact]
    public async Task Post_InactiveUnit_Returns400()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Unit objUnit = CreateUnit(objDbContext);
        objUnit.Active = false;
        objDbContext.SaveChanges();
        AuthorizeController objController = CreateController(objDbContext, new FakeUnifiClient());

        ActionResult<AuthorizeResponse> objResult =
            await objController.Post(CreateRequest(), CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(objResult.Result);
        Assert.Empty(objDbContext.Leads);
    }

    [Fact]
    public async Task Post_NoMac_Returns400WithMessage()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        CreateUnit(objDbContext);
        AuthorizeController objController = CreateController(objDbContext, new FakeUnifiClient());

        ActionResult<AuthorizeResponse> objResult =
            await objController.Post(CreateRequest(sMac: null), CancellationToken.None);

        BadRequestObjectResult objBadRequest = Assert.IsType<BadRequestObjectResult>(objResult.Result);
        AuthorizeResponse objResponse = Assert.IsType<AuthorizeResponse>(objBadRequest.Value);
        Assert.Equal("MAC do cliente ausente.", objResponse.Error);
        Assert.Empty(objDbContext.Leads);
    }

    [Fact]
    public async Task Post_NoConsent_Returns400WithLgpdMessage()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        CreateUnit(objDbContext);
        AuthorizeController objController = CreateController(objDbContext, new FakeUnifiClient());

        ActionResult<AuthorizeResponse> objResult =
            await objController.Post(CreateRequest(consent: false), CancellationToken.None);

        BadRequestObjectResult objBadRequest = Assert.IsType<BadRequestObjectResult>(objResult.Result);
        AuthorizeResponse objResponse = Assert.IsType<AuthorizeResponse>(objBadRequest.Value);
        Assert.Equal("É necessário aceitar os termos (LGPD).", objResponse.Error);
        Assert.Empty(objDbContext.Leads);
    }

    [Fact]
    public async Task Post_ValidData_SavesLeadWithUnitIdAndCallsUnitUnifi()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Unit objUnit = CreateUnit(objDbContext);
        FakeUnifiClient objUnifiClient = new FakeUnifiClient();
        AuthorizeController objController = CreateController(objDbContext, objUnifiClient);

        ActionResult<AuthorizeResponse> objResult =
            await objController.Post(CreateRequest(), CancellationToken.None);

        OkObjectResult objOk = Assert.IsType<OkObjectResult>(objResult.Result);
        AuthorizeResponse objResponse = Assert.IsType<AuthorizeResponse>(objOk.Value);
        Assert.True(objResponse.Authorized);
        Assert.Equal("https://www.exemplo.com.br", objResponse.Redirect);

        Assert.Equal("AA:BB:CC:DD:EE:FF", objUnifiClient.SAuthorizedMac);
        Assert.Same(objUnit.Unifi, objUnifiClient.ObjReceivedConfig);
        Assert.Single(objDbContext.Leads);
        Assert.Equal(objUnit.Id, objDbContext.Leads.Single().IDUnit);
    }

    [Fact]
    public async Task Post_WithSavedSettings_PassesCompanyAccessMinutes()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Unit objUnit = CreateUnit(objDbContext);
        objDbContext.PortalSettings.Add(new PortalSettings
        {
            IDCompany = objUnit.IDCompany,
            AccessMinutes = 90,
        });
        objDbContext.SaveChanges();
        FakeUnifiClient objUnifiClient = new FakeUnifiClient();
        AuthorizeController objController = CreateController(objDbContext, objUnifiClient);

        await objController.Post(CreateRequest(), CancellationToken.None);

        Assert.Equal(90, objUnifiClient.IReceivedMinutes);
    }

    [Fact]
    public async Task Post_SameMacSameUnit_UpdatesLeadInsteadOfDuplicating()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        CreateUnit(objDbContext);
        AuthorizeController objController = CreateController(objDbContext, new FakeUnifiClient());

        await objController.Post(CreateRequest(), CancellationToken.None);
        // Mesmo aparelho (mesmo MAC) volta com o nome atualizado.
        AuthorizeRequest objSecondSignup = CreateRequest() with { Name = "Ana B. Souza (novo)" };
        await objController.Post(objSecondSignup, CancellationToken.None);

        Lead objLead = Assert.Single(objDbContext.Leads);
        Assert.Equal("Ana B. Souza (novo)", objLead.Name);
    }

    [Fact]
    public async Task Post_DifferentMacsSameUnit_CreateSeparateLeads()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        CreateUnit(objDbContext);
        AuthorizeController objController = CreateController(objDbContext, new FakeUnifiClient());

        await objController.Post(CreateRequest(sMac: "AA:AA:AA:AA:AA:AA"), CancellationToken.None);
        await objController.Post(CreateRequest(sMac: "BB:BB:BB:BB:BB:BB"), CancellationToken.None);

        Assert.Equal(2, objDbContext.Leads.Count());
    }

    [Fact]
    public async Task Post_WithCompanyRedirectUrl_RedirectsToIt()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Unit objUnit = CreateUnit(objDbContext);
        objDbContext.PortalSettings.Add(new PortalSettings
        {
            IDCompany = objUnit.IDCompany,
            RedirectUrl = "https://instagram.com/exemplo",
        });
        objDbContext.SaveChanges();
        AuthorizeController objController = CreateController(objDbContext, new FakeUnifiClient());

        ActionResult<AuthorizeResponse> objResult =
            await objController.Post(CreateRequest(), CancellationToken.None);

        OkObjectResult objOk = Assert.IsType<OkObjectResult>(objResult.Result);
        AuthorizeResponse objResponse = Assert.IsType<AuthorizeResponse>(objOk.Value);
        // A URL da empresa vence a URL enviada pela UniFi no request.
        Assert.Equal("https://instagram.com/exemplo", objResponse.Redirect);
    }

    [Fact]
    public async Task Post_UnitWithOwnUrl_BeatsCompanyGeneral()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Unit objUnit = CreateUnit(objDbContext);
        objUnit.RedirectUrl = "https://instagram.com/exemplo-matriz";
        objDbContext.PortalSettings.Add(new PortalSettings
        {
            IDCompany = objUnit.IDCompany,
            RedirectUrl = "https://instagram.com/exemplo",
        });
        objDbContext.SaveChanges();
        AuthorizeController objController = CreateController(objDbContext, new FakeUnifiClient());

        ActionResult<AuthorizeResponse> objResult =
            await objController.Post(CreateRequest(), CancellationToken.None);

        AuthorizeResponse objResponse = Assert.IsType<AuthorizeResponse>(
            Assert.IsType<OkObjectResult>(objResult.Result).Value);
        Assert.Equal("https://instagram.com/exemplo-matriz", objResponse.Redirect);
    }

    [Fact]
    public async Task Post_UnitWithOwnUrlAndNoGeneral_UsesUnits()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Unit objUnit = CreateUnit(objDbContext);
        objUnit.RedirectUrl = "https://instagram.com/exemplo-matriz";
        objDbContext.SaveChanges();
        AuthorizeController objController = CreateController(objDbContext, new FakeUnifiClient());

        ActionResult<AuthorizeResponse> objResult =
            await objController.Post(CreateRequest(), CancellationToken.None);

        AuthorizeResponse objResponse = Assert.IsType<AuthorizeResponse>(
            Assert.IsType<OkObjectResult>(objResult.Result).Value);
        // Vence também a URL que a UniFi mandou no request.
        Assert.Equal("https://instagram.com/exemplo-matriz", objResponse.Redirect);
    }

    [Fact]
    public async Task Post_NoUrlAtAll_GoesToGoogle()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        CreateUnit(objDbContext);
        AuthorizeController objController = CreateController(objDbContext, new FakeUnifiClient());

        ActionResult<AuthorizeResponse> objResult =
            await objController.Post(CreateRequest() with { Url = null }, CancellationToken.None);

        AuthorizeResponse objResponse = Assert.IsType<AuthorizeResponse>(
            Assert.IsType<OkObjectResult>(objResult.Result).Value);
        Assert.Equal("https://www.google.com", objResponse.Redirect);
    }

    [Fact]
    public async Task Post_NoRedirectUrl_UsesRequestUrl()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        CreateUnit(objDbContext);
        AuthorizeController objController = CreateController(objDbContext, new FakeUnifiClient());

        ActionResult<AuthorizeResponse> objResult =
            await objController.Post(CreateRequest(), CancellationToken.None);

        OkObjectResult objOk = Assert.IsType<OkObjectResult>(objResult.Result);
        AuthorizeResponse objResponse = Assert.IsType<AuthorizeResponse>(objOk.Value);
        Assert.Equal("https://www.exemplo.com.br", objResponse.Redirect);
    }

    [Fact]
    public async Task Post_NoSettings_UsesDefault1440Minutes()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        CreateUnit(objDbContext);
        FakeUnifiClient objUnifiClient = new FakeUnifiClient();
        AuthorizeController objController = CreateController(objDbContext, objUnifiClient);

        await objController.Post(CreateRequest(), CancellationToken.None);

        Assert.Equal(1440, objUnifiClient.IReceivedMinutes);
    }

    [Fact]
    public async Task Post_RegistersCompanyCustomerByPhone_WithoutChangingResponse()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Unit objUnit = CreateUnit(objDbContext);
        AuthorizeController objController = CreateController(objDbContext, new FakeUnifiClient());

        ActionResult<AuthorizeResponse> objResult =
            await objController.Post(CreateRequest(), CancellationToken.None);

        Assert.IsType<OkObjectResult>(objResult.Result);
        Customer objCustomer = Assert.Single(objDbContext.Customers);
        Assert.Equal(objUnit.IDCompany, objCustomer.IDCompany);
        Assert.Equal("91988881234", objCustomer.Phone);
        Assert.Equal(new DateOnly(1998, 3, 12), objCustomer.BirthDate);
        Assert.Equal(objUnit.Id, objCustomer.IDLastUnit);
    }

    [Fact]
    public async Task Post_SavesProfileLinkOnLeadAndCustomer()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        CreateUnit(objDbContext);
        AuthorizeController objController = CreateController(objDbContext, new FakeUnifiClient());

        // Sem o "@" no começo também vale.
        await objController.Post(CreateRequest() with { Instagram = " Ana.Souza " }, CancellationToken.None);

        Assert.Equal("https://www.instagram.com/ana.souza", Assert.Single(objDbContext.Leads).Instagram);
        Assert.Equal("https://www.instagram.com/ana.souza", Assert.Single(objDbContext.Customers).Instagram);
    }

    [Fact]
    public async Task Post_InvalidInstagram_SavesEmptyAndAllowsAnyway()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        CreateUnit(objDbContext);
        FakeUnifiClient objUnifi = new FakeUnifiClient();
        AuthorizeController objController = CreateController(objDbContext, objUnifi);

        ActionResult<AuthorizeResponse> objResult = await objController.Post(
            CreateRequest() with { Instagram = "biell6555@gmail.com" }, CancellationToken.None);

        Assert.IsType<OkObjectResult>(objResult.Result);
        Assert.Equal("AA:BB:CC:DD:EE:FF", objUnifi.SAuthorizedMac);
        Assert.Equal("", Assert.Single(objDbContext.Leads).Instagram);
    }

    [Fact]
    public async Task Post_UnifiFailure_Returns502ButKeepsLead()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        CreateUnit(objDbContext);
        AuthorizeController objController =
            CreateController(objDbContext, new FakeUnifiClient { Fail = true });

        ActionResult<AuthorizeResponse> objResult =
            await objController.Post(CreateRequest(), CancellationToken.None);

        ObjectResult objObjectResult = Assert.IsType<ObjectResult>(objResult.Result);
        Assert.Equal(StatusCodes.Status502BadGateway, objObjectResult.StatusCode);
        AuthorizeResponse objResponse = Assert.IsType<AuthorizeResponse>(objObjectResult.Value);
        Assert.False(objResponse.Authorized);
        Assert.Equal("Falha ao autorizar na UniFi.", objResponse.Error);
        Assert.Single(objDbContext.Leads);
    }
}
