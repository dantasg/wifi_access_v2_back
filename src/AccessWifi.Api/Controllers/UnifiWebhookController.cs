using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Models.DataBase;
using Models.Persistence;
using Models.Visits;

namespace AccessWifi.Api.Controllers;

/// <summary>
/// Recebe os avisos do Alarm Manager da UniFi ("cliente conectou/desconectou") e transforma em visitas e tempo na
/// loja (PROPOSTA_VISITAS_UNIFI.md, <see cref="UnifiVisitTracker"/>).
///
/// Endereço: /webhooks/unifi/{código}. O código acha a unidade: cada unidade tem o seu, gerado no painel e guardado
/// só como impressão (<see cref="Unit.UnifiWebhookHash"/>, D1). O código da captura de 22/10/2026
/// (UnifiWebhook:CaptureToken, no /etc/accesswifi/accesswifi.env) continua aceito com "?unit=slug", para a Itaituba
/// não precisar trocar o endereço. Código errado é 404, para não confirmar que a rota existe.
///
/// TEMPORÁRIO: enquanto o aviso de desconexão não for analisado, tudo o que chega também é guardado como chegou em
/// UnifiWebhookCaptures (até <see cref="MaxCaptures"/> linhas).
/// </summary>
[ApiController]
[Route("webhooks/unifi")]
[AllowAnonymous]
[EnableRateLimiting("unifi-webhook")]
public partial class UnifiWebhookController : ControllerBase
{
    /// <summary>Teto da tabela: passou disso, para de guardar (protege o banco se alguém insistir).</summary>
    public const int MaxCaptures = 10_000;

    private const int MaxBodyBytes = 64 * 1024;

    [GeneratedRegex("^[a-z0-9-]{1,60}$")]
    private static partial Regex LabelRegex();

    private readonly AppDbContext _objDbContext;
    private readonly IConfiguration _objConfiguration;
    private readonly ILogger<UnifiWebhookController> _objLogger;

    public UnifiWebhookController(
        AppDbContext objDbContext, IConfiguration objConfiguration, ILogger<UnifiWebhookController> objLogger)
    {
        _objDbContext = objDbContext;
        _objConfiguration = objConfiguration;
        _objLogger = objLogger;
    }

    [HttpGet("{token}")]
    [HttpPost("{token}")]
    [RequestSizeLimit(MaxBodyBytes)]
    public async Task<IActionResult> Capture(
        string token, [FromQuery(Name = "unit")] string? sLabel, CancellationToken objCancellationToken)
    {
        string sCleanLabel = (sLabel ?? "").Trim().ToLowerInvariant();
        if (!LabelRegex().IsMatch(sCleanLabel))
        {
            sCleanLabel = "";
        }

        Unit? objUnit = await FindUnitAsync(token, objCancellationToken);
        bool bCaptureToken = IsCaptureToken(token);
        if (objUnit is null && bCaptureToken && sCleanLabel.Length > 0)
        {
            objUnit = await _objDbContext.Units.FirstOrDefaultAsync(unit => unit.Slug == sCleanLabel, objCancellationToken);
        }
        if (objUnit is null && !bCaptureToken)
        {
            return NotFound();
        }

        string sBody;
        using (StreamReader objReader = new StreamReader(Request.Body, Encoding.UTF8))
        {
            sBody = await objReader.ReadToEndAsync(objCancellationToken);
        }

        await SaveCaptureAsync(sBody, objUnit is null ? sCleanLabel : objUnit.Slug, objCancellationToken);

        if (objUnit is not null && objUnit.Active)
        {
            await HandleAsync(objUnit, sBody, objCancellationToken);
        }
        return Ok(new { ok = true });
    }

    /// <summary>
    /// Processa o aviso. Nunca derruba a resposta: o que der errado fica no log (a UniFi não tem o que fazer com um
    /// erro nosso). Uma segunda tentativa cobre dois avisos do mesmo aparelho chegando juntos (registro do dia criado
    /// pelos dois ao mesmo tempo).
    /// </summary>
    private async Task HandleAsync(Unit objUnit, string sBody, CancellationToken objCancellationToken)
    {
        UnifiNotice? objNotice = UnifiNotice.Parse(sBody);
        DateTime dtNowUtc = DateTime.UtcNow;
        for (int iAttempt = 1; iAttempt <= 2; iAttempt++)
        {
            try
            {
                Unit objTracked = await _objDbContext.Units.FirstAsync(unit => unit.Id == objUnit.Id, objCancellationToken);
                objTracked.UnifiWebhookLastAt = dtNowUtc;
                if (objNotice is null)
                {
                    await _objDbContext.SaveChangesAsync(objCancellationToken);
                    return;
                }
                await UnifiVisitTracker.HandleAsync(_objDbContext, objTracked, objNotice, dtNowUtc, objCancellationToken);
                await _objDbContext.SaveChangesAsync(objCancellationToken);
                return;
            }
            catch (Exception objException) when (objException is not OperationCanceledException)
            {
                _objDbContext.ChangeTracker.Clear();
                if (iAttempt == 2)
                {
                    // Sem dado pessoal no log: só a unidade e o motivo técnico.
                    _objLogger.LogWarning(objException, "Aviso da UniFi não processado na unidade {Slug}.", objUnit.Slug);
                }
            }
        }
    }

    private async Task SaveCaptureAsync(string sBody, string sLabel, CancellationToken objCancellationToken)
    {
        if (await _objDbContext.UnifiWebhookCaptures.CountAsync(objCancellationToken) >= MaxCaptures)
        {
            return;
        }

        JsonObject objHeaders = [];
        foreach (KeyValuePair<string, Microsoft.Extensions.Primitives.StringValues> objHeader in Request.Headers)
        {
            objHeaders[objHeader.Key] = objHeader.Value.ToString();
        }

        _objDbContext.UnifiWebhookCaptures.Add(new UnifiWebhookCapture
        {
            ReceivedAt = DateTime.UtcNow,
            Method = Request.Method,
            Label = sLabel,
            QueryString = Truncate(Request.QueryString.Value ?? "", 2000),
            HeadersJson = objHeaders.ToJsonString(),
            ContentType = Truncate(Request.ContentType ?? "", 200),
            Body = sBody,
            BodyJson = TryJson(sBody),
            RemoteIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "",
        });
        await _objDbContext.SaveChangesAsync(objCancellationToken);
        _objDbContext.ChangeTracker.Clear();
    }

    /// <summary>A unidade dona do código (pela impressão; o código em si não fica no banco).</summary>
    private async Task<Unit?> FindUnitAsync(string sToken, CancellationToken objCancellationToken)
    {
        if (string.IsNullOrWhiteSpace(sToken) || sToken.Length < 20)
        {
            return null;
        }
        string sHash = HashToken(sToken);
        return await _objDbContext.Units.AsNoTracking()
            .FirstOrDefaultAsync(unit => unit.UnifiWebhookHash == sHash, objCancellationToken);
    }

    /// <summary>Impressão do código do endereço: SHA-256 em hexadecimal minúsculo.</summary>
    public static string HashToken(string sToken) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sToken))).ToLowerInvariant();

    /// <summary>Código da captura de 22/10/2026. Compara em tempo constante, para não poder ser adivinhado pelo tempo de resposta.</summary>
    private bool IsCaptureToken(string sToken)
    {
        string sExpected = _objConfiguration["UnifiWebhook:CaptureToken"] ?? "";
        if (sExpected.Length < 20 || string.IsNullOrEmpty(sToken))
        {
            return false;
        }
        byte[] arrExpected = SHA256.HashData(Encoding.UTF8.GetBytes(sExpected));
        byte[] arrGiven = SHA256.HashData(Encoding.UTF8.GetBytes(sToken));
        return CryptographicOperations.FixedTimeEquals(arrExpected, arrGiven);
    }

    private static string? TryJson(string sBody)
    {
        if (string.IsNullOrWhiteSpace(sBody))
        {
            return null;
        }
        try
        {
            return JsonNode.Parse(sBody)?.ToJsonString();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string Truncate(string sValue, int iMax) => sValue.Length <= iMax ? sValue : sValue[..iMax];
}
