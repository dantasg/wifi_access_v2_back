using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AccessWifiService.SiteContacts
{
    /// <summary>Roda o <see cref="SiteContactNotifier"/> a cada minuto. Uma falha numa volta fica no log e a próxima tenta de novo.</summary>
    public sealed class SiteContactWorker : BackgroundService
    {
        private readonly IServiceScopeFactory _objScopeFactory;
        private readonly SiteContactOptions _objOptions;
        private readonly ILogger<SiteContactWorker> _objLogger;

        public SiteContactWorker(
            IServiceScopeFactory objScopeFactory,
            IOptions<SiteContactOptions> objOptions,
            ILogger<SiteContactWorker> objLogger)
        {
            _objScopeFactory = objScopeFactory;
            _objOptions = objOptions.Value;
            _objLogger = objLogger;
        }

        protected override async Task ExecuteAsync(CancellationToken objStoppingToken)
        {
            if (string.IsNullOrWhiteSpace(_objOptions.NotifyEmail))
            {
                _objLogger.LogInformation("Contatos do site: sem SiteContact:NotifyEmail, o aviso por e-mail está desligado.");
                return;
            }

            TimeSpan tsInterval = TimeSpan.FromSeconds(Math.Max(5, _objOptions.TickSeconds));
            while (!objStoppingToken.IsCancellationRequested)
            {
                try
                {
                    using IServiceScope objScope = _objScopeFactory.CreateScope();
                    SiteContactNotifier objNotifier = objScope.ServiceProvider.GetRequiredService<SiteContactNotifier>();
                    await objNotifier.TickAsync(DateTime.UtcNow, objStoppingToken);
                }
                catch (OperationCanceledException) when (objStoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception objException)
                {
                    _objLogger.LogError(objException, "Contatos do site: falha numa volta do aviso por e-mail.");
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
