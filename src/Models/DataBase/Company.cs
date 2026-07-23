namespace Models.DataBase
{
    public class Company
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Name { get; set; } = "";
        public string Slug { get; set; } = "";
        public bool Active { get; set; } = true;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// E-mail que recebe o relatório mensal de cadastros (enviado pelo AccessWifiService).
        /// Nulo/vazio = a empresa não recebe relatório.
        /// </summary>
        public string? ReportEmail { get; set; }

        /// <summary>
        /// Dia do mês em que o relatório é enviado (o serviço envia os cadastros do mês
        /// anterior). Padrão 1 = primeiro dia do mês. Faixa válida: 1 a 28.
        /// </summary>
        public int ReportSendDay { get; set; } = 1;

        /// <summary>
        /// Quando o último relatório foi enviado (UTC). Marcador durável de idempotência:
        /// o serviço não reenvia se já enviou no mês corrente. Nulo = nunca enviado.
        /// </summary>
        public DateTime? LastReportSentAt { get; set; }

        public CompanyUnifi Unifi { get; set; } = new CompanyUnifi();
    }
}
