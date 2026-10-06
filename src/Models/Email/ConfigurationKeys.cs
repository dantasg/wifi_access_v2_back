namespace Models.Email
{
    /// <summary>
    /// Chaves esperadas na tabela Configuration (coluna IDConfiguration).
    /// Editadas no painel do super admin (Configurações do sistema) — a senha vai cifrada.
    /// </summary>
    public static class ConfigurationKeys
    {
        public const string SmtpHost = "SMTP_HOST";
        public const string SmtpPort = "SMTP_PORT";
        public const string SmtpUsername = "SMTP_USERNAME";
        public const string SmtpPassword = "SMTP_PASSWORD";
        public const string SmtpFromEmail = "SMTP_FROM_EMAIL";
        public const string SmtpFromName = "SMTP_FROM_NAME";
        public const string SmtpUseStartTls = "SMTP_USE_STARTTLS";
    }
}
