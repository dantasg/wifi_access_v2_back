using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AccessWifiService
{
    /// <summary>
    /// Serviço de fundo: uma vez por dia (e ao subir) verifica quais empresas têm relatório
    /// para enviar hoje e dispara o envio dos cadastros do mês anterior.
    /// </summary>
    public class SrvWifiService : BackgroundService
    {
        // Hora do dia (local) da verificação diária.
        private static readonly TimeSpan s_tsDailyCheck = new TimeSpan(8, 0, 0);

        private readonly IServiceScopeFactory _objScopeFactory;
        private readonly ILogger<SrvWifiService> _objLogger;

        public SrvWifiService(IServiceScopeFactory objScopeFactory, ILogger<SrvWifiService> objLogger)
        {
            _objScopeFactory = objScopeFactory;
            _objLogger = objLogger;
        }

        protected override async Task ExecuteAsync(CancellationToken objStoppingToken)
        {
            _objLogger.LogInformation("AccessWifiService iniciado.");

            while (!objStoppingToken.IsCancellationRequested)
            {
                try
                {
                    await RunOnceAsync(objStoppingToken);
                }
                catch (OperationCanceledException) when (objStoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception objException)
                {
                    _objLogger.LogError(objException, "Falha no ciclo de envio de relatórios.");
                }

                TimeSpan tsDelay = TimeUntilNextCheck(DateTime.Now);
                try
                {
                    await Task.Delay(tsDelay, objStoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }

            _objLogger.LogInformation("AccessWifiService encerrado.");
        }

        private async Task RunOnceAsync(CancellationToken objCancellationToken)
        {
            using IServiceScope objScope = _objScopeFactory.CreateScope();
            ReportService objReportService = objScope.ServiceProvider.GetRequiredService<ReportService>();
            // Referência em UTC (leads, período e o carimbo LastReportSentAt são todos UTC).
            await objReportService.SendDueReportsAsync(DateTime.UtcNow.Date, objCancellationToken);
        }

        /// <summary>Tempo até a próxima verificação diária (amanhã no horário configurado).</summary>
        private static TimeSpan TimeUntilNextCheck(DateTime dtNow)
        {
            DateTime dtNext = dtNow.Date.Add(s_tsDailyCheck);
            if (dtNext <= dtNow)
            {
                dtNext = dtNext.AddDays(1);
            }
            return dtNext - dtNow;
        }
    }
}
