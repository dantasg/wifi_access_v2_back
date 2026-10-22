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

namespace AccessWifi.Api.Controllers;

/// <summary>
/// TEMPORÁRIO (22/10/2026): recebe o webhook do Alarm Manager da UniFi ("cliente conectou") e guarda tudo como
/// chegou em UnifiWebhookCaptures, para analisarmos o conteúdo antes de desenhar o registro de visitas sem
/// portal. Não processa nada nem muda nenhum dado do sistema.
///
/// Endereço: /webhooks/unifi/{código}?unit=nome-da-loja. O código secreto vem de UnifiWebhook:CaptureToken
/// (no servidor, /etc/accesswifi/accesswifi.env); sem ele configurado, a rota não existe (404). Código errado
/// também é 404, para não confirmar que a rota existe.
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
        if (!IsValidToken(token))
        {
            return NotFound();
        }

        if (await _objDbContext.UnifiWebhookCaptures.CountAsync(objCancellationToken) >= MaxCaptures)
        {
            _objLogger.LogWarning("Captura de avisos da UniFi cheia ({Max}); aviso ignorado.", MaxCaptures);
            return Ok(new { ok = true });
        }

        string sBody;
        using (StreamReader objReader = new StreamReader(Request.Body, Encoding.UTF8))
        {
            sBody = await objReader.ReadToEndAsync(objCancellationToken);
        }

        JsonObject objHeaders = [];
        foreach (KeyValuePair<string, Microsoft.Extensions.Primitives.StringValues> objHeader in Request.Headers)
        {
            objHeaders[objHeader.Key] = objHeader.Value.ToString();
        }

        string sCleanLabel = (sLabel ?? "").Trim().ToLowerInvariant();
        _objDbContext.UnifiWebhookCaptures.Add(new UnifiWebhookCapture
        {
            ReceivedAt = DateTime.UtcNow,
            Method = Request.Method,
            Label = LabelRegex().IsMatch(sCleanLabel) ? sCleanLabel : "",
            QueryString = Truncate(Request.QueryString.Value ?? "", 2000),
            HeadersJson = objHeaders.ToJsonString(),
            ContentType = Truncate(Request.ContentType ?? "", 200),
            Body = sBody,
            BodyJson = TryJson(sBody),
            RemoteIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "",
        });
        await _objDbContext.SaveChangesAsync(objCancellationToken);
        return Ok(new { ok = true });
    }

    /// <summary>Compara em tempo constante, para o código não poder ser adivinhado pelo tempo de resposta.</summary>
    private bool IsValidToken(string sToken)
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
