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

    [GeneratedRegex("^#[0-9a-fA-F]{6}$")]
    private static partial Regex HexColorRegex();

    private readonly AppDbContext _objDbContext;
    private readonly UnitLocator _objUnitLocator;

    public SettingsController(AppDbContext objDbContext, UnitLocator? objUnitLocator = null)
    {
        _objDbContext = objDbContext;
        _objUnitLocator = objUnitLocator ?? new UnitLocator(objDbContext);
    }

    /// <summary>
    /// Tema/marca do portal — público, porque o visitante carrega o tema sem estar logado.
    /// A unidade vem por <c>?unit=slug</c> ou, quando a UniFi não pôde mandar a query string,
    /// pelo ponto de acesso (<c>?ap=</c>, o MAC que a UniFi manda) e pelo endereço em que o portal foi
    /// aberto (<c>?host=</c>) — ver <see cref="UnitLocator"/>. O tema é da empresa dona da unidade; sem
    /// linha gravada, devolve os padrões (neutros). As imagens vão como endereço (<see cref="GetImage"/>).
    /// </summary>
    [HttpGet("/settings")]
    public async Task<ActionResult<SettingsDto>> Get(
        [FromQuery(Name = "unit")] string? sUnitSlug,
        [FromQuery(Name = "host")] string? sPortalHost,
        [FromQuery(Name = "ap")] string? sAp,
        CancellationToken objCancellationToken)
    {
        if (string.IsNullOrWhiteSpace(sUnitSlug) && string.IsNullOrWhiteSpace(sPortalHost))
        {
            return BadRequest(new ErrorResponse("Informe a unidade (?unit=slug) ou o host (?host=)."));
        }

        Unit? objUnit = await FindPortalUnitAsync(sUnitSlug, sPortalHost, sAp, objCancellationToken);
        if (objUnit is null)
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
        return Ok(SettingsDto.ForPortal(objSettings, objUnit));
    }

    /// <summary>
    /// Logo, favicon ou banner da empresa dona da unidade, como arquivo. Público, como o tema. Com o
    /// <c>?v=</c> da versão atual, o celular guarda por um ano (o endereço muda quando a imagem muda).
    /// </summary>
    [HttpGet("/settings/image/{unit}/{kind}")]
    public async Task<IActionResult> GetImage(
        string unit,
        string kind,
        [FromQuery(Name = "v")] string? sVersion,
        CancellationToken objCancellationToken)
    {
        if (kind is not (PortalImage.Logo or PortalImage.Favicon or PortalImage.Banner))
        {
            return NotFound();
        }

        Unit? objUnit = await FindPortalUnitAsync(unit, null, null, objCancellationToken);
        if (objUnit is null)
        {
            return NotFound();
        }

        IQueryable<PortalSettings> objQuery = _objDbContext.PortalSettings
            .AsNoTracking()
            .Where(settings => settings.IDCompany == objUnit.IDCompany);
        string? sDataUrl = kind switch
        {
            PortalImage.Logo => await objQuery.Select(settings => settings.Logo).FirstOrDefaultAsync(objCancellationToken),
            PortalImage.Favicon => await objQuery.Select(settings => settings.Favicon).FirstOrDefaultAsync(objCancellationToken),
            _ => await objQuery.Select(settings => settings.Banner).FirstOrDefaultAsync(objCancellationToken),
        };
        if (!PortalImage.TryDecode(sDataUrl, out byte[] arrBytes, out string sContentType))
        {
            return NotFound();
        }

        // Endereço de uma versão antiga (tema guardado no celular antes da troca): entrega a imagem
        // atual, mas sem guardar para sempre com o endereço velho.
        Response.Headers.CacheControl = sVersion == PortalImage.Version(sDataUrl!)
            ? "public, max-age=31536000, immutable"
            : "no-cache";
        // Uma imagem SVG aberta direto no navegador não roda script nenhum.
        Response.Headers["Content-Security-Policy"] = "default-src 'none'; style-src 'unsafe-inline'; sandbox";
        return File(arrBytes, sContentType);
    }

    /// <summary>
    /// Tema da empresa para o editor do painel, com as imagens inteiras (o Salvar manda de volta). Não
    /// depende de unidade: dá para preparar o tema antes de cadastrar a primeira. Super admin indica a
    /// empresa via ?company=slug.
    /// </summary>
    [HttpGet("/admin/settings")]
    [Authorize]
    public async Task<ActionResult<SettingsDto>> GetAdmin(
        [FromQuery(Name = "company")] string? sCompanySlug,
        CancellationToken objCancellationToken)
    {
        (Guid? objCompanyId, ActionResult? objError) = await ResolveCompanyAsync(sCompanySlug, objCancellationToken);
        if (objCompanyId is null)
        {
            return objError!;
        }

        PortalSettings objSettings = await _objDbContext.PortalSettings
            .AsNoTracking()
            .FirstOrDefaultAsync(settings => settings.IDCompany == objCompanyId, objCancellationToken)
            ?? new PortalSettings { IDCompany = objCompanyId.Value };

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
        (Guid? objCompanyId, ActionResult? objError) = await ResolveCompanyAsync(sCompanySlug, objCancellationToken);
        if (objCompanyId is null)
        {
            return objError!;
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
        objSettings.RedirectUrl = string.IsNullOrWhiteSpace(objRequest.RedirectUrl)
            ? null
            : objRequest.RedirectUrl.Trim();
        // Nulo = manter (um painel aberto antes desta versão não apaga o DDD ao salvar).
        if (objRequest.Ddd is not null)
        {
            objSettings.Ddd = DddRules.Normalize(objRequest.Ddd)!;
        }
        objSettings.UpdatedAt = DateTime.UtcNow;

        await _objDbContext.SaveChangesAsync(objCancellationToken);

        return Ok(SettingsDto.FromEntity(objSettings));
    }

    /// <summary>Unidade ativa, de empresa ativa, pelo slug, host ou ponto de acesso; null se não houver.</summary>
    private async Task<Unit?> FindPortalUnitAsync(
        string? sUnitSlug, string? sPortalHost, string? sAp, CancellationToken objCancellationToken)
    {
        Unit? objUnit = await _objUnitLocator.FindAsync(
            _objDbContext.Units.AsNoTracking(), sUnitSlug, sPortalHost, sAp, objCancellationToken);
        if (objUnit is null || !objUnit.Active)
        {
            return null;
        }

        bool bCompanyAtiva = await _objDbContext.Companies
            .AsNoTracking()
            .AnyAsync(company => company.Id == objUnit.IDCompany && company.Active, objCancellationToken);
        return bCompanyAtiva ? objUnit : null;
    }

    /// <summary>Empresa do token; para o super admin, a do ?company=slug. Sem empresa, devolve o erro.</summary>
    private async Task<(Guid? objCompanyId, ActionResult? objError)> ResolveCompanyAsync(
        string? sCompanySlug, CancellationToken objCancellationToken)
    {
        Guid? objCompanyId = User.GetCompanyId();
        if (objCompanyId is not null)
        {
            return (objCompanyId, null);
        }

        // Super admin: a empresa vem da query string.
        if (string.IsNullOrWhiteSpace(sCompanySlug))
        {
            return (null, BadRequest(new ErrorResponse("Informe a empresa (?company=slug).")));
        }
        Company? objCompany = await _objDbContext.Companies
            .AsNoTracking()
            .FirstOrDefaultAsync(company => company.Slug == sCompanySlug, objCancellationToken);
        if (objCompany is null)
        {
            return (null, NotFound(new ErrorResponse("Empresa não encontrada.")));
        }
        return (objCompany.Id, null);
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

        // URL "Geral" da empresa: mesma regra da URL própria de cada unidade. O DDD também.
        return RedirectUrlRules.Validate(objRequest.RedirectUrl)
            ?? DddRules.Validate(DddRules.Normalize(objRequest.Ddd));
    }
}
