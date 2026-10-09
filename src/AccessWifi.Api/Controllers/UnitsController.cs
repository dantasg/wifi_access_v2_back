using System.Text.RegularExpressions;
using AccessWifi.Api.Features;
using AccessWifi.Api.Features.Units;
using Models.Persistence;
using AccessWifi.Api.Infrastructure.Security;
using AccessWifi.Api.Infrastructure.Unifi;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Models.DataBase;
using Models.Security;

namespace AccessWifi.Api.Controllers;

/// <summary>
/// Unidades (franquias). A listagem é aberta a qualquer admin (o admin de empresa vê só as
/// da sua empresa); criar/editar é exclusivo do super admin.
/// </summary>
[ApiController]
[Route("admin/units")]
[Authorize]
public partial class UnitsController : ControllerBase
{
    [GeneratedRegex("^[a-z0-9-]{2,40}$")]
    private static partial Regex SlugRegex();

    // FQDN simples: rótulos alfanuméricos com hífen no meio, pelo menos um ponto.
    [GeneratedRegex(@"^[a-z0-9]([a-z0-9-]*[a-z0-9])?(\.[a-z0-9]([a-z0-9-]*[a-z0-9])?)+$")]
    private static partial Regex PortalHostRegex();

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$")]
    private static partial Regex EmailRegex();

    private readonly AppDbContext _objDbContext;
    private readonly IEncryptor _objEncryptor;
    private readonly IUnifiClient _objUnifiClient;
    private readonly ILogger<UnitsController> _objLogger;
    private readonly UnitDeviceSync? _objDeviceSync;

    public UnitsController(
        AppDbContext objDbContext,
        IEncryptor objEncryptor,
        IUnifiClient objUnifiClient,
        ILogger<UnitsController> objLogger,
        UnitDeviceSync? objDeviceSync = null)
    {
        _objDbContext = objDbContext;
        _objEncryptor = objEncryptor;
        _objUnifiClient = objUnifiClient;
        _objLogger = objLogger;
        _objDeviceSync = objDeviceSync;
    }

    /// <summary>
    /// Lista unidades (sem expor a senha da UniFi). O admin de empresa recebe só as unidades
    /// da própria empresa (o filtro ?company é ignorado) e o usuário de unidade, só as dele. O super
    /// admin lista todas, com filtro opcional ?company={id da empresa}.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<List<UnitDto>>> GetAll(
        [FromQuery(Name = "company")] Guid? objCompanyId, CancellationToken objCancellationToken)
    {
        IQueryable<Unit> objQuery = _objDbContext.Units.AsNoTracking();

        Guid? objTokenCompanyId = User.GetCompanyId();
        if (objTokenCompanyId is not null)
        {
            // Admin de empresa: sempre restrito à própria empresa; usuário de unidade, às dele.
            AccessScope objScope = await AccessScope.LoadAsync(_objDbContext, User, objCancellationToken);
            objQuery = objScope.Apply(objQuery.Where(unit => unit.IDCompany == objTokenCompanyId));
        }
        else if (objCompanyId is not null)
        {
            // Super admin: filtro opcional por empresa.
            objQuery = objQuery.Where(unit => unit.IDCompany == objCompanyId);
        }

        List<Unit> objUnits = await objQuery
            .OrderBy(unit => unit.Name)
            .ToListAsync(objCancellationToken);

        Guid[] arrIds = objUnits.Select(unit => unit.Id).ToArray();
        Dictionary<Guid, int> objDeviceCounts = await _objDbContext.UnitDevices.AsNoTracking()
            .Where(device => arrIds.Contains(device.IDUnit))
            .GroupBy(device => device.IDUnit)
            .Select(group => new { group.Key, Count = group.Count() })
            .ToDictionaryAsync(item => item.Key, item => item.Count, objCancellationToken);

        return Ok(objUnits
            .Select(unit => UnitDto.FromEntity(unit, objDeviceCounts.GetValueOrDefault(unit.Id)))
            .ToList());
    }

    /// <summary>Pontos de acesso da unidade, como foram lidos na nuvem da UniFi.</summary>
    [HttpGet("{id:guid}/devices")]
    [Authorize(Roles = ClaimsExtensions.RoleSuperAdmin)]
    public async Task<ActionResult<List<UnitDeviceDto>>> GetDevices(
        Guid id, CancellationToken objCancellationToken)
    {
        bool bExists = await _objDbContext.Units.AnyAsync(unit => unit.Id == id, objCancellationToken);
        if (!bExists)
        {
            return NotFound(new ErrorResponse("Unidade não encontrada."));
        }

        List<UnitDevice> objDevices = await _objDbContext.UnitDevices.AsNoTracking()
            .Where(device => device.IDUnit == id)
            .OrderBy(device => device.Name)
            .ThenBy(device => device.Mac)
            .ToListAsync(objCancellationToken);
        return Ok(objDevices.Select(UnitDeviceDto.FromEntity).ToList());
    }

    /// <summary>
    /// Lê agora, na nuvem da UniFi, os pontos de acesso de todas as unidades no modo nuvem (o serviço já
    /// faz isso sozinho a cada 5 minutos; o botão é para não esperar depois de instalar uma loja).
    /// </summary>
    [HttpPost("devices/sync")]
    [Authorize(Roles = ClaimsExtensions.RoleSuperAdmin)]
    public async Task<ActionResult<UnitDeviceSyncResponse>> SyncDevices(CancellationToken objCancellationToken)
    {
        if (_objDeviceSync is null)
        {
            return Ok(new UnitDeviceSyncResponse(0, 0, 0));
        }

        UnitDeviceSync.Result objResult = await _objDeviceSync.SyncAsync(null, objCancellationToken);
        return Ok(new UnitDeviceSyncResponse(objResult.Units, objResult.Devices, objResult.Failures));
    }

    /// <summary>Cria uma unidade. O slug identifica o portal (?unit=slug), é único e imutável.</summary>
    [HttpPost]
    [Authorize(Roles = ClaimsExtensions.RoleSuperAdmin)]
    public async Task<ActionResult<UnitDto>> Create(
        CreateUnitRequest objRequest, CancellationToken objCancellationToken)
    {
        if (string.IsNullOrWhiteSpace(objRequest.Name) || objRequest.Name.Trim().Length > 120)
        {
            return BadRequest(new ErrorResponse("Nome é obrigatório (máximo de 120 caracteres)."));
        }

        if (objRequest.Slug is null || !SlugRegex().IsMatch(objRequest.Slug))
        {
            return BadRequest(new ErrorResponse(
                "Slug inválido: use só letras minúsculas, números e hífen (2 a 40 caracteres)."));
        }

        bool bCompanyExists = await _objDbContext.Companies
            .AnyAsync(company => company.Id == objRequest.IDCompany, objCancellationToken);
        if (!bCompanyExists)
        {
            return BadRequest(new ErrorResponse("Empresa não encontrada."));
        }

        bool bSlugInUse = await _objDbContext.Units
            .AnyAsync(unit => unit.Slug == objRequest.Slug, objCancellationToken);
        if (bSlugInUse)
        {
            return BadRequest(new ErrorResponse("Já existe uma unidade com esse slug."));
        }

        Unit objUnit = new Unit
        {
            IDCompany = objRequest.IDCompany,
            Name = objRequest.Name.Trim(),
            Slug = objRequest.Slug,
        };

        string? sHostError = await ApplyPortalHostAsync(
            objUnit, objRequest.PortalHost, objCancellationToken);
        if (sHostError is not null)
        {
            return BadRequest(new ErrorResponse(sHostError));
        }

        string? sRedirectError = ApplyRedirectUrl(objUnit, objRequest.RedirectUrl)
            ?? ApplyEmail(objUnit, objRequest.Email)
            ?? ApplyAreaCode(objUnit, objRequest.AreaCode);
        if (sRedirectError is not null)
        {
            return BadRequest(new ErrorResponse(sRedirectError));
        }

        ApplyUnifi(objUnit, objRequest.Unifi);

        _objDbContext.Units.Add(objUnit);
        await _objDbContext.SaveChangesAsync(objCancellationToken);

        return Ok(await WithDevicesAsync(objUnit, objCancellationToken));
    }

    /// <summary>
    /// Atualiza nome, situação, endereço do portal, URL de redirecionamento e config UniFi
    /// (nulos = manter o atual).
    /// </summary>
    [HttpPut("{id:guid}")]
    [Authorize(Roles = ClaimsExtensions.RoleSuperAdmin)]
    public async Task<ActionResult<UnitDto>> Update(
        Guid id, UpdateUnitRequest objRequest, CancellationToken objCancellationToken)
    {
        Unit? objUnit = await _objDbContext.Units
            .FirstOrDefaultAsync(unit => unit.Id == id, objCancellationToken);
        if (objUnit is null)
        {
            return NotFound(new ErrorResponse("Unidade não encontrada."));
        }

        if (string.IsNullOrWhiteSpace(objRequest.Name) || objRequest.Name.Trim().Length > 120)
        {
            return BadRequest(new ErrorResponse("Nome é obrigatório (máximo de 120 caracteres)."));
        }

        objUnit.Name = objRequest.Name.Trim();
        objUnit.Active = objRequest.Active;
        string sConsoleBefore = objUnit.Unifi.Mode == UnifiMode.Cloud ? objUnit.Unifi.ConsoleId : "";

        string? sHostError = await ApplyPortalHostAsync(
            objUnit, objRequest.PortalHost, objCancellationToken);
        if (sHostError is not null)
        {
            return BadRequest(new ErrorResponse(sHostError));
        }

        string? sRedirectError = ApplyRedirectUrl(objUnit, objRequest.RedirectUrl)
            ?? ApplyEmail(objUnit, objRequest.Email)
            ?? ApplyAreaCode(objUnit, objRequest.AreaCode);
        if (sRedirectError is not null)
        {
            return BadRequest(new ErrorResponse(sRedirectError));
        }

        ApplyUnifi(objUnit, objRequest.Unifi);

        string sConsoleAfter = objUnit.Unifi.Mode == UnifiMode.Cloud ? objUnit.Unifi.ConsoleId : "";
        if (sConsoleAfter != sConsoleBefore)
        {
            // Outro console (ou saiu da nuvem): os pontos de acesso gravados são de outro equipamento.
            _objDbContext.UnitDevices.RemoveRange(
                await _objDbContext.UnitDevices.Where(device => device.IDUnit == objUnit.Id).ToListAsync(objCancellationToken));
            objUnit.DevicesSyncedAt = null;
            objUnit.DevicesSyncError = "";
        }

        await _objDbContext.SaveChangesAsync(objCancellationToken);

        return Ok(await WithDevicesAsync(objUnit, objCancellationToken));
    }

    /// <summary>
    /// Depois de gravar uma unidade no modo nuvem, já lê os pontos de acesso dela — a loja nova é
    /// reconhecida no portal sem esperar a próxima rodada. Falhar aqui não desfaz a gravação: o motivo
    /// aparece na unidade e o serviço tenta de novo sozinho.
    /// </summary>
    private async Task<UnitDto> WithDevicesAsync(Unit objUnit, CancellationToken objCancellationToken)
    {
        if (_objDeviceSync is not null && objUnit.Active && objUnit.Unifi.Mode == UnifiMode.Cloud)
        {
            try
            {
                await _objDeviceSync.SyncAsync([objUnit.Id], objCancellationToken);
            }
            catch (Exception objException) when (objException is not OperationCanceledException)
            {
                _objLogger.LogWarning(
                    objException, "Pontos de acesso da unidade {Slug} não lidos depois de salvar.", objUnit.Slug);
            }
        }

        int iDevices = await _objDbContext.UnitDevices
            .CountAsync(device => device.IDUnit == objUnit.Id, objCancellationToken);
        return UnitDto.FromEntity(objUnit, iDevices);
    }

    /// <summary>
    /// D7: valida a configuração da unidade sem autorizar ninguém. Falha de configuração volta
    /// como 200 com success=false — é resultado de teste, não erro da requisição.
    /// </summary>
    [HttpPost("{id:guid}/unifi/test")]
    [Authorize(Roles = ClaimsExtensions.RoleSuperAdmin)]
    public async Task<ActionResult<UnifiTestResponse>> TestUnifi(
        Guid id, CancellationToken objCancellationToken)
    {
        Unit? objUnit = await _objDbContext.Units
            .FirstOrDefaultAsync(unit => unit.Id == id, objCancellationToken);
        if (objUnit is null)
        {
            return NotFound(new ErrorResponse("Unidade não encontrada."));
        }

        try
        {
            string sDetail = await _objUnifiClient.TestConnectionAsync(
                objUnit.Unifi, objCancellationToken);

            // O teste pode ter descoberto o SiteId; guardar aqui evita a descoberta no primeiro
            // visitante, que é justamente a hora em que ninguém quer surpresa.
            await _objDbContext.SaveChangesAsync(objCancellationToken);

            return Ok(new UnifiTestResponse(true, sDetail));
        }
        catch (UnifiException objException)
        {
            // Sem dados pessoais no log — só a unidade e o motivo técnico.
            _objLogger.LogWarning(
                objException, "Teste de conexão UniFi falhou na unidade {Slug}.", objUnit.Slug);
            return Ok(new UnifiTestResponse(false, objException.Message));
        }
    }

    /// <summary>
    /// Grava o endereço do portal da unidade. Nulo mantém o atual, "" limpa. Devolve a mensagem
    /// de erro ou null. O host é único porque é ele que identifica a unidade quando a UniFi não
    /// consegue mandar o "?unit=" — dois donos para o mesmo endereço seria ambíguo.
    /// </summary>
    private async Task<string?> ApplyPortalHostAsync(
        Unit objUnit, string? sPortalHost, CancellationToken objCancellationToken)
    {
        if (sPortalHost is null)
        {
            return null;
        }

        string sHost = UnitResolver.NormalizeHost(sPortalHost);
        if (sHost.Length == 0)
        {
            objUnit.PortalHost = "";
            return null;
        }

        if (sHost.Length > 200 || !PortalHostRegex().IsMatch(sHost))
        {
            return "Endereço do portal inválido: informe um domínio, como itaituba.wifi.exemplo.com.br.";
        }

        bool bInUse = await _objDbContext.Units.AnyAsync(
            unit => unit.PortalHost == sHost && unit.Id != objUnit.Id, objCancellationToken);
        if (bInUse)
        {
            return "Já existe uma unidade usando esse endereço de portal.";
        }

        objUnit.PortalHost = sHost;
        return null;
    }

    /// <summary>
    /// Grava para onde o visitante desta unidade vai depois de liberado (ex.: o Instagram da loja).
    /// Nulo mantém a atual; "" limpa, e a unidade volta a usar a URL "Geral" da empresa. Devolve a
    /// mensagem de erro ou null.
    /// </summary>
    private static string? ApplyRedirectUrl(Unit objUnit, string? sRedirectUrl)
    {
        if (sRedirectUrl is null)
        {
            return null;
        }

        string? sError = RedirectUrlRules.Validate(sRedirectUrl);
        if (sError is not null)
        {
            return sError;
        }

        objUnit.RedirectUrl = sRedirectUrl.Trim();
        return null;
    }

    /// <summary>DDD da loja no exemplo de telefone do portal. Nulo mantém o atual; "" volta a usar o da empresa.</summary>
    private static string? ApplyAreaCode(Unit objUnit, string? sAreaCode)
    {
        string? sDigits = AreaCodeRules.Normalize(sAreaCode);
        if (sDigits is null)
        {
            return null;
        }

        string? sError = AreaCodeRules.Validate(sDigits);
        if (sError is not null)
        {
            return sError;
        }

        objUnit.AreaCode = sDigits;
        return null;
    }

    /// <summary>
    /// Grava o e-mail da unidade (relatório mensal e PDF das campanhas). Nulo mantém o atual; "" limpa.
    /// Devolve a mensagem de erro ou null.
    /// </summary>
    private static string? ApplyEmail(Unit objUnit, string? sEmail)
    {
        if (sEmail is null)
        {
            return null;
        }

        string sValue = sEmail.Trim();
        if (sValue.Length > 200 || (sValue.Length > 0 && !EmailRegex().IsMatch(sValue)))
        {
            return "E-mail da unidade inválido.";
        }

        objUnit.Email = sValue;
        return null;
    }

    private void ApplyUnifi(Unit objUnit, UnitUnifiRequest? objUnifi)
    {
        if (objUnifi is null)
        {
            return;
        }

        objUnit.Unifi.Mode =
            string.Equals(objUnifi.Mode, UnifiMode.Cloud, StringComparison.OrdinalIgnoreCase)
                ? UnifiMode.Cloud
                : UnifiMode.Local;

        objUnit.Unifi.Host = objUnifi.Host?.Trim() ?? "";
        objUnit.Unifi.Site = string.IsNullOrWhiteSpace(objUnifi.Site) ? "default" : objUnifi.Site.Trim();
        objUnit.Unifi.Username = objUnifi.Username?.Trim() ?? "";
        objUnit.Unifi.UnifiOs = objUnifi.UnifiOs;
        objUnit.Unifi.VerifySsl = objUnifi.VerifySsl;
        // Senha nula = manter a atual; caso contrário, guarda cifrada.
        if (objUnifi.Password is not null)
        {
            objUnit.Unifi.Password = _objEncryptor.Encrypt(objUnifi.Password) ?? "";
        }

        string sConsoleId = objUnifi.ConsoleId?.Trim() ?? "";
        if (objUnifi.ConsoleId is not null && sConsoleId != objUnit.Unifi.ConsoleId)
        {
            // Trocou de console: o site guardado é de outro equipamento e precisa ser redescoberto.
            objUnit.Unifi.ConsoleId = sConsoleId;
            objUnit.Unifi.SiteId = "";
        }

        if (objUnifi.SiteId is not null)
        {
            objUnit.Unifi.SiteId = objUnifi.SiteId.Trim();
        }

        // Chave nula = manter a atual, mesma regra da senha.
        if (objUnifi.ApiKey is not null)
        {
            objUnit.Unifi.ApiKey = _objEncryptor.Encrypt(objUnifi.ApiKey) ?? "";
        }
    }
}
