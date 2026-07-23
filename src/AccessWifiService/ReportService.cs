using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Models.DataBase;
using Models.Persistence;

namespace AccessWifiService
{
    /// <summary>
    /// Monta e envia o relatório mensal de cadastros de cada empresa (cadastros do mês anterior),
    /// nas empresas cujo dia de envio configurado bate com a data de hoje.
    /// </summary>
    public class ReportService
    {
        private readonly AppDbContext _objDbContext;
        private readonly IEmailSender _objEmailSender;
        private readonly ILogger<ReportService> _objLogger;

        public ReportService(
            AppDbContext objDbContext, IEmailSender objEmailSender, ILogger<ReportService> objLogger)
        {
            _objDbContext = objDbContext;
            _objEmailSender = objEmailSender;
            _objLogger = objLogger;
        }

        /// <param name="dtToday">Data de referência (normalmente DateTime.Today).</param>
        public async Task SendDueReportsAsync(DateTime dtToday, CancellationToken objCancellationToken = default)
        {
            (DateTime dtStartUtc, DateTime dtEndUtc) = ReportSchedule.PreviousMonthRangeUtc(dtToday);

            // Empresas ativas, com e-mail e dia de envio == hoje, que ainda não receberam
            // o relatório deste mês (LastReportSentAt anterior ao início do mês corrente).
            List<Company> objCompanies = await _objDbContext.Companies
                .Where(company => company.Active
                    && company.ReportEmail != null && company.ReportEmail != ""
                    && company.ReportSendDay == dtToday.Day
                    && (company.LastReportSentAt == null || company.LastReportSentAt < dtEndUtc))
                .ToListAsync(objCancellationToken);

            foreach (Company objCompany in objCompanies)
            {
                await SendForCompanyAsync(objCompany, dtStartUtc, dtEndUtc, objCancellationToken);
                objCompany.LastReportSentAt = DateTime.UtcNow;
                await _objDbContext.SaveChangesAsync(objCancellationToken);
            }
        }

        private async Task SendForCompanyAsync(Company objCompany, DateTime dtStartUtc, DateTime dtEndUtc, CancellationToken objCancellationToken)
        {
            // Agrega os leads de todas as unidades da empresa; a coluna "unidade" distingue.
            List<LeadReportRow> objRows = await
                (from lead in _objDbContext.Leads.AsNoTracking()
                 join unit in _objDbContext.Units.AsNoTracking() on lead.IDUnit equals unit.Id
                 where unit.IDCompany == objCompany.Id
                     && lead.Timestamp >= dtStartUtc && lead.Timestamp < dtEndUtc
                 orderby lead.Timestamp
                 select new LeadReportRow(lead, unit.Name))
                .ToListAsync(objCancellationToken);

            string sPeriodo = dtStartUtc.ToString("MM/yyyy", CultureInfo.InvariantCulture);
            byte[] objCsv = LeadsCsv.Build(objRows);
            string sFileName = $"cadastros-{objCompany.Slug}-{dtStartUtc:yyyy-MM}.csv";
            string sSubject = $"Relatório de cadastros — {objCompany.Name} — {sPeriodo}";
            string sBody =
                $"Olá,\r\n\r\nSegue em anexo o relatório de cadastros de {objCompany.Name} " +
                $"referente a {sPeriodo}.\r\nTotal de cadastros no período: {objRows.Count}.\r\n\r\n" +
                "Mensagem automática do AccessWifi.";

            await _objEmailSender.SendAsync(objCompany.ReportEmail!, sSubject, sBody, objCsv, sFileName, objCancellationToken);

            _objLogger.LogInformation(
                "Relatório de {Count} cadastros ({Periodo}) enviado para {Email} (empresa {Slug}).",
                objRows.Count, sPeriodo, objCompany.ReportEmail, objCompany.Slug);
        }
    }
}
