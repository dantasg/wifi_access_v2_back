using Models.DataBase;
using AccessWifi.Api.Controllers;
using AccessWifi.Api.Features;
using AccessWifi.Api.Features.Units;
using Models.Persistence;
using Microsoft.AspNetCore.Mvc;

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
        UnitsController objController = new UnitsController(objDbContext);

        ActionResult<UnitDto> objResult =
            await objController.Create(CreateRequest(objCompany.Id), CancellationToken.None);

        OkObjectResult objOk = Assert.IsType<OkObjectResult>(objResult.Result);
        UnitDto objUnit = Assert.IsType<UnitDto>(objOk.Value);
        Assert.Equal("doce-matriz", objUnit.Slug);
        Assert.Equal("https://192.168.1.1", objUnit.Unifi.Host);

        // A senha fica só na entidade — o DTO não tem a propriedade.
        Assert.Equal("unifi-pass", objDbContext.Units.Single().Unifi.Password);
        Assert.DoesNotContain(
            typeof(UnitUnifiDto).GetProperties(), objProperty => objProperty.Name == "Password");
    }

    [Fact]
    public async Task Create_EmpresaInexistente_Retorna400()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        UnitsController objController = new UnitsController(objDbContext);

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
        UnitsController objController = new UnitsController(objDbContext);
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
        UnitsController objController = new UnitsController(objDbContext);

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
        UnitsController objController = new UnitsController(objDbContext);
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
        Assert.Equal("unifi-pass", objUnit.Unifi.Password);
    }

    [Fact]
    public async Task GetAll_SuperAdminComFiltroDeEmpresa_SoTrazAsUnidadesDaEmpresa()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Company objCompanyA = CreateCompany(objDbContext, "doce");
        Company objCompanyB = CreateCompany(objDbContext, "outra");
        UnitsController objController = new UnitsController(objDbContext);
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
        UnitsController objController = new UnitsController(objDbContext);
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
}
