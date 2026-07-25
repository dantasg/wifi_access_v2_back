using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Models.DataBase;
using Models.Persistence;

namespace AccessWifiService
{
    /// <summary>
    /// Expurgo de leads antigos (LGPD): apaga cadastros mais velhos que o prazo de retenção.
    /// O prazo vem de "Retention:LeadMonths" (padrão 12 meses = 1 ano); 0 ou negativo desliga.
    /// </summary>
    public class LeadRetentionService
    {
        private readonly AppDbContext _objDbContext;
        private readonly ILogger<LeadRetentionService> _objLogger;
        private readonly int _iRetentionMonths;

        public LeadRetentionService(
            AppDbContext objDbContext, IConfiguration objConfiguration, ILogger<LeadRetentionService> objLogger)
        {
            _objDbContext = objDbContext;
            _objLogger = objLogger;
            _iRetentionMonths = objConfiguration.GetValue("Retention:LeadMonths", 12);
        }

        /// <param name="dtNowUtc">Referência de "agora" em UTC (a data de cadastro é gravada em UTC).</param>
        public async Task PurgeExpiredLeadsAsync(DateTime dtNowUtc, CancellationToken objCancellationToken = default)
        {
            if (_iRetentionMonths <= 0)
            {
                return; // retenção desabilitada.
            }

            DateTime dtCutoffUtc = dtNowUtc.AddMonths(-_iRetentionMonths);

            List<Lead> objExpirados = await _objDbContext.Leads
                .Where(lead => lead.CreatedAt < dtCutoffUtc)
                .ToListAsync(objCancellationToken);
            if (objExpirados.Count == 0)
            {
                return;
            }

            _objDbContext.Leads.RemoveRange(objExpirados);
            await _objDbContext.SaveChangesAsync(objCancellationToken);

            _objLogger.LogInformation(
                "Retenção: {Count} lead(s) anteriores a {Cutoff:yyyy-MM-dd} removidos.",
                objExpirados.Count, dtCutoffUtc);
        }
    }
}
