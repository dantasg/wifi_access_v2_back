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
    ///   das campanhas ficam;</item>
    ///   <item>a cópia dos avisos da Meta (WhatsApp) com mais de "Retention:WhatsAppWebhookDays" (padrão 90, D12) e os
    ///   alertas do WhatsApp do mesmo período;</item>
    ///   <item>os sinais dos aparelhos na rede de clientes (avisos da UniFi) com mais de "Retention:WifiPresenceDays"
    ///   (padrão 90, PROPOSTA_VISITAS_UNIFI.md D9). O tempo na loja de cada visita fica na visita.</item>
    ///   <item>os contatos do site institucional com mais de "Retention:SiteContactMonths" (padrão 12,
    ///   PROPOSTA_SITE.md D11), menos os que viraram cliente.</item>
    /// </list>
    /// 0 ou negativo desliga a regra correspondente.
    /// </summary>
    public class LeadRetentionService
    {
        private readonly AppDbContext _objDbContext;
        private readonly ILogger<LeadRetentionService> _objLogger;
        private readonly int _iRetentionMonths;
        private readonly int _iRecipientMonths;
        private readonly int _iWhatsAppWebhookDays;
        private readonly int _iPresenceDays;
        private readonly int _iSiteContactMonths;

        public LeadRetentionService(
            AppDbContext objDbContext, IConfiguration objConfiguration, ILogger<LeadRetentionService> objLogger)
        {
            _objDbContext = objDbContext;
            _objLogger = objLogger;
            _iRetentionMonths = objConfiguration.GetValue("Retention:LeadMonths", 24);
            _iRecipientMonths = objConfiguration.GetValue("Retention:CampaignRecipientMonths", 12);
            _iWhatsAppWebhookDays = objConfiguration.GetValue("Retention:WhatsAppWebhookDays", 90);
            _iPresenceDays = objConfiguration.GetValue("Retention:WifiPresenceDays", 90);
            _iSiteContactMonths = objConfiguration.GetValue("Retention:SiteContactMonths", 12);
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

            if (_iWhatsAppWebhookDays > 0)
            {
                // D12: a cópia dos avisos da Meta mistura clientes; fica só 90 dias. Os pedaços de cada cliente seguem
                // a regra do cliente (vão embora com ele). Os alertas já mandados vão junto.
                DateTime dtCutoffUtc = dtNowUtc.AddDays(-_iWhatsAppWebhookDays);
                List<WhatsAppWebhookRequest> objRequests = await _objDbContext.WhatsAppWebhookRequests
                    .Where(request => request.ReceivedAt < dtCutoffUtc)
                    .ToListAsync(objCancellationToken);
                List<WhatsAppAlert> objAlerts = await _objDbContext.WhatsAppAlerts
                    .Where(alert => alert.CreatedAt < dtCutoffUtc)
                    .ToListAsync(objCancellationToken);
                if (objRequests.Count > 0 || objAlerts.Count > 0)
                {
                    _objDbContext.WhatsAppWebhookRequests.RemoveRange(objRequests);
                    _objDbContext.WhatsAppAlerts.RemoveRange(objAlerts);
                    await _objDbContext.SaveChangesAsync(objCancellationToken);
                    _objLogger.LogInformation(
                        "Retenção: {Avisos} aviso(s) da Meta e {Alertas} alerta(s) do WhatsApp anteriores a {Cutoff:yyyy-MM-dd} removidos.",
                        objRequests.Count, objAlerts.Count, dtCutoffUtc);
                }
            }

            if (_iPresenceDays > 0)
            {
                DateTime dtCutoffUtc = dtNowUtc.AddDays(-_iPresenceDays);
                List<WifiPresence> objPresences = await _objDbContext.WifiPresences
                    .Where(presence => presence.LastSeenAt < dtCutoffUtc)
                    .ToListAsync(objCancellationToken);
                if (objPresences.Count > 0)
                {
                    _objDbContext.WifiPresences.RemoveRange(objPresences);
                    await _objDbContext.SaveChangesAsync(objCancellationToken);
                    _objLogger.LogInformation(
                        "Retenção: {Count} registro(s) de sinais de aparelhos anteriores a {Cutoff:yyyy-MM-dd} removidos.",
                        objPresences.Count, dtCutoffUtc);
                }
            }

            if (_iSiteContactMonths > 0)
            {
                // D11: quem virou cliente fica (o andamento é a história da venda).
                DateTime dtCutoffUtc = dtNowUtc.AddMonths(-_iSiteContactMonths);
                List<SiteContact> objContacts = await _objDbContext.SiteContacts
                    .Where(contact => contact.CreatedAt < dtCutoffUtc && contact.Status != SiteContactStatus.Client)
                    .ToListAsync(objCancellationToken);
                if (objContacts.Count > 0)
                {
                    _objDbContext.SiteContacts.RemoveRange(objContacts);
                    await _objDbContext.SaveChangesAsync(objCancellationToken);
                    _objLogger.LogInformation(
                        "Retenção: {Count} contato(s) do site anteriores a {Cutoff:yyyy-MM-dd} removidos.",
                        objContacts.Count, dtCutoffUtc);
                }
            }
        }
    }
}
