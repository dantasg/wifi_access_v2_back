using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Models.DataBase;
using Models.Email;
using Models.Persistence;

namespace AccessWifiService.WhatsApp
{
    /// <summary>
    /// Manda por e-mail os alertas do WhatsApp que estão na fila (D9), para os endereços de "Configurações do
    /// sistema → E-mails para alertas do WhatsApp", pelo mesmo e-mail de envio do sistema. Sem endereço configurado,
    /// o alerta fica só no registro. Falha de envio tenta de novo nas próximas voltas, até 5 vezes.
    /// </summary>
    public sealed class WhatsAppAlertService
    {
        private const int MaxAttempts = 5;

        private readonly AppDbContext _objDbContext;
        private readonly IEmailSender _objEmailSender;
        private readonly ConfigurationReader _objConfigurationReader;
        private readonly ILogger<WhatsAppAlertService> _objLogger;

        public WhatsAppAlertService(
            AppDbContext objDbContext,
            IEmailSender objEmailSender,
            ConfigurationReader objConfigurationReader,
            ILogger<WhatsAppAlertService> objLogger)
        {
            _objDbContext = objDbContext;
            _objEmailSender = objEmailSender;
            _objConfigurationReader = objConfigurationReader;
            _objLogger = objLogger;
        }

        /// <summary>Os e-mails configurados, separados por vírgula ou ponto e vírgula.</summary>
        public static List<string> Recipients(string? sValue) =>
            (sValue ?? "").Split([',', ';', ' ', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

        public async Task SendPendingAsync(DateTime dtNowUtc, CancellationToken objCancellationToken)
        {
            List<WhatsAppAlert> objPending = await _objDbContext.WhatsAppAlerts
                .Where(alert => alert.SentAt == null && alert.Attempts < MaxAttempts)
                .OrderBy(alert => alert.CreatedAt)
                .Take(20)
                .ToListAsync(objCancellationToken);
            if (objPending.Count == 0)
            {
                return;
            }

            List<string> objTo = Recipients(
                await _objConfigurationReader.GetValueAsync(ConfigurationKeys.WhatsAppAlertEmails, objCancellationToken));
            Dictionary<Guid, string> objCompanies = await _objDbContext.Companies.AsNoTracking()
                .ToDictionaryAsync(company => company.Id, company => company.Name, objCancellationToken);

            foreach (WhatsAppAlert objAlert in objPending)
            {
                objAlert.Attempts++;
                if (objTo.Count == 0)
                {
                    // Sem para quem mandar: fica só no registro.
                    objAlert.Attempts = MaxAttempts;
                    objAlert.Error = "Sem e-mail para alertas (Configurações do sistema).";
                    continue;
                }
                string sBody = objAlert.Body + "\r\n\r\nEmpresa: " + objCompanies.GetValueOrDefault(objAlert.IDCompany, "")
                    + "\r\nVeja em Configurações → WhatsApp, no painel.\r\n\r\nMensagem automática do AccessWifi.";
                try
                {
                    foreach (string sTo in objTo)
                    {
                        await _objEmailSender.SendAsync(sTo, "[AccessWifi] " + objAlert.Subject, sBody, null, null,
                            objCancellationToken);
                    }
                    objAlert.SentAt = dtNowUtc;
                    objAlert.Error = "";
                }
                catch (Exception objException) when (objException is not OperationCanceledException)
                {
                    objAlert.Error = Truncate(objException.GetBaseException().Message, 1000);
                    _objLogger.LogWarning("WhatsApp: alerta {Id} não saiu (tentativa {Tentativa}): {Erro}",
                        objAlert.Id, objAlert.Attempts, objAlert.Error);
                }
            }
            await _objDbContext.SaveChangesAsync(objCancellationToken);
        }

        private static string Truncate(string sValue, int iMax) => sValue.Length <= iMax ? sValue : sValue[..iMax];
    }
}
