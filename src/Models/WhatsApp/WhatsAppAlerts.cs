using Microsoft.EntityFrameworkCore;
using Models.DataBase;
using Models.Persistence;

namespace Models.WhatsApp
{
    /// <summary>
    /// A fila dos alertas do WhatsApp por e-mail (D9). A API e o serviço só põem na fila; quem manda é o serviço
    /// (WhatsAppTemplateWorker), para os avisos da Meta não esperarem o e-mail.
    /// </summary>
    public static class WhatsAppAlerts
    {
        /// <summary>O mesmo assunto não volta a ser avisado antes disto.</summary>
        public static readonly TimeSpan RepeatAfter = TimeSpan.FromHours(12);

        /// <summary>
        /// Põe um alerta na fila, se o mesmo assunto (Key) não foi avisado nas últimas 12 horas. Não grava: quem chama
        /// salva junto com o resto. Devolve false quando o alerta é repetido.
        /// </summary>
        public static async Task<bool> EnqueueAsync(
            AppDbContext objDbContext, Guid objCompanyId, string sKey, string sSubject, string sBody, DateTime dtNowUtc,
            CancellationToken objCancellationToken = default)
        {
            DateTime dtSince = dtNowUtc - RepeatAfter;
            bool bRepeated = objDbContext.WhatsAppAlerts.Local.Any(alert =>
                    alert.IDCompany == objCompanyId && alert.Key == sKey && alert.CreatedAt >= dtSince)
                || await objDbContext.WhatsAppAlerts.AnyAsync(alert =>
                    alert.IDCompany == objCompanyId && alert.Key == sKey && alert.CreatedAt >= dtSince, objCancellationToken);
            if (bRepeated)
            {
                return false;
            }
            objDbContext.WhatsAppAlerts.Add(new WhatsAppAlert
            {
                IDCompany = objCompanyId,
                Key = Truncate(sKey, 200),
                Subject = Truncate(sSubject, 300),
                Body = Truncate(sBody, 4000),
                CreatedAt = dtNowUtc,
            });
            return true;
        }

        private static string Truncate(string sValue, int iMax) => sValue.Length <= iMax ? sValue : sValue[..iMax];
    }
}
