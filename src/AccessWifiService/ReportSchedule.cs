namespace AccessWifiService
{
    /// <summary>Regras de qual período coletar (puras — testáveis).</summary>
    public static class ReportSchedule
    {
        /// <summary>
        /// Intervalo [início, fim) do mês anterior à data de referência, em UTC.
        /// Ex.: referência 2026-08-01 → [2026-07-01, 2026-08-01).
        /// </summary>
        public static (DateTime StartUtc, DateTime EndUtc) PreviousMonthRangeUtc(DateTime dtReference)
        {
            DateTime dtFirstOfCurrent = new DateTime(
                dtReference.Year, dtReference.Month, 1, 0, 0, 0, DateTimeKind.Utc);
            DateTime dtFirstOfPrevious = dtFirstOfCurrent.AddMonths(-1);
            return (dtFirstOfPrevious, dtFirstOfCurrent);
        }
    }
}
