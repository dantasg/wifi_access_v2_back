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

    /// <summary>Cria uma unidade para a empresa e devolve o slug da unidade.</summary>
    private static string CreateUnit(AppDbContext objDbContext, Guid objCompanyId, string sUnitSlug)
    {
        objDbContext.Units.Add(new Unit { IDCompany = objCompanyId, Name = sUnitSlug, Slug = sUnitSlug });
        objDbContext.SaveChanges();
        return sUnitSlug;
    }

    private static SettingsDto CreateDto(
        string sBrand = "#112233",
        string? sLogo = null,
        string sSsid = "Doce",
        int iAccessMinutes = 720,
        string? sRedirectUrl = null)
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
            AccessMinutes: iAccessMinutes,
            RedirectUrl: sRedirectUrl);
    }

    [Fact]
    public async Task Get_SemSlug_Retorna400()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        SettingsController objController = new SettingsController(objDbContext);

        ActionResult<SettingsDto> objResult = await objController.Get(null, null, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(objResult.Result);
    }

    [Fact]
    public async Task Get_UnidadeInexistente_Retorna404()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        SettingsController objController = new SettingsController(objDbContext);

        ActionResult<SettingsDto> objResult = await objController.Get("nada", null, CancellationToken.None);

        NotFoundObjectResult objNotFound = Assert.IsType<NotFoundObjectResult>(objResult.Result);
        ErrorResponse objError = Assert.IsType<ErrorResponse>(objNotFound.Value);
        Assert.Equal("Unidade não encontrada.", objError.Error);
    }

    [Fact]
    public async Task Get_UnidadeSemLinhaGravada_DevolveOsPadroesDaMarca()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Company objCompany = CreateCompany(objDbContext);
        string sUnitSlug = CreateUnit(objDbContext, objCompany.Id, "doce-matriz");
        SettingsController objController = new SettingsController(objDbContext);

        ActionResult<SettingsDto> objResult = await objController.Get(sUnitSlug, null, CancellationToken.None);

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
        string sUnitSlug = CreateUnit(objDbContext, objCompany.Id, "doce-matriz");
        SettingsController objController = new SettingsController(objDbContext);
        TestHelpers.SetUser(objController, objCompany.Id);

        ActionResult<SettingsDto> objPutResult = await objController.Put(
            CreateDto(sLogo: "data:image/png;base64,AAAA"), null, CancellationToken.None);

        OkObjectResult objOk = Assert.IsType<OkObjectResult>(objPutResult.Result);
        SettingsDto objSaved = Assert.IsType<SettingsDto>(objOk.Value);
        Assert.Equal("#112233", objSaved.Colors.Brand);

        PortalSettings objRow = Assert.Single(objDbContext.PortalSettings);
        Assert.Equal(objCompany.Id, objRow.IDCompany);

        ActionResult<SettingsDto> objGetResult = await objController.Get(sUnitSlug, null, CancellationToken.None);
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

    [Fact]
    public async Task Put_UrlDeRedirecionamentoValida_SalvaEODevolveNoGet()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Company objCompany = CreateCompany(objDbContext);
        string sUnitSlug = CreateUnit(objDbContext, objCompany.Id, "doce-matriz");
        SettingsController objController = new SettingsController(objDbContext);
        TestHelpers.SetUser(objController, objCompany.Id);

        await objController.Put(
            CreateDto(sRedirectUrl: "https://instagram.com/doce"), null, CancellationToken.None);

        Assert.Equal(
            "https://instagram.com/doce",
            objDbContext.PortalSettings.Single().RedirectUrl);

        ActionResult<SettingsDto> objGetResult = await objController.Get(sUnitSlug, null, CancellationToken.None);
        SettingsDto objLoaded =
            Assert.IsType<SettingsDto>(Assert.IsType<OkObjectResult>(objGetResult.Result).Value);
        Assert.Equal("https://instagram.com/doce", objLoaded.RedirectUrl);
    }

    [Fact]
    public async Task Put_UrlDeRedirecionamentoVazia_GravaComoNula()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Company objCompany = CreateCompany(objDbContext);
        SettingsController objController = new SettingsController(objDbContext);
        TestHelpers.SetUser(objController, objCompany.Id);

        await objController.Put(CreateDto(sRedirectUrl: "   "), null, CancellationToken.None);

        Assert.Null(objDbContext.PortalSettings.Single().RedirectUrl);
    }

    private static Unit GetUnit(AppDbContext objDbContext, string sUnitSlug)
    {
        return objDbContext.Units.Single(unit => unit.Slug == sUnitSlug);
    }

    [Fact]
    public async Task Put_ComUrlDaUnidade_GravaNaUnidadeEMantemAGeral()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Company objCompany = CreateCompany(objDbContext);
        Unit objUnit = GetUnit(objDbContext, CreateUnit(objDbContext, objCompany.Id, "doce-matriz"));
        SettingsController objController = new SettingsController(objDbContext);
        TestHelpers.SetUser(objController, objCompany.Id);

        ActionResult<SettingsDto> objResult = await objController.Put(
            CreateDto(sRedirectUrl: "https://instagram.com/doce") with
            {
                UnitRedirects = [new UnitRedirectDto(objUnit.Id, "  https://instagram.com/doce-matriz  ")],
            },
            null, CancellationToken.None);

        Assert.IsType<OkObjectResult>(objResult.Result);
        Assert.Equal("https://instagram.com/doce-matriz", objUnit.RedirectUrl);
        Assert.Equal("https://instagram.com/doce", objDbContext.PortalSettings.Single().RedirectUrl);
    }

    [Fact]
    public async Task Put_UrlDaUnidadeVazia_VoltaAUsarAGeral()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Company objCompany = CreateCompany(objDbContext);
        Unit objUnit = GetUnit(objDbContext, CreateUnit(objDbContext, objCompany.Id, "doce-matriz"));
        objUnit.RedirectUrl = "https://instagram.com/doce-matriz";
        objDbContext.SaveChanges();
        SettingsController objController = new SettingsController(objDbContext);
        TestHelpers.SetUser(objController, objCompany.Id);

        await objController.Put(
            CreateDto() with { UnitRedirects = [new UnitRedirectDto(objUnit.Id, "   ")] },
            null, CancellationToken.None);

        Assert.Equal("", objUnit.RedirectUrl);
    }

    [Fact]
    public async Task Put_SemListaDeUnidades_NaoMexeNasUnidades()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Company objCompany = CreateCompany(objDbContext);
        Unit objUnit = GetUnit(objDbContext, CreateUnit(objDbContext, objCompany.Id, "doce-matriz"));
        objUnit.RedirectUrl = "https://instagram.com/doce-matriz";
        objDbContext.SaveChanges();
        SettingsController objController = new SettingsController(objDbContext);
        TestHelpers.SetUser(objController, objCompany.Id);

        await objController.Put(CreateDto(), null, CancellationToken.None);

        Assert.Equal("https://instagram.com/doce-matriz", objUnit.RedirectUrl);
    }

    [Fact]
    public async Task Put_UnidadeDeOutraEmpresa_Retorna400ENaoGravaNada()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Company objCompany = CreateCompany(objDbContext);
        Company objOutra = CreateCompany(objDbContext, "outra");
        Unit objUnitDaOutra = GetUnit(objDbContext, CreateUnit(objDbContext, objOutra.Id, "outra-loja"));
        SettingsController objController = new SettingsController(objDbContext);
        TestHelpers.SetUser(objController, objCompany.Id);

        ActionResult<SettingsDto> objResult = await objController.Put(
            CreateDto() with
            {
                UnitRedirects = [new UnitRedirectDto(objUnitDaOutra.Id, "https://instagram.com/invasor")],
            },
            null, CancellationToken.None);

        ErrorResponse objError = Assert.IsType<ErrorResponse>(
            Assert.IsType<BadRequestObjectResult>(objResult.Result).Value);
        Assert.Equal("Unidade não encontrada nesta empresa.", objError.Error);
        Assert.Equal("", objUnitDaOutra.RedirectUrl);
        Assert.Empty(objDbContext.PortalSettings);
    }

    [Fact]
    public async Task Put_UrlDaUnidadeInvalida_Retorna400ComONomeDaUnidadeENaoGravaNada()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Company objCompany = CreateCompany(objDbContext);
        Unit objUnit = GetUnit(objDbContext, CreateUnit(objDbContext, objCompany.Id, "doce-matriz"));
        SettingsController objController = new SettingsController(objDbContext);
        TestHelpers.SetUser(objController, objCompany.Id);

        ActionResult<SettingsDto> objResult = await objController.Put(
            CreateDto(sRedirectUrl: "https://instagram.com/doce") with
            {
                UnitRedirects = [new UnitRedirectDto(objUnit.Id, "instagram.com/sem-https")],
            },
            null, CancellationToken.None);

        ErrorResponse objError = Assert.IsType<ErrorResponse>(
            Assert.IsType<BadRequestObjectResult>(objResult.Result).Value);
        Assert.StartsWith("Unidade doce-matriz:", objError.Error);
        // Nem a Geral foi gravada: o erro numa unidade barra o salvamento inteiro.
        Assert.Empty(objDbContext.PortalSettings);
        Assert.Equal("", objUnit.RedirectUrl);
    }

    [Fact]
    public async Task Put_UrlDeRedirecionamentoInvalida_Retorna400()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Company objCompany = CreateCompany(objDbContext);
        SettingsController objController = new SettingsController(objDbContext);
        TestHelpers.SetUser(objController, objCompany.Id);

        ActionResult<SettingsDto> objResult = await objController.Put(
            CreateDto(sRedirectUrl: "javascript:alert(1)"), null, CancellationToken.None);

        BadRequestObjectResult objBadRequest = Assert.IsType<BadRequestObjectResult>(objResult.Result);
        ErrorResponse objError = Assert.IsType<ErrorResponse>(objBadRequest.Value);
        Assert.Contains("redirecionamento", objError.Error);
        Assert.Empty(objDbContext.PortalSettings);
    }
}
