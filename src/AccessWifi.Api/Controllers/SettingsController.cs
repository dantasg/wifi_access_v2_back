using System.Text.RegularExpressions;
using AccessWifi.Api.Features;
using AccessWifi.Api.Features.Companies;
using AccessWifi.Api.Features.Settings;
using AccessWifi.Api.Features.Units;
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
    private const int MaxRedirectUrlChars = 2048;

    [GeneratedRegex("^#[0-9a-fA-F]{6}$")]
    private static partial Regex HexColorRegex();

    private readonly AppDbContext _objDbContext;

    public SettingsController(AppDbContext objDbContext)
    {
        _objDbContext = objDbContext;
    }

    /// <summary>
    /// Tema/marca do portal — público, porque o visitante carrega o tema sem estar logado.
    /// A unidade vem por <c>?unit=slug</c> ou, quando a UniFi não pôde mandar a query string,
    /// por <c>?host=</c> (o endereço em que o portal foi aberto). O tema é da empresa dona da
    /// unidade; sem linha gravada, devolve os padrões da marca.
    /// </summary>
    [HttpGet("/settings")]
    public async Task<ActionResult<SettingsDto>> Get(
        [FromQuery(Name = "unit")] string? sUnitSlug,
        [FromQuery(Name = "host")] string? sPortalHost,
        CancellationToken objCancellationToken)
    {
        if (string.IsNullOrWhiteSpace(sUnitSlug) && string.IsNullOrWhiteSpace(sPortalHost))
        {
            return BadRequest(new ErrorResponse("Informe a unidade (?unit=slug) ou o host (?host=)."));
        }

        Unit? objUnit = await UnitResolver.FindAsync(
            _objDbContext.Units.AsNoTracking(), sUnitSlug, sPortalHost, objCancellationToken);
        if (objUnit is null || !objUnit.Active)
        {
            return NotFound(new ErrorResponse("Unidade não encontrada."));
        }

        bool bCompanyAtiva = await _objDbContext.Companies
            .AsNoTracking()
            .AnyAsync(company => company.Id == objUnit.IDCompany && company.Active, objCancellationToken);
        if (!bCompanyAtiva)
        {
            return NotFound(new ErrorResponse("Unidade não encontrada."));
        }

        PortalSettings objSettings = await _objDbContext.PortalSettings
            .AsNoTracking()
            .FirstOrDefaultAsync(
                settings => settings.IDCompany == objUnit.IDCompany, objCancellationToken)
            ?? new PortalSettings { IDCompany = objUnit.IDCompany };

        // Devolve o slug resolvido: quando a unidade veio pelo host, é assim que o front
        // descobre o que mandar depois no /authorize.
        return Ok(SettingsDto.FromEntity(objSettings, objUnit.Slug));
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

        // URLs próprias das unidades: tudo é conferido antes de gravar qualquer coisa, para um erro
        // numa unidade não deixar o resto salvo pela metade.
        List<(Unit objUnit, string sUrl)> objUnitRedirects = [];
        if (objRequest.UnitRedirects is { Count: > 0 })
        {
            List<Guid> objUnitIds = objRequest.UnitRedirects.Select(item => item.UnitId).Distinct().ToList();
            Dictionary<Guid, Unit> objUnits = await _objDbContext.Units
                .Where(unit => unit.IDCompany == objCompanyId && objUnitIds.Contains(unit.Id))
                .ToDictionaryAsync(unit => unit.Id, objCancellationToken);

            foreach (UnitRedirectDto objItem in objRequest.UnitRedirects)
            {
                // Unidade de outra empresa ou inexistente: a mesma resposta, sem dizer qual das duas.
                if (!objUnits.TryGetValue(objItem.UnitId, out Unit? objUnit))
                {
                    return BadRequest(new ErrorResponse("Unidade não encontrada nesta empresa."));
                }

                string? sUrlError = ValidateRedirectUrl(objItem.RedirectUrl);
                if (sUrlError is not null)
                {
                    return BadRequest(new ErrorResponse($"Unidade {objUnit.Name}: {sUrlError}"));
                }

                objUnitRedirects.Add((objUnit, objItem.RedirectUrl?.Trim() ?? ""));
            }
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
        objSettings.RedirectUrl = string.IsNullOrWhiteSpace(objRequest.RedirectUrl)
            ? null
            : objRequest.RedirectUrl.Trim();
        objSettings.UpdatedAt = DateTime.UtcNow;

        foreach ((Unit objUnit, string sUrl) in objUnitRedirects)
        {
            objUnit.RedirectUrl = sUrl;
        }

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

        return ValidateRedirectUrl(objRequest.RedirectUrl);
    }

    /// <summary>
    /// A mesma regra para a URL "Geral" da empresa e para a de cada unidade: vazia (usa o padrão)
    /// ou um endereço http/https completo dentro do limite.
    /// </summary>
    private static string? ValidateRedirectUrl(string? sUrl)
    {
        if (string.IsNullOrWhiteSpace(sUrl))
        {
            return null;
        }

        string sRedirectUrl = sUrl.Trim();
        if (sRedirectUrl.Length > MaxRedirectUrlChars)
        {
            return $"URL de redirecionamento muito longa (máximo de {MaxRedirectUrlChars} caracteres).";
        }

        bool bUrlValida =
            Uri.TryCreate(sRedirectUrl, UriKind.Absolute, out Uri? objUri) &&
            (objUri.Scheme == Uri.UriSchemeHttp || objUri.Scheme == Uri.UriSchemeHttps);
        if (!bUrlValida)
        {
            return "URL de redirecionamento inválida (informe um endereço http ou https completo).";
        }

        return null;
    }
}
