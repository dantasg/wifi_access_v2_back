using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Models.DataBase;
using Models.Persistence;
using Models.Security;
using Models.WhatsApp;

namespace AccessWifi.Api.Controllers;

/// <summary>
/// Os avisos da Meta para o WhatsApp de cada empresa (PROPOSTA_WHATSAPP.md, §5.2 e parte 4), em
/// webhooks/whatsapp/{empresa}, sem login:
/// <list type="bullet">
/// <item>GET: a Meta confere o endereço com o código de verificação da empresa e espera o hub.challenge de volta;</item>
/// <item>POST: o aviso, assinado com a chave secreta do app da empresa (X-Hub-Signature-256). Sem assinatura válida,
/// é recusado e não é tratado.</item>
/// </list>
/// Todo aviso fica guardado como chegou por 90 dias (D12) antes de ser tratado. Se o tratamento falhar, responde 500
/// para a Meta mandar de novo (ela tenta por até 7 dias); tratar duas vezes não muda nada.
/// </summary>
[ApiController]
[Route("webhooks/whatsapp")]
[AllowAnonymous]
[EnableRateLimiting("whatsapp-webhook")]
public class WhatsAppWebhookController : ControllerBase
{
    /// <summary>De um aviso sem assinatura válida, só o começo é guardado (para não encher o banco).</summary>
    private const int MaxInvalidBodyChars = 4096;

    private readonly AppDbContext _objDbContext;
    private readonly IEncryptor _objEncryptor;
    private readonly ILogger<WhatsAppWebhookController> _objLogger;

    public WhatsAppWebhookController(
        AppDbContext objDbContext, IEncryptor objEncryptor, ILogger<WhatsAppWebhookController> objLogger)
    {
        _objDbContext = objDbContext;
        _objEncryptor = objEncryptor;
        _objLogger = objLogger;
    }

    /// <summary>Verificação da Meta: código certo devolve o hub.challenge (200, texto); errado, 403.</summary>
    [HttpGet("{slug}")]
    public async Task<IActionResult> Verify(
        string slug,
        [FromQuery(Name = "hub.mode")] string? sMode,
        [FromQuery(Name = "hub.verify_token")] string? sVerifyToken,
        [FromQuery(Name = "hub.challenge")] string? sChallenge,
        CancellationToken objCancellationToken)
    {
        Company? objCompany = await _objDbContext.Companies
            .FirstOrDefaultAsync(company => company.Slug == slug, objCancellationToken);
        if (objCompany is null)
        {
            return NotFound();
        }

        string sStored = _objEncryptor.Decrypt(objCompany.WhatsApp.VerifyToken) ?? "";
        bool bValid = sMode == "subscribe" && sStored.Length > 0 && !string.IsNullOrEmpty(sChallenge)
            && sChallenge.Length <= 200 && SameSecret(sVerifyToken ?? "", sStored);
        if (!bValid)
        {
            _objLogger.LogWarning("WhatsApp: verificação dos avisos recusada para a empresa {Slug}.", slug);
            return StatusCode(StatusCodes.Status403Forbidden);
        }

        _objDbContext.WhatsAppAccountEvents.Add(new WhatsAppAccountEvent
        {
            IDCompany = objCompany.Id,
            Kind = WhatsAppAccountEventKind.WebhookVerified,
            Details = "A Meta conferiu o endereço dos avisos (código de verificação certo).",
            CreatedAt = DateTime.UtcNow,
        });
        await _objDbContext.SaveChangesAsync(objCancellationToken);
        return Content(sChallenge!, "text/plain", Encoding.UTF8);
    }

    /// <summary>Um aviso da Meta (até 3 MB, segundo a Meta).</summary>
    [HttpPost("{slug}")]
    [RequestSizeLimit(4 * 1024 * 1024)]
    public async Task<IActionResult> Receive(string slug, CancellationToken objCancellationToken)
    {
        Company? objCompany = await _objDbContext.Companies
            .FirstOrDefaultAsync(company => company.Slug == slug, objCancellationToken);
        if (objCompany is null)
        {
            return NotFound();
        }

        byte[] arrBody;
        using (MemoryStream objBuffer = new MemoryStream())
        {
            await Request.Body.CopyToAsync(objBuffer, objCancellationToken);
            arrBody = objBuffer.ToArray();
        }
        string sBody = Encoding.UTF8.GetString(arrBody);
        bool bValid = WhatsAppWebhookProcessor.IsSignatureValid(
            arrBody, Request.Headers["X-Hub-Signature-256"].FirstOrDefault(),
            _objEncryptor.Decrypt(objCompany.WhatsApp.AppSecret) ?? "");

        DateTime dtNow = DateTime.UtcNow;
        JsonNode? objPayload = null;
        string sParseError = "";
        if (bValid)
        {
            try
            {
                objPayload = JsonNode.Parse(sBody);
            }
            catch (JsonException)
            {
                sParseError = "O aviso não é um JSON válido.";
            }
        }
        WhatsAppWebhookRequest objRequest = new WhatsAppWebhookRequest
        {
            IDCompany = objCompany.Id,
            ReceivedAt = dtNow,
            SignatureValid = bValid,
            Body = bValid || sBody.Length <= MaxInvalidBodyChars ? sBody : sBody[..MaxInvalidBodyChars],
            Fields = Truncate(WhatsAppWebhookProcessor.Fields(objPayload), 300),
            Error = bValid ? sParseError : "Assinatura inválida: não veio da Meta com a chave secreta desta empresa.",
            RemoteIp = Truncate(HttpContext.Connection.RemoteIpAddress?.ToString() ?? "", 64),
        };
        _objDbContext.WhatsAppWebhookRequests.Add(objRequest);
        await _objDbContext.SaveChangesAsync(objCancellationToken);

        if (!bValid)
        {
            _objLogger.LogWarning("WhatsApp: aviso com assinatura inválida para a empresa {Slug}.", slug);
            return Unauthorized();
        }
        if (objPayload is null)
        {
            // Lixo não melhora tentando de novo.
            return Ok();
        }

        try
        {
            await new WhatsAppWebhookProcessor(_objDbContext, objCompany, dtNow).ProcessAsync(objPayload, objCancellationToken);
            objRequest.ProcessedAt = dtNow;
            await _objDbContext.SaveChangesAsync(objCancellationToken);
            return Ok();
        }
        catch (Exception objException) when (objException is not OperationCanceledException)
        {
            _objLogger.LogError(objException, "WhatsApp: falha ao tratar o aviso {Id} da empresa {Slug}.", objRequest.Id, slug);
            // O que ficou pela metade não vai; só o motivo, na cópia do aviso. A Meta manda de novo.
            _objDbContext.ChangeTracker.Clear();
            WhatsAppWebhookRequest? objStored = await _objDbContext.WhatsAppWebhookRequests
                .FirstOrDefaultAsync(request => request.Id == objRequest.Id, objCancellationToken);
            if (objStored is not null)
            {
                objStored.Error = Truncate(objException.GetBaseException().Message, 2000);
                await _objDbContext.SaveChangesAsync(objCancellationToken);
            }
            return StatusCode(StatusCodes.Status500InternalServerError);
        }
    }

    /// <summary>Compara em tempo constante (pelo hash, para não depender do tamanho).</summary>
    private static bool SameSecret(string sGiven, string sStored) =>
        CryptographicOperations.FixedTimeEquals(
            SHA256.HashData(Encoding.UTF8.GetBytes(sGiven)), SHA256.HashData(Encoding.UTF8.GetBytes(sStored)));

    private static string Truncate(string sValue, int iMax) => sValue.Length <= iMax ? sValue : sValue[..iMax];
}
