using AccessWifi.Api.Controllers;
using AccessWifi.Api.Features.Authorize;
using AccessWifi.Api.Infrastructure.Persistence;
using AccessWifi.Api.Infrastructure.Unifi;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace AccessWifi.Api.Tests;

public class AuthorizeControllerTests
{
    /// <summary>Dublê da controladora: registra o MAC autorizado ou simula falha.</summary>
    private class FakeUnifiClient : IUnifiClient
    {
        public string? SMacAutorizado { get; private set; }
        public int? IMinutosRecebidos { get; private set; }
        public bool Falhar { get; set; }

        public Task AuthorizeGuestAsync(
            string sMac, int? iAccessMinutes = null, CancellationToken objCancellationToken = default)
        {
            if (Falhar)
            {
                throw new UnifiException("Simulação de falha.");
            }
            SMacAutorizado = sMac;
            IMinutosRecebidos = iAccessMinutes;
            return Task.CompletedTask;
        }
    }

    private static AppDbContext CreateDbContext()
    {
        DbContextOptions<AppDbContext> objOptions = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(objOptions);
    }

    private static AuthorizeRequest CreateRequest(string? sMac = "AA:BB:CC:DD:EE:FF", bool consentimento = true)
    {
        return new AuthorizeRequest(
            Nome: "Ana Beatriz Souza",
            Instagram: "@anabsouza",
            Telefone: "(91) 98888-1234",
            Nascimento: "12/03/1998",
            Consentimento: consentimento,
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
    public async Task Post_SemMac_Retorna400ComMensagem()
    {
        using AppDbContext objDbContext = CreateDbContext();
        AuthorizeController objController = CreateController(objDbContext, new FakeUnifiClient());

        ActionResult<AuthorizeResponse> objResult =
            await objController.Post(CreateRequest(sMac: null), CancellationToken.None);

        BadRequestObjectResult objBadRequest = Assert.IsType<BadRequestObjectResult>(objResult.Result);
        AuthorizeResponse objResponse = Assert.IsType<AuthorizeResponse>(objBadRequest.Value);
        Assert.False(objResponse.Authorized);
        Assert.Equal("MAC do cliente ausente.", objResponse.Error);
        Assert.Empty(objDbContext.Leads);
    }

    [Fact]
    public async Task Post_SemConsentimento_Retorna400ComMensagemLgpd()
    {
        using AppDbContext objDbContext = CreateDbContext();
        AuthorizeController objController = CreateController(objDbContext, new FakeUnifiClient());

        ActionResult<AuthorizeResponse> objResult =
            await objController.Post(CreateRequest(consentimento: false), CancellationToken.None);

        BadRequestObjectResult objBadRequest = Assert.IsType<BadRequestObjectResult>(objResult.Result);
        AuthorizeResponse objResponse = Assert.IsType<AuthorizeResponse>(objBadRequest.Value);
        Assert.Equal("É necessário aceitar os termos (LGPD).", objResponse.Error);
        Assert.Empty(objDbContext.Leads);
    }

    [Fact]
    public async Task Post_ComDadosValidos_GravaLeadEChamaUnifi()
    {
        using AppDbContext objDbContext = CreateDbContext();
        FakeUnifiClient objUnifiClient = new FakeUnifiClient();
        AuthorizeController objController = CreateController(objDbContext, objUnifiClient);

        ActionResult<AuthorizeResponse> objResult =
            await objController.Post(CreateRequest(), CancellationToken.None);

        OkObjectResult objOk = Assert.IsType<OkObjectResult>(objResult.Result);
        AuthorizeResponse objResponse = Assert.IsType<AuthorizeResponse>(objOk.Value);
        Assert.True(objResponse.Authorized);
        Assert.Equal("https://www.doce.com.br", objResponse.Redirect);

        Assert.Equal("AA:BB:CC:DD:EE:FF", objUnifiClient.SMacAutorizado);
        Assert.Single(objDbContext.Leads);
        Assert.Equal("Ana Beatriz Souza", objDbContext.Leads.Single().Nome);
    }

    [Fact]
    public async Task Post_ComSettingsGravadas_RepassaOAccessMinutesSalvo()
    {
        using AppDbContext objDbContext = CreateDbContext();
        objDbContext.PortalSettings.Add(new Features.Settings.PortalSettings { AccessMinutes = 90 });
        await objDbContext.SaveChangesAsync();
        FakeUnifiClient objUnifiClient = new FakeUnifiClient();
        AuthorizeController objController = CreateController(objDbContext, objUnifiClient);

        await objController.Post(CreateRequest(), CancellationToken.None);

        Assert.Equal(90, objUnifiClient.IMinutosRecebidos);
    }

    [Fact]
    public async Task Post_SemSettings_DeixaOUnifiClientUsarOPadraoDoAppsettings()
    {
        using AppDbContext objDbContext = CreateDbContext();
        FakeUnifiClient objUnifiClient = new FakeUnifiClient();
        AuthorizeController objController = CreateController(objDbContext, objUnifiClient);

        await objController.Post(CreateRequest(), CancellationToken.None);

        Assert.Null(objUnifiClient.IMinutosRecebidos);
    }

    [Fact]
    public async Task Post_FalhaNaUnifi_Retorna502MasMantemOLead()
    {
        using AppDbContext objDbContext = CreateDbContext();
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
