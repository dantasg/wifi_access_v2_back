using System.Text.RegularExpressions;
using AccessWifi.Api.Features;
using AccessWifi.Api.Features.Companies;
using AccessWifi.Api.Features.Settings;
using Models.Persistence;
using AccessWifi.Api.Infrastructure.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Models.DataBase;

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
    /// Tema/marca do portal da empresa (?company=slug) — público, porque o visitante
    /// carrega o tema sem estar logado. Sem linha gravada, devolve os padrões da marca.
    /// </summary>
    [HttpGet("/settings")]
    public async Task<ActionResult<SettingsDto>> Get(
        [FromQuery(Name = "company")] string? sCompanySlug, CancellationToken objCancellationToken)
    {
        if (string.IsNullOrWhiteSpace(sCompanySlug))
        {
            return BadRequest(new ErrorResponse("Informe a empresa (?company=slug)."));
        }

        Company? objCompany = await _objDbContext.Companies
            .AsNoTracking()
            .FirstOrDefaultAsync(company => company.Slug == sCompanySlug, objCancellationToken);
        if (objCompany is null || !objCompany.Active)
        {
            return NotFound(new ErrorResponse("Empresa não encontrada."));
        }

        PortalSettings objSettings = await _objDbContext.PortalSettings
            .AsNoTracking()
            .FirstOrDefaultAsync(
                settings => settings.IDCompany == objCompany.Id, objCancellationToken)
            ?? new PortalSettings { IDCompany = objCompany.Id };

        return Ok(SettingsDto.FromEntity(objSettings));
    }

    /// <summary>
    /// Salva tema + parâmetros da empresa do token (upsert). Super admin indica a
    /// empresa via ?company=slug.
    /// </summary>
    [HttpPut("/admin/settings")]
    [Authorize]
    [RequestSizeLimit(12 * 1024 * 1024)]
    public async Task<ActionResult<SettingsDto>> Put(
        SettingsDto objRequest,
        [FromQuery(Name = "company")] string? sCompanySlug,
        CancellationToken objCancellationToken)
    {
        Guid? objCompanyId = User.GetCompanyId();
        if (objCompanyId is null)
        {
            // Super admin: a empresa vem da query string.
            if (string.IsNullOrWhiteSpace(sCompanySlug))
            {
                return BadRequest(new ErrorResponse("Informe a empresa (?company=slug)."));
            }
            Company? objCompany = await _objDbContext.Companies
                .FirstOrDefaultAsync(company => company.Slug == sCompanySlug, objCancellationToken);
            if (objCompany is null)
            {
                return NotFound(new ErrorResponse("Empresa não encontrada."));
            }
            objCompanyId = objCompany.Id;
        }

        string? sValidationError = Validate(objRequest);
        if (sValidationError is not null)
        {
            return BadRequest(new ErrorResponse(sValidationError));
        }

        PortalSettings? objSettings = await _objDbContext.PortalSettings
            .FirstOrDefaultAsync(
                settings => settings.IDCompany == objCompanyId, objCancellationToken);
        if (objSettings is null)
        {
            objSettings = new PortalSettings { IDCompany = objCompanyId.Value };
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
