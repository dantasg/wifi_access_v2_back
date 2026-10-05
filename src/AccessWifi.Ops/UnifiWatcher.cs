using System.Diagnostics;
using System.Runtime.Versioning;
using System.Threading.Channels;

namespace AccessWifi.Ops
{
    /// <summary>
    /// Serviço accesswifi-unifi (sempre ligado): acompanha o log da API em tempo real (journalctl -f) e entrega
    /// cada linha ao <see cref="UnifiMonitor"/>. Se o journalctl parar, encerra com erro e o systemd reinicia o
    /// serviço em 10 s (Restart=always). A conferência de 5 em 5 min avisa se ele ficar parado.
    /// </summary>
    [SupportedOSPlatform("linux")]
    public class UnifiWatcher
    {
        private readonly INotifier _objNotifier;
        private readonly TimeProvider _objTime;

        public UnifiWatcher(INotifier objNotifier, TimeProvider objTime)
        {
            _objNotifier = objNotifier;
            _objTime = objTime;
        }

        public async Task<int> RunAsync(CancellationToken objStoppingToken)
        {
            UnifiMonitor objMonitor = new UnifiMonitor(_objNotifier);
            using Process objJournal = ProcessRunner.Start(
                "journalctl", ["-u", "accesswifi-api", "-f", "-n", "0", "-o", "cat", "--no-pager"], bRedirectInput: false);
            Console.WriteLine("vigia da UniFi: acompanhando o log da API");

            // Uma tarefa só lê o log; o laço principal acorda a cada linha OU a cada segundo (para os prazos).
            Channel<string> objLines = Channel.CreateUnbounded<string>();
            Task objReader = Task.Run(async () =>
            {
                string? sLine;
                while ((sLine = await objJournal.StandardOutput.ReadLineAsync()) is not null)
                {
                    await objLines.Writer.WriteAsync(sLine);
                }
                objLines.Writer.Complete();
            });

            while (!objStoppingToken.IsCancellationRequested)
            {
                using CancellationTokenSource objSecond = CancellationTokenSource.CreateLinkedTokenSource(objStoppingToken);
                objSecond.CancelAfter(TimeSpan.FromSeconds(1));
                try
                {
                    if (!await objLines.Reader.WaitToReadAsync(objSecond.Token))
                    {
                        Console.WriteLine("journalctl encerrou — o systemd reinicia este serviço");
                        return 1;
                    }
                    while (objLines.Reader.TryRead(out string? sLine))
                    {
                        await objMonitor.LineAsync(sLine, _objTime.GetUtcNow());
                    }
                }
                catch (OperationCanceledException) when (!objStoppingToken.IsCancellationRequested)
                {
                    // passou um segundo sem linha nova
                }
                await objMonitor.TickAsync(_objTime.GetUtcNow());
            }

            objJournal.Kill();
            await objReader.ContinueWith(_ => { });
            return 0;
        }
    }
}
