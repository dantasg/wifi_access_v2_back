namespace AccessWifi.Api.Features.Units;

/// <summary>
/// Mantém a lista de aparelhos das unidades em dia: lê a nuvem da UniFi logo depois de a API subir e
/// depois a cada "UnitDevices:SyncMinutes" (padrão 5). Uma chamada por chave de API, ~0,2 s para 18 lojas.
/// AP novo antes da próxima leitura é achado pela leitura na hora do portal (<see cref="UnitLocator"/>).
/// </summary>
public class UnitDeviceSyncWorker : BackgroundService
{
    private static readonly TimeSpan s_tsFirstDelay = TimeSpan.FromSeconds(15);

    private readonly IServiceScopeFactory _objScopeFactory;
    private readonly ILogger<UnitDeviceSyncWorker> _objLogger;
    private readonly TimeSpan _tsInterval;

    public UnitDeviceSyncWorker(
        IServiceScopeFactory objScopeFactory, IConfiguration objConfiguration, ILogger<UnitDeviceSyncWorker> objLogger)
    {
        _objScopeFactory = objScopeFactory;
        _objLogger = objLogger;
        _tsInterval = TimeSpan.FromMinutes(Math.Max(1, objConfiguration.GetValue("UnitDevices:SyncMinutes", 5)));
    }

    protected override async Task ExecuteAsync(CancellationToken objStoppingToken)
    {
        try
        {
            await Task.Delay(s_tsFirstDelay, objStoppingToken);
            while (!objStoppingToken.IsCancellationRequested)
            {
                await SyncOnceAsync(objStoppingToken);
                await Task.Delay(_tsInterval, objStoppingToken);
            }
        }
        catch (OperationCanceledException) when (objStoppingToken.IsCancellationRequested)
        {
            // API desligando.
        }
    }

    private async Task SyncOnceAsync(CancellationToken objStoppingToken)
    {
        try
        {
            using IServiceScope objScope = _objScopeFactory.CreateScope();
            UnitDeviceSync.Result objResult = await objScope.ServiceProvider
                .GetRequiredService<UnitDeviceSync>()
                .SyncAsync(null, objStoppingToken);
            if (objResult.Units > 0)
            {
                _objLogger.LogInformation(
                    "Aparelhos das unidades lidos na nuvem da UniFi: {Unidades} unidades, {Aparelhos} aparelhos, {Falhas} falhas.",
                    objResult.Units, objResult.Devices, objResult.Failures);
            }
        }
        catch (Exception objException) when (!objStoppingToken.IsCancellationRequested)
        {
            // Nunca derruba a API: os aparelhos já gravados continuam valendo até a próxima rodada.
            _objLogger.LogError(objException, "Falha ao ler os aparelhos das unidades na nuvem da UniFi.");
        }
    }
}
