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
        /// Dia do mês em que o relatório mensal é enviado (o serviço envia os cadastros do mês
        /// anterior), o mesmo para todas as unidades. Cada unidade recebe o dela no próprio e-mail
        /// (<see cref="Unit.Email"/>). Padrão 1 = primeiro dia do mês. Faixa válida: 1 a 28.
        /// </summary>
        public int ReportSendDay { get; set; } = 1;

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
