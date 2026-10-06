namespace Models.DataBase
{
    /// <summary>
    /// Campanha de uma empresa. A configuração (mensagem, horário, filtros, agenda) fica em JSON
    /// (<see cref="Campaigns.CampaignConfig"/>); cada salvamento vira uma <see cref="CampaignVersion"/>.
    /// </summary>
    public class Campaign
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid IDCompany { get; set; }

        /// <summary>Ver <see cref="CampaignKind"/>. Não muda depois de criada.</summary>
        public string Kind { get; set; } = CampaignKind.Filtered;

        public string Name { get; set; } = "";

        /// <summary>Ver <see cref="CampaignStatus"/>.</summary>
        public string Status { get; set; } = CampaignStatus.Active;

        /// <summary>Configuração atual — cópia da última versão, para o agendador e a tela.</summary>
        public string ConfigJson { get; set; } = "{}";

        public int CurrentVersion { get; set; } = 1;

        /// <summary>Próximo disparo (UTC). Nulo = não há próximo (pausada ou encerrada).</summary>
        public DateTime? NextRunAt { get; set; }

        public DateTime? LastRunAt { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }

    /// <summary>
    /// Foto completa da campanha a cada salvamento, com quem salvou. Nunca é alterada: é o histórico
    /// de edição, e cada execução guarda a versão com que rodou (D13).
    /// </summary>
    public class CampaignVersion
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid IDCampaign { get; set; }
        public int Number { get; set; }
        public string Name { get; set; } = "";
        public string ConfigJson { get; set; } = "{}";

        /// <summary>Resumo do que mudou em relação à versão anterior (ex.: "Horário: 09:00 → 10:00").</summary>
        public string Changes { get; set; } = "";

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public Guid? IDUser { get; set; }
        public string Username { get; set; } = "";
    }

    /// <summary>Um disparo da campanha: "os aniversariantes de 29/09". Uma por campanha por dia.</summary>
    public class CampaignRun
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid IDCampaign { get; set; }
        public Guid IDCompany { get; set; }
        public Guid IDCampaignVersion { get; set; }
        public int VersionNumber { get; set; }

        /// <summary>Horário previsto do disparo (UTC).</summary>
        public DateTime ScheduledFor { get; set; }

        /// <summary>
        /// Dia do disparo no fuso da empresa. Com a campanha, é a chave que impede duplicar (uma execução
        /// por dia) e a base do limite de 1 mensagem por cliente por dia (D11).
        /// </summary>
        public DateOnly LocalDate { get; set; }

        /// <summary>Ver <see cref="CampaignRunStatus"/>.</summary>
        public string Status { get; set; } = CampaignRunStatus.Selecting;

        /// <summary>
        /// Modo simulação (D14): percorria tudo sem enviar. Só as execuções antigas; desde o envio por
        /// e-mail à unidade (D17), as novas saem com false.
        /// </summary>
        public bool Simulation { get; set; }

        public int TotalCount { get; set; }
        public int SentCount { get; set; }
        public int SimulatedCount { get; set; }
        public int FailedCount { get; set; }
        public int IgnoredCount { get; set; }
        public int CancelledCount { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? StartedAt { get; set; }
        public DateTime? FinishedAt { get; set; }
        public string? Error { get; set; }
    }

    /// <summary>
    /// Um cliente dentro de uma execução, com a mensagem já montada e o seu status. É o que permite
    /// pausar e retomar de onde parou: o processamento sempre pega os pendentes.
    /// </summary>
    public class CampaignRecipient
    {
        /// <summary>Sequencial: o processamento anda em ordem e em lotes por ele.</summary>
        public long Id { get; set; }
        public Guid IDRun { get; set; }
        public Guid IDCustomer { get; set; }

        /// <summary>
        /// Unidade cujo gerente recebe este cliente no PDF: a da última visita (D17). Nulo = cliente sem
        /// unidade, fica como falha.
        /// </summary>
        public Guid? IDUnit { get; set; }
        public string Phone { get; set; } = "";
        public string Name { get; set; } = "";

        /// <summary>Usuário do Instagram, sem o @ ("" = não informou). Vira link no PDF.</summary>
        public string Instagram { get; set; } = "";
        public string Message { get; set; } = "";

        /// <summary>
        /// A coluna de informação do PDF, já pronta: "sex, 09/10 · 29 anos" no aniversário, "2 anos de
        /// cadastro", "5ª visita", "Última visita em 01/09/2026".
        /// </summary>
        public string Info { get; set; } = "";

        /// <summary>Aniversário: o dia em que o cliente faz anos nesta semana (ordena o PDF e marca "hoje").</summary>
        public DateOnly? EventDate { get; set; }

        /// <summary>Ver <see cref="CampaignRecipientStatus"/>.</summary>
        public string Status { get; set; } = CampaignRecipientStatus.Pending;

        /// <summary>Motivo de "ignorado"/"falhou" (ex.: "Limite do dia: já recebeu Aniversário").</summary>
        public string? Reason { get; set; }

        /// <summary>Cliente frequente: o marco de visitas que esta mensagem comemora (5, 10…).</summary>
        public int? Milestone { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? ProcessedAt { get; set; }
    }

    /// <summary>
    /// O e-mail de uma unidade numa execução (D17): um PDF com os clientes dela, para o gerente fazer o
    /// contato. Guarda para onde foi, quando, quantas tentativas e o motivo de uma falha.
    /// </summary>
    public class CampaignDelivery
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid IDRun { get; set; }

        /// <summary>Nulo = clientes sem unidade (não tem para onde mandar).</summary>
        public Guid? IDUnit { get; set; }

        /// <summary>Nome e e-mail da unidade no momento do envio (o histórico não muda se a unidade mudar).</summary>
        public string UnitName { get; set; } = "";
        public string Email { get; set; } = "";

        /// <summary>Ver <see cref="CampaignDeliveryStatus"/>.</summary>
        public string Status { get; set; } = CampaignDeliveryStatus.Pending;
        public int RecipientCount { get; set; }
        public int Attempts { get; set; }

        /// <summary>Quando tentar de novo depois de uma falha (UTC).</summary>
        public DateTime? NextAttemptAt { get; set; }
        public string? Error { get; set; }

        /// <summary>Nome do PDF anexado (ex.: "campanha-aniversario-itaituba-2026-10-12.pdf").</summary>
        public string FileName { get; set; } = "";
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? SentAt { get; set; }
    }

    /// <summary>Ações sobre a campanha ou uma execução (ativar, pausar, retomar, cancelar), com quem fez.</summary>
    public class CampaignEvent
    {
        public long Id { get; set; }
        public Guid IDCampaign { get; set; }
        public Guid? IDRun { get; set; }
        public string Action { get; set; } = "";
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public Guid? IDUser { get; set; }
        public string Username { get; set; } = "";
    }

    /// <summary>Tipo de campanha liberado para a empresa (D6). A linha existir = liberado.</summary>
    public class CompanyCampaignKind
    {
        public Guid IDCompany { get; set; }
        public string Kind { get; set; } = "";
        public DateTime EnabledAt { get; set; } = DateTime.UtcNow;
        public string EnabledBy { get; set; } = "";
    }
}
