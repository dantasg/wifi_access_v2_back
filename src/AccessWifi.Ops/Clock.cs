namespace AccessWifi.Ops
{
    /// <summary>Horário das mensagens: Belém (-03:00, sem horário de verão), como as lojas.</summary>
    public static class Clock
    {
        public static readonly TimeSpan s_tsBelem = TimeSpan.FromHours(-3);

        public static DateTimeOffset Now(TimeProvider objTime)
        {
            return objTime.GetUtcNow().ToOffset(s_tsBelem);
        }

        public static string Format(DateTimeOffset dtMoment)
        {
            return dtMoment.ToOffset(s_tsBelem).ToString("dd/MM/yyyy HH:mm");
        }

        public static string Time(DateTimeOffset dtMoment)
        {
            return dtMoment.ToOffset(s_tsBelem).ToString("HH:mm:ss");
        }

        public static string Size(long lBytes)
        {
            return lBytes < 1024 * 1024
                ? $"{lBytes / 1024.0:0} KB"
                : $"{lBytes / 1024.0 / 1024.0:0.0} MB";
        }
    }
}
