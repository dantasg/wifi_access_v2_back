using Models.DataBase;
using AccessWifi.Api.Controllers;
using AccessWifi.Api.Features;
using AccessWifi.Api.Features.Companies;
using AccessWifi.Api.Features.Settings;
using Models.Persistence;
using Microsoft.AspNetCore.Mvc;

namespace AccessWifi.Api.Tests;

public class SettingsControllerTests
{
    private static Company CreateCompany(AppDbContext objDbContext, string sSlug = "doce")
    {
        Company objCompany = new Company { Name = "Dôce Cafeteria", Slug = sSlug };
        objDbContext.Companies.Add(objCompany);
        objDbContext.SaveChanges();
        return objCompany;
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
    public async Task Get_SemSlug_Retorna400()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        SettingsController objController = new SettingsController(objDbContext);

        ActionResult<SettingsDto> objResult = await objController.Get(null, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(objResult.Result);
    }

    [Fact]
    public async Task Get_EmpresaInexistente_Retorna404()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        SettingsController objController = new SettingsController(objDbContext);

        ActionResult<SettingsDto> objResult = await objController.Get("nada", CancellationToken.None);

        NotFoundObjectResult objNotFound = Assert.IsType<NotFoundObjectResult>(objResult.Result);
        ErrorResponse objError = Assert.IsType<ErrorResponse>(objNotFound.Value);
        Assert.Equal("Empresa não encontrada.", objError.Error);
    }

    [Fact]
    public async Task Get_EmpresaSemLinhaGravada_DevolveOsPadroesDaMarca()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        CreateCompany(objDbContext);
        SettingsController objController = new SettingsController(objDbContext);

        ActionResult<SettingsDto> objResult = await objController.Get("doce", CancellationToken.None);

        OkObjectResult objOk = Assert.IsType<OkObjectResult>(objResult.Result);
        SettingsDto objSettings = Assert.IsType<SettingsDto>(objOk.Value);
        Assert.Equal("#c8a46d", objSettings.Colors.Brand);
        Assert.Null(objSettings.Logo);
        Assert.Equal(1440, objSettings.AccessMinutes);
    }

    [Fact]
    public async Task Put_AdminDaEmpresa_CriaALinhaDaSuaEmpresaEOGetPassaADevolver()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Company objCompany = CreateCompany(objDbContext);
        SettingsController objController = new SettingsController(objDbContext);
        TestHelpers.SetUser(objController, objCompany.Id);

        ActionResult<SettingsDto> objPutResult = await objController.Put(
            CreateDto(sLogo: "data:image/png;base64,AAAA"), null, CancellationToken.None);

        OkObjectResult objOk = Assert.IsType<OkObjectResult>(objPutResult.Result);
        SettingsDto objSaved = Assert.IsType<SettingsDto>(objOk.Value);
        Assert.Equal("#112233", objSaved.Colors.Brand);

        PortalSettings objRow = Assert.Single(objDbContext.PortalSettings);
        Assert.Equal(objCompany.Id, objRow.IDCompany);

        ActionResult<SettingsDto> objGetResult = await objController.Get("doce", CancellationToken.None);
        SettingsDto objLoaded =
            Assert.IsType<SettingsDto>(Assert.IsType<OkObjectResult>(objGetResult.Result).Value);
        Assert.Equal("#112233", objLoaded.Colors.Brand);
    }

    [Fact]
    public async Task Put_DuasEmpresas_CadaUmaTemSuaLinha()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Company objCompanyA = CreateCompany(objDbContext, "doce");
        Company objCompanyB = CreateCompany(objDbContext, "outra");
        SettingsController objController = new SettingsController(objDbContext);

        TestHelpers.SetUser(objController, objCompanyA.Id);
        await objController.Put(CreateDto(sBrand: "#111111"), null, CancellationToken.None);

        TestHelpers.SetUser(objController, objCompanyB.Id);
        await objController.Put(CreateDto(sBrand: "#222222"), null, CancellationToken.None);

        Assert.Equal(2, objDbContext.PortalSettings.Count());
        Assert.Equal(
            "#111111",
            objDbContext.PortalSettings.Single(settings => settings.IDCompany == objCompanyA.Id).Colors.Brand);
        Assert.Equal(
            "#222222",
            objDbContext.PortalSettings.Single(settings => settings.IDCompany == objCompanyB.Id).Colors.Brand);
    }

    [Fact]
    public async Task Put_SuperAdminSemSlug_Retorna400()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        CreateCompany(objDbContext);
        SettingsController objController = new SettingsController(objDbContext);
        TestHelpers.SetUser(objController, null); // super admin

        ActionResult<SettingsDto> objResult =
            await objController.Put(CreateDto(), null, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(objResult.Result);
    }

    [Fact]
    public async Task Put_SuperAdminComSlug_SalvaNaEmpresaIndicada()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Company objCompany = CreateCompany(objDbContext);
        SettingsController objController = new SettingsController(objDbContext);
        TestHelpers.SetUser(objController, null); // super admin

        ActionResult<SettingsDto> objResult =
            await objController.Put(CreateDto(), "doce", CancellationToken.None);

        Assert.IsType<OkObjectResult>(objResult.Result);
        Assert.Equal(objCompany.Id, objDbContext.PortalSettings.Single().IDCompany);
    }

    [Fact]
    public async Task Put_CorInvalida_Retorna400()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Company objCompany = CreateCompany(objDbContext);
        SettingsController objController = new SettingsController(objDbContext);
        TestHelpers.SetUser(objController, objCompany.Id);

        ActionResult<SettingsDto> objResult = await objController.Put(
            CreateDto(sBrand: "vermelho"), null, CancellationToken.None);

        BadRequestObjectResult objBadRequest = Assert.IsType<BadRequestObjectResult>(objResult.Result);
        ErrorResponse objError = Assert.IsType<ErrorResponse>(objBadRequest.Value);
        Assert.Contains("brand", objError.Error);
        Assert.Empty(objDbContext.PortalSettings);
    }

    [Fact]
    public async Task Put_ImagemQueNaoEDataUrl_Retorna400()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Company objCompany = CreateCompany(objDbContext);
        SettingsController objController = new SettingsController(objDbContext);
        TestHelpers.SetUser(objController, objCompany.Id);

        ActionResult<SettingsDto> objResult = await objController.Put(
            CreateDto(sLogo: "https://exemplo.com/logo.png"), null, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(objResult.Result);
    }

    [Fact]
    public async Task Put_MinutosForaDoIntervalo_Retorna400()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Company objCompany = CreateCompany(objDbContext);
        SettingsController objController = new SettingsController(objDbContext);
        TestHelpers.SetUser(objController, objCompany.Id);

        ActionResult<SettingsDto> objResult = await objController.Put(
            CreateDto(iAccessMinutes: 0), null, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(objResult.Result);
    }
}
