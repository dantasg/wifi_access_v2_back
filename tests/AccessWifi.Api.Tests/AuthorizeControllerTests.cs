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
        public CompanyUnifi? ObjConfigRecebida { get; private set; }
        public string? SMacAutorizado { get; private set; }
        public int? IMinutosRecebidos { get; private set; }
        public bool Falhar { get; set; }

        public Task AuthorizeGuestAsync(
            CompanyUnifi objConfig, string sMac, int iAccessMinutes,
            CancellationToken objCancellationToken = default)
        {
            if (Falhar)
            {
                throw new UnifiException("Simulação de falha.");
            }
            ObjConfigRecebida = objConfig;
            SMacAutorizado = sMac;
            IMinutosRecebidos = iAccessMinutes;
            return Task.CompletedTask;
        }
    }

    /// <summary>Cria empresa + unidade (com a controladora) e devolve a unidade.</summary>
    private static Unit CreateUnit(AppDbContext objDbContext, string sUnitSlug = "doce-matriz")
    {
        Company objCompany = new Company { Name = "Dôce Cafeteria", Slug = "doce" };
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
        string? sUnit = "doce-matriz", string? sMac = "AA:BB:CC:DD:EE:FF", bool consentimento = true)
    {
        return new AuthorizeRequest(
            Nome: "Ana Beatriz Souza",
            Instagram: "@anabsouza",
            Telefone: "(91) 98888-1234",
            Nascimento: "12/03/1998",
            Consentimento: consentimento,
            Unit: sUnit,
            Mac: sMac,
            Ap: "11:22:33:44:55:66",
            Ssid: "Doce",
            Url: "https://www.doce.com.br");
    }

    private static AuthorizeController CreateController(AppDbContext objDbContext, IUnifiClient objUnifiClient)
    {
        return new AuthorizeController(
            objDbContext, objUnifiClient, NullLogger<AuthorizeController>.Instance);
    }

    [Fact]
    public async Task Post_SemUnidade_Retorna400()
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
    public async Task Post_UnidadeInexistente_Retorna400()
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
    public async Task Post_UnidadeInativa_Retorna400()
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
    public async Task Post_SemMac_Retorna400ComMensagem()
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
    public async Task Post_SemConsentimento_Retorna400ComMensagemLgpd()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        CreateUnit(objDbContext);
        AuthorizeController objController = CreateController(objDbContext, new FakeUnifiClient());

        ActionResult<AuthorizeResponse> objResult =
            await objController.Post(CreateRequest(consentimento: false), CancellationToken.None);

        BadRequestObjectResult objBadRequest = Assert.IsType<BadRequestObjectResult>(objResult.Result);
        AuthorizeResponse objResponse = Assert.IsType<AuthorizeResponse>(objBadRequest.Value);
        Assert.Equal("É necessário aceitar os termos (LGPD).", objResponse.Error);
        Assert.Empty(objDbContext.Leads);
    }

    [Fact]
    public async Task Post_ComDadosValidos_GravaLeadComIDUnitEChamaUnifiDaUnidade()
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
        Assert.Equal("https://www.doce.com.br", objResponse.Redirect);

        Assert.Equal("AA:BB:CC:DD:EE:FF", objUnifiClient.SMacAutorizado);
        Assert.Same(objUnit.Unifi, objUnifiClient.ObjConfigRecebida);
        Assert.Single(objDbContext.Leads);
        Assert.Equal(objUnit.Id, objDbContext.Leads.Single().IDUnit);
    }

    [Fact]
    public async Task Post_ComSettingsGravadas_RepassaOAccessMinutesDaEmpresa()
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

        Assert.Equal(90, objUnifiClient.IMinutosRecebidos);
    }

    [Fact]
    public async Task Post_SemSettings_UsaODefaultDe1440Minutos()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        CreateUnit(objDbContext);
        FakeUnifiClient objUnifiClient = new FakeUnifiClient();
        AuthorizeController objController = CreateController(objDbContext, objUnifiClient);

        await objController.Post(CreateRequest(), CancellationToken.None);

        Assert.Equal(1440, objUnifiClient.IMinutosRecebidos);
    }

    [Fact]
    public async Task Post_FalhaNaUnifi_Retorna502MasMantemOLead()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        CreateUnit(objDbContext);
        AuthorizeController objController =
            CreateController(objDbContext, new FakeUnifiClient { Falhar = true });

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
