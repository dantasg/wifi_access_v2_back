namespace Models.Campaigns
{
    /// <summary>Dados de um cliente para preencher os campos da mensagem.</summary>
    public sealed record CampaignMessageData(
        string Name,
        string CompanyName,
        string UnitName,
        int? Age,
        int? YearsSinceSignup);

    /// <summary>
    /// Os campos que a empresa usa no texto e como cada um vira o dado do cliente. O mesmo código monta
    /// a prévia da tela e as mensagens do disparo.
    /// </summary>
    public static class CampaignMessage
    {
        public const string FirstName = "{primeiro_nome}";
        public const string FullName = "{nome}";
        public const string Company = "{empresa}";
        public const string UnitField = "{unidade}";
        public const string Age = "{idade}";
        public const string YearsSinceSignup = "{anos_de_cadastro}";

        public static readonly IReadOnlyList<string> Fields =
            [FirstName, FullName, Company, UnitField, Age, YearsSinceSignup];

        public static string Render(string sTemplate, CampaignMessageData objData)
        {
            string sName = (objData.Name ?? "").Trim();
            string sFirstName = sName.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";

            return sTemplate
                .Replace(FirstName, sFirstName)
                .Replace(FullName, sName)
                .Replace(Company, objData.CompanyName)
                .Replace(UnitField, objData.UnitName)
                .Replace(Age, objData.Age?.ToString() ?? "")
                .Replace(YearsSinceSignup, objData.YearsSinceSignup?.ToString() ?? "");
        }

        /// <summary>
        /// Preenche só os campos que são iguais para todos os clientes de um PDF (empresa e unidade). Os que
        /// dependem do cliente ({primeiro_nome}, {idade}…) ficam entre chaves, para o gerente ver onde muda.
        /// </summary>
        public static string RenderShared(string sTemplate, string sCompanyName, string sUnitName) =>
            sTemplate
                .Replace(Company, sCompanyName)
                .Replace(UnitField, sUnitName);

        /// <summary>Idade completa na data (o aniversário de 29/02 conta em 28/02 nos anos não bissextos).</summary>
        public static int? AgeOn(DateOnly? dtBirth, DateOnly dtDate)
        {
            if (dtBirth is not DateOnly dtBirthDate)
            {
                return null;
            }
            return FullYearsBetween(dtBirthDate, dtDate);
        }

        /// <summary>Anos completos de <paramref name="dtStart"/> até <paramref name="dtDate"/>.</summary>
        public static int FullYearsBetween(DateOnly dtStart, DateOnly dtDate)
        {
            int iYears = dtDate.Year - dtStart.Year;
            bool bHadThisYear =
                dtDate.Month > dtStart.Month
                || (dtDate.Month == dtStart.Month && dtDate.Day >= dtStart.Day)
                || CampaignCalendar.IsAnniversary(dtStart.Month, dtStart.Day, dtDate);
            return bHadThisYear ? iYears : iYears - 1;
        }
    }
}
