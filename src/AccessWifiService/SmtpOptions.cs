namespace AccessWifiService
{
    /// <summary>Opções de SMTP, preenchidas a partir da tabela Configuration (chaves SMTP_*).</summary>
    public class SmtpOptions
    {
        public string Host { get; set; } = "";
        public int Port { get; set; } = 587;
        public string Username { get; set; } = "";
        public string Password { get; set; } = "";
        public string FromEmail { get; set; } = "";
        public string FromName { get; set; } = "AccessWifi";
        public bool UseStartTls { get; set; } = true;
    }
}
