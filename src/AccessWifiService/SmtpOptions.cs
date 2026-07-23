namespace AccessWifiService
{
    /// <summary>Seção "Smtp" do appsettings — servidor de envio global do serviço.</summary>
    public class SmtpOptions
    {
        public const string SectionName = "Smtp";

        /// <summary>Vazio = modo simulação (não envia de fato, só registra em log).</summary>
        public string Host { get; set; } = "";
        public int Port { get; set; } = 587;
        public string Username { get; set; } = "";
        public string Password { get; set; } = "";
        public string FromEmail { get; set; } = "";
        public string FromName { get; set; } = "AccessWifi";
        public bool UseStartTls { get; set; } = true;
    }
}
