namespace AccessWifi.Ops
{
    /// <summary>
    /// Os segredos das rotinas (/etc/accesswifi/ops.env). As chaves são as mesmas desde a primeira versão
    /// (Python, 01/10/2026): trocar o programa não exige configurar de novo.
    /// </summary>
    public class OpsSettings
    {
        public const string KeyTelegramToken = "TELEGRAM_TOKEN";
        public const string KeyTelegramChat = "TELEGRAM_CHAT_ID";
        public const string KeyBackupPassword = "BACKUP_SENHA";
        public const string KeyAlertEmails = "AVISO_EMAILS";
        public const string KeySmtpHost = "SMTP_HOST";
        public const string KeySmtpPort = "SMTP_PORTA";
        public const string KeySmtpUser = "SMTP_USUARIO";
        public const string KeySmtpPassword = "SMTP_SENHA";
        public const string KeySmtpFrom = "SMTP_REMETENTE";
        public const string KeySmtpFromName = "SMTP_NOME";

        /// <summary>Ordem de gravação no arquivo.</summary>
        public static readonly string[] s_arrKeys =
        [
            KeyTelegramToken, KeyTelegramChat, KeyBackupPassword, KeyAlertEmails, KeySmtpHost, KeySmtpPort,
            KeySmtpUser, KeySmtpPassword, KeySmtpFrom, KeySmtpFromName,
        ];

        public Dictionary<string, string> Values { get; }

        public OpsSettings(Dictionary<string, string> dicValues)
        {
            Values = dicValues;
        }

        public static OpsSettings Load(string sPath = OpsPaths.Config)
        {
            return new OpsSettings(EnvFile.Read(sPath));
        }

        public string Get(string sKey)
        {
            return Values.TryGetValue(sKey, out string? sValue) ? sValue : "";
        }

        public void Set(string sKey, string sValue)
        {
            Values[sKey] = sValue;
        }

        public bool HasTelegram => Get(KeyTelegramToken).Length > 0 && Get(KeyTelegramChat).Length > 0;

        public bool HasEmail => Get(KeySmtpHost).Length > 0 && AlertEmails.Length > 0;

        public string[] AlertEmails => Get(KeyAlertEmails)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        public int SmtpPort => int.TryParse(Get(KeySmtpPort), out int iPort) ? iPort : 587;

        public string SmtpFrom => Get(KeySmtpFrom).Length > 0 ? Get(KeySmtpFrom) : Get(KeySmtpUser);

        public string SmtpFromName => Get(KeySmtpFromName).Length > 0 ? Get(KeySmtpFromName) : "AccessWifi";

        public void Save(string sPath = OpsPaths.Config)
        {
            Dictionary<string, string> dicOrdered = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string sKey in s_arrKeys)
            {
                dicOrdered[sKey] = Get(sKey);
            }
            foreach (KeyValuePair<string, string> objPair in Values.Where(pair => !dicOrdered.ContainsKey(pair.Key)))
            {
                dicOrdered[objPair.Key] = objPair.Value;
            }
            EnvFile.WriteSecret(sPath, dicOrdered,
                "Rotinas de proteção do AccessWifi (accesswifi-ops configurar). Só o root lê.");
        }
    }
}
