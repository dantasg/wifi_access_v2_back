using AccessWifi.Api.Controllers;
using AccessWifi.Api.Features;
using AccessWifi.Api.Features.Admin;
using Microsoft.AspNetCore.Mvc;
using Models.DataBase;
using Models.Persistence;

namespace AccessWifi.Api.Tests;

public class UsersControllerTests
{
    private static AdminUser CreateUser(
        AppDbContext objDbContext, string sUsername, Guid? objCompanyId = null, bool bActive = true)
    {
        AdminUser objUser = new AdminUser
        {
            Username = sUsername,
            PasswordHash = "hash",
            IDCompany = objCompanyId,
            Active = bActive,
        };
        objDbContext.Users.Add(objUser);
        objDbContext.SaveChanges();
        return objUser;
    }

    private static Company CreateCompany(AppDbContext objDbContext)
    {
        Company objCompany = new Company { Name = "Loja Exemplo", Slug = "exemplo" };
        objDbContext.Companies.Add(objCompany);
        objDbContext.SaveChanges();
        return objCompany;
    }

    private static UsersController CreateController(AppDbContext objDbContext, string sLoggedUsername = "root")
    {
        UsersController objController = new UsersController(objDbContext);
        TestHelpers.SetUser(objController, null, sLoggedUsername);
        return objController;
    }

    [Fact]
    public async Task Update_DisablesCompanyAdmin_AndReturnsActiveFalse()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        CreateUser(objDbContext, "root");
        Company objCompany = CreateCompany(objDbContext);
        AdminUser objAdmin = CreateUser(objDbContext, "gerente", objCompany.Id);
        UsersController objController = CreateController(objDbContext);

        ActionResult<UserDto> objResult =
            await objController.Update(objAdmin.Id, new UpdateUserRequest(false), CancellationToken.None);

        UserDto objUser = Assert.IsType<UserDto>(Assert.IsType<OkObjectResult>(objResult.Result).Value);
        Assert.False(objUser.Active);
        Assert.Equal("Loja Exemplo", objUser.CompanyName);
        Assert.False(objDbContext.Users.Single(user => user.Id == objAdmin.Id).Active);
    }

    [Fact]
    public async Task Update_Disabling_RevokesUserOpenSessions()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        CreateUser(objDbContext, "root");
        Company objCompany = CreateCompany(objDbContext);
        AdminUser objAdmin = CreateUser(objDbContext, "gerente", objCompany.Id);
        AdminUser objOther = CreateUser(objDbContext, "outro", objCompany.Id);
        objDbContext.RefreshTokens.AddRange(
            new RefreshToken { IDUser = objAdmin.Id, TokenHash = "a1", ExpiresAt = DateTime.UtcNow.AddDays(1) },
            new RefreshToken { IDUser = objAdmin.Id, TokenHash = "a2", ExpiresAt = DateTime.UtcNow.AddDays(1) },
            new RefreshToken { IDUser = objOther.Id, TokenHash = "b1", ExpiresAt = DateTime.UtcNow.AddDays(1) });
        objDbContext.SaveChanges();
        UsersController objController = CreateController(objDbContext);

        await objController.Update(objAdmin.Id, new UpdateUserRequest(false), CancellationToken.None);

        Assert.All(
            objDbContext.RefreshTokens.Where(token => token.IDUser == objAdmin.Id),
            objToken => Assert.NotNull(objToken.RevokedAt));
        // Sessões de outros usuários não são afetadas.
        Assert.Null(objDbContext.RefreshTokens.Single(token => token.IDUser == objOther.Id).RevokedAt);
    }

    [Fact]
    public async Task Update_Reactivate_BecomesActiveAgain()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        CreateUser(objDbContext, "root");
        AdminUser objAdmin = CreateUser(objDbContext, "gerente", CreateCompany(objDbContext).Id, bActive: false);
        UsersController objController = CreateController(objDbContext);

        ActionResult<UserDto> objResult =
            await objController.Update(objAdmin.Id, new UpdateUserRequest(true), CancellationToken.None);

        Assert.IsType<OkObjectResult>(objResult.Result);
        Assert.True(objDbContext.Users.Single(user => user.Id == objAdmin.Id).Active);
    }

    [Fact]
    public async Task Update_UnknownUser_Returns404()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        UsersController objController = CreateController(objDbContext);

        ActionResult<UserDto> objResult =
            await objController.Update(Guid.NewGuid(), new UpdateUserRequest(false), CancellationToken.None);

        NotFoundObjectResult objNotFound = Assert.IsType<NotFoundObjectResult>(objResult.Result);
        Assert.Equal("Usuário não encontrado.", Assert.IsType<ErrorResponse>(objNotFound.Value).Error);
    }

    [Fact]
    public async Task Update_DisablingOwnUser_Returns400()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        AdminUser objRoot = CreateUser(objDbContext, "root");
        CreateUser(objDbContext, "root2"); // existe outro super admin: o bloqueio é por ser ele mesmo
        UsersController objController = CreateController(objDbContext, "root");

        ActionResult<UserDto> objResult =
            await objController.Update(objRoot.Id, new UpdateUserRequest(false), CancellationToken.None);

        BadRequestObjectResult objBadRequest = Assert.IsType<BadRequestObjectResult>(objResult.Result);
        Assert.Equal(
            "Você não pode desativar o próprio usuário.",
            Assert.IsType<ErrorResponse>(objBadRequest.Value).Error);
        Assert.True(objDbContext.Users.Single(user => user.Id == objRoot.Id).Active);
    }

    [Fact]
    public async Task Update_DisablingLastActiveSuperAdmin_Returns400()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        AdminUser objRoot = CreateUser(objDbContext, "root");
        CreateUser(objDbContext, "antigo", bActive: false); // super admin inativo não conta
        UsersController objController = CreateController(objDbContext, "outro-logado");

        ActionResult<UserDto> objResult =
            await objController.Update(objRoot.Id, new UpdateUserRequest(false), CancellationToken.None);

        BadRequestObjectResult objBadRequest = Assert.IsType<BadRequestObjectResult>(objResult.Result);
        Assert.Equal(
            "Não é possível desativar o último super admin ativo.",
            Assert.IsType<ErrorResponse>(objBadRequest.Value).Error);
    }

    [Fact]
    public async Task Update_DisablingSuperAdminWithAnotherActive_Allows()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        CreateUser(objDbContext, "root");
        AdminUser objRoot2 = CreateUser(objDbContext, "root2");
        UsersController objController = CreateController(objDbContext, "root");

        ActionResult<UserDto> objResult =
            await objController.Update(objRoot2.Id, new UpdateUserRequest(false), CancellationToken.None);

        Assert.IsType<OkObjectResult>(objResult.Result);
        Assert.False(objDbContext.Users.Single(user => user.Id == objRoot2.Id).Active);
    }

    [Fact]
    public async Task GetAll_ReturnsActiveField()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        CreateUser(objDbContext, "root");
        CreateUser(objDbContext, "inativo", bActive: false);
        UsersController objController = CreateController(objDbContext);

        ActionResult<List<UserDto>> objResult = await objController.GetAll(null, CancellationToken.None);

        List<UserDto> objUsers =
            Assert.IsType<List<UserDto>>(Assert.IsType<OkObjectResult>(objResult.Result).Value);
        Assert.True(objUsers.Single(user => user.Username == "root").Active);
        Assert.False(objUsers.Single(user => user.Username == "inativo").Active);
    }
}
