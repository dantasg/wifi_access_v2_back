using System.Security.Cryptography;
using AccessWifi.Api.Infrastructure.Unifi;
using Microsoft.EntityFrameworkCore;
using Models.DataBase;
using Models.Persistence;
using Models.Security;

namespace AccessWifi.Api.Features.Units;

/// <summary>
/// Lê na nuvem da UniFi os aparelhos de cada unidade (modo nuvem) e grava em <see cref="UnitDevice"/>: é essa
/// lista que diz de qual loja é cada ponto de acesso. Uma chamada por chave de API (a mesma chave costuma
/// enxergar todas as lojas da empresa). Se a leitura de uma unidade falhar, o que já estava gravado continua
/// valendo — só o motivo fica anotado na unidade.
/// </summary>
public class UnitDeviceSync
{
    public record Result(int Units, int Devices, int Failures);

    // O worker, a leitura na hora do portal e o botão do painel gravam a mesma tabela: um de cada vez.
    private static readonly SemaphoreSlim s_objLock = new SemaphoreSlim(1, 1);

    private readonly AppDbContext _objDbContext;
    private readonly UnifiCloudClient _objCloudClient;
    private readonly IEncryptor _objEncryptor;
    private readonly ILogger<UnitDeviceSync> _objLogger;

    public UnitDeviceSync(
        AppDbContext objDbContext, UnifiCloudClient objCloudClient, IEncryptor objEncryptor,
        ILogger<UnitDeviceSync> objLogger)
    {
        _objDbContext = objDbContext;
        _objCloudClient = objCloudClient;
        _objEncryptor = objEncryptor;
        _objLogger = objLogger;
    }

    /// <summary>Sincroniza as unidades indicadas (null = todas as ativas no modo nuvem).</summary>
    public async Task<Result> SyncAsync(
        IReadOnlyCollection<Guid>? objUnitIds = null, CancellationToken objCancellationToken = default)
    {
        await s_objLock.WaitAsync(objCancellationToken);
        try
        {
            return await SyncLockedAsync(objUnitIds, objCancellationToken);
        }
        finally
        {
            s_objLock.Release();
        }
    }

    private async Task<Result> SyncLockedAsync(
        IReadOnlyCollection<Guid>? objUnitIds, CancellationToken objCancellationToken)
    {
        IQueryable<Unit> objQuery = _objDbContext.Units.Where(unit =>
            unit.Active && unit.Unifi.Mode == UnifiMode.Cloud && unit.Unifi.ConsoleId != "" && unit.Unifi.ApiKey != "");
        if (objUnitIds is not null)
        {
            Guid[] arrIds = objUnitIds.ToArray();
            objQuery = objQuery.Where(unit => arrIds.Contains(unit.Id));
        }

        List<Unit> objUnits = await objQuery.ToListAsync(objCancellationToken);
        if (objUnits.Count == 0)
        {
            return new Result(0, 0, 0);
        }

        // Agrupa pela chave já decifrada: o texto cifrado muda a cada gravação, a chave não.
        Dictionary<string, List<Unit>> objByKey = [];
        int iFailures = 0;
        foreach (Unit objUnit in objUnits)
        {
            string sKey = DecryptKey(objUnit.Unifi.ApiKey);
            if (sKey.Length == 0)
            {
                MarkFailed(objUnit, "Chave de API da nuvem UniFi ilegível.");
                iFailures++;
                continue;
            }
            if (!objByKey.TryGetValue(sKey, out List<Unit>? objList))
            {
                objList = [];
                objByKey[sKey] = objList;
            }
            objList.Add(objUnit);
        }

        DateTime dtNowUtc = DateTime.UtcNow;
        int iDevices = 0;
        foreach ((string sKey, List<Unit> objList) in objByKey)
        {
            List<UnifiCloudClient.CloudDevice> objFromCloud;
            try
            {
                objFromCloud = await _objCloudClient.ListDevicesAsync(sKey, objCancellationToken);
            }
            catch (UnifiException objException)
            {
                foreach (Unit objUnit in objList)
                {
                    MarkFailed(objUnit, objException.Message);
                }
                iFailures += objList.Count;
                continue;
            }

            foreach (Unit objUnit in objList)
            {
                string sConsole = objUnit.Unifi.ConsoleId.Trim();
                List<UnifiCloudClient.CloudDevice> objFromConsole = objFromCloud
                    .Where(device => string.Equals(device.HostId, sConsole, StringComparison.OrdinalIgnoreCase))
                    .GroupBy(device => device.Mac)
                    .Select(group => group.First())
                    .ToList();
                if (objFromConsole.Count == 0)
                {
                    MarkFailed(objUnit, "Nenhum aparelho deste console na nuvem da UniFi (confira o console da unidade).");
                    iFailures++;
                    continue;
                }

                await SaveAsync(objUnit, objFromConsole, dtNowUtc, objCancellationToken);
                iDevices += objFromConsole.Count;
            }
        }

        await _objDbContext.SaveChangesAsync(objCancellationToken);
        await WarnRepeatedAsync(objCancellationToken);
        return new Result(objUnits.Count, iDevices, iFailures);
    }

    private async Task SaveAsync(
        Unit objUnit, List<UnifiCloudClient.CloudDevice> objFromConsole, DateTime dtNowUtc,
        CancellationToken objCancellationToken)
    {
        Dictionary<string, UnitDevice> objCurrent = await _objDbContext.UnitDevices
            .Where(device => device.IDUnit == objUnit.Id)
            .ToDictionaryAsync(device => device.Mac, objCancellationToken);

        foreach (UnifiCloudClient.CloudDevice objDevice in objFromConsole)
        {
            if (objCurrent.Remove(objDevice.Mac, out UnitDevice? objExisting))
            {
                objExisting.Name = Truncate(objDevice.Name, 120);
                objExisting.Model = Truncate(objDevice.Model, 60);
                objExisting.SyncedAt = dtNowUtc;
            }
            else
            {
                _objDbContext.UnitDevices.Add(new UnitDevice
                {
                    IDUnit = objUnit.Id,
                    Mac = objDevice.Mac,
                    Name = Truncate(objDevice.Name, 120),
                    Model = Truncate(objDevice.Model, 60),
                    SyncedAt = dtNowUtc,
                });
            }
        }

        // Saiu do console (trocado, levado para outra loja): deixa de identificar esta unidade.
        _objDbContext.UnitDevices.RemoveRange(objCurrent.Values);
        objUnit.DevicesSyncedAt = dtNowUtc;
        objUnit.DevicesSyncError = "";
    }

    /// <summary>O mesmo MAC em duas unidades (duas unidades com o mesmo console): o portal não escolhe por ele.</summary>
    private async Task WarnRepeatedAsync(CancellationToken objCancellationToken)
    {
        List<string> objRepeated = await _objDbContext.UnitDevices
            .GroupBy(device => device.Mac)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToListAsync(objCancellationToken);
        foreach (string sMac in objRepeated)
        {
            _objLogger.LogWarning(
                "Ponto de acesso {Mac} em mais de uma unidade: o portal não escolhe a loja por ele.", sMac);
        }
    }

    private void MarkFailed(Unit objUnit, string sReason)
    {
        objUnit.DevicesSyncError = Truncate(sReason, 300);
        _objLogger.LogWarning(
            "Aparelhos da unidade {Unidade} não lidos na nuvem da UniFi: {Motivo}", objUnit.Slug, sReason);
    }

    private string DecryptKey(string sEncrypted)
    {
        try
        {
            return _objEncryptor.Decrypt(sEncrypted) ?? "";
        }
        catch (Exception objException) when (objException is CryptographicException or FormatException)
        {
            return "";
        }
    }

    private static string Truncate(string sValue, int iMax) => sValue.Length <= iMax ? sValue : sValue[..iMax];
}
