using Models.DataBase;
using AccessWifi.Api.Controllers;
using AccessWifi.Api.Features;
using AccessWifi.Api.Features.Admin;
using AccessWifi.Api.Features.Companies;
using AccessWifi.Api.Features.Leads;
using Models.Persistence;
using AccessWifi.Api.Infrastructure.Security;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace AccessWifi.Api.Tests;

public class AdminControllerTests
{
    private static readonly string s_sHashSenhaCorreta = BCrypt.Net.BCrypt.HashPassword("senha-forte");

    private static TokenService CreateTokenService()
    {
        return new TokenService(Options.Create(new JwtOptions
        {
            Secret = "segredo-de-teste-3f9a1c7e5b2d8046a1e9c3b7d5f20486",
        }));
    }

    private static AdminController CreateController(AppDbContext objDbContext)
    {
        return new AdminController(objDbContext, CreateTokenService());
    }

    private static Company CreateCompany(AppDbContext objDbContext, string sSlug)
    {
        Company objCompany = new Company { Name = sSlug, Slug = sSlug };
        objDbContext.Companies.Add(objCompany);
        objDbContext.SaveChanges();
        return objCompany;
    }

    private static Unit CreateUnit(AppDbContext objDbContext, Guid objCompanyId, string sSlug)
    {
        Unit objUnit = new Unit { IDCompany = objCompanyId, Name = sSlug, Slug = sSlug };
        objDbContext.Units.Add(objUnit);
        objDbContext.SaveChanges();
        return objUnit;
    }

    private static void CreateUser(AppDbContext objDbContext, string sUsername, Guid? objCompanyId)
    {
        objDbContext.Users.Add(new AdminUser
        {
            Username = sUsername,
            PasswordHash = s_sHashSenhaCorreta,
            IDCompany = objCompanyId,
        });
        objDbContext.SaveChanges();
    }

    [Fact]
    public async Task Login_CredenciaisValidas_DevolveTokenRoleEEmpresa()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Company objCompany = CreateCompany(objDbContext, "doce");
        CreateUser(objDbContext, "admin", objCompany.Id);
        AdminController objController = CreateController(objDbContext);

        ActionResult<LoginResponse> objResult = await objController.Login(
            new LoginRequest("admin", "senha-forte"), CancellationToken.None);

        OkObjectResult objOk = Assert.IsType<OkObjectResult>(objResult.Result);
        LoginResponse objResponse = Assert.IsType<LoginResponse>(objOk.Value);
        Assert.NotEmpty(objResponse.Token);
        Assert.Equal("admin", objResponse.Role);
        Assert.NotNull(objResponse.Company);
        Assert.Equal("doce", objResponse.Company.Slug);
    }

    [Fact]
    public async Task Login_SuperAdmin_DevolveRoleSuperadminSemEmpresa()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        CreateUser(objDbContext, "root", null);
        AdminController objController = CreateController(objDbContext);

        ActionResult<LoginResponse> objResult = await objController.Login(
            new LoginRequest("root", "senha-forte"), CancellationToken.None);

        LoginResponse objResponse =
            Assert.IsType<LoginResponse>(Assert.IsType<OkObjectResult>(objResult.Result).Value);
        Assert.Equal("superadmin", objResponse.Role);
        Assert.Null(objResponse.Company);
    }

    [Fact]
    public async Task Login_SenhaErrada_Retorna401()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        CreateUser(objDbContext, "admin", null);
        AdminController objController = CreateController(objDbContext);

        ActionResult<LoginResponse> objResult = await objController.Login(
            new LoginRequest("admin", "errada"), CancellationToken.None);

        Assert.IsType<UnauthorizedResult>(objResult.Result);
    }

    [Fact]
    public async Task Login_EmpresaInativa_Retorna401()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Company objCompany = CreateCompany(objDbContext, "doce");
        objCompany.Active = false;
        objDbContext.SaveChanges();
        CreateUser(objDbContext, "admin", objCompany.Id);
        AdminController objController = CreateController(objDbContext);

        ActionResult<LoginResponse> objResult = await objController.Login(
            new LoginRequest("admin", "senha-forte"), CancellationToken.None);

        Assert.IsType<UnauthorizedResult>(objResult.Result);
    }

    [Fact]
    public async Task GetLeads_AdminDeEmpresa_VeOsLeadsDeTodasAsSuasUnidades()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Company objCompanyA = CreateCompany(objDbContext, "doce");
        Company objCompanyB = CreateCompany(objDbContext, "outra");
        Unit objUnitA1 = CreateUnit(objDbContext, objCompanyA.Id, "doce-um");
        Unit objUnitA2 = CreateUnit(objDbContext, objCompanyA.Id, "doce-dois");
        Unit objUnitB = CreateUnit(objDbContext, objCompanyB.Id, "outra-um");
        objDbContext.Leads.Add(new Lead { IDUnit = objUnitA1.Id, Nome = "Da Unidade Um" });
        objDbContext.Leads.Add(new Lead { IDUnit = objUnitA2.Id, Nome = "Da Unidade Dois" });
        objDbContext.Leads.Add(new Lead { IDUnit = objUnitB.Id, Nome = "Da Outra" });
        objDbContext.SaveChanges();
        AdminController objController = CreateController(objDbContext);
        TestHelpers.SetUser(objController, objCompanyA.Id);

        ActionResult<List<LeadDto>> objResult =
            await objController.GetLeads(null, null, CancellationToken.None);

        List<LeadDto> objLeads =
            Assert.IsType<List<LeadDto>>(Assert.IsType<OkObjectResult>(objResult.Result).Value);
        Assert.Equal(2, objLeads.Count);
        Assert.DoesNotContain(objLeads, lead => lead.Nome == "Da Outra");
    }

    [Fact]
    public async Task GetLeads_ComFiltroDeUnidade_SoVeOsLeadsDaqueleSlug()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Company objCompany = CreateCompany(objDbContext, "doce");
        Unit objUnit1 = CreateUnit(objDbContext, objCompany.Id, "doce-um");
        Unit objUnit2 = CreateUnit(objDbContext, objCompany.Id, "doce-dois");
        objDbContext.Leads.Add(new Lead { IDUnit = objUnit1.Id, Nome = "Da Unidade Um" });
        objDbContext.Leads.Add(new Lead { IDUnit = objUnit2.Id, Nome = "Da Unidade Dois" });
        objDbContext.SaveChanges();
        AdminController objController = CreateController(objDbContext);
        TestHelpers.SetUser(objController, objCompany.Id);

        ActionResult<List<LeadDto>> objResult =
            await objController.GetLeads(null, "doce-dois", CancellationToken.None);

        List<LeadDto> objLeads =
            Assert.IsType<List<LeadDto>>(Assert.IsType<OkObjectResult>(objResult.Result).Value);
        LeadDto objLead = Assert.Single(objLeads);
        Assert.Equal("Da Unidade Dois", objLead.Nome);
        Assert.Equal("doce-dois", objLead.UnitSlug);
    }

    [Fact]
    public async Task GetLeads_SuperAdminSemSlug_Retorna400()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        AdminController objController = CreateController(objDbContext);
        TestHelpers.SetUser(objController, null);

        ActionResult<List<LeadDto>> objResult =
            await objController.GetLeads(null, null, CancellationToken.None);

        BadRequestObjectResult objBadRequest = Assert.IsType<BadRequestObjectResult>(objResult.Result);
        Assert.IsType<ErrorResponse>(objBadRequest.Value);
    }

    [Fact]
    public async Task GetLeads_SuperAdminComSlug_VeOsLeadsDaEmpresaIndicada()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Company objCompanyA = CreateCompany(objDbContext, "doce");
        Company objCompanyB = CreateCompany(objDbContext, "outra");
        Unit objUnitA = CreateUnit(objDbContext, objCompanyA.Id, "doce-um");
        Unit objUnitB = CreateUnit(objDbContext, objCompanyB.Id, "outra-um");
        objDbContext.Leads.Add(new Lead { IDUnit = objUnitA.Id, Nome = "Da Dôce" });
        objDbContext.Leads.Add(new Lead { IDUnit = objUnitB.Id, Nome = "Da Outra" });
        objDbContext.SaveChanges();
        AdminController objController = CreateController(objDbContext);
        TestHelpers.SetUser(objController, null);

        ActionResult<List<LeadDto>> objResult =
            await objController.GetLeads("outra", null, CancellationToken.None);

        List<LeadDto> objLeads =
            Assert.IsType<List<LeadDto>>(Assert.IsType<OkObjectResult>(objResult.Result).Value);
        LeadDto objLead = Assert.Single(objLeads);
        Assert.Equal("Da Outra", objLead.Nome);
    }
}
