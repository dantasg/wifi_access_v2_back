using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AccessWifiService.WhatsApp
{
    /// <summary>
    /// Roda o <see cref="WhatsAppTemplateService"/> e o <see cref="WhatsAppAlertService"/> a cada minuto: o modelo de
    /// uma campanha salva vai para a Meta em até um minuto, e os alertas na fila saem por e-mail. Uma falha numa volta é
    /// registrada e a próxima tenta de novo; o estado está no banco.
    /// </summary>
    public sealed class WhatsAppTemplateWorker : BackgroundService
    {
        private readonly IServiceScopeFactory _objScopeFactory;
        private readonly WhatsAppTemplateOptions _objOptions;
        private readonly ILogger<WhatsAppTemplateWorker> _objLogger;

        public WhatsAppTemplateWorker(
            IServiceScopeFactory objScopeFactory,
            WhatsAppTemplateOptions objOptions,
            ILogger<WhatsAppTemplateWorker> objLogger)
        {
            _objScopeFactory = objScopeFactory;
            _objOptions = objOptions;
            _objLogger = objLogger;
        }

        protected override async Task ExecuteAsync(CancellationToken objStoppingToken)
        {
            TimeSpan tsInterval = TimeSpan.FromSeconds(Math.Max(5, _objOptions.TickSeconds));
            _objLogger.LogInformation(
                "WhatsApp: modelos de mensagem ativos (a cada {Segundos} s).", tsInterval.TotalSeconds);

            while (!objStoppingToken.IsCancellationRequested)
            {
                try
                {
                    using IServiceScope objScope = _objScopeFactory.CreateScope();
                    WhatsAppTemplateService objService =
                        objScope.ServiceProvider.GetRequiredService<WhatsAppTemplateService>();
                    await objService.TickAsync(DateTime.UtcNow, objStoppingToken);
                    // Os alertas que a API (avisos da Meta) e o serviço puseram na fila (D9).
                    await objScope.ServiceProvider.GetRequiredService<WhatsAppAlertService>()
                        .SendPendingAsync(DateTime.UtcNow, objStoppingToken);
                }
                catch (OperationCanceledException) when (objStoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception objException)
                {
                    _objLogger.LogError(objException, "WhatsApp: falha numa volta dos modelos.");
                }

                try
                {
                    await Task.Delay(tsInterval, objStoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }
}
