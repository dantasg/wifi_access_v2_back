using Models.DataBase;
using AccessWifi.Api.Controllers;
using AccessWifi.Api.Features;
using AccessWifi.Api.Features.Units;
using Models.Persistence;
using AccessWifi.Api.Infrastructure.Unifi;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace AccessWifi.Api.Tests;

public class UnitsControllerTests
{
    private static Company CreateCompany(AppDbContext objDbContext, string sSlug = "exemplo")
    {
        Company objCompany = new Company { Name = "Loja Exemplo", Slug = sSlug };
        objDbContext.Companies.Add(objCompany);
        objDbContext.SaveChanges();
        return objCompany;
    }

    private static UnitsController CreateController(
        AppDbContext objDbContext, IUnifiClient? objUnifiClient = null)
    {
        return new UnitsController(
            objDbContext,
            TestHelpers.CreateEncryptor(),
            objUnifiClient ?? new FakeUnifiClient(),
            NullLogger<UnitsController>.Instance);
    }

    /// <summary>Dublê só para a rota de teste de conexão: devolve sucesso ou lança o erro pedido.</summary>
    private class FakeUnifiClient : IUnifiClient
    {
        public UnifiException? ObjError { get; set; }
        public string? SDiscoveredSiteId { get; set; }

        public Task AuthorizeGuestAsync(
            CompanyUnifi objConfig, string sMac, int iAccessMinutes,
            CancellationToken objCancellationToken = default) => Task.CompletedTask;

        public Task<string> TestConnectionAsync(
            CompanyUnifi objConfig, CancellationToken objCancellationToken = default)
        {
            if (ObjError is not null)
            {
                throw ObjError;
            }
            if (SDiscoveredSiteId is not null)
            {
                objConfig.SiteId = SDiscoveredSiteId;
            }
            return Task.FromResult("Console respondeu pela nuvem.");
        }
    }

    private static CreateUnitRequest CreateRequest(Guid objCompanyId, string sSlug = "exemplo-matriz")
    {
        return new CreateUnitRequest(
            IDCompany: objCompanyId,
            Name: "Matriz",
            Slug: sSlug,
            Unifi: new UnitUnifiRequest(
                Host: "https://192.168.1.1",
                Site: "default",
                Username: "unifi-user",
                Password: "unifi-pass",
                UnifiOs: true,
                VerifySsl: false));
    }

    [Fact]
    public async Task Create_ValidData_CreatesWithoutExposingUnifiPassword()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Company objCompany = CreateCompany(objDbContext);
        UnitsController objController = CreateController(objDbContext);

        ActionResult<UnitDto> objResult =
            await objController.Create(CreateRequest(objCompany.Id), CancellationToken.None);

        OkObjectResult objOk = Assert.IsType<OkObjectResult>(objResult.Result);
        UnitDto objUnit = Assert.IsType<UnitDto>(objOk.Value);
        Assert.Equal("exemplo-matriz", objUnit.Slug);
        Assert.Equal("https://192.168.1.1", objUnit.Unifi.Host);

        // A senha fica só na entidade (o DTO não tem a propriedade) e é guardada CIFRADA.
        string sStored = objDbContext.Units.Single().Unifi.Password;
        Assert.NotEqual("unifi-pass", sStored);
        Assert.Equal("unifi-pass", TestHelpers.CreateEncryptor().Decrypt(sStored));
        Assert.DoesNotContain(
            typeof(UnitUnifiDto).GetProperties(), objProperty => objProperty.Name == "Password");
    }

    [Fact]
    public async Task Create_UnknownCompany_Returns400()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        UnitsController objController = CreateController(objDbContext);

        ActionResult<UnitDto> objResult =
            await objController.Create(CreateRequest(Guid.NewGuid()), CancellationToken.None);

        BadRequestObjectResult objBadRequest = Assert.IsType<BadRequestObjectResult>(objResult.Result);
        Assert.Equal("Empresa não encontrada.", Assert.IsType<ErrorResponse>(objBadRequest.Value).Error);
        Assert.Empty(objDbContext.Units);
    }

    [Fact]
    public async Task Create_SlugDuplicatedGlobally_Returns400()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Company objCompanyA = CreateCompany(objDbContext, "exemplo");
        Company objCompanyB = CreateCompany(objDbContext, "outra");
        UnitsController objController = CreateController(objDbContext);
        await objController.Create(CreateRequest(objCompanyA.Id, "matriz"), CancellationToken.None);

        // Mesmo slug, outra empresa: barrado (slug é único globalmente).
        ActionResult<UnitDto> objResult =
            await objController.Create(CreateRequest(objCompanyB.Id, "matriz"), CancellationToken.None);

        BadRequestObjectResult objBadRequest = Assert.IsType<BadRequestObjectResult>(objResult.Result);
        Assert.Equal("Já existe uma unidade com esse slug.", Assert.IsType<ErrorResponse>(objBadRequest.Value).Error);
    }

    [Fact]
    public async Task Create_InvalidSlug_Returns400()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Company objCompany = CreateCompany(objDbContext);
        UnitsController objController = CreateController(objDbContext);

        ActionResult<UnitDto> objResult =
            await objController.Create(CreateRequest(objCompany.Id, "Matriz Central!"), CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(objResult.Result);
        Assert.Empty(objDbContext.Units);
    }

    [Fact]
    public async Task Update_NullUnifiPassword_KeepsCurrentPassword()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Company objCompany = CreateCompany(objDbContext);
        UnitsController objController = CreateController(objDbContext);
        await objController.Create(CreateRequest(objCompany.Id), CancellationToken.None);
        Guid objUnitId = objDbContext.Units.Single().Id;

        UpdateUnitRequest objUpdate = new UpdateUnitRequest(
            Name: "Matriz Nova",
            Active: true,
            Unifi: new UnitUnifiRequest(
                Host: "https://10.0.0.1",
                Site: "default",
                Username: "unifi-user",
                Password: null,
                UnifiOs: true,
                VerifySsl: false));

        ActionResult<UnitDto> objResult =
            await objController.Update(objUnitId, objUpdate, CancellationToken.None);

        Assert.IsType<OkObjectResult>(objResult.Result);
        Unit objUnit = objDbContext.Units.Single();
        Assert.Equal("Matriz Nova", objUnit.Name);
        Assert.Equal("https://10.0.0.1", objUnit.Unifi.Host);
        // Senha nula no update = mantém a atual (que segue cifrada, decifrando para o valor original).
        Assert.Equal("unifi-pass", TestHelpers.CreateEncryptor().Decrypt(objUnit.Unifi.Password));
    }

    [Fact]
    public async Task Create_WithRedirectUrl_SavesTrimmed()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Company objCompany = CreateCompany(objDbContext);
        UnitsController objController = CreateController(objDbContext);

        ActionResult<UnitDto> objResult = await objController.Create(
            CreateRequest(objCompany.Id) with { RedirectUrl = "  https://instagram.com/exemplo-matriz  " },
            CancellationToken.None);

        UnitDto objDto = Assert.IsType<UnitDto>(Assert.IsType<OkObjectResult>(objResult.Result).Value);
        Assert.Equal("https://instagram.com/exemplo-matriz", objDto.RedirectUrl);
        Assert.Equal("https://instagram.com/exemplo-matriz", objDbContext.Units.Single().RedirectUrl);
    }

    [Fact]
    public async Task Create_NoRedirectUrl_StaysEmptyAndUsesGeneral()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Company objCompany = CreateCompany(objDbContext);
        UnitsController objController = CreateController(objDbContext);

        await objController.Create(CreateRequest(objCompany.Id), CancellationToken.None);

        Assert.Equal("", objDbContext.Units.Single().RedirectUrl);
    }

    [Fact]
    public async Task Update_NullRedirectUrl_KeepsCurrent()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Company objCompany = CreateCompany(objDbContext);
        Unit objUnit = new Unit
        {
            IDCompany = objCompany.Id, Name = "Matriz", Slug = "exemplo-matriz",
            RedirectUrl = "https://instagram.com/exemplo-matriz",
        };
        objDbContext.Units.Add(objUnit);
        objDbContext.SaveChanges();
        UnitsController objController = CreateController(objDbContext);

        await objController.Update(
            objUnit.Id, new UpdateUnitRequest("Matriz Centro", true, null), CancellationToken.None);

        Assert.Equal("https://instagram.com/exemplo-matriz", objUnit.RedirectUrl);
        Assert.Equal("Matriz Centro", objUnit.Name);
    }

    [Fact]
    public async Task Update_EmptyRedirectUrl_FallsBackToGeneral()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Company objCompany = CreateCompany(objDbContext);
        Unit objUnit = new Unit
        {
            IDCompany = objCompany.Id, Name = "Matriz", Slug = "exemplo-matriz",
            RedirectUrl = "https://instagram.com/exemplo-matriz",
        };
        objDbContext.Units.Add(objUnit);
        objDbContext.SaveChanges();
        UnitsController objController = CreateController(objDbContext);

        await objController.Update(
            objUnit.Id, new UpdateUnitRequest("Matriz", true, null, RedirectUrl: "   "),
            CancellationToken.None);

        Assert.Equal("", objUnit.RedirectUrl);
    }

    [Fact]
    public async Task Update_InvalidRedirectUrl_Returns400AndDoesNotSave()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Company objCompany = CreateCompany(objDbContext);
        Unit objUnit = new Unit { IDCompany = objCompany.Id, Name = "Matriz", Slug = "exemplo-matriz" };
        objDbContext.Units.Add(objUnit);
        objDbContext.SaveChanges();
        UnitsController objController = CreateController(objDbContext);

        ActionResult<UnitDto> objResult = await objController.Update(
            objUnit.Id, new UpdateUnitRequest("Matriz", true, null, RedirectUrl: "instagram.com/sem-https"),
            CancellationToken.None);

        ErrorResponse objError = Assert.IsType<ErrorResponse>(
            Assert.IsType<BadRequestObjectResult>(objResult.Result).Value);
        Assert.Contains("redirecionamento", objError.Error);
        Assert.Equal("", objDbContext.Units.AsNoTracking().Single().RedirectUrl);
    }

    [Fact]
    public async Task Email_CreatesWithEmail_NullKeeps_EmptyClears()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Company objCompany = CreateCompany(objDbContext);
        UnitsController objController = CreateController(objDbContext);

        UnitDto objCreated = Assert.IsType<UnitDto>(Assert.IsType<OkObjectResult>((await objController.Create(
            CreateRequest(objCompany.Id) with { Email = "  gerente@loja.com.br  " }, CancellationToken.None)).Result).Value);
        Assert.Equal("gerente@loja.com.br", objCreated.Email);

        await objController.Update(objCreated.Id, new UpdateUnitRequest("Matriz", true, null), CancellationToken.None);
        Assert.Equal("gerente@loja.com.br", objDbContext.Units.AsNoTracking().Single().Email);

        await objController.Update(objCreated.Id, new UpdateUnitRequest("Matriz", true, null, Email: ""), CancellationToken.None);
        Assert.Equal("", objDbContext.Units.AsNoTracking().Single().Email);
    }

    [Fact]
    public async Task Email_Invalid_Returns400AndDoesNotSave()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Company objCompany = CreateCompany(objDbContext);
        Unit objUnit = new Unit { IDCompany = objCompany.Id, Name = "Matriz", Slug = "exemplo-matriz", Email = "a@a.com" };
        objDbContext.Units.Add(objUnit);
        objDbContext.SaveChanges();

        ActionResult<UnitDto> objResult = await CreateController(objDbContext).Update(
            objUnit.Id, new UpdateUnitRequest("Matriz", true, null, Email: "gerente-sem-arroba"), CancellationToken.None);

        Assert.Equal("E-mail da unidade inválido.", Assert.IsType<ErrorResponse>(
            Assert.IsType<BadRequestObjectResult>(objResult.Result).Value).Error);
        Assert.Equal("a@a.com", objDbContext.Units.AsNoTracking().Single().Email);
    }

    [Fact]
    public async Task GetAll_SuperAdminWithCompanyFilter_ReturnsOnlyThatCompanyUnits()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Company objCompanyA = CreateCompany(objDbContext, "exemplo");
        Company objCompanyB = CreateCompany(objDbContext, "outra");
        UnitsController objController = CreateController(objDbContext);
        await objController.Create(CreateRequest(objCompanyA.Id, "exemplo-um"), CancellationToken.None);
        await objController.Create(CreateRequest(objCompanyB.Id, "outra-um"), CancellationToken.None);
        TestHelpers.SetUser(objController, null); // super admin

        ActionResult<List<UnitDto>> objResult =
            await objController.GetAll(objCompanyA.Id, CancellationToken.None);

        List<UnitDto> objUnits =
            Assert.IsType<List<UnitDto>>(Assert.IsType<OkObjectResult>(objResult.Result).Value);
        UnitDto objUnit = Assert.Single(objUnits);
        Assert.Equal("exemplo-um", objUnit.Slug);
    }

    [Fact]
    public async Task GetAll_CompanyAdmin_SeesOnlyOwnCompanyUnitsIgnoringFilter()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Company objCompanyA = CreateCompany(objDbContext, "exemplo");
        Company objCompanyB = CreateCompany(objDbContext, "outra");
        UnitsController objController = CreateController(objDbContext);
        await objController.Create(CreateRequest(objCompanyA.Id, "exemplo-um"), CancellationToken.None);
        await objController.Create(CreateRequest(objCompanyB.Id, "outra-um"), CancellationToken.None);
        TestHelpers.SetCompanyUser(objController, objDbContext, objCompanyA.Id); // admin da empresa A

        // Mesmo passando o id da empresa B no filtro, só enxerga a própria empresa.
        ActionResult<List<UnitDto>> objResult =
            await objController.GetAll(objCompanyB.Id, CancellationToken.None);

        List<UnitDto> objUnits =
            Assert.IsType<List<UnitDto>>(Assert.IsType<OkObjectResult>(objResult.Result).Value);
        UnitDto objUnit = Assert.Single(objUnits);
        Assert.Equal("exemplo-um", objUnit.Slug);
    }

    // ------------------------------------------------------------------ Modo nuvem (D1/D2/D3/D7)

    private static UnitUnifiRequest CloudRequest(
        string? sApiKey = "chave-da-nuvem", string sConsoleId = "CONSOLE-A:123456789")
    {
        return new UnitUnifiRequest(
            Host: "",
            Site: "default",
            Username: "",
            Password: null,
            UnifiOs: true,
            VerifySsl: false,
            Mode: UnifiMode.Cloud,
            ConsoleId: sConsoleId,
            ApiKey: sApiKey);
    }

    [Fact]
    public async Task Create_CloudMode_StoresKeyEncryptedAndNeverExposesIt()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Company objCompany = CreateCompany(objDbContext);
        UnitsController objController = CreateController(objDbContext);

        ActionResult<UnitDto> objResult = await objController.Create(
            new CreateUnitRequest(objCompany.Id, "Itaituba", "itaituba", CloudRequest()),
            CancellationToken.None);

        UnitDto objDto = Assert.IsType<UnitDto>(Assert.IsType<OkObjectResult>(objResult.Result).Value);
        Assert.Equal(UnifiMode.Cloud, objDto.Unifi.Mode);
        Assert.Equal("CONSOLE-A:123456789", objDto.Unifi.ConsoleId);
        Assert.True(objDto.Unifi.HasApiKey);

        // A chave é guardada CIFRADA e não existe propriedade para ela no DTO de leitura.
        string sStored = objDbContext.Units.Single().Unifi.ApiKey;
        Assert.NotEqual("chave-da-nuvem", sStored);
        Assert.Equal("chave-da-nuvem", TestHelpers.CreateEncryptor().Decrypt(sStored));
        Assert.DoesNotContain(
            typeof(UnitUnifiDto).GetProperties(), objProperty => objProperty.Name == "ApiKey");
    }

    [Fact]
    public async Task Create_NoModeGiven_StaysLocal()
    {
        // As unidades que já existem (e qualquer front antigo) não podem virar nuvem por acidente.
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Company objCompany = CreateCompany(objDbContext);
        UnitsController objController = CreateController(objDbContext);

        await objController.Create(CreateRequest(objCompany.Id), CancellationToken.None);

        Assert.Equal(UnifiMode.Local, objDbContext.Units.Single().Unifi.Mode);
    }

    [Fact]
    public async Task Update_NullApiKey_KeepsCurrent()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Company objCompany = CreateCompany(objDbContext);
        UnitsController objController = CreateController(objDbContext);
        await objController.Create(
            new CreateUnitRequest(objCompany.Id, "Itaituba", "itaituba", CloudRequest()),
            CancellationToken.None);
        Guid objUnitId = objDbContext.Units.Single().Id;

        await objController.Update(
            objUnitId,
            new UpdateUnitRequest("Itaituba Centro", true, CloudRequest(sApiKey: null)),
            CancellationToken.None);

        Assert.Equal(
            "chave-da-nuvem",
            TestHelpers.CreateEncryptor().Decrypt(objDbContext.Units.Single().Unifi.ApiKey));
    }

    [Fact]
    public async Task Update_ChangingConsole_DiscardsStoredSite()
    {
        // O site é um UUID de dentro do console: guardar o do console anterior daria erro mudo.
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Company objCompany = CreateCompany(objDbContext);
        UnitsController objController = CreateController(objDbContext);
        await objController.Create(
            new CreateUnitRequest(objCompany.Id, "Itaituba", "itaituba", CloudRequest()),
            CancellationToken.None);

        Unit objUnit = objDbContext.Units.Single();
        objUnit.Unifi.SiteId = "88f7af54-98f8-306a-a1c7-c9349722b1f6";
        objDbContext.SaveChanges();

        await objController.Update(
            objUnit.Id,
            new UpdateUnitRequest("Itaituba", true, CloudRequest(sConsoleId: "CONSOLE-B:987654321")),
            CancellationToken.None);

        Assert.Equal("CONSOLE-B:987654321", objDbContext.Units.Single().Unifi.ConsoleId);
        Assert.Equal("", objDbContext.Units.Single().Unifi.SiteId);
    }

    [Fact]
    public async Task TestUnifi_Success_StoresDiscoveredSiteOnUnit()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Company objCompany = CreateCompany(objDbContext);
        FakeUnifiClient objFake = new FakeUnifiClient
        {
            SDiscoveredSiteId = "88f7af54-98f8-306a-a1c7-c9349722b1f6",
        };
        UnitsController objController = CreateController(objDbContext, objFake);
        await objController.Create(
            new CreateUnitRequest(objCompany.Id, "Itaituba", "itaituba", CloudRequest()),
            CancellationToken.None);
        Guid objUnitId = objDbContext.Units.Single().Id;

        ActionResult<UnifiTestResponse> objResult =
            await objController.TestUnifi(objUnitId, CancellationToken.None);

        UnifiTestResponse objResponse =
            Assert.IsType<UnifiTestResponse>(Assert.IsType<OkObjectResult>(objResult.Result).Value);
        Assert.True(objResponse.Success);
        // O site descoberto no teste fica gravado — o primeiro visitante não paga essa consulta.
        Assert.Equal(
            "88f7af54-98f8-306a-a1c7-c9349722b1f6", objDbContext.Units.Single().Unifi.SiteId);
    }

    [Fact]
    public async Task TestUnifi_WrongConfiguration_Returns200WithReason()
    {
        // Configuração errada é resultado do teste, não erro da requisição: o painel mostra a causa.
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Company objCompany = CreateCompany(objDbContext);
        FakeUnifiClient objFake = new FakeUnifiClient
        {
            ObjError = new UnifiException("Chave de API da nuvem UniFi inválida ou revogada."),
        };
        UnitsController objController = CreateController(objDbContext, objFake);
        await objController.Create(
            new CreateUnitRequest(objCompany.Id, "Itaituba", "itaituba", CloudRequest()),
            CancellationToken.None);

        ActionResult<UnifiTestResponse> objResult = await objController.TestUnifi(
            objDbContext.Units.Single().Id, CancellationToken.None);

        UnifiTestResponse objResponse =
            Assert.IsType<UnifiTestResponse>(Assert.IsType<OkObjectResult>(objResult.Result).Value);
        Assert.False(objResponse.Success);
        Assert.Equal("Chave de API da nuvem UniFi inválida ou revogada.", objResponse.Message);
    }

    [Fact]
    public async Task TestUnifi_UnknownUnit_Returns404()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        UnitsController objController = CreateController(objDbContext);

        ActionResult<UnifiTestResponse> objResult =
            await objController.TestUnifi(Guid.NewGuid(), CancellationToken.None);

        NotFoundObjectResult objNotFound = Assert.IsType<NotFoundObjectResult>(objResult.Result);
        Assert.Equal("Unidade não encontrada.", Assert.IsType<ErrorResponse>(objNotFound.Value).Error);
    }

    // ---- DDD da loja (exemplo de telefone no portal) ----

    [Fact]
    public async Task Create_WithAreaCode_SavesOnlyDigits()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Company objCompany = CreateCompany(objDbContext);
        UnitsController objController = CreateController(objDbContext);

        ActionResult<UnitDto> objResult = await objController.Create(
            CreateRequest(objCompany.Id) with { AreaCode = " (91) " }, CancellationToken.None);

        UnitDto objDto = Assert.IsType<UnitDto>(Assert.IsType<OkObjectResult>(objResult.Result).Value);
        Assert.Equal("91", objDto.AreaCode);
        Assert.Equal("91", objDbContext.Units.Single().AreaCode);
    }

    [Theory]
    [InlineData("20")] // não existe
    [InlineData("9")]
    [InlineData("911")]
    public async Task Create_UnknownAreaCode_Returns400(string sAreaCode)
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Company objCompany = CreateCompany(objDbContext);
        UnitsController objController = CreateController(objDbContext);

        ActionResult<UnitDto> objResult = await objController.Create(
            CreateRequest(objCompany.Id) with { AreaCode = sAreaCode }, CancellationToken.None);

        BadRequestObjectResult objBadRequest = Assert.IsType<BadRequestObjectResult>(objResult.Result);
        Assert.Contains("DDD", Assert.IsType<ErrorResponse>(objBadRequest.Value).Error);
        Assert.Empty(objDbContext.Units);
    }

    [Fact]
    public async Task Update_NullAreaCodeKeepsAndEmptyFallsBackToCompanys()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Company objCompany = CreateCompany(objDbContext);
        Unit objUnit = new Unit { IDCompany = objCompany.Id, Name = "Matriz", Slug = "exemplo-matriz", AreaCode = "93" };
        objDbContext.Units.Add(objUnit);
        objDbContext.SaveChanges();
        UnitsController objController = CreateController(objDbContext);

        await objController.Update(objUnit.Id, new UpdateUnitRequest("Matriz", true, null), CancellationToken.None);
        Assert.Equal("93", objDbContext.Units.Single().AreaCode);

        await objController.Update(objUnit.Id, new UpdateUnitRequest("Matriz", true, null, AreaCode: ""), CancellationToken.None);
        Assert.Equal("", objDbContext.Units.Single().AreaCode);
    }
}
