using AccessWifi.Api.Controllers;
using AccessWifi.Api.Features;
using AccessWifi.Api.Features.Settings;
using AccessWifi.Api.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AccessWifi.Api.Tests;

public class SettingsControllerTests
{
    private static AppDbContext CreateDbContext()
    {
        DbContextOptions<AppDbContext> objOptions = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(objOptions);
    }

    private static SettingsDto CreateDto(
        string sBrand = "#112233",
        string? sLogo = null,
        string sSsid = "Doce",
        int iAccessMinutes = 720)
    {
        return new SettingsDto(
            Colors: new ThemeColorsDto(
                Brand: sBrand,
                BrandDark: "#8a6d3c",
                Surface: "#f3ebdd",
                Card: "#fffdf8",
                Field: "#fbf7ef",
                Ink: "#3a3128",
                Muted: "#9a8c78",
                Line: "#e7ddcc"),
            Logo: sLogo,
            Favicon: null,
            Banner: null,
            Ssid: sSsid,
            AccessMinutes: iAccessMinutes);
    }

    [Fact]
    public async Task Get_SemLinhaGravada_DevolveOsPadroesDaMarca()
    {
        using AppDbContext objDbContext = CreateDbContext();
        SettingsController objController = new SettingsController(objDbContext);

        ActionResult<SettingsDto> objResult = await objController.Get(CancellationToken.None);

        OkObjectResult objOk = Assert.IsType<OkObjectResult>(objResult.Result);
        SettingsDto objSettings = Assert.IsType<SettingsDto>(objOk.Value);
        Assert.Equal("#c8a46d", objSettings.Colors.Brand);
        Assert.Null(objSettings.Logo);
        Assert.Equal("Doce", objSettings.Ssid);
        Assert.Equal(1440, objSettings.AccessMinutes);
    }

    [Fact]
    public async Task Put_PrimeiraVez_CriaALinhaEOGetPassaADevolver()
    {
        using AppDbContext objDbContext = CreateDbContext();
        SettingsController objController = new SettingsController(objDbContext);
        SettingsDto objDto = CreateDto(sLogo: "data:image/png;base64,AAAA");

        ActionResult<SettingsDto> objPutResult =
            await objController.Put(objDto, CancellationToken.None);

        OkObjectResult objOk = Assert.IsType<OkObjectResult>(objPutResult.Result);
        SettingsDto objSaved = Assert.IsType<SettingsDto>(objOk.Value);
        Assert.Equal("#112233", objSaved.Colors.Brand);
        Assert.Equal("data:image/png;base64,AAAA", objSaved.Logo);
        Assert.Equal(720, objSaved.AccessMinutes);

        ActionResult<SettingsDto> objGetResult = await objController.Get(CancellationToken.None);
        SettingsDto objLoaded =
            Assert.IsType<SettingsDto>(Assert.IsType<OkObjectResult>(objGetResult.Result).Value);
        Assert.Equal("#112233", objLoaded.Colors.Brand);
        Assert.Single(objDbContext.PortalSettings);
    }

    [Fact]
    public async Task Put_SegundaVez_AtualizaAMesmaLinha()
    {
        using AppDbContext objDbContext = CreateDbContext();
        SettingsController objController = new SettingsController(objDbContext);

        await objController.Put(CreateDto(sBrand: "#111111"), CancellationToken.None);
        await objController.Put(CreateDto(sBrand: "#222222"), CancellationToken.None);

        Assert.Single(objDbContext.PortalSettings);
        Assert.Equal("#222222", objDbContext.PortalSettings.Single().Colors.Brand);
    }

    [Fact]
    public async Task Put_CorInvalida_Retorna400()
    {
        using AppDbContext objDbContext = CreateDbContext();
        SettingsController objController = new SettingsController(objDbContext);

        ActionResult<SettingsDto> objResult =
            await objController.Put(CreateDto(sBrand: "vermelho"), CancellationToken.None);

        BadRequestObjectResult objBadRequest = Assert.IsType<BadRequestObjectResult>(objResult.Result);
        ErrorResponse objError = Assert.IsType<ErrorResponse>(objBadRequest.Value);
        Assert.Contains("brand", objError.Error);
        Assert.Empty(objDbContext.PortalSettings);
    }

    [Fact]
    public async Task Put_ImagemQueNaoEDataUrl_Retorna400()
    {
        using AppDbContext objDbContext = CreateDbContext();
        SettingsController objController = new SettingsController(objDbContext);

        ActionResult<SettingsDto> objResult = await objController.Put(
            CreateDto(sLogo: "https://exemplo.com/logo.png"), CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(objResult.Result);
    }

    [Fact]
    public async Task Put_MinutosForaDoIntervalo_Retorna400()
    {
        using AppDbContext objDbContext = CreateDbContext();
        SettingsController objController = new SettingsController(objDbContext);

        ActionResult<SettingsDto> objResult =
            await objController.Put(CreateDto(iAccessMinutes: 0), CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(objResult.Result);
    }
}
