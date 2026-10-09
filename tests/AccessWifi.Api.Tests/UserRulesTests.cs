using AccessWifi.Api.Controllers;
using AccessWifi.Api.Features;
using AccessWifi.Api.Features.Admin;
using AccessWifi.Api.Infrastructure.Security;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Models.DataBase;
using Models.Persistence;

namespace AccessWifi.Api.Tests;

/// <summary>Usuário e senha do painel: o que o cadastro aceita e como o login acha o usuário.</summary>
public class UserRulesTests
{
    [Theory]
    [InlineData("genival")]
    [InlineData("gerente.itaituba")]
    [InlineData("loja-04")]
    [InlineData("ana_souza")]
    [InlineData("abc")]
    public void Username_Valid(string sUsername)
    {
        Assert.Null(UserRules.ValidateUsername(UserRules.NormalizeUsername(sUsername)));
    }

    [Theory]
    [InlineData("gerente itaituba")]   // espaço no meio
    [InlineData("gerente\titaituba")]  // tab
    [InlineData("gerente loja")]  // espaço invisível (copiado de outro lugar)
    [InlineData("joão")]               // acento
    [InlineData("ana@loja")]
    [InlineData("ab")]                 // curto demais
    [InlineData(".gerente")]           // começa com ponto
    [InlineData("gerente-")]           // termina com hífen
    [InlineData("")]
    public void Username_Invalid(string sUsername)
    {
        Assert.NotNull(UserRules.ValidateUsername(UserRules.NormalizeUsername(sUsername)));
    }

    [Fact]
    public void Username_StoredLowercase_WithoutOuterSpaces()
    {
        Assert.Equal("gerente.loja", UserRules.NormalizeUsername("  Gerente.Loja "));
    }

    [Theory]
    [InlineData("1234567", "mínimo")]
    [InlineData("        ", "só espaços")]
    [InlineData("senha-com-73-bytes-xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx", "muito longa")]
    [InlineData("çççççççççççççççççççççççççççççççççççççç", "muito longa")] // 37 caracteres, 74 bytes
    public void Password_Invalid(string sPassword, string sSnippet)
    {
        Assert.Contains(sSnippet, UserRules.ValidatePassword(sPassword));
    }

    [Theory]
    [InlineData("senha-forte")]
    [InlineData("12345678")]
    [InlineData("com espaço no meio")]
    public void Password_Valid(string sPassword)
    {
        Assert.Null(UserRules.ValidatePassword(sPassword));
    }

    [Fact]
    public async Task Create_UsernameWithSpace_400_AndDoesNotSave()
    {
        using AppDbContext objDb = TestHelpers.CreateDbContext();
        UsersController objController = new UsersController(objDb);
        TestHelpers.SetUser(objController, null, "root");

        ActionResult<UserDto> objResult = await objController.Create(
            new CreateUserRequest("gerente itaituba", "senha-forte", null), CancellationToken.None);

        ErrorResponse objError = Assert.IsType<ErrorResponse>(Assert.IsType<BadRequestObjectResult>(objResult.Result).Value);
        Assert.Contains("sem espaços", objError.Error);
        Assert.Empty(objDb.Users);
    }

    [Fact]
    public async Task Create_UsernameWithUppercaseAndOuterSpaces_SavesNormalized()
    {
        using AppDbContext objDb = TestHelpers.CreateDbContext();
        UsersController objController = new UsersController(objDb);
        TestHelpers.SetUser(objController, null, "root");

        ActionResult<UserDto> objResult = await objController.Create(
            new CreateUserRequest("  Gerente.Loja ", "senha-forte", null), CancellationToken.None);

        Assert.IsType<OkObjectResult>(objResult.Result);
        Assert.Equal("gerente.loja", Assert.Single(objDb.Users).Username);
    }

    [Theory]
    [InlineData("gerente")]
    [InlineData("Gerente")]
    [InlineData("  gerente ")]
    public async Task Login_IgnoresUppercaseAndOuterSpaces(string sTyped)
    {
        using AppDbContext objDb = TestHelpers.CreateDbContext();
        objDb.Users.Add(new AdminUser { Username = "gerente", PasswordHash = BCrypt.Net.BCrypt.HashPassword("senha-forte") });
        objDb.SaveChanges();
        AdminController objController = new AdminController(objDb, new TokenService(Options.Create(new JwtOptions
        {
            Secret = "segredo-de-teste-3f9a1c7e5b2d8046a1e9c3b7d5f20486",
        })));

        ActionResult<LoginResponse> objResult = await objController.Login(
            new LoginRequest(sTyped, "senha-forte"), CancellationToken.None);

        Assert.IsType<OkObjectResult>(objResult.Result);
    }

    [Fact]
    public async Task Login_ConfigAdminWithUppercase_StillLogsIn()
    {
        // O super admin inicial vem da configuração e pode ter sido escrito com maiúscula.
        using AppDbContext objDb = TestHelpers.CreateDbContext();
        objDb.Users.Add(new AdminUser { Username = "Root", PasswordHash = BCrypt.Net.BCrypt.HashPassword("senha-forte") });
        objDb.SaveChanges();
        AdminController objController = new AdminController(objDb, new TokenService(Options.Create(new JwtOptions
        {
            Secret = "segredo-de-teste-3f9a1c7e5b2d8046a1e9c3b7d5f20486",
        })));

        ActionResult<LoginResponse> objResult = await objController.Login(
            new LoginRequest("Root", "senha-forte"), CancellationToken.None);

        Assert.IsType<OkObjectResult>(objResult.Result);
    }
}
