namespace Models.DataBase
{
    /// <summary>Tipos de campanha. Os de sistema são nossos; o super admin libera cada um por empresa (D6).</summary>
    public static class CampaignKind
    {
        public const string Birthday = "Birthday";
        public const string SignupAnniversary = "SignupAnniversary";
        public const string Welcome = "Welcome";
        public const string WeMissYou = "WeMissYou";
        public const string FrequentCustomer = "FrequentCustomer";

        /// <summary>A empresa escolhe quem recebe (filtros) e quando (agenda com repetição).</summary>
        public const string Filtered = "Filtered";

        /// <summary>
        /// Ordem de prioridade (menor = mais importante). Com o limite de 1 mensagem por cliente por
        /// dia (D11), quando dois disparos caem no mesmo dia vale o de maior prioridade.
        /// </summary>
        public static readonly IReadOnlyList<string> All =
            [Birthday, SignupAnniversary, FrequentCustomer, Welcome, WeMissYou, Filtered];

        public static bool IsValid(string? sKind) => sKind is not null && All.Contains(sKind);

        /// <summary>De sistema: uma por empresa, sempre diária no horário configurado.</summary>
        public static bool IsSystem(string sKind) => sKind != Filtered;

        public static int Priority(string sKind)
        {
            int iIndex = All.ToList().IndexOf(sKind);
            return iIndex < 0 ? int.MaxValue : iIndex;
        }

        public static string Label(string sKind) => sKind switch
        {
            Birthday => "Aniversário",
            SignupAnniversary => "Aniversário de cadastro",
            Welcome => "Boas-vindas",
            WeMissYou => "Sentimos sua falta",
            FrequentCustomer => "Cliente frequente",
            Filtered => "Campanha filtrada",
            _ => sKind,
        };
    }

    /// <summary>Por onde a mensagem sai (o envio real vem numa fase seguinte).</summary>
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
        public const string Sent = "Sent";

        /// <summary>Modo simulação (D14): passou por tudo, mas nada foi enviado.</summary>
        public const string Simulated = "Simulated";
        public const string Failed = "Failed";

        /// <summary>Não recebe nesta execução (ex.: limite de 1 mensagem por dia, D11). O motivo vai junto.</summary>
        public const string Ignored = "Ignored";
        public const string Cancelled = "Cancelled";

        /// <summary>Estados que contam como "este cliente recebeu (ou vai receber)".</summary>
        public static readonly IReadOnlyList<string> Delivered = [Pending, Sent, Simulated];
    }
}
