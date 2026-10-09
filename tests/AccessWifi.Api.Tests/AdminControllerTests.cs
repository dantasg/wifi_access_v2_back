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
    private static readonly string s_sCorrectPasswordHash = BCrypt.Net.BCrypt.HashPassword("senha-forte");

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
            PasswordHash = s_sCorrectPasswordHash,
            IDCompany = objCompanyId,
        });
        objDbContext.SaveChanges();
    }

    [Fact]
    public async Task Login_ValidCredentials_ReturnsTokenRoleAndCompany()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Company objCompany = CreateCompany(objDbContext, "exemplo");
        CreateUser(objDbContext, "admin", objCompany.Id);
        AdminController objController = CreateController(objDbContext);

        ActionResult<LoginResponse> objResult = await objController.Login(
            new LoginRequest("admin", "senha-forte"), CancellationToken.None);

        OkObjectResult objOk = Assert.IsType<OkObjectResult>(objResult.Result);
        LoginResponse objResponse = Assert.IsType<LoginResponse>(objOk.Value);
        Assert.NotEmpty(objResponse.Token);
        Assert.Equal("admin", objResponse.Role);
        Assert.NotNull(objResponse.Company);
        Assert.Equal("exemplo", objResponse.Company.Slug);
    }

    [Fact]
    public async Task Login_SuperAdmin_ReturnsSuperadminRoleWithoutCompany()
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
    public async Task Login_WrongPassword_Returns401()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        CreateUser(objDbContext, "admin", null);
        AdminController objController = CreateController(objDbContext);

        ActionResult<LoginResponse> objResult = await objController.Login(
            new LoginRequest("admin", "errada"), CancellationToken.None);

        Assert.IsType<UnauthorizedResult>(objResult.Result);
    }

    [Fact]
    public async Task Login_InactiveCompany_Returns401()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Company objCompany = CreateCompany(objDbContext, "exemplo");
        objCompany.Active = false;
        objDbContext.SaveChanges();
        CreateUser(objDbContext, "admin", objCompany.Id);
        AdminController objController = CreateController(objDbContext);

        ActionResult<LoginResponse> objResult = await objController.Login(
            new LoginRequest("admin", "senha-forte"), CancellationToken.None);

        Assert.IsType<UnauthorizedResult>(objResult.Result);
    }

    [Fact]
    public async Task Login_InactiveUser_Returns401EvenWithRightPassword()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        CreateUser(objDbContext, "admin", CreateCompany(objDbContext, "exemplo").Id);
        objDbContext.Users.Single().Active = false;
        objDbContext.SaveChanges();
        AdminController objController = CreateController(objDbContext);

        ActionResult<LoginResponse> objResult = await objController.Login(
            new LoginRequest("admin", "senha-forte"), CancellationToken.None);

        Assert.IsType<UnauthorizedResult>(objResult.Result);
        Assert.Empty(objDbContext.RefreshTokens);
    }

    [Fact]
    public async Task Refresh_UserDisabledAfterLogin_Returns401()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        CreateUser(objDbContext, "admin", CreateCompany(objDbContext, "exemplo").Id);
        AdminController objController = CreateController(objDbContext);
        LoginResponse objLogin = Assert.IsType<LoginResponse>(Assert.IsType<OkObjectResult>(
            (await objController.Login(new LoginRequest("admin", "senha-forte"), CancellationToken.None)).Result).Value);
        // Desativado direto no banco, sem revogar o token: o refresh deve barrar pelo Active.
        objDbContext.Users.Single().Active = false;
        objDbContext.SaveChanges();

        ActionResult<LoginResponse> objResult =
            await objController.Refresh(new RefreshRequest(objLogin.RefreshToken), CancellationToken.None);

        Assert.IsType<UnauthorizedResult>(objResult.Result);
    }

    [Fact]
    public async Task Login_ReturnsRefreshTokenAndPersistsIt()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        CreateUser(objDbContext, "root", null);
        AdminController objController = CreateController(objDbContext);

        ActionResult<LoginResponse> objResult = await objController.Login(
            new LoginRequest("root", "senha-forte"), CancellationToken.None);

        LoginResponse objResponse =
            Assert.IsType<LoginResponse>(Assert.IsType<OkObjectResult>(objResult.Result).Value);
        Assert.NotEmpty(objResponse.RefreshToken);
        Assert.Single(objDbContext.RefreshTokens);
    }

    [Fact]
    public async Task Refresh_ValidToken_ReturnsNewPairAndRevokesOld()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        CreateUser(objDbContext, "root", null);
        AdminController objController = CreateController(objDbContext);
        LoginResponse objLogin = Assert.IsType<LoginResponse>(Assert.IsType<OkObjectResult>(
            (await objController.Login(new LoginRequest("root", "senha-forte"), CancellationToken.None)).Result).Value);

        ActionResult<LoginResponse> objRefreshResult =
            await objController.Refresh(new RefreshRequest(objLogin.RefreshToken), CancellationToken.None);

        LoginResponse objRefresh =
            Assert.IsType<LoginResponse>(Assert.IsType<OkObjectResult>(objRefreshResult.Result).Value);
        Assert.NotEmpty(objRefresh.Token);
        Assert.NotEqual(objLogin.RefreshToken, objRefresh.RefreshToken);
        // O token antigo fica revogado (rotação) e existe um novo.
        string sOldHash = TokenService.HashRefreshToken(objLogin.RefreshToken);
        Assert.NotNull(objDbContext.RefreshTokens.Single(token => token.TokenHash == sOldHash).RevokedAt);
        Assert.Equal(2, objDbContext.RefreshTokens.Count());
    }

    [Fact]
    public async Task Refresh_RevokedToken_Returns401()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        CreateUser(objDbContext, "root", null);
        AdminController objController = CreateController(objDbContext);
        LoginResponse objLogin = Assert.IsType<LoginResponse>(Assert.IsType<OkObjectResult>(
            (await objController.Login(new LoginRequest("root", "senha-forte"), CancellationToken.None)).Result).Value);
        // Primeira renovação revoga o token original.
        await objController.Refresh(new RefreshRequest(objLogin.RefreshToken), CancellationToken.None);

        // Reusar o token original (já revogado) deve falhar.
        ActionResult<LoginResponse> objResult =
            await objController.Refresh(new RefreshRequest(objLogin.RefreshToken), CancellationToken.None);

        Assert.IsType<UnauthorizedResult>(objResult.Result);
    }

    [Fact]
    public async Task Refresh_UnknownToken_Returns401()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        AdminController objController = CreateController(objDbContext);

        ActionResult<LoginResponse> objResult =
            await objController.Refresh(new RefreshRequest("token-que-nao-existe"), CancellationToken.None);

        Assert.IsType<UnauthorizedResult>(objResult.Result);
    }

    [Fact]
    public async Task Logout_RevokesRefreshToken()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        CreateUser(objDbContext, "root", null);
        AdminController objController = CreateController(objDbContext);
        LoginResponse objLogin = Assert.IsType<LoginResponse>(Assert.IsType<OkObjectResult>(
            (await objController.Login(new LoginRequest("root", "senha-forte"), CancellationToken.None)).Result).Value);

        IActionResult objLogout =
            await objController.Logout(new RefreshRequest(objLogin.RefreshToken), CancellationToken.None);
        Assert.IsType<NoContentResult>(objLogout);

        // Após o logout, o refresh não vale mais.
        ActionResult<LoginResponse> objRefresh =
            await objController.Refresh(new RefreshRequest(objLogin.RefreshToken), CancellationToken.None);
        Assert.IsType<UnauthorizedResult>(objRefresh.Result);
    }

    [Fact]
    public async Task GetLeads_CompanyAdmin_SeesLeadsOfAllItsUnits()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Company objCompanyA = CreateCompany(objDbContext, "exemplo");
        Company objCompanyB = CreateCompany(objDbContext, "outra");
        Unit objUnitA1 = CreateUnit(objDbContext, objCompanyA.Id, "exemplo-um");
        Unit objUnitA2 = CreateUnit(objDbContext, objCompanyA.Id, "exemplo-dois");
        Unit objUnitB = CreateUnit(objDbContext, objCompanyB.Id, "outra-um");
        objDbContext.Leads.Add(new Lead { IDUnit = objUnitA1.Id, Name = "Da Unidade Um" });
        objDbContext.Leads.Add(new Lead { IDUnit = objUnitA2.Id, Name = "Da Unidade Dois" });
        objDbContext.Leads.Add(new Lead { IDUnit = objUnitB.Id, Name = "Da Outra" });
        objDbContext.SaveChanges();
        AdminController objController = CreateController(objDbContext);
        TestHelpers.SetCompanyUser(objController, objDbContext, objCompanyA.Id);

        ActionResult<List<LeadDto>> objResult =
            await objController.GetLeads(null, null, CancellationToken.None);

        List<LeadDto> objLeads =
            Assert.IsType<List<LeadDto>>(Assert.IsType<OkObjectResult>(objResult.Result).Value);
        Assert.Equal(2, objLeads.Count);
        Assert.DoesNotContain(objLeads, lead => lead.Name == "Da Outra");
    }

    [Fact]
    public async Task GetLeads_WithUnitFilter_SeesOnlyThatSlugLeads()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Company objCompany = CreateCompany(objDbContext, "exemplo");
        Unit objUnit1 = CreateUnit(objDbContext, objCompany.Id, "exemplo-um");
        Unit objUnit2 = CreateUnit(objDbContext, objCompany.Id, "exemplo-dois");
        objDbContext.Leads.Add(new Lead { IDUnit = objUnit1.Id, Name = "Da Unidade Um" });
        objDbContext.Leads.Add(new Lead { IDUnit = objUnit2.Id, Name = "Da Unidade Dois" });
        objDbContext.SaveChanges();
        AdminController objController = CreateController(objDbContext);
        TestHelpers.SetCompanyUser(objController, objDbContext, objCompany.Id);

        ActionResult<List<LeadDto>> objResult =
            await objController.GetLeads(null, "exemplo-dois", CancellationToken.None);

        List<LeadDto> objLeads =
            Assert.IsType<List<LeadDto>>(Assert.IsType<OkObjectResult>(objResult.Result).Value);
        LeadDto objLead = Assert.Single(objLeads);
        Assert.Equal("Da Unidade Dois", objLead.Name);
        Assert.Equal("exemplo-dois", objLead.UnitSlug);
    }

    [Fact]
    public async Task GetLeads_ReturnsFirstSignupSeparateFromLastAccess()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Company objCompany = CreateCompany(objDbContext, "exemplo");
        Unit objUnit = CreateUnit(objDbContext, objCompany.Id, "exemplo-um");
        DateTime dtFirst = new DateTime(2026, 9, 21, 12, 0, 0, DateTimeKind.Utc);
        DateTime dtLast = new DateTime(2026, 9, 29, 11, 26, 0, DateTimeKind.Utc);
        objDbContext.Leads.Add(new Lead { IDUnit = objUnit.Id, Name = "Voltou", CreatedAt = dtFirst, Timestamp = dtLast });
        objDbContext.SaveChanges();
        AdminController objController = CreateController(objDbContext);
        TestHelpers.SetCompanyUser(objController, objDbContext, objCompany.Id);

        ActionResult<List<LeadDto>> objResult = await objController.GetLeads(null, null, CancellationToken.None);

        LeadDto objLead = Assert.Single(
            Assert.IsType<List<LeadDto>>(Assert.IsType<OkObjectResult>(objResult.Result).Value));
        Assert.Equal(dtFirst, objLead.CreatedAt);
        Assert.Equal(dtLast, objLead.Timestamp);
    }

    [Fact]
    public async Task GetLeads_SuperAdminWithoutSlug_Returns400()
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
    public async Task GetLeads_SuperAdminWithSlug_SeesGivenCompanyLeads()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Company objCompanyA = CreateCompany(objDbContext, "exemplo");
        Company objCompanyB = CreateCompany(objDbContext, "outra");
        Unit objUnitA = CreateUnit(objDbContext, objCompanyA.Id, "exemplo-um");
        Unit objUnitB = CreateUnit(objDbContext, objCompanyB.Id, "outra-um");
        objDbContext.Leads.Add(new Lead { IDUnit = objUnitA.Id, Name = "Da Exemplo" });
        objDbContext.Leads.Add(new Lead { IDUnit = objUnitB.Id, Name = "Da Outra" });
        objDbContext.SaveChanges();
        AdminController objController = CreateController(objDbContext);
        TestHelpers.SetUser(objController, null);

        ActionResult<List<LeadDto>> objResult =
            await objController.GetLeads("outra", null, CancellationToken.None);

        List<LeadDto> objLeads =
            Assert.IsType<List<LeadDto>>(Assert.IsType<OkObjectResult>(objResult.Result).Value);
        LeadDto objLead = Assert.Single(objLeads);
        Assert.Equal("Da Outra", objLead.Name);
    }
}
