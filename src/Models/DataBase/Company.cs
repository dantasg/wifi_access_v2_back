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

        /// <summary>
        /// Fuso horário das lojas (IANA). As campanhas disparam no horário daqui e o "dia" do cliente
        /// (aniversário, visita) é contado nele. Padrão: Belém (-03, sem horário de verão).
        /// </summary>
        public string TimeZone { get; set; } = CompanyTimeZone.Default;
    }

    public static class CompanyTimeZone
    {
        public const string Default = "America/Belem";

        /// <summary>Resolve o fuso da empresa; um valor inválido cai no padrão em vez de derrubar a campanha.</summary>
        public static TimeZoneInfo Resolve(string? sTimeZone)
        {
            if (!string.IsNullOrWhiteSpace(sTimeZone)
                && TimeZoneInfo.TryFindSystemTimeZoneById(sTimeZone, out TimeZoneInfo? objZone))
            {
                return objZone;
            }
            return TimeZoneInfo.FindSystemTimeZoneById(Default);
        }

        public static bool IsValid(string? sTimeZone) =>
            !string.IsNullOrWhiteSpace(sTimeZone) && TimeZoneInfo.TryFindSystemTimeZoneById(sTimeZone, out _);

        /// <summary>Dia de hoje no fuso informado.</summary>
        public static DateOnly Today(TimeZoneInfo objZone, DateTime dtUtc) =>
            DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(dtUtc, DateTimeKind.Utc), objZone));
    }
}
