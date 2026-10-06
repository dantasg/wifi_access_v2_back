namespace Models.DataBase
{
    /// <summary>Tipos de campanha. Os de sistema são nossos; o super admin libera cada um por empresa (D6).</summary>
    public static class CampaignKind
    {
        /// <summary>
        /// Aniversariantes da semana (D21): de segunda a sábado, a lista vai de hoje até sábado; a de
        /// segunda pega também o domingo. No domingo não dispara.
        /// </summary>
        public const string Birthday = "Birthday";
        public const string SignupAnniversary = "SignupAnniversary";
        public const string WeMissYou = "WeMissYou";
        public const string FrequentCustomer = "FrequentCustomer";

        /// <summary>
        /// A empresa escolhe quem recebe (filtros) e quando (agenda com repetição). Por enquanto "em
        /// breve" (D23): o motor sabe rodar, mas não dá para liberar nem criar.
        /// </summary>
        public const string Filtered = "Filtered";

        /// <summary>
        /// Ordem de prioridade (menor = mais importante). Com o limite de 1 mensagem por cliente por
        /// dia (D11), quando dois disparos caem no mesmo dia vale o de maior prioridade. A de
        /// boas-vindas saiu do sistema (D22).
        /// </summary>
        public static readonly IReadOnlyList<string> All =
            [Birthday, SignupAnniversary, FrequentCustomer, WeMissYou, Filtered];

        public static bool IsValid(string? sKind) => sKind is not null && All.Contains(sKind);

        /// <summary>Pode ser liberado e criado hoje. Os demais aparecem como "em breve".</summary>
        public static bool IsAvailable(string? sKind) => IsValid(sKind) && sKind != Filtered;

        /// <summary>De sistema: uma por empresa, sempre diária no horário configurado.</summary>
        public static bool IsSystem(string sKind) => sKind != Filtered;

        /// <summary>Dispara neste dia? Todas as de sistema todo dia, menos o aniversário, que folga no domingo.</summary>
        public static bool RunsOn(string sKind, DateOnly dtDate) =>
            sKind != Birthday || dtDate.DayOfWeek != DayOfWeek.Sunday;

        /// <summary>Nome curto para arquivos (ex.: "aniversario" em "campanha-aniversario-itaituba-2026-10-12.pdf").</summary>
        public static string FileSlug(string sKind) => sKind switch
        {
            Birthday => "aniversario",
            SignupAnniversary => "aniversario-de-cadastro",
            WeMissYou => "sentimos-sua-falta",
            FrequentCustomer => "cliente-frequente",
            _ => "campanha",
        };

        public static int Priority(string sKind)
        {
            int iIndex = All.ToList().IndexOf(sKind);
            return iIndex < 0 ? int.MaxValue : iIndex;
        }

        public static string Label(string sKind) => sKind switch
        {
            Birthday => "Aniversário",
            SignupAnniversary => "Aniversário de cadastro",
            WeMissYou => "Sentimos sua falta",
            FrequentCustomer => "Cliente frequente",
            Filtered => "Campanha filtrada",
            _ => sKind,
        };
    }

    /// <summary>
    /// Como o gerente fala com o cliente. A campanha não manda nada direto para o cliente (D17): vai um
    /// PDF para o e-mail da unidade, com o link do WhatsApp de cada um.
    /// </summary>
    public static class CampaignChannel
    {
        public const string WhatsApp = "WhatsApp";

        /// <summary>
        /// Desligado para campanhas até confirmarmos com a Meta (D15): a API do Instagram não deixa a
        /// empresa iniciar conversa.
        /// </summary>
        public const string Instagram = "Instagram";

        public static bool IsValid(string? sChannel) => sChannel is WhatsApp or Instagram;
    }

    public static class CampaignStatus
    {
        public const string Active = "Active";
        public const string Paused = "Paused";

        /// <summary>Sem próximos disparos (ex.: "uma vez" que já rodou, ou passou da data de fim).</summary>
        public const string Finished = "Finished";
    }

    public static class CampaignRunStatus
    {
        /// <summary>Escolhendo os clientes e montando as mensagens.</summary>
        public const string Selecting = "Selecting";
        public const string Running = "Running";
        public const string Paused = "Paused";
        public const string Completed = "Completed";
        public const string Cancelled = "Cancelled";
        public const string Failed = "Failed";

        /// <summary>O horário passou com o servidor fora do ar e o dia já virou (D10): não dispara mais.</summary>
        public const string Missed = "Missed";

        public static bool IsFinal(string sStatus) => sStatus is Completed or Cancelled or Failed or Missed;
    }

    /// <summary>Ações registradas no histórico da campanha, com o usuário que fez.</summary>
    public static class CampaignEventAction
    {
        public const string Created = "Created";
        public const string Edited = "Edited";
        public const string Paused = "Paused";
        public const string Resumed = "Resumed";
        public const string RunPaused = "RunPaused";
        public const string RunResumed = "RunResumed";
        public const string RunCancelled = "RunCancelled";
    }

    public static class CampaignRecipientStatus
    {
        public const string Pending = "Pending";

        /// <summary>Foi no PDF que chegou ao e-mail da unidade (D17): agora é com o gerente.</summary>
        public const string Sent = "Sent";

        /// <summary>Execuções antigas, do modo simulação (D14): passou por tudo, mas nada foi enviado.</summary>
        public const string Simulated = "Simulated";
        public const string Failed = "Failed";

        /// <summary>Não recebe nesta execução (ex.: limite de 1 mensagem por dia, D11). O motivo vai junto.</summary>
        public const string Ignored = "Ignored";
        public const string Cancelled = "Cancelled";

        /// <summary>Estados que contam como "este cliente recebeu (ou vai receber)".</summary>
        public static readonly IReadOnlyList<string> Delivered = [Pending, Sent, Simulated];
    }

    /// <summary>Situação do e-mail de uma unidade numa execução (<see cref="CampaignDelivery"/>).</summary>
    public static class CampaignDeliveryStatus
    {
        /// <summary>Esperando o envio (ou a próxima tentativa, depois de uma falha).</summary>
        public const string Pending = "Pending";
        public const string Sent = "Sent";

        /// <summary>Não saiu: a unidade não tem e-mail, ou as tentativas acabaram. O motivo vai junto.</summary>
        public const string Failed = "Failed";
        public const string Cancelled = "Cancelled";
    }
}
