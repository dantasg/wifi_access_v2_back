using System.Text.RegularExpressions;

namespace AccessWifi.Ops
{
    /// <summary>
    /// Liberações recusadas pela UniFi (o cliente preencheu o portal e ficou sem internet): avisa na PRIMEIRA,
    /// manda um resumo a cada 10 min enquanto continuar e avisa quando a próxima liberação dá certo.
    ///
    /// Só a regra — quem lê o log em tempo real é o <see cref="UnifiWatcher"/>. O horário entra por parâmetro,
    /// para os testes simularem a passagem do tempo. Limites conhecidos: a linha de sucesso não diz a unidade
    /// (com várias unidades, qualquer liberação boa encerra o incidente) e só o modo nuvem registra sucesso.
    /// </summary>
    public partial class UnifiMonitor
    {
        /// <summary>O motivo vem na linha seguinte do log; sem ele em 3 s, avisa mesmo assim.</summary>
        public static readonly TimeSpan s_tsWaitReason = TimeSpan.FromSeconds(3);

        public static readonly TimeSpan s_tsSummary = TimeSpan.FromMinutes(10);

        // Linhas que a API escreve (AuthorizeController e UnifiCloudClient).
        [GeneratedRegex(@"Falha ao autorizar guest na UniFi da unidade (\S+?)\.?$")]
        private static partial Regex FailureRegex();

        private const string SuccessText = "Autorização UniFi"; // "...(nuvem) pelo caminho clássico em 812 ms."
        private const string ReasonText = "UnifiException:";

        private readonly INotifier _objNotifier;
        private Incident? _objIncident;
        private (string Unit, DateTimeOffset At)? _objPending;

        public UnifiMonitor(INotifier objNotifier)
        {
            _objNotifier = objNotifier;
        }

        /// <summary>Recusas desde o primeiro aviso (null = tudo normal).</summary>
        public Incident? CurrentIncident => _objIncident;

        public bool WaitingReason => _objPending is not null;

        public async Task LineAsync(string sLine, DateTimeOffset dtNow)
        {
            string sText = sLine.Trim();
            Match objFailure = FailureRegex().Match(sText);
            if (objFailure.Success)
            {
                await CloseWithoutReasonAsync();
                _objPending = (objFailure.Groups[1].Value, dtNow);
            }
            else if (sText.Contains(ReasonText) && _objPending is not null)
            {
                (string sUnit, DateTimeOffset dtAt) = _objPending.Value;
                _objPending = null;
                await RegisterAsync(sUnit, sText[(sText.IndexOf(ReasonText, StringComparison.Ordinal) + ReasonText.Length)..].Trim(), dtAt);
            }
            else if (sText.Contains(SuccessText))
            {
                await CloseWithoutReasonAsync();
                if (_objIncident is not null)
                {
                    Incident objIncident = _objIncident;
                    _objIncident = null;
                    await _objNotifier.NotifyAsync("✅ UniFi voltou a liberar os clientes",
                        $"{objIncident.Attempts} tentativa(s) recusada(s) entre {Clock.Time(objIncident.Since)} e "
                        + $"{Clock.Time(objIncident.Last)} ({objIncident.PerUnit()}).\n"
                        + $"A liberação das {Clock.Time(dtNow)} deu certo.");
                }
            }
        }

        /// <summary>Chamado a cada segundo sem linha nova: fecha a recusa sem motivo e manda o resumo, se for a hora.</summary>
        public async Task TickAsync(DateTimeOffset dtNow)
        {
            if (_objPending is not null && dtNow - _objPending.Value.At >= s_tsWaitReason)
            {
                await CloseWithoutReasonAsync();
            }

            if (_objIncident is { NewSinceAlert: > 0 } && dtNow - _objIncident.LastAlert >= s_tsSummary)
            {
                await _objNotifier.NotifyAsync("🔴 UniFi continua recusando liberações",
                    $"Mais {_objIncident.NewSinceAlert} tentativa(s) recusada(s) desde o último aviso — "
                    + $"{_objIncident.Attempts} desde {Clock.Time(_objIncident.Since)} ({_objIncident.PerUnit()}).\n"
                    + $"Motivo: {_objIncident.Reasons()}");
                _objIncident.NewSinceAlert = 0;
                _objIncident.LastAlert = dtNow;
            }
        }

        private async Task CloseWithoutReasonAsync()
        {
            if (_objPending is not null)
            {
                (string sUnit, DateTimeOffset dtAt) = _objPending.Value;
                _objPending = null;
                await RegisterAsync(sUnit, null, dtAt);
            }
        }

        private async Task RegisterAsync(string sUnit, string? sReason, DateTimeOffset dtAt)
        {
            string sCause = string.IsNullOrWhiteSpace(sReason) ? "(sem detalhe no log)" : sReason;
            if (_objIncident is null)
            {
                _objIncident = new Incident { Since = dtAt, Last = dtAt, LastAlert = dtAt, Attempts = 1 };
                _objIncident.Count(sUnit, sCause);
                await _objNotifier.NotifyAsync("🔴 UniFi recusou a liberação de um cliente",
                    $"Unidade: {sUnit}\nMotivo: {sCause}\n\n"
                    + "O cliente preencheu o portal e NÃO ganhou internet. Se continuar, chega um resumo a cada "
                    + "10 min; quando a próxima liberação der certo, chega o aviso de que voltou.\n"
                    + "Ver PRODUCAO.md §7 (tabela de causas).");
                return;
            }

            _objIncident.Attempts++;
            _objIncident.NewSinceAlert++;
            _objIncident.Last = dtAt;
            _objIncident.Count(sUnit, sCause);
        }

        public class Incident
        {
            public DateTimeOffset Since { get; set; }
            public DateTimeOffset Last { get; set; }
            public DateTimeOffset LastAlert { get; set; }
            public int Attempts { get; set; }
            public int NewSinceAlert { get; set; }
            public Dictionary<string, int> Units { get; } = new Dictionary<string, int>();
            public Dictionary<string, int> Causes { get; } = new Dictionary<string, int>();

            public void Count(string sUnit, string sCause)
            {
                Units[sUnit] = Units.GetValueOrDefault(sUnit) + 1;
                Causes[sCause] = Causes.GetValueOrDefault(sCause) + 1;
            }

            public string PerUnit()
            {
                return string.Join(", ", Units.OrderBy(pair => pair.Key).Select(pair => $"{pair.Key}: {pair.Value}"));
            }

            public string Reasons()
            {
                return string.Join("; ", Causes.Select(pair => $"{pair.Key} ({pair.Value}x)"));
            }
        }
    }
}
