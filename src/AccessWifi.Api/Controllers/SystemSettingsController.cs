using System.Globalization;
using System.Text.RegularExpressions;
using AccessWifi.Api.Features;
using AccessWifi.Api.Features.SystemSettings;
using AccessWifi.Api.Infrastructure.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Models.DataBase;
using Models.Email;
using Models.Persistence;
using Models.Security;

namespace AccessWifi.Api.Controllers;

/// <summary>
/// Configurações do sistema inteiro (não de uma empresa) — exclusivo do super admin. Hoje: a conta de e-mail
/// que envia o relatório mensal e os PDFs das campanhas (chaves SMTP_* da tabela Configuration).
/// </summary>
[ApiController]
[Route("admin/system-settings")]
[Authorize(Roles = ClaimsExtensions.RoleSuperAdmin)]
public partial class SystemSettingsController : ControllerBase
{
    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$")]
    private static partial Regex EmailRegex();

    // Nome de servidor ou IP: rótulos alfanuméricos com hífen no meio, separados por ponto.
    [GeneratedRegex(@"^[A-Za-z0-9]([A-Za-z0-9-]*[A-Za-z0-9])?(\.[A-Za-z0-9]([A-Za-z0-9-]*[A-Za-z0-9])?)*$")]
    private static partial Regex HostRegex();

    private const int DefaultPort = 587;
    private const string DefaultFromName = "AccessWifi";

    private readonly AppDbContext _objDbContext;
    private readonly IEncryptor _objEncryptor;
    private readonly ConfigurationReader _objConfigurationReader;
    private readonly IEmailSender _objEmailSender;
    private readonly ILogger<SystemSettingsController> _objLogger;

    public SystemSettingsController(
        AppDbContext objDbContext,
        IEncryptor objEncryptor,
        ConfigurationReader objConfigurationReader,
        IEmailSender objEmailSender,
        ILogger<SystemSettingsController> objLogger)
    {
        _objDbContext = objDbContext;
        _objEncryptor = objEncryptor;
        _objConfigurationReader = objConfigurationReader;
        _objEmailSender = objEmailSender;
        _objLogger = objLogger;
    }

    /// <summary>As configurações atuais, sem a senha (só se ela existe).</summary>
    [HttpGet]
    public async Task<ActionResult<SystemSettingsDto>> Get(CancellationToken objCancellationToken)
    {
        return Ok(await LoadAsync(objCancellationToken));
    }

    /// <summary>Grava a conta de e-mail. Senha nula = manter a atual; quando vem, é guardada cifrada.</summary>
    [HttpPut]
    public async Task<ActionResult<SystemSettingsDto>> Update(
        UpdateSystemSettingsRequest objRequest, CancellationToken objCancellationToken)
    {
        SmtpSettingsRequest? objSmtp = objRequest.Smtp;
        if (objSmtp is null)
        {
            return BadRequest(new ErrorResponse("Informe as configurações de e-mail."));
        }

        string sHost = (objSmtp.Host ?? "").Trim();
        int iPort = objSmtp.Port ?? DefaultPort;
        string sUsername = (objSmtp.Username ?? "").Trim();
        string sFromEmail = (objSmtp.FromEmail ?? "").Trim();
        string sFromName = string.IsNullOrWhiteSpace(objSmtp.FromName) ? DefaultFromName : objSmtp.FromName.Trim();

        string? sError = Validate(sHost, iPort, sUsername, objSmtp.Password, sFromEmail, sFromName);
        if (sError is not null)
        {
            return BadRequest(new ErrorResponse(sError));
        }

        Dictionary<string, string> dicValues = new Dictionary<string, string>
        {
            [ConfigurationKeys.SmtpHost] = sHost,
            [ConfigurationKeys.SmtpPort] = iPort.ToString(CultureInfo.InvariantCulture),
            [ConfigurationKeys.SmtpUsername] = sUsername,
            [ConfigurationKeys.SmtpFromEmail] = sFromEmail,
            [ConfigurationKeys.SmtpFromName] = sFromName,
            [ConfigurationKeys.SmtpUseStartTls] = (objSmtp.UseStartTls ?? true) ? "true" : "false",
        };
        if (objSmtp.Password is not null)
        {
            // A "senha de app" do Gmail aparece com espaços ("abcd efgh ijkl mnop"); o Gmail quer sem.
            string sPassword = sHost.Contains("gmail", StringComparison.OrdinalIgnoreCase)
                ? objSmtp.Password.Replace(" ", "", StringComparison.Ordinal)
                : objSmtp.Password;
            dicValues[ConfigurationKeys.SmtpPassword] = _objEncryptor.Encrypt(sPassword) ?? "";
        }

        string[] arrKeys = [.. dicValues.Keys];
        List<Configuration> objCurrent = await _objDbContext.Configurations
            .Where(config => arrKeys.Contains(config.IDConfiguration))
            .ToListAsync(objCancellationToken);
        foreach (KeyValuePair<string, string> objPair in dicValues)
        {
            Configuration? objRow = objCurrent.FirstOrDefault(config => config.IDConfiguration == objPair.Key);
            if (objRow is null)
            {
                _objDbContext.Configurations.Add(new Configuration { IDConfiguration = objPair.Key, Value = objPair.Value });
            }
            else
            {
                objRow.Value = objPair.Value;
            }
        }
        await _objDbContext.SaveChangesAsync(objCancellationToken);

        // Sem valores no log: só quem mudou e se a senha foi trocada.
        _objLogger.LogInformation(
            "Configurações do sistema: e-mail de envio alterado por {Usuario} (servidor {Host}, senha {Senha}).",
            User.GetUsername(), sHost.Length > 0 ? sHost : "nenhum",
            objSmtp.Password is null ? "mantida" : "trocada");

        return Ok(await LoadAsync(objCancellationToken));
    }

    /// <summary>
    /// Manda um e-mail de teste com a configuração GRAVADA (salve antes de testar). Falha de envio volta como 200
    /// com success=false e o motivo — é resultado de teste, não erro da requisição.
    /// </summary>
    [HttpPost("smtp/test")]
    public async Task<ActionResult<SmtpTestResponse>> TestSmtp(
        SmtpTestRequest objRequest, CancellationToken objCancellationToken)
    {
        string sTo = (objRequest.To ?? "").Trim();
        if (!EmailRegex().IsMatch(sTo))
        {
            return BadRequest(new ErrorResponse("Informe um e-mail válido para receber o teste."));
        }

        try
        {
            await _objEmailSender.SendAsync(
                sTo,
                "Teste do AccessWifi — e-mail de envio configurado",
                "Olá,\r\n\r\nEste é um e-mail de teste do AccessWifi. Se ele chegou, o relatório mensal e os PDFs das " +
                "campanhas vão sair por esta conta.\r\n\r\nMensagem automática do AccessWifi.",
                null, null, objCancellationToken);
        }
        catch (Exception objException) when (objException is not OperationCanceledException)
        {
            _objLogger.LogWarning(objException, "Configurações do sistema: e-mail de teste para {Para} falhou.", sTo);
            return Ok(new SmtpTestResponse(false, $"Não foi possível enviar: {objException.GetBaseException().Message}"));
        }

        return Ok(new SmtpTestResponse(true,
            $"E-mail de teste enviado para {sTo}. Confira a caixa de entrada (e a de spam)."));
    }

    private async Task<SystemSettingsDto> LoadAsync(CancellationToken objCancellationToken)
    {
        SmtpOptions objSmtp = await _objConfigurationReader.GetSmtpAsync(objCancellationToken);
        return new SystemSettingsDto(new SmtpSettingsDto(
            objSmtp.Host, objSmtp.Port, objSmtp.Username, objSmtp.Password.Length > 0,
            objSmtp.FromEmail, objSmtp.FromName, objSmtp.UseStartTls));
    }

    private static string? Validate(
        string sHost, int iPort, string sUsername, string? sPassword, string sFromEmail, string sFromName)
    {
        if (sHost.Length > 200 || (sHost.Length > 0 && !HostRegex().IsMatch(sHost)))
        {
            return "Servidor SMTP inválido: só o nome (ex.: smtp.gmail.com), sem http:// e sem porta.";
        }
        if (iPort is < 1 or > 65535)
        {
            return "Porta inválida (de 1 a 65535).";
        }
        if (iPort == 465)
        {
            return "A porta 465 (SSL direto) não é suportada: use a 587 com STARTTLS.";
        }
        if (sUsername.Length > 200 || (sPassword?.Length ?? 0) > 500 || sFromName.Length > 100)
        {
            return "Algum campo passou do tamanho máximo.";
        }
        if (sHost.Length > 0 && (sFromEmail.Length == 0 || sFromEmail.Length > 200 || !EmailRegex().IsMatch(sFromEmail)))
        {
            return "Informe o e-mail do remetente (quem aparece como \"de\" nos e-mails).";
        }
        return null;
    }
}
