using AccessWifiService;
using Models.DataBase;
using Models.Persistence;

namespace AccessWifi.Api.Tests;

public class ConfigurationReaderTests
{
    private static void AddConfig(AppDbContext objDbContext, string sKey, string sValue)
    {
        objDbContext.Configurations.Add(new Configuration { IDConfiguration = sKey, Value = sValue });
        objDbContext.SaveChanges();
    }

    [Fact]
    public async Task GetSmtpAsync_LeAsChavesDaTabela()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        AddConfig(objDbContext, ConfigurationKeys.SmtpHost, "smtp.doce.com.br");
        AddConfig(objDbContext, ConfigurationKeys.SmtpPort, "465");
        AddConfig(objDbContext, ConfigurationKeys.SmtpUsername, "user@doce.com.br");
        AddConfig(objDbContext, ConfigurationKeys.SmtpPassword, "segredo");
        AddConfig(objDbContext, ConfigurationKeys.SmtpFromEmail, "no-reply@doce.com.br");
        AddConfig(objDbContext, ConfigurationKeys.SmtpUseStartTls, "false");

        SmtpOptions objSmtp = await new ConfigurationReader(objDbContext, TestHelpers.CreateEncryptor()).GetSmtpAsync();

        Assert.Equal("smtp.doce.com.br", objSmtp.Host);
        Assert.Equal(465, objSmtp.Port);
        Assert.Equal("user@doce.com.br", objSmtp.Username);
        Assert.Equal("segredo", objSmtp.Password);
        Assert.Equal("no-reply@doce.com.br", objSmtp.FromEmail);
        Assert.False(objSmtp.UseStartTls);
    }

    [Fact]
    public async Task GetSmtpAsync_SenhaCifrada_DevolveEmClaro()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        string? sSenhaCifrada = TestHelpers.CreateEncryptor().Encrypt("segredo-smtp");
        AddConfig(objDbContext, ConfigurationKeys.SmtpPassword, sSenhaCifrada!);

        SmtpOptions objSmtp =
            await new ConfigurationReader(objDbContext, TestHelpers.CreateEncryptor()).GetSmtpAsync();

        Assert.Equal("segredo-smtp", objSmtp.Password);
    }

    [Fact]
    public async Task GetSmtpAsync_SemChaves_UsaOsPadroes()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();

        SmtpOptions objSmtp = await new ConfigurationReader(objDbContext, TestHelpers.CreateEncryptor()).GetSmtpAsync();

        Assert.Equal("", objSmtp.Host);
        Assert.Equal(587, objSmtp.Port);
        Assert.True(objSmtp.UseStartTls);
        Assert.Equal("AccessWifi", objSmtp.FromName);
    }

    [Fact]
    public async Task GetValueAsync_ChaveInexistente_DevolveNull()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();

        string? sValue = await new ConfigurationReader(objDbContext, TestHelpers.CreateEncryptor()).GetValueAsync("NAO_EXISTE");

        Assert.Null(sValue);
    }
}
