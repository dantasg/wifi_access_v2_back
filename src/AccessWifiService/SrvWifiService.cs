using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AccessWifiService
{
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
                    _objLogger.LogError(objException, "Falha no ciclo diário.");
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

        /// <summary>
        /// Tarefas do dia. Cada uma tem a sua proteção: um relatório que falha (ex.: SMTP fora do ar)
        /// não pode impedir o expurgo da LGPD, e vice-versa.
        /// </summary>
        private async Task RunOnceAsync(CancellationToken objCancellationToken)
        {
            await RunJobAsync("envio de relatórios", async objScope =>
            {
                ReportService objReportService = objScope.ServiceProvider.GetRequiredService<ReportService>();
                // Referência em UTC (leads, período e o carimbo LastReportSentAt são todos UTC).
                await objReportService.SendDueReportsAsync(DateTime.UtcNow.Date, objCancellationToken);
            }, objCancellationToken);

            await RunJobAsync("retenção (LGPD)", async objScope =>
            {
                LeadRetentionService objRetentionService =
                    objScope.ServiceProvider.GetRequiredService<LeadRetentionService>();
                await objRetentionService.PurgeExpiredLeadsAsync(DateTime.UtcNow, objCancellationToken);
            }, objCancellationToken);
        }

        private async Task RunJobAsync(
            string sName, Func<IServiceScope, Task> objJob, CancellationToken objCancellationToken)
        {
            try
            {
                // Escopo próprio: um erro de banco numa tarefa não contamina a outra.
                using IServiceScope objScope = _objScopeFactory.CreateScope();
                await objJob(objScope);
            }
            catch (OperationCanceledException) when (objCancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception objException)
            {
                _objLogger.LogError(objException, "Falha no ciclo diário: {Tarefa}.", sName);
            }
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
