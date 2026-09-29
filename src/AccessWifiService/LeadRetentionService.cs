using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Models.DataBase;
using Models.Persistence;

namespace AccessWifiService
{
    /// <summary>
    /// Expurgo por retenção (LGPD):
    /// <list type="bullet">
    ///   <item>cadastros e clientes que ficaram "Retention:LeadMonths" (padrão 24) <b>sem voltar</b>. A
    ///   conta é pela última visita (D4): quem continua vindo não é apagado, e o aniversário de cadastro
    ///   das campanhas continua possível;</item>
    ///   <item>a lista de destinatários das execuções de campanha com mais de
    ///   "Retention:CampaignRecipientMonths" (padrão 12, D16). O resumo de cada execução e as versões
    ///   das campanhas ficam.</item>
    /// </list>
    /// 0 ou negativo desliga a regra correspondente.
    /// </summary>
    public class LeadRetentionService
    {
        private readonly AppDbContext _objDbContext;
        private readonly ILogger<LeadRetentionService> _objLogger;
        private readonly int _iRetentionMonths;
        private readonly int _iRecipientMonths;

        public LeadRetentionService(
            AppDbContext objDbContext, IConfiguration objConfiguration, ILogger<LeadRetentionService> objLogger)
        {
            _objDbContext = objDbContext;
            _objLogger = objLogger;
            _iRetentionMonths = objConfiguration.GetValue("Retention:LeadMonths", 24);
            _iRecipientMonths = objConfiguration.GetValue("Retention:CampaignRecipientMonths", 12);
        }

        /// <param name="dtNowUtc">Referência de "agora" em UTC (as datas são gravadas em UTC).</param>
        public async Task PurgeExpiredLeadsAsync(DateTime dtNowUtc, CancellationToken objCancellationToken = default)
        {
            if (_iRetentionMonths > 0)
            {
                DateTime dtCutoffUtc = dtNowUtc.AddMonths(-_iRetentionMonths);

                // Timestamp = última conexão daquele aparelho naquela loja.
                List<Lead> objLeads = await _objDbContext.Leads
                    .Where(lead => lead.Timestamp < dtCutoffUtc)
                    .ToListAsync(objCancellationToken);
                List<Customer> objCustomers = await _objDbContext.Customers
                    .Where(customer => customer.LastVisitAt < dtCutoffUtc)
                    .ToListAsync(objCancellationToken);

                if (objLeads.Count > 0 || objCustomers.Count > 0)
                {
                    _objDbContext.Leads.RemoveRange(objLeads);
                    // No banco, apagar o cliente leva junto as unidades dele e as mensagens de campanha.
                    _objDbContext.Customers.RemoveRange(objCustomers);
                    await _objDbContext.SaveChangesAsync(objCancellationToken);
                    _objLogger.LogInformation(
                        "Retenção: {Leads} cadastro(s) e {Clientes} cliente(s) sem voltar desde {Cutoff:yyyy-MM-dd} removidos.",
                        objLeads.Count, objCustomers.Count, dtCutoffUtc);
                }
            }

            if (_iRecipientMonths > 0)
            {
                DateTime dtCutoffUtc = dtNowUtc.AddMonths(-_iRecipientMonths);
                List<CampaignRecipient> objRecipients = await _objDbContext.CampaignRecipients
                    .Where(recipient => _objDbContext.CampaignRuns.Any(run =>
                        run.Id == recipient.IDRun && run.CreatedAt < dtCutoffUtc))
                    .ToListAsync(objCancellationToken);
                if (objRecipients.Count > 0)
                {
                    _objDbContext.CampaignRecipients.RemoveRange(objRecipients);
                    await _objDbContext.SaveChangesAsync(objCancellationToken);
                    _objLogger.LogInformation(
                        "Retenção: {Count} destinatário(s) de execuções anteriores a {Cutoff:yyyy-MM-dd} removidos.",
                        objRecipients.Count, dtCutoffUtc);
                }
            }
        }
    }
}
