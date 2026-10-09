using AccessWifi.Ops;

namespace AccessWifi.Ops.Tests;

/// <summary>
/// A versão em C# lê os mesmos arquivos que a primeira versão (Python) deixou no servidor: trocar o programa não
/// pode exigir configurar tudo de novo nem perder o histórico da conferência.
/// </summary>
public class ServerFilesTests
{
    // Cópia do /var/lib/accesswifi-ops/estado.json gravado pela versão em Python em 05/10/2026.
    private const string StateFromPython = """
        {
          "instalado_em": "2026-10-01T16:42:50.598173-03:00",
          "checagens": {
            "api": { "falhando": false },
            "portal:vps11702.panel.icontainer.online": { "falhando": false },
            "disco": { "falhando": true, "desde": "2026-10-05T10:00:00.123456-03:00", "ultimo_aviso": "2026-10-05T10:00:00.123456-03:00" }
          },
          "backup_ok_em": "2026-10-05T03:19:54.140426-03:00"
        }
        """;

    [Fact]
    public void State_SavedByPython_IsReadInFull()
    {
        OpsState objState = OpsState.Parse(StateFromPython);

        Assert.Equal("2026-10-01 16:42:50 -03:00", objState.InstalledAt!.Value.ToString("yyyy-MM-dd HH:mm:ss zzz"));
        Assert.Equal("2026-10-05 03:19:54 -03:00", objState.BackupOkAt!.Value.ToString("yyyy-MM-dd HH:mm:ss zzz"));
        Assert.False(objState.Checks["api"].Failing);
        Assert.True(objState.Checks["disco"].Failing);
        Assert.NotNull(objState.Checks["disco"].Since);
        Assert.Contains("portal:vps11702.panel.icontainer.online", objState.Checks.Keys);
    }

    [Fact]
    public void State_SavesWithSameKeyNames()
    {
        OpsState objState = OpsState.Parse(StateFromPython);

        string sJson = objState.ToJson();

        Assert.Contains("\"instalado_em\"", sJson);
        Assert.Contains("\"backup_ok_em\"", sJson);
        Assert.Contains("\"checagens\"", sJson);
        Assert.Contains("\"falhando\"", sJson);
        Assert.Contains("\"ultimo_aviso\"", sJson);
        Assert.Equal(objState.BackupOkAt, OpsState.Parse(sJson).BackupOkAt);
    }

    [Fact]
    public void State_Corrupted_StartsFromScratch()
    {
        OpsState objState = OpsState.Parse("{ isto não é json");

        Assert.Empty(objState.Checks);
        Assert.Null(objState.BackupOkAt);
    }

    [Fact]
    public void OpsEnv_ReadsPasswordWithEquals_CommentsAndWindowsLineEndings()
    {
        string sText = "# Rotinas de proteção do AccessWifi\r\n"
            + "TELEGRAM_TOKEN=123456:ABC-def\r\n"
            + "\r\n"
            + "BACKUP_SENHA=a=b==c\r\n"
            + "AVISO_EMAILS=um@exemplo.com, dois@exemplo.com\n"
            + "linha sem igual\n";

        OpsSettings objSettings = new OpsSettings(EnvFile.Parse(sText));

        Assert.Equal("123456:ABC-def", objSettings.Get(OpsSettings.KeyTelegramToken));
        Assert.Equal("a=b==c", objSettings.Get(OpsSettings.KeyBackupPassword));
        Assert.Equal(new[] { "um@exemplo.com", "dois@exemplo.com" }, objSettings.AlertEmails);
        Assert.Equal(587, objSettings.SmtpPort);
        Assert.False(objSettings.HasTelegram); // falta o chat
    }

    [Fact]
    public void OpsEnv_WritesAndReadsBack_AndEmptyIsNotWritten()
    {
        Dictionary<string, string> dicValues = new Dictionary<string, string>
        {
            ["SMTP_HOST"] = "smtp.gmail.com",
            ["SMTP_SENHA"] = "abc=def",
            ["SMTP_NOME"] = "",
        };

        string sText = EnvFile.Format(dicValues, "teste");
        Dictionary<string, string> dicBack = EnvFile.Parse(sText);

        Assert.Equal("smtp.gmail.com", dicBack["SMTP_HOST"]);
        Assert.Equal("abc=def", dicBack["SMTP_SENHA"]);
        Assert.False(dicBack.ContainsKey("SMTP_NOME"));
    }

    [Fact]
    public void OpsEnv_ValueWithLineBreak_IsRejected()
    {
        Dictionary<string, string> dicValues = new Dictionary<string, string> { ["X"] = "um\ndois" };

        Assert.Throws<ArgumentException>(() => EnvFile.Format(dicValues, "teste"));
    }

    [Fact]
    public void OpsSettings_DefaultSenderAndName()
    {
        OpsSettings objSettings = new OpsSettings(new Dictionary<string, string>
        {
            [OpsSettings.KeySmtpUser] = "avisos@exemplo.com",
            [OpsSettings.KeySmtpPort] = "587",
        });

        Assert.Equal("avisos@exemplo.com", objSettings.SmtpFrom);
        Assert.Equal("AccessWifi", objSettings.SmtpFromName);
    }
}
