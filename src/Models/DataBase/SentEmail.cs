namespace Models.DataBase
{
    /// <summary>Tipos de e-mail que o sistema manda para as unidades.</summary>
    public static class SentEmailKind
    {
        /// <summary>Relatório mensal de cadastros (CSV), no dia de envio da empresa.</summary>
        public const string Report = "Report";

        /// <summary>PDF de uma execução de campanha, com os clientes da unidade.</summary>
        public const string Campaign = "Campaign";
    }

    /// <summary>
    /// Um e-mail que saiu para uma unidade (tela "Correio eletrônico"): para quem, quando, o assunto e o
    /// texto exatamente como foram. O anexo não é guardado (decisão de 09/10/2026): é remontado na hora a
    /// partir dos dados que existem — a lista da campanha (enquanto a regra da LGPD não apagar) ou os
    /// cadastros do mês do relatório. Só entram os e-mails que o servidor de e-mail aceitou.
    /// </summary>
    public class SentEmail
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid IDCompany { get; set; }

        /// <summary>Nula se a unidade for apagada depois; o nome de quando saiu fica em <see cref="UnitName"/>.</summary>
        public Guid? IDUnit { get; set; }
        public string UnitName { get; set; } = "";

        /// <summary>Ver <see cref="SentEmailKind"/>.</summary>
        public string Kind { get; set; } = "";
        public string ToEmail { get; set; } = "";
        public string Subject { get; set; } = "";

        /// <summary>Texto do e-mail. Nulo nos e-mails de campanha enviados antes do registro existir.</summary>
        public string? Body { get; set; }

        public string AttachmentName { get; set; } = "";
        public DateTime SentAt { get; set; } = DateTime.UtcNow;

        /// <summary>Campanha: a execução de onde o PDF é remontado.</summary>
        public Guid? IDCampaignRun { get; set; }

        /// <summary>Relatório: o mês dos cadastros (UTC, fim exclusivo), de onde o CSV é remontado.</summary>
        public DateTime? PeriodStart { get; set; }
        public DateTime? PeriodEnd { get; set; }
    }
}
