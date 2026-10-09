using AccessWifi.Api.Controllers;
using AccessWifi.Api.Features;
using AccessWifi.Api.Features.SystemSettings;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Models.Email;
using Models.Persistence;
using Models.Security;

namespace AccessWifi.Api.Tests;

/// <summary>Configurações do sistema: a conta de e-mail global, com a senha só de ida.</summary>
public class SystemSettingsControllerTests
{
    private static SystemSettingsController CreateController(AppDbContext objDbContext, FakeEmailSender? objSender = null)
    {
        IEncryptor objEncryptor = TestHelpers.CreateEncryptor();
        SystemSettingsController objController = new SystemSettingsController(
            objDbContext, objEncryptor, new ConfigurationReader(objDbContext, objEncryptor),
            objSender ?? new FakeEmailSender(), NullLogger<SystemSettingsController>.Instance);
        TestHelpers.SetUser(objController, null, "root");
        return objController;
    }

    private static UpdateSystemSettingsRequest Gmail(string? sPassword) => new UpdateSystemSettingsRequest(
        new SmtpSettingsRequest("smtp.gmail.com", 587, "avisos@regional.com.br", sPassword,
            "avisos@regional.com.br", "Lojas Regional", true));

    private static T Ok<T>(ActionResult<T> objResult) =>
        Assert.IsType<T>(Assert.IsType<OkObjectResult>(objResult.Result).Value);

    private static string ErrorMessage<T>(ActionResult<T> objResult) =>
        Assert.IsType<ErrorResponse>(Assert.IsType<BadRequestObjectResult>(objResult.Result).Value).Error;

    private static string StoredValue(AppDbContext objDbContext, string sKey) =>
        objDbContext.Configurations.Single(config => config.IDConfiguration == sKey).Value;

    [Fact]
    public async Task Get_NothingConfigured_ReturnsDefaultsWithoutPassword()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();

        SmtpSettingsDto objSmtp = Ok(await CreateController(objDbContext).Get(CancellationToken.None)).Smtp;

        Assert.Equal("", objSmtp.Host);
        Assert.Equal(587, objSmtp.Port);
        Assert.False(objSmtp.HasPassword);
        Assert.True(objSmtp.UseStartTls);
    }

    [Fact]
    public async Task Update_StoresPasswordEncrypted_AndNeverReturnsIt()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        SystemSettingsController objController = CreateController(objDbContext);

        SmtpSettingsDto objSmtp = Ok(await objController.Update(Gmail("segredo123"), CancellationToken.None)).Smtp;

        Assert.True(objSmtp.HasPassword);
        Assert.Equal("smtp.gmail.com", objSmtp.Host);
        Assert.Equal("Lojas Regional", objSmtp.FromName);
        string sStored = StoredValue(objDbContext, ConfigurationKeys.SmtpPassword);
        Assert.StartsWith("enc:", sStored);
        Assert.DoesNotContain("segredo123", sStored);
        // O DTO não tem campo de senha: nem por engano ela volta para a tela.
        Assert.DoesNotContain(typeof(SmtpSettingsDto).GetProperties(), objProp => objProp.Name.Contains("Password") && objProp.PropertyType == typeof(string));
    }

    [Fact]
    public async Task Update_NullPassword_KeepsCurrent_AndGmailStripsAppPasswordSpaces()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        SystemSettingsController objController = CreateController(objDbContext);
        await objController.Update(Gmail("abcd efgh ijkl mnop"), CancellationToken.None);
        string sBefore = StoredValue(objDbContext, ConfigurationKeys.SmtpPassword);

        SmtpSettingsDto objSmtp = Ok(await objController.Update(
            Gmail(null) with { Smtp = Gmail(null).Smtp with { FromName = "Regional Avisos" } }, CancellationToken.None)).Smtp;

        Assert.True(objSmtp.HasPassword);
        Assert.Equal("Regional Avisos", objSmtp.FromName);
        Assert.Equal(sBefore, StoredValue(objDbContext, ConfigurationKeys.SmtpPassword));
        SmtpOptions objRead = await new ConfigurationReader(objDbContext, TestHelpers.CreateEncryptor()).GetSmtpAsync();
        Assert.Equal("abcdefghijklmnop", objRead.Password);
    }

    [Theory]
    [InlineData("http://smtp.gmail.com", 587, "a@b.com", "Servidor SMTP inválido")]
    [InlineData("smtp.gmail.com:587", 587, "a@b.com", "Servidor SMTP inválido")]
    [InlineData("smtp.gmail.com", 465, "a@b.com", "465")]
    [InlineData("smtp.gmail.com", 0, "a@b.com", "Porta inválida")]
    [InlineData("smtp.gmail.com", 587, "", "remetente")]
    [InlineData("smtp.gmail.com", 587, "sem-arroba", "remetente")]
    public async Task Update_InvalidData_Rejects_AndDoesNotSave(string sHost, int iPort, string sFrom, string sSnippet)
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();

        ActionResult<SystemSettingsDto> objResult = await CreateController(objDbContext).Update(
            new UpdateSystemSettingsRequest(new SmtpSettingsRequest(sHost, iPort, "u", "p", sFrom, null, true)),
            CancellationToken.None);

        Assert.Contains(sSnippet, ErrorMessage(objResult));
        Assert.Empty(objDbContext.Configurations);
    }

    [Fact]
    public async Task Update_EmptyServer_DisablesSending_WithoutRequiringSender()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();

        SmtpSettingsDto objSmtp = Ok(await CreateController(objDbContext).Update(
            new UpdateSystemSettingsRequest(new SmtpSettingsRequest("", null, null, null, null, null, null)),
            CancellationToken.None)).Smtp;

        Assert.Equal("", objSmtp.Host);
        Assert.Equal("AccessWifi", objSmtp.FromName);
    }

    [Fact]
    public async Task SendTest_Worked_OrFailedWithReason()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        FakeEmailSender objSender = new FakeEmailSender();
        SystemSettingsController objController = CreateController(objDbContext, objSender);

        SmtpTestResponse objOk = Ok(await objController.TestSmtp(new SmtpTestRequest(" gerente@regional.com.br "), CancellationToken.None));
        Assert.True(objOk.Success);
        Assert.Equal("gerente@regional.com.br", Assert.Single(objSender.Sent).To);

        objSender.FailTimes = 1;
        SmtpTestResponse objFailure = Ok(await objController.TestSmtp(new SmtpTestRequest("gerente@regional.com.br"), CancellationToken.None));
        Assert.False(objFailure.Success);
        Assert.Contains("SMTP fora do ar", objFailure.Message);

        Assert.Contains("e-mail válido", ErrorMessage(await objController.TestSmtp(new SmtpTestRequest("nao-e-email"), CancellationToken.None)));
    }
}
