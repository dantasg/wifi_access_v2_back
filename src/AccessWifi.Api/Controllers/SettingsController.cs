using System.Text.RegularExpressions;
using AccessWifi.Api.Features;
using AccessWifi.Api.Features.Settings;
using AccessWifi.Api.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AccessWifi.Api.Controllers;

[ApiController]
public partial class SettingsController : ControllerBase
{
    // Front limita cada imagem a 2 MB; em data URL (base64) isso dá ~2,8M chars — 4M dá folga.
    private const int MaxImageChars = 4 * 1024 * 1024;
    private const int MaxAccessMinutes = 525_600; // 1 ano

    [GeneratedRegex("^#[0-9a-fA-F]{6}$")]
    private static partial Regex HexColorRegex();

    private readonly AppDbContext _objDbContext;

    public SettingsController(AppDbContext objDbContext)
    {
        _objDbContext = objDbContext;
    }

    /// <summary>
    /// Tema/marca do portal — público, porque o visitante carrega o tema sem estar logado.
    /// Sem linha gravada, devolve os padrões da Dôce (mesmos defaults do front).
    /// </summary>
    [HttpGet("/settings")]
    public async Task<ActionResult<SettingsDto>> Get(CancellationToken objCancellationToken)
    {
        PortalSettings objSettings = await _objDbContext.PortalSettings
            .AsNoTracking()
            .FirstOrDefaultAsync(objCancellationToken)
            ?? new PortalSettings();

        return Ok(SettingsDto.FromEntity(objSettings));
    }

    /// <summary>Salva tema + parâmetros de acesso (linha única, upsert).</summary>
    [HttpPut("/admin/settings")]
    [Authorize]
    [RequestSizeLimit(12 * 1024 * 1024)]
    public async Task<ActionResult<SettingsDto>> Put(
        SettingsDto objRequest, CancellationToken objCancellationToken)
    {
        string? sValidationError = Validate(objRequest);
        if (sValidationError is not null)
        {
            return BadRequest(new ErrorResponse(sValidationError));
        }

        PortalSettings? objSettings = await _objDbContext.PortalSettings
            .FirstOrDefaultAsync(objCancellationToken);
        if (objSettings is null)
        {
            objSettings = new PortalSettings();
            _objDbContext.PortalSettings.Add(objSettings);
        }

        objSettings.Colors = objRequest.Colors.ToEntity();
        objSettings.Logo = objRequest.Logo;
        objSettings.Favicon = objRequest.Favicon;
        objSettings.Banner = objRequest.Banner;
        objSettings.Ssid = objRequest.Ssid.Trim();
        objSettings.AccessMinutes = objRequest.AccessMinutes;
        objSettings.UpdatedAt = DateTime.UtcNow;

        await _objDbContext.SaveChangesAsync(objCancellationToken);

        return Ok(SettingsDto.FromEntity(objSettings));
    }

    private static string? Validate(SettingsDto objRequest)
    {
        (string sName, string sValue)[] objColors =
        [
            ("brand", objRequest.Colors.Brand),
            ("brandDark", objRequest.Colors.BrandDark),
            ("surface", objRequest.Colors.Surface),
            ("card", objRequest.Colors.Card),
            ("field", objRequest.Colors.Field),
            ("ink", objRequest.Colors.Ink),
            ("muted", objRequest.Colors.Muted),
            ("line", objRequest.Colors.Line),
        ];
        foreach ((string sName, string sValue) in objColors)
        {
            if (sValue is null || !HexColorRegex().IsMatch(sValue))
            {
                return $"Cor inválida em '{sName}' (esperado #rrggbb).";
            }
        }

        (string sName, string? sValue)[] objImages =
        [
            ("logo", objRequest.Logo),
            ("favicon", objRequest.Favicon),
            ("banner", objRequest.Banner),
        ];
        foreach ((string sName, string? sValue) in objImages)
        {
            if (sValue is null)
            {
                continue;
            }
            if (!sValue.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase))
            {
                return $"Imagem inválida em '{sName}' (esperado data URL de imagem).";
            }
            if (sValue.Length > MaxImageChars)
            {
                return $"Imagem muito grande em '{sName}' (máximo de 2 MB).";
            }
        }

        if (string.IsNullOrWhiteSpace(objRequest.Ssid) || objRequest.Ssid.Trim().Length > 32)
        {
            return "SSID é obrigatório e deve ter no máximo 32 caracteres.";
        }

        if (objRequest.AccessMinutes < 1 || objRequest.AccessMinutes > MaxAccessMinutes)
        {
            return $"Tempo de acesso deve ficar entre 1 e {MaxAccessMinutes} minutos.";
        }

        return null;
    }
}
