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
    public void Usuario_Valido(string sUsuario)
    {
        Assert.Null(UserRules.ValidateUsername(UserRules.NormalizeUsername(sUsuario)));
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
    public void Usuario_Invalido(string sUsuario)
    {
        Assert.NotNull(UserRules.ValidateUsername(UserRules.NormalizeUsername(sUsuario)));
    }

    [Fact]
    public void Usuario_GuardadoEmMinusculas_SemEspacosNasPontas()
    {
        Assert.Equal("gerente.loja", UserRules.NormalizeUsername("  Gerente.Loja "));
    }

    [Theory]
    [InlineData("1234567", "mínimo")]
    [InlineData("        ", "só espaços")]
    [InlineData("senha-com-73-bytes-xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx", "muito longa")]
    [InlineData("çççççççççççççççççççççççççççççççççççççç", "muito longa")] // 37 caracteres, 74 bytes
    public void Senha_Invalida(string sSenha, string sTrecho)
    {
        Assert.Contains(sTrecho, UserRules.ValidatePassword(sSenha));
    }

    [Theory]
    [InlineData("senha-forte")]
    [InlineData("12345678")]
    [InlineData("com espaço no meio")]
    public void Senha_Valida(string sSenha)
    {
        Assert.Null(UserRules.ValidatePassword(sSenha));
    }

    [Fact]
    public async Task Criar_UsuarioComEspaco_400_ENaoGrava()
    {
        using AppDbContext objDb = TestHelpers.CreateDbContext();
        UsersController objController = new UsersController(objDb);
        TestHelpers.SetUser(objController, null, "root");

        ActionResult<UserDto> objResult = await objController.Create(
            new CreateUserRequest("gerente itaituba", "senha-forte", null), CancellationToken.None);

        ErrorResponse objErro = Assert.IsType<ErrorResponse>(Assert.IsType<BadRequestObjectResult>(objResult.Result).Value);
        Assert.Contains("sem espaços", objErro.Error);
        Assert.Empty(objDb.Users);
    }

    [Fact]
    public async Task Criar_UsuarioComMaiusculaEEspacoNasPontas_GravaNormalizado()
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
    public async Task Login_IgnoraMaiusculaEEspacoNasPontas(string sDigitado)
    {
        using AppDbContext objDb = TestHelpers.CreateDbContext();
        objDb.Users.Add(new AdminUser { Username = "gerente", PasswordHash = BCrypt.Net.BCrypt.HashPassword("senha-forte") });
        objDb.SaveChanges();
        AdminController objController = new AdminController(objDb, new TokenService(Options.Create(new JwtOptions
        {
            Secret = "segredo-de-teste-3f9a1c7e5b2d8046a1e9c3b7d5f20486",
        })));

        ActionResult<LoginResponse> objResult = await objController.Login(
            new LoginRequest(sDigitado, "senha-forte"), CancellationToken.None);

        Assert.IsType<OkObjectResult>(objResult.Result);
    }

    [Fact]
    public async Task Login_AdminDaConfiguracaoComMaiuscula_AindaEntra()
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
