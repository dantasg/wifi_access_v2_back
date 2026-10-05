namespace AccessWifi.Ops
{
    /// <summary>Quem recebe os avisos. A interface existe para os testes trocarem o envio por uma lista.</summary>
    public interface INotifier
    {
        Task NotifyAsync(string sTitle, string sText);
    }

    /// <summary>
    /// Avisa pelo Telegram e pelo e-mail. Nunca derruba quem chamou: o canal que falhar fica registrado no log
    /// (journal) e o outro segue. Em simulação, só mostra o que mandaria.
    /// </summary>
    public class Notifier : INotifier
    {
        private readonly OpsSettings _objSettings;
        private readonly TimeProvider _objTime;
        private readonly bool _bSimulate;

        public Notifier(OpsSettings objSettings, TimeProvider objTime, bool bSimulate)
        {
            _objSettings = objSettings;
            _objTime = objTime;
            _bSimulate = bSimulate;
        }

        public async Task NotifyAsync(string sTitle, string sText)
        {
            string sBody = $"{sTitle}\n\n{sText}".Trim() + $"\n\n— AccessWifi · {Clock.Format(Clock.Now(_objTime))}";
            if (_bSimulate)
            {
                Console.WriteLine($"[simulação] avisaria:\n{sBody}\n");
                return;
            }

            await SendChannelAsync("Telegram", sTitle, () => SendTelegramAsync(sBody));
            await SendChannelAsync("e-mail", sTitle, () => AlertEmail.SendAsync(_objSettings, sTitle, sBody));
        }

        public Task SendTelegramAsync(string sText)
        {
            if (!_objSettings.HasTelegram)
            {
                throw new InvalidOperationException("Telegram não configurado (rode: accesswifi-ops configurar)");
            }
            return new TelegramClient(_objSettings.Get(OpsSettings.KeyTelegramToken))
                .SendMessageAsync(_objSettings.Get(OpsSettings.KeyTelegramChat), sText);
        }

        private static async Task SendChannelAsync(string sChannel, string sTitle, Func<Task> objSend)
        {
            try
            {
                await objSend();
                Console.WriteLine($"aviso enviado por {sChannel}: {sTitle}");
            }
            catch (Exception objException)
            {
                Console.WriteLine($"AVISO NÃO ENVIADO por {sChannel} ({objException.Message}): {sTitle}");
            }
        }
    }
}
