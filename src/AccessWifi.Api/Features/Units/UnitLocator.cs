using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Models.DataBase;
using Models.Persistence;

namespace AccessWifi.Api.Features.Units;

/// <summary>
/// Acha a unidade do portal (PROPOSTA_UNIDADE_PELO_AP.md). Ordem:
/// <list type="number">
///   <item><c>?unit=</c> (slug) — como sempre;</item>
///   <item>o MAC do ponto de acesso (<c>ap</c>) que a UniFi manda em toda visita, na lista de aparelhos das
///   unidades. AP ainda desconhecido: lê a nuvem da UniFi na hora (uma vez, com limite de tempo) e procura de
///   novo;</item>
///   <item>o endereço em que o portal foi aberto (<c>PortalHost</c>) — mas, com um AP que nenhuma unidade tem,
///   só enquanto a unidade do endereço ainda não tiver a lista de aparelhos. Depois disso o endereço pode ser
///   de várias lojas, e chutar mandaria o cadastro e a liberação para a loja errada.</item>
/// </list>
/// Na visita normal é só uma consulta ao banco: nada fica mais lento para o visitante.
/// </summary>
public class UnitLocator
{
    // Leitura na hora: no máximo uma a cada 30 s (um AP inventado não vira enxurrada na nuvem), com no
    // máximo 4 s de espera, e o mesmo AP desconhecido só volta a provocar leitura depois de 10 min.
    private const string LastLiveKey = "ap-leitura-na-hora";
    private static readonly SemaphoreSlim s_objLiveLock = new SemaphoreSlim(1, 1);
    private static readonly TimeSpan s_tsLiveInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan s_tsLiveTimeout = TimeSpan.FromSeconds(4);
    private static readonly TimeSpan s_tsUnknownCache = TimeSpan.FromMinutes(10);

    private readonly AppDbContext _objDbContext;
    private readonly IServiceScopeFactory? _objScopeFactory;
    private readonly IMemoryCache? _objCache;
    private readonly ILogger<UnitLocator>? _objLogger;

    /// <summary>
    /// Sem <paramref name="objScopeFactory"/> e <paramref name="objCache"/> não há leitura na hora: só o
    /// que já está gravado (testes e chamadores que não podem esperar a nuvem).
    /// </summary>
    public UnitLocator(
        AppDbContext objDbContext, IServiceScopeFactory? objScopeFactory = null, IMemoryCache? objCache = null,
        ILogger<UnitLocator>? objLogger = null)
    {
        _objDbContext = objDbContext;
        _objScopeFactory = objScopeFactory;
        _objCache = objCache;
        _objLogger = objLogger;
    }

    public async Task<Unit?> FindAsync(
        IQueryable<Unit> objUnits, string? sUnitSlug, string? sPortalHost, string? sAp,
        CancellationToken objCancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(sUnitSlug))
        {
            return await UnitResolver.FindAsync(objUnits, sUnitSlug, null, objCancellationToken);
        }

        string sMac = MacAddress.Normalize(sAp);
        List<Guid> objByAp = [];
        if (sMac.Length > 0)
        {
            objByAp = await UnitsWithDeviceAsync(sMac, objCancellationToken);
            if (objByAp.Count == 0 && await LiveLookupAsync(sMac, objCancellationToken))
            {
                objByAp = await UnitsWithDeviceAsync(sMac, objCancellationToken);
            }

            if (objByAp.Count == 1)
            {
                Guid objUnitId = objByAp[0];
                Unit? objUnit = await objUnits.FirstOrDefaultAsync(unit => unit.Id == objUnitId, objCancellationToken);
                if (objUnit is not null)
                {
                    return objUnit;
                }
            }
        }

        // Endereço do portal.
        string sHost = UnitResolver.NormalizeHost(sPortalHost);
        if (sHost.Length == 0)
        {
            return null;
        }
        Unit? objFromAddress = await objUnits.FirstOrDefaultAsync(unit => unit.PortalHost == sHost, objCancellationToken);
        if (objFromAddress is null || sMac.Length == 0)
        {
            return objFromAddress;
        }

        // O mesmo AP em mais de uma unidade: só aceita se a do endereço for uma delas.
        if (objByAp.Count > 1)
        {
            return objByAp.Contains(objFromAddress.Id) ? objFromAddress : null;
        }

        // AP que nenhuma unidade tem: o endereço só vale enquanto a unidade dele não tem a lista de aparelhos.
        bool bHasList = await _objDbContext.UnitDevices
            .AnyAsync(device => device.IDUnit == objFromAddress.Id, objCancellationToken);
        if (!bHasList)
        {
            return objFromAddress;
        }

        _objLogger?.LogWarning(
            "Portal aberto por um ponto de acesso desconhecido {Mac} no endereço {Host}: a loja não foi identificada.",
            sMac, sHost);
        return null;
    }

    private Task<List<Guid>> UnitsWithDeviceAsync(string sMac, CancellationToken objCancellationToken) =>
        _objDbContext.UnitDevices.AsNoTracking()
            .Where(device => device.Mac == sMac)
            .Select(device => device.IDUnit)
            .Distinct()
            .ToListAsync(objCancellationToken);

    /// <summary>AP ainda desconhecido: lê a nuvem da UniFi agora (todas as unidades) e diz se ele apareceu.</summary>
    private async Task<bool> LiveLookupAsync(string sMac, CancellationToken objCancellationToken)
    {
        if (_objScopeFactory is null || _objCache is null)
        {
            return false;
        }

        string sCacheKey = "ap-desconhecido:" + sMac;
        if (_objCache.TryGetValue(sCacheKey, out _))
        {
            return false;
        }

        using CancellationTokenSource objTimeout = CancellationTokenSource.CreateLinkedTokenSource(objCancellationToken);
        objTimeout.CancelAfter(s_tsLiveTimeout);
        try
        {
            await s_objLiveLock.WaitAsync(objTimeout.Token);
            try
            {
                // Outro visitante pode ter acabado de ler a nuvem enquanto este esperava.
                bool bAlreadySaved = await _objDbContext.UnitDevices.AsNoTracking()
                    .AnyAsync(device => device.Mac == sMac, objTimeout.Token);
                if (!bAlreadySaved && !_objCache.TryGetValue(LastLiveKey, out _))
                {
                    _objCache.Set(LastLiveKey, true, s_tsLiveInterval);
                    // Escopo próprio: uma leitura cortada no meio não deixa nada pendente no banco desta requisição.
                    using IServiceScope objScope = _objScopeFactory.CreateScope();
                    await objScope.ServiceProvider.GetRequiredService<UnitDeviceSync>().SyncAsync(null, objTimeout.Token);
                }
            }
            finally
            {
                s_objLiveLock.Release();
            }
        }
        catch (Exception objException) when (!objCancellationToken.IsCancellationRequested)
        {
            _objLogger?.LogWarning(
                objException, "Leitura dos aparelhos na nuvem da UniFi falhou ao procurar o ponto de acesso {Mac}.", sMac);
        }

        bool bFound = await _objDbContext.UnitDevices.AsNoTracking()
            .AnyAsync(device => device.Mac == sMac, objCancellationToken);
        if (!bFound)
        {
            _objCache.Set(sCacheKey, true, s_tsUnknownCache);
        }
        return bFound;
    }
}
