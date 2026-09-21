using Models.DataBase;
using AccessWifi.Api.Controllers;
using AccessWifi.Api.Features;
using AccessWifi.Api.Features.Units;
using Models.Persistence;
using AccessWifi.Api.Infrastructure.Unifi;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;

namespace AccessWifi.Api.Tests;

public class UnitsControllerTests
{
    private static Company CreateCompany(AppDbContext objDbContext, string sSlug = "doce")
    {
        Company objCompany = new Company { Name = "Dôce Cafeteria", Slug = sSlug };
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
        public UnifiException? ObjErro { get; set; }
        public string? SSiteIdDescoberto { get; set; }

        public Task AuthorizeGuestAsync(
            CompanyUnifi objConfig, string sMac, int iAccessMinutes,
            CancellationToken objCancellationToken = default) => Task.CompletedTask;

        public Task<string> TestConnectionAsync(
            CompanyUnifi objConfig, CancellationToken objCancellationToken = default)
        {
            if (ObjErro is not null)
            {
                throw ObjErro;
            }
            if (SSiteIdDescoberto is not null)
            {
                objConfig.SiteId = SSiteIdDescoberto;
            }
            return Task.FromResult("Console respondeu pela nuvem.");
        }

        public Task PrepareAsync(
            CompanyUnifi objConfig, string sMac, CancellationToken objCancellationToken = default) =>
            Task.CompletedTask;
    }

    private static CreateUnitRequest CreateRequest(Guid objCompanyId, string sSlug = "doce-matriz")
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
    public async Task Create_ComDadosValidos_CriaESemExporASenhaUnifi()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Company objCompany = CreateCompany(objDbContext);
        UnitsController objController = CreateController(objDbContext);

        ActionResult<UnitDto> objResult =
            await objController.Create(CreateRequest(objCompany.Id), CancellationToken.None);

        OkObjectResult objOk = Assert.IsType<OkObjectResult>(objResult.Result);
        UnitDto objUnit = Assert.IsType<UnitDto>(objOk.Value);
        Assert.Equal("doce-matriz", objUnit.Slug);
        Assert.Equal("https://192.168.1.1", objUnit.Unifi.Host);

        // A senha fica só na entidade (o DTO não tem a propriedade) e é guardada CIFRADA.
        string sStored = objDbContext.Units.Single().Unifi.Password;
        Assert.NotEqual("unifi-pass", sStored);
        Assert.Equal("unifi-pass", TestHelpers.CreateEncryptor().Decrypt(sStored));
        Assert.DoesNotContain(
            typeof(UnitUnifiDto).GetProperties(), objProperty => objProperty.Name == "Password");
    }

    [Fact]
    public async Task Create_EmpresaInexistente_Retorna400()
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
    public async Task Create_SlugDuplicadoGlobalmente_Retorna400()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Company objCompanyA = CreateCompany(objDbContext, "doce");
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
    public async Task Create_SlugInvalido_Retorna400()
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
    public async Task Update_SenhaUnifiNula_MantemASenhaAtual()
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
    public async Task GetAll_SuperAdminComFiltroDeEmpresa_SoTrazAsUnidadesDaEmpresa()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Company objCompanyA = CreateCompany(objDbContext, "doce");
        Company objCompanyB = CreateCompany(objDbContext, "outra");
        UnitsController objController = CreateController(objDbContext);
        await objController.Create(CreateRequest(objCompanyA.Id, "doce-um"), CancellationToken.None);
        await objController.Create(CreateRequest(objCompanyB.Id, "outra-um"), CancellationToken.None);
        TestHelpers.SetUser(objController, null); // super admin

        ActionResult<List<UnitDto>> objResult =
            await objController.GetAll(objCompanyA.Id, CancellationToken.None);

        List<UnitDto> objUnits =
            Assert.IsType<List<UnitDto>>(Assert.IsType<OkObjectResult>(objResult.Result).Value);
        UnitDto objUnit = Assert.Single(objUnits);
        Assert.Equal("doce-um", objUnit.Slug);
    }

    [Fact]
    public async Task GetAll_AdminDeEmpresa_SoVeAsUnidadesDaPropriaEmpresaIgnorandoFiltro()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Company objCompanyA = CreateCompany(objDbContext, "doce");
        Company objCompanyB = CreateCompany(objDbContext, "outra");
        UnitsController objController = CreateController(objDbContext);
        await objController.Create(CreateRequest(objCompanyA.Id, "doce-um"), CancellationToken.None);
        await objController.Create(CreateRequest(objCompanyB.Id, "outra-um"), CancellationToken.None);
        TestHelpers.SetUser(objController, objCompanyA.Id); // admin da empresa A

        // Mesmo passando o id da empresa B no filtro, só enxerga a própria empresa.
        ActionResult<List<UnitDto>> objResult =
            await objController.GetAll(objCompanyB.Id, CancellationToken.None);

        List<UnitDto> objUnits =
            Assert.IsType<List<UnitDto>>(Assert.IsType<OkObjectResult>(objResult.Result).Value);
        UnitDto objUnit = Assert.Single(objUnits);
        Assert.Equal("doce-um", objUnit.Slug);
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
    public async Task Create_ModoNuvem_GuardaAChaveCifradaESemExporNaLeitura()
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
    public async Task Create_SemInformarOModo_ContinuaLocal()
    {
        // As unidades que já existem (e qualquer front antigo) não podem virar nuvem por acidente.
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Company objCompany = CreateCompany(objDbContext);
        UnitsController objController = CreateController(objDbContext);

        await objController.Create(CreateRequest(objCompany.Id), CancellationToken.None);

        Assert.Equal(UnifiMode.Local, objDbContext.Units.Single().Unifi.Mode);
    }

    [Fact]
    public async Task Update_ChaveDeApiNula_MantemAAtual()
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
    public async Task Update_TrocandoDeConsole_DescartaOSiteGuardado()
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
    public async Task TestUnifi_Sucesso_GuardaOSiteDescobertoNaUnidade()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Company objCompany = CreateCompany(objDbContext);
        FakeUnifiClient objFake = new FakeUnifiClient
        {
            SSiteIdDescoberto = "88f7af54-98f8-306a-a1c7-c9349722b1f6",
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
    public async Task TestUnifi_ConfiguracaoErrada_Retorna200ComOMotivo()
    {
        // Configuração errada é resultado do teste, não erro da requisição: o painel mostra a causa.
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Company objCompany = CreateCompany(objDbContext);
        FakeUnifiClient objFake = new FakeUnifiClient
        {
            ObjErro = new UnifiException("Chave de API da nuvem UniFi inválida ou revogada."),
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
    public async Task TestUnifi_UnidadeInexistente_Retorna404()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        UnitsController objController = CreateController(objDbContext);

        ActionResult<UnifiTestResponse> objResult =
            await objController.TestUnifi(Guid.NewGuid(), CancellationToken.None);

        NotFoundObjectResult objNotFound = Assert.IsType<NotFoundObjectResult>(objResult.Result);
        Assert.Equal("Unidade não encontrada.", Assert.IsType<ErrorResponse>(objNotFound.Value).Error);
    }
}
