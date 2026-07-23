using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Models.Persistence;

namespace AccessWifiService
{
    /// <summary>Lê as configurações globais da tabela Configuration (chave-valor).</summary>
    public class ConfigurationReader
    {
        private readonly AppDbContext _objDbContext;

        public ConfigurationReader(AppDbContext objDbContext)
        {
            _objDbContext = objDbContext;
        }

        /// <summary>Valor de uma chave, ou null se não existir.</summary>
        public async Task<string?> GetValueAsync(string sKey, CancellationToken objCancellationToken = default)
        {
            return await _objDbContext.Configurations
                .AsNoTracking()
                .Where(config => config.IDConfiguration == sKey)
                .Select(config => config.Value)
                .FirstOrDefaultAsync(objCancellationToken);
        }

        /// <summary>Monta as opções de SMTP a partir das chaves SMTP_* da tabela.</summary>
        public async Task<SmtpOptions> GetSmtpAsync(CancellationToken objCancellationToken = default)
        {
            string[] arrKeys =
            [
                ConfigurationKeys.SmtpHost,
                ConfigurationKeys.SmtpPort,
                ConfigurationKeys.SmtpUsername,
                ConfigurationKeys.SmtpPassword,
                ConfigurationKeys.SmtpFromEmail,
                ConfigurationKeys.SmtpFromName,
                ConfigurationKeys.SmtpUseStartTls,
            ];

            Dictionary<string, string> dicValues = await _objDbContext.Configurations
                .AsNoTracking()
                .Where(config => arrKeys.Contains(config.IDConfiguration))
                .ToDictionaryAsync(config => config.IDConfiguration, config => config.Value, objCancellationToken);

            SmtpOptions objOptions = new SmtpOptions
            {
                Host = GetString(dicValues, ConfigurationKeys.SmtpHost, ""),
                Username = GetString(dicValues, ConfigurationKeys.SmtpUsername, ""),
                Password = GetString(dicValues, ConfigurationKeys.SmtpPassword, ""),
                FromEmail = GetString(dicValues, ConfigurationKeys.SmtpFromEmail, ""),
                FromName = GetString(dicValues, ConfigurationKeys.SmtpFromName, "AccessWifi"),
                Port = GetInt(dicValues, ConfigurationKeys.SmtpPort, 587),
                UseStartTls = GetBool(dicValues, ConfigurationKeys.SmtpUseStartTls, true),
            };
            return objOptions;
        }

        private static string GetString(Dictionary<string, string> dicValues, string sKey, string sDefault)
        {
            return dicValues.TryGetValue(sKey, out string? sValue) && !string.IsNullOrWhiteSpace(sValue)
                ? sValue
                : sDefault;
        }

        private static int GetInt(Dictionary<string, string> dicValues, string sKey, int iDefault)
        {
            return dicValues.TryGetValue(sKey, out string? sValue)
                && int.TryParse(sValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out int iValue)
                ? iValue
                : iDefault;
        }

        private static bool GetBool(Dictionary<string, string> dicValues, string sKey, bool bDefault)
        {
            return dicValues.TryGetValue(sKey, out string? sValue) && bool.TryParse(sValue, out bool bValue)
                ? bValue
                : bDefault;
        }
    }
}
