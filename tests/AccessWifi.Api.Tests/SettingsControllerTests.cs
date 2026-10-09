using Models.DataBase;
using AccessWifi.Api.Controllers;
using AccessWifi.Api.Features;
using AccessWifi.Api.Features.Companies;
using AccessWifi.Api.Features.Settings;
using Models.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace AccessWifi.Api.Tests;

public class SettingsControllerTests
{
    private static Company CreateCompany(AppDbContext objDbContext, string sSlug = "exemplo")
    {
        Company objCompany = new Company { Name = "Loja Exemplo", Slug = sSlug };
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
        string sSsid = "Exemplo",
        int iAccessMinutes = 720,
        string? sRedirectUrl = null)
    {
        return new SettingsDto(
            Colors: new ThemeColorsDto(
                Brand: sBrand,
                BrandDark: "#1f2937",
                Surface: "#f3f4f6",
                Card: "#ffffff",
                Field: "#f9fafb",
                Ink: "#111827",
                Muted: "#6b7280",
                Line: "#e5e7eb"),
            Logo: sLogo,
            Favicon: null,
            Banner: null,
            Ssid: sSsid,
            AccessMinutes: iAccessMinutes,
            RedirectUrl: sRedirectUrl);
    }

    [Fact]
    public async Task Get_NoSlug_Returns400()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        SettingsController objController = new SettingsController(objDbContext);

        ActionResult<SettingsDto> objResult = await objController.Get(null, null, null, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(objResult.Result);
    }

    [Fact]
    public async Task Get_UnknownUnit_Returns404()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        SettingsController objController = new SettingsController(objDbContext);

        ActionResult<SettingsDto> objResult = await objController.Get("nada", null, null, CancellationToken.None);

        NotFoundObjectResult objNotFound = Assert.IsType<NotFoundObjectResult>(objResult.Result);
        ErrorResponse objError = Assert.IsType<ErrorResponse>(objNotFound.Value);
        Assert.Equal("Unidade não encontrada.", objError.Error);
    }

    [Fact]
    public async Task Get_UnitWithoutSavedRow_ReturnsNeutralDefault()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Company objCompany = CreateCompany(objDbContext);
        string sUnitSlug = CreateUnit(objDbContext, objCompany.Id, "exemplo-matriz");
        SettingsController objController = new SettingsController(objDbContext);

        ActionResult<SettingsDto> objResult = await objController.Get(sUnitSlug, null, null, CancellationToken.None);

        OkObjectResult objOk = Assert.IsType<OkObjectResult>(objResult.Result);
        SettingsDto objSettings = Assert.IsType<SettingsDto>(objOk.Value);
        Assert.Equal("#4b5563", objSettings.Colors.Brand);
        Assert.Null(objSettings.Logo);
        Assert.Equal(1440, objSettings.AccessMinutes);
    }

    [Fact]
    public async Task Put_CompanyAdmin_CreatesOwnCompanyRowAndGetReturnsIt()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Company objCompany = CreateCompany(objDbContext);
        string sUnitSlug = CreateUnit(objDbContext, objCompany.Id, "exemplo-matriz");
        SettingsController objController = new SettingsController(objDbContext);
        TestHelpers.SetUser(objController, objCompany.Id);

        ActionResult<SettingsDto> objPutResult = await objController.Put(
            CreateDto(sLogo: "data:image/png;base64,AAAA"), null, CancellationToken.None);

        OkObjectResult objOk = Assert.IsType<OkObjectResult>(objPutResult.Result);
        SettingsDto objSaved = Assert.IsType<SettingsDto>(objOk.Value);
        Assert.Equal("#112233", objSaved.Colors.Brand);

        PortalSettings objRow = Assert.Single(objDbContext.PortalSettings);
        Assert.Equal(objCompany.Id, objRow.IDCompany);

        ActionResult<SettingsDto> objGetResult = await objController.Get(sUnitSlug, null, null, CancellationToken.None);
        SettingsDto objLoaded =
            Assert.IsType<SettingsDto>(Assert.IsType<OkObjectResult>(objGetResult.Result).Value);
        Assert.Equal("#112233", objLoaded.Colors.Brand);
    }

    [Fact]
    public async Task Put_TwoCompanies_EachHasOwnRow()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Company objCompanyA = CreateCompany(objDbContext, "exemplo");
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
    public async Task Put_SuperAdminWithoutSlug_Returns400()
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
    public async Task Put_SuperAdminWithSlug_SavesOnGivenCompany()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Company objCompany = CreateCompany(objDbContext);
        SettingsController objController = new SettingsController(objDbContext);
        TestHelpers.SetUser(objController, null); // super admin

        ActionResult<SettingsDto> objResult =
            await objController.Put(CreateDto(), "exemplo", CancellationToken.None);

        Assert.IsType<OkObjectResult>(objResult.Result);
        Assert.Equal(objCompany.Id, objDbContext.PortalSettings.Single().IDCompany);
    }

    [Fact]
    public async Task Put_InvalidColor_Returns400()
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
    public async Task Put_ImageNotDataUrl_Returns400()
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
    public async Task Put_MinutesOutOfRange_Returns400()
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
    public async Task Put_ValidRedirectUrl_SavesAndGetReturnsIt()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Company objCompany = CreateCompany(objDbContext);
        string sUnitSlug = CreateUnit(objDbContext, objCompany.Id, "exemplo-matriz");
        SettingsController objController = new SettingsController(objDbContext);
        TestHelpers.SetUser(objController, objCompany.Id);

        await objController.Put(
            CreateDto(sRedirectUrl: "https://instagram.com/exemplo"), null, CancellationToken.None);

        Assert.Equal(
            "https://instagram.com/exemplo",
            objDbContext.PortalSettings.Single().RedirectUrl);

        ActionResult<SettingsDto> objGetResult = await objController.Get(sUnitSlug, null, null, CancellationToken.None);
        SettingsDto objLoaded =
            Assert.IsType<SettingsDto>(Assert.IsType<OkObjectResult>(objGetResult.Result).Value);
        Assert.Equal("https://instagram.com/exemplo", objLoaded.RedirectUrl);
    }

    [Fact]
    public async Task Put_EmptyRedirectUrl_SavesAsNull()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Company objCompany = CreateCompany(objDbContext);
        SettingsController objController = new SettingsController(objDbContext);
        TestHelpers.SetUser(objController, objCompany.Id);

        await objController.Put(CreateDto(sRedirectUrl: "   "), null, CancellationToken.None);

        Assert.Null(objDbContext.PortalSettings.Single().RedirectUrl);
    }

    [Fact]
    public async Task Put_InvalidRedirectUrl_Returns400()
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

    // ---- Imagens fora da resposta do tema (portal) e leitura do editor (painel) ----

    private const string LogoPng = "data:image/png;base64,iVBORw0KGgo=";

    /// <summary>Empresa com tema gravado (logo, sem favicon e sem banner) e uma unidade.</summary>
    private static (Company objCompany, string sUnitSlug) CreateCompanyWithLogo(AppDbContext objDbContext)
    {
        Company objCompany = CreateCompany(objDbContext);
        string sUnitSlug = CreateUnit(objDbContext, objCompany.Id, "exemplo-matriz");
        objDbContext.PortalSettings.Add(new PortalSettings { IDCompany = objCompany.Id, Ssid = "Exemplo", Logo = LogoPng });
        objDbContext.SaveChanges();
        return (objCompany, sUnitSlug);
    }

    private static SettingsController CreateAnonymousController(AppDbContext objDbContext)
    {
        return new SettingsController(objDbContext)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };
    }

    [Fact]
    public async Task Get_WithLogo_ReturnsImageAddressInsteadOfData()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        (_, string sUnitSlug) = CreateCompanyWithLogo(objDbContext);
        SettingsController objController = CreateAnonymousController(objDbContext);

        ActionResult<SettingsDto> objResult = await objController.Get(sUnitSlug, null, null, CancellationToken.None);

        SettingsDto objSettings = Assert.IsType<SettingsDto>(Assert.IsType<OkObjectResult>(objResult.Result).Value);
        Assert.Equal($"/settings/image/exemplo-matriz/logo?v={PortalImage.Version(LogoPng)}", objSettings.Logo);
        Assert.Null(objSettings.Favicon);
        Assert.Null(objSettings.Banner);
        Assert.Equal("exemplo-matriz", objSettings.Unit);
    }

    [Fact]
    public async Task GetImage_CurrentVersion_ReturnsFileCachedForOneYear()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        (_, string sUnitSlug) = CreateCompanyWithLogo(objDbContext);
        SettingsController objController = CreateAnonymousController(objDbContext);

        IActionResult objResult = await objController.GetImage(
            sUnitSlug, "logo", PortalImage.Version(LogoPng), CancellationToken.None);

        FileContentResult objFile = Assert.IsType<FileContentResult>(objResult);
        Assert.Equal("image/png", objFile.ContentType);
        Assert.Equal(Convert.FromBase64String("iVBORw0KGgo="), objFile.FileContents);
        Assert.Equal("public, max-age=31536000, immutable", objController.Response.Headers.CacheControl.ToString());
        Assert.Contains("sandbox", objController.Response.Headers["Content-Security-Policy"].ToString());
    }

    [Fact]
    public async Task GetImage_OldVersion_ServesCurrentWithoutCachingForever()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        (_, string sUnitSlug) = CreateCompanyWithLogo(objDbContext);
        SettingsController objController = CreateAnonymousController(objDbContext);

        IActionResult objResult = await objController.GetImage(sUnitSlug, "logo", "versao-velha", CancellationToken.None);

        Assert.IsType<FileContentResult>(objResult);
        Assert.Equal("no-cache", objController.Response.Headers.CacheControl.ToString());
    }

    [Theory]
    [InlineData("exemplo-matriz", "banner")] // empresa sem banner
    [InlineData("exemplo-matriz", "senha")] // tipo que não existe
    [InlineData("nada", "logo")] // unidade que não existe
    public async Task GetImage_NoImage_Returns404(string sUnitSlug, string sKind)
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        CreateCompanyWithLogo(objDbContext);
        SettingsController objController = CreateAnonymousController(objDbContext);

        IActionResult objResult = await objController.GetImage(sUnitSlug, sKind, null, CancellationToken.None);

        Assert.IsType<NotFoundResult>(objResult);
    }

    [Fact]
    public async Task GetImage_DisabledUnit_Returns404()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        (_, string sUnitSlug) = CreateCompanyWithLogo(objDbContext);
        objDbContext.Units.Single().Active = false;
        objDbContext.SaveChanges();
        SettingsController objController = CreateAnonymousController(objDbContext);

        IActionResult objResult = await objController.GetImage(sUnitSlug, "logo", null, CancellationToken.None);

        Assert.IsType<NotFoundResult>(objResult);
    }

    [Theory]
    [InlineData("data:image/png;base64,iVBORw0KGgo=", true, "image/png")]
    [InlineData("data:image/svg+xml;base64,PHN2Zy8+", true, "image/svg+xml")]
    [InlineData("data:text/html;base64,PGgxPg==", false, "")] // não é imagem
    [InlineData("data:image/png,naoebase64", false, "")]
    [InlineData("data:image/png;base64,%%%", false, "")]
    [InlineData("https://exemplo.com/logo.png", false, "")]
    public void PortalImage_TryDecode_AcceptsOnlyBase64Image(string sDataUrl, bool bExpected, string sKind)
    {
        bool bOk = PortalImage.TryDecode(sDataUrl, out _, out string sContentType);

        Assert.Equal(bExpected, bOk);
        Assert.Equal(sKind, sContentType);
    }

    [Fact]
    public async Task GetAdmin_CompanyAdmin_ReturnsFullImagesWithoutUnit()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Company objCompany = CreateCompany(objDbContext);
        objDbContext.PortalSettings.Add(new PortalSettings { IDCompany = objCompany.Id, Ssid = "Exemplo", Logo = LogoPng });
        objDbContext.SaveChanges();
        SettingsController objController = new SettingsController(objDbContext);
        TestHelpers.SetUser(objController, objCompany.Id);

        ActionResult<SettingsDto> objResult = await objController.GetAdmin(null, CancellationToken.None);

        SettingsDto objSettings = Assert.IsType<SettingsDto>(Assert.IsType<OkObjectResult>(objResult.Result).Value);
        Assert.Equal(LogoPng, objSettings.Logo);
        Assert.Equal("Exemplo", objSettings.Ssid);
    }

    [Fact]
    public async Task GetAdmin_CompanyWithoutSavedTheme_ReturnsNeutralDefault()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        CreateCompany(objDbContext);
        SettingsController objController = new SettingsController(objDbContext);
        TestHelpers.SetUser(objController, null); // super admin

        ActionResult<SettingsDto> objResult = await objController.GetAdmin("exemplo", CancellationToken.None);

        SettingsDto objSettings = Assert.IsType<SettingsDto>(Assert.IsType<OkObjectResult>(objResult.Result).Value);
        Assert.Equal("#4b5563", objSettings.Colors.Brand);
        Assert.Equal("#1f2937", objSettings.Colors.BrandDark);
        Assert.Null(objSettings.Logo);
    }

    // ---- DDD do exemplo de telefone no portal ----

    [Fact]
    public async Task Put_WithAreaCode_SavesAndUnitPortalWithoutOwnGetsCompanys()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Company objCompany = CreateCompany(objDbContext);
        string sUnitSlug = CreateUnit(objDbContext, objCompany.Id, "exemplo-matriz");
        SettingsController objController = CreateAnonymousController(objDbContext);
        TestHelpers.SetUser(objController, objCompany.Id);

        await objController.Put(CreateDto() with { AreaCode = "(93)" }, null, CancellationToken.None);

        Assert.Equal("93", objDbContext.PortalSettings.Single().AreaCode);
        ActionResult<SettingsDto> objResult = await objController.Get(sUnitSlug, null, null, CancellationToken.None);
        SettingsDto objPortal = Assert.IsType<SettingsDto>(Assert.IsType<OkObjectResult>(objResult.Result).Value);
        Assert.Equal("93", objPortal.AreaCode);
    }

    [Fact]
    public async Task Get_UnitWithOwnAreaCode_GetsItsOwnNotCompanys()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Company objCompany = CreateCompany(objDbContext);
        objDbContext.Units.Add(new Unit { IDCompany = objCompany.Id, Name = "Outra cidade", Slug = "exemplo-outra", AreaCode = "91" });
        objDbContext.PortalSettings.Add(new PortalSettings { IDCompany = objCompany.Id, Ssid = "Exemplo", AreaCode = "93" });
        objDbContext.SaveChanges();
        SettingsController objController = CreateAnonymousController(objDbContext);

        ActionResult<SettingsDto> objResult = await objController.Get("exemplo-outra", null, null, CancellationToken.None);

        SettingsDto objPortal = Assert.IsType<SettingsDto>(Assert.IsType<OkObjectResult>(objResult.Result).Value);
        Assert.Equal("91", objPortal.AreaCode);
    }

    [Fact]
    public async Task Put_NullAreaCode_KeepsSavedOne()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Company objCompany = CreateCompany(objDbContext);
        objDbContext.PortalSettings.Add(new PortalSettings { IDCompany = objCompany.Id, Ssid = "Exemplo", AreaCode = "93" });
        objDbContext.SaveChanges();
        SettingsController objController = new SettingsController(objDbContext);
        TestHelpers.SetUser(objController, objCompany.Id);

        await objController.Put(CreateDto(), null, CancellationToken.None);

        Assert.Equal("93", objDbContext.PortalSettings.Single().AreaCode);
    }

    [Fact]
    public async Task Put_UnknownAreaCode_Returns400()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Company objCompany = CreateCompany(objDbContext);
        SettingsController objController = new SettingsController(objDbContext);
        TestHelpers.SetUser(objController, objCompany.Id);

        ActionResult<SettingsDto> objResult =
            await objController.Put(CreateDto() with { AreaCode = "20" }, null, CancellationToken.None);

        BadRequestObjectResult objBadRequest = Assert.IsType<BadRequestObjectResult>(objResult.Result);
        Assert.Contains("DDD", Assert.IsType<ErrorResponse>(objBadRequest.Value).Error);
        Assert.Empty(objDbContext.PortalSettings);
    }

    // ---- PDF de campanha de exemplo ----

    [Fact]
    public async Task CampaignPdfPreview_WithScreenColors_ReturnsPdfWithoutSaving()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Company objCompany = CreateCompany(objDbContext);
        CreateUnit(objDbContext, objCompany.Id, "exemplo-matriz");
        SettingsController objController = new SettingsController(objDbContext);
        TestHelpers.SetUser(objController, objCompany.Id);

        IActionResult objResult = await objController.CampaignPdfPreview(
            new CampaignPdfPreviewRequest(CreateDto(sBrand: "#A41F24").Colors, "data:image/png;base64,naoeumaimagem"), null, CancellationToken.None);

        FileContentResult objPdf = Assert.IsType<FileContentResult>(objResult);
        Assert.Equal("application/pdf", objPdf.ContentType);
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(objPdf.FileContents, 0, 4));
        Assert.Empty(objDbContext.PortalSettings);
    }

    [Fact]
    public async Task CampaignPdfPreview_InvalidColor_Returns400()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Company objCompany = CreateCompany(objDbContext);
        SettingsController objController = new SettingsController(objDbContext);
        TestHelpers.SetUser(objController, objCompany.Id);

        IActionResult objResult = await objController.CampaignPdfPreview(
            new CampaignPdfPreviewRequest(CreateDto(sBrand: "vermelho").Colors, null), null, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(objResult);
    }

    [Fact]
    public void CampaignPdfSample_UsesCompanyAreaCodeAndHighlightsToday()
    {
        Models.Campaigns.CampaignPdfData objData = CampaignPdfSample.Build(
            "Lojas Nacional", "NACIONAL ADM", "91", null, new ThemeColors(), new DateOnly(2026, 10, 9));

        Assert.Equal(6, objData.Rows.Count);
        Assert.All(objData.Rows, row => Assert.StartsWith("91", row.Phone));
        Assert.Equal(2, objData.Rows.Count(row => row.IsToday));
        Assert.Contains("Lojas Nacional", objData.Rows[0].Message);
    }

    [Fact]
    public async Task GetAdmin_SuperAdminWithoutCompany_Returns400AndUnknownCompany404()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        SettingsController objController = new SettingsController(objDbContext);
        TestHelpers.SetUser(objController, null); // super admin

        Assert.IsType<BadRequestObjectResult>((await objController.GetAdmin(null, CancellationToken.None)).Result);
        Assert.IsType<NotFoundObjectResult>((await objController.GetAdmin("nada", CancellationToken.None)).Result);
    }
}
