using AccessWifi.Api.Controllers;
using AccessWifi.Api.Features;
using AccessWifi.Api.Features.Authorize;
using AccessWifi.Api.Features.Settings;
using AccessWifi.Api.Features.Units;
using AccessWifi.Api.Infrastructure.Unifi;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Models.DataBase;
using Models.Persistence;

namespace AccessWifi.Api.Tests;

/// <summary>
/// O portal descobre a unidade pelo endereço em que foi aberto. Existe porque o campo de portal
/// externo da UniFi aceita só "IP ou FQDN" — sem caminho e sem query string, então o "?unit=" não
/// chega. O slug continua valendo e tem prioridade.
/// </summary>
public class PortalHostTests
{
    private const string HostItaituba = "itaituba.wifi.exemplo.com.br";

    private class FakeUnifiClient : IUnifiClient
    {
        public string? SAuthorizedMac { get; private set; }

        public Task AuthorizeGuestAsync(
            CompanyUnifi objConfig, string sMac, int iAccessMinutes,
            CancellationToken objCancellationToken = default)
        {
            SAuthorizedMac = sMac;
            return Task.CompletedTask;
        }

        public Task<string> TestConnectionAsync(
            CompanyUnifi objConfig, CancellationToken objCancellationToken = default) =>
            Task.FromResult("ok");
    }

    private static Unit CreateUnit(
        AppDbContext objDbContext, string sSlug, string sPortalHost, string sCompanySlug = "regional")
    {
        Company objCompany = new Company { Name = "Lojas Regional", Slug = sCompanySlug };
        objDbContext.Companies.Add(objCompany);
        Unit objUnit = new Unit
        {
            IDCompany = objCompany.Id,
            Name = sSlug,
            Slug = sSlug,
            PortalHost = sPortalHost,
        };
        objDbContext.Units.Add(objUnit);
        objDbContext.SaveChanges();
        return objUnit;
    }

    private static UnitsController CreateUnitsController(AppDbContext objDbContext)
    {
        return new UnitsController(
            objDbContext, TestHelpers.CreateEncryptor(), new FakeUnifiClient(),
            NullLogger<UnitsController>.Instance);
    }

    private static AuthorizeRequest CreateAuthorizeRequest(string? sUnit, string? sHost)
    {
        return new AuthorizeRequest(
            Name: "Ana Beatriz",
            Instagram: "@ana",
            Phone: "(93) 98888-1234",
            BirthDate: "10/05/1990",
            Consent: true,
            Unit: sUnit,
            Mac: "aa:bb:cc:dd:ee:ff",
            Ap: "8c:30:66:4e:9b:58",
            Ssid: "PIX REGIONAL",
            Url: "http://www.msftconnecttest.com/redirect",
            Host: sHost);
    }

    // ------------------------------------------------------------------ GET /settings

    [Fact]
    public async Task Settings_ByHost_FindsUnitAndReturnsSlug()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        CreateUnit(objDbContext, "itaituba", HostItaituba);
        SettingsController objController = new SettingsController(objDbContext);

        ActionResult<SettingsDto> objResult =
            await objController.Get(null, HostItaituba, null, CancellationToken.None);

        SettingsDto objSettings =
            Assert.IsType<SettingsDto>(Assert.IsType<OkObjectResult>(objResult.Result).Value);
        // O slug volta na resposta: é assim que o front sabe o que mandar no /authorize.
        Assert.Equal("itaituba", objSettings.Unit);
    }

    [Theory]
    [InlineData("ITAITUBA.Wifi.Exemplo.com.BR")]
    [InlineData("itaituba.wifi.exemplo.com.br:443")]
    [InlineData("  itaituba.wifi.exemplo.com.br.  ")]
    public async Task Settings_HostSpellingVariations_StillFound(string sHost)
    {
        // O navegador pode mandar com maiúsculas, com porta ou com o ponto final do FQDN absoluto.
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        CreateUnit(objDbContext, "itaituba", HostItaituba);
        SettingsController objController = new SettingsController(objDbContext);

        ActionResult<SettingsDto> objResult =
            await objController.Get(null, sHost, null, CancellationToken.None);

        Assert.IsType<OkObjectResult>(objResult.Result);
    }

    [Fact]
    public async Task Settings_WithUnitAndHost_SlugTakesPriority()
    {
        // Não quebra nada que já esteja configurado com "?unit=".
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        CreateUnit(objDbContext, "itaituba", HostItaituba);
        CreateUnit(objDbContext, "exemplo-matriz", "exemplo.wifi.exemplo.com.br", "exemplo");
        SettingsController objController = new SettingsController(objDbContext);

        ActionResult<SettingsDto> objResult =
            await objController.Get("exemplo-matriz", HostItaituba, null, CancellationToken.None);

        SettingsDto objSettings =
            Assert.IsType<SettingsDto>(Assert.IsType<OkObjectResult>(objResult.Result).Value);
        Assert.Equal("exemplo-matriz", objSettings.Unit);
    }

    [Fact]
    public async Task Settings_UnknownHost_Returns404()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        CreateUnit(objDbContext, "itaituba", HostItaituba);
        SettingsController objController = new SettingsController(objDbContext);

        ActionResult<SettingsDto> objResult =
            await objController.Get(null, "outro.dominio.com", null, CancellationToken.None);

        Assert.IsType<NotFoundObjectResult>(objResult.Result);
    }

    [Fact]
    public async Task Settings_NoUnitAndNoHost_Returns400()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        SettingsController objController = new SettingsController(objDbContext);

        ActionResult<SettingsDto> objResult =
            await objController.Get(null, null, null, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(objResult.Result);
    }

    [Fact]
    public async Task Settings_EmptyHostDoesNotMatchUnitWithoutHost()
    {
        // Unidades sem host ficam com "" no banco: um host vazio não pode "achar" qualquer uma.
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        CreateUnit(objDbContext, "exemplo-matriz", "", "exemplo");
        SettingsController objController = new SettingsController(objDbContext);

        ActionResult<SettingsDto> objResult =
            await objController.Get(null, "   ", null, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(objResult.Result);
    }

    // ------------------------------------------------------------------ POST /authorize

    [Fact]
    public async Task Authorize_ByHost_SavesLeadInRightUnit()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Unit objUnit = CreateUnit(objDbContext, "itaituba", HostItaituba);
        CreateUnit(objDbContext, "exemplo-matriz", "exemplo.wifi.exemplo.com.br", "exemplo");
        AuthorizeController objController = new AuthorizeController(
            objDbContext, new FakeUnifiClient(), NullLogger<AuthorizeController>.Instance);

        ActionResult<AuthorizeResponse> objResult = await objController.Post(
            CreateAuthorizeRequest(sUnit: null, sHost: HostItaituba), CancellationToken.None);

        Assert.IsType<OkObjectResult>(objResult.Result);
        Lead objLead = Assert.Single(objDbContext.Leads);
        Assert.Equal(objUnit.Id, objLead.IDUnit);
    }

    [Fact]
    public async Task Authorize_UnknownHost_Returns400()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        CreateUnit(objDbContext, "itaituba", HostItaituba);
        AuthorizeController objController = new AuthorizeController(
            objDbContext, new FakeUnifiClient(), NullLogger<AuthorizeController>.Instance);

        ActionResult<AuthorizeResponse> objResult = await objController.Post(
            CreateAuthorizeRequest(sUnit: null, sHost: "outro.dominio.com"), CancellationToken.None);

        BadRequestObjectResult objBadRequest = Assert.IsType<BadRequestObjectResult>(objResult.Result);
        Assert.Equal(
            "Unidade não encontrada ou inativa.",
            Assert.IsType<AuthorizeResponse>(objBadRequest.Value).Error);
        Assert.Empty(objDbContext.Leads);
    }

    [Fact]
    public async Task Authorize_NoUnitAndNoHost_Returns400()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        AuthorizeController objController = new AuthorizeController(
            objDbContext, new FakeUnifiClient(), NullLogger<AuthorizeController>.Instance);

        ActionResult<AuthorizeResponse> objResult = await objController.Post(
            CreateAuthorizeRequest(sUnit: null, sHost: null), CancellationToken.None);

        BadRequestObjectResult objBadRequest = Assert.IsType<BadRequestObjectResult>(objResult.Result);
        Assert.Equal(
            "Unidade não informada.",
            Assert.IsType<AuthorizeResponse>(objBadRequest.Value).Error);
    }

    // ------------------------------------------------------------------ Cadastro do host

    [Fact]
    public async Task Update_SavesNormalizedHost()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Unit objUnit = CreateUnit(objDbContext, "itaituba", "");
        UnitsController objController = CreateUnitsController(objDbContext);

        ActionResult<UnitDto> objResult = await objController.Update(
            objUnit.Id,
            new UpdateUnitRequest("Itaituba", true, null, "  ITAITUBA.Wifi.Exemplo.com.BR  "),
            CancellationToken.None);

        UnitDto objDto = Assert.IsType<UnitDto>(Assert.IsType<OkObjectResult>(objResult.Result).Value);
        Assert.Equal(HostItaituba, objDto.PortalHost);
        Assert.Equal(HostItaituba, objDbContext.Units.Single(unit => unit.Id == objUnit.Id).PortalHost);
    }

    [Theory]
    [InlineData("nao-e-um-dominio")]
    [InlineData("http://itaituba.wifi.exemplo.com.br")]
    [InlineData("itaituba.wifi.exemplo.com.br/guest")]
    [InlineData("-comeca-com-hifen.com")]
    public async Task Update_InvalidHost_Returns400(string sHost)
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Unit objUnit = CreateUnit(objDbContext, "itaituba", "");
        UnitsController objController = CreateUnitsController(objDbContext);

        ActionResult<UnitDto> objResult = await objController.Update(
            objUnit.Id,
            new UpdateUnitRequest("Itaituba", true, null, sHost),
            CancellationToken.None);

        BadRequestObjectResult objBadRequest = Assert.IsType<BadRequestObjectResult>(objResult.Result);
        Assert.Contains("Endereço do portal inválido", Assert.IsType<ErrorResponse>(objBadRequest.Value).Error);
    }

    [Fact]
    public async Task Update_HostUsedByAnotherUnit_Returns400()
    {
        // Duas unidades com o mesmo endereço tornariam a identificação ambígua.
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        CreateUnit(objDbContext, "itaituba", HostItaituba);
        Unit objOther = CreateUnit(objDbContext, "santarem", "", "regional2");
        UnitsController objController = CreateUnitsController(objDbContext);

        ActionResult<UnitDto> objResult = await objController.Update(
            objOther.Id,
            new UpdateUnitRequest("Santarém", true, null, HostItaituba),
            CancellationToken.None);

        BadRequestObjectResult objBadRequest = Assert.IsType<BadRequestObjectResult>(objResult.Result);
        Assert.Equal(
            "Já existe uma unidade usando esse endereço de portal.",
            Assert.IsType<ErrorResponse>(objBadRequest.Value).Error);
    }

    [Fact]
    public async Task Update_NullHost_KeepsCurrent()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Unit objUnit = CreateUnit(objDbContext, "itaituba", HostItaituba);
        UnitsController objController = CreateUnitsController(objDbContext);

        await objController.Update(
            objUnit.Id, new UpdateUnitRequest("Itaituba", true, null, null), CancellationToken.None);

        Assert.Equal(HostItaituba, objDbContext.Units.Single(unit => unit.Id == objUnit.Id).PortalHost);
    }

    [Fact]
    public async Task Update_EmptyHost_Clears()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Unit objUnit = CreateUnit(objDbContext, "itaituba", HostItaituba);
        UnitsController objController = CreateUnitsController(objDbContext);

        await objController.Update(
            objUnit.Id, new UpdateUnitRequest("Itaituba", true, null, ""), CancellationToken.None);

        Assert.Equal("", objDbContext.Units.Single(unit => unit.Id == objUnit.Id).PortalHost);
    }

    [Fact]
    public async Task Update_SameHostOfOwnUnit_IsNotDuplicate()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Unit objUnit = CreateUnit(objDbContext, "itaituba", HostItaituba);
        UnitsController objController = CreateUnitsController(objDbContext);

        ActionResult<UnitDto> objResult = await objController.Update(
            objUnit.Id,
            new UpdateUnitRequest("Itaituba Centro", true, null, HostItaituba),
            CancellationToken.None);

        Assert.IsType<OkObjectResult>(objResult.Result);
    }
}
