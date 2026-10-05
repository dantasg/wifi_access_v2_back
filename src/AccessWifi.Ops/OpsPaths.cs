namespace AccessWifi.Ops
{
    /// <summary>Onde cada coisa mora no servidor (Ubuntu da VPS). Ver PRODUCAO.md §2.</summary>
    public static class OpsPaths
    {
        /// <summary>Segredos das rotinas: Telegram, senha do backup, e-mail dos avisos. Só o root lê.</summary>
        public const string Config = "/etc/accesswifi/ops.env";

        /// <summary>Configuração da API/worker (conexão do banco, Encryption__Key…). Vai junto no backup.</summary>
        public const string AppConfig = "/etc/accesswifi/accesswifi.env";

        public const string AppVersion = "/opt/accesswifi/VERSAO";

        /// <summary>Estado da conferência (o que está quebrado desde quando, último backup bom).</summary>
        public const string State = "/var/lib/accesswifi-ops/estado.json";

        /// <summary>Marcado pela publicação antes de reiniciar a API: a conferência não alerta por 5 min.</summary>
        public const string Maintenance = "/run/accesswifi-ops/manutencao";

        public const string BackupFolder = "/var/backups/accesswifi";

        public const string Database = "accesswifi";

        public const string ApiLocal = "http://127.0.0.1:5000/settings";
    }
}
