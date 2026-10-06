using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AccessWifiService.Campaigns
{
    /// <summary>
    /// Roda o <see cref="CampaignEngine"/> a cada poucos segundos. O disparo começa no máximo alguns
    /// segundos depois do horário cadastrado (D10). Uma falha numa volta é registrada e a próxima
    /// volta tenta de novo — nada se perde, o estado está no banco.
    /// </summary>
    public sealed class CampaignWorker : BackgroundService
    {
        private readonly IServiceScopeFactory _objScopeFactory;
        private readonly CampaignEngineOptions _objOptions;
        private readonly ILogger<CampaignWorker> _objLogger;

        public CampaignWorker(
            IServiceScopeFactory objScopeFactory,
            IOptions<CampaignEngineOptions> objOptions,
            ILogger<CampaignWorker> objLogger)
        {
            _objScopeFactory = objScopeFactory;
            _objOptions = objOptions.Value;
            _objLogger = objLogger;
        }

        protected override async Task ExecuteAsync(CancellationToken objStoppingToken)
        {
            TimeSpan tsIntervalo = TimeSpan.FromSeconds(Math.Max(1, _objOptions.TickSeconds));
            _objLogger.LogInformation(
                "Campanhas: agendador ativo (a cada {Segundos} s; PDF por e-mail para cada unidade).",
                tsIntervalo.TotalSeconds);

            while (!objStoppingToken.IsCancellationRequested)
            {
                try
                {
                    using IServiceScope objScope = _objScopeFactory.CreateScope();
                    CampaignEngine objEngine = objScope.ServiceProvider.GetRequiredService<CampaignEngine>();
                    await objEngine.TickAsync(DateTime.UtcNow, objStoppingToken);
                }
                catch (OperationCanceledException) when (objStoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception objException)
                {
                    _objLogger.LogError(objException, "Campanhas: falha numa volta do agendador.");
                }

                try
                {
                    await Task.Delay(tsIntervalo, objStoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }
}
