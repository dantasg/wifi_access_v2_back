using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Models.DataBase;
using Models.Persistence;

namespace AccessWifiService
{
    /// <summary>
    /// Monta e envia o relatório mensal de cadastros (os do mês anterior), por unidade (D24): cada unidade
    /// com e-mail recebe só os cadastros dela, no dia de envio configurado na empresa.
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

            // Unidades ativas, com e-mail, de empresas ativas cujo dia de envio é hoje, que ainda não
            // receberam o relatório deste mês (LastReportSentAt anterior ao início do mês corrente).
            var objDue = await (
                from unit in _objDbContext.Units
                join company in _objDbContext.Companies on unit.IDCompany equals company.Id
                where company.Active && unit.Active && unit.Email != ""
                    && company.ReportSendDay == dtToday.Day
                    && (unit.LastReportSentAt == null || unit.LastReportSentAt < dtEndUtc)
                orderby company.Name, unit.Name
                select new { Unit = unit, Company = company })
                .ToListAsync(objCancellationToken);

            foreach (var objItem in objDue)
            {
                try
                {
                    await SendForUnitAsync(objItem.Company, objItem.Unit, dtStartUtc, dtEndUtc, objCancellationToken);
                }
                catch (Exception objException) when (objException is not OperationCanceledException)
                {
                    // Não marca como enviado: a próxima volta do dia tenta de novo. As outras unidades seguem.
                    _objLogger.LogError(objException,
                        "Relatório mensal da unidade {Unidade} ({Empresa}) não saiu para {Email}.",
                        objItem.Unit.Name, objItem.Company.Name, objItem.Unit.Email);
                    continue;
                }
                objItem.Unit.LastReportSentAt = DateTime.UtcNow;
                await _objDbContext.SaveChangesAsync(objCancellationToken);
            }
        }

        private async Task SendForUnitAsync(
            Company objCompany, Unit objUnit, DateTime dtStartUtc, DateTime dtEndUtc, CancellationToken objCancellationToken)
        {
            List<LeadReportRow> objRows = await _objDbContext.Leads.AsNoTracking()
                .Where(lead => lead.IDUnit == objUnit.Id && lead.CreatedAt >= dtStartUtc && lead.CreatedAt < dtEndUtc)
                .OrderBy(lead => lead.CreatedAt)
                .Select(lead => new LeadReportRow(lead, objUnit.Name))
                .ToListAsync(objCancellationToken);

            string sPeriodo = dtStartUtc.ToString("MM/yyyy", CultureInfo.InvariantCulture);
            byte[] objCsv = LeadsCsv.Build(objRows);
            string sFileName = $"cadastros-{objCompany.Slug}-{objUnit.Slug}-{dtStartUtc:yyyy-MM}.csv";
            string sSubject = $"Relatório de cadastros — {objCompany.Name} — {objUnit.Name} — {sPeriodo}";
            string sBody =
                $"Olá,\r\n\r\nSegue em anexo o relatório de cadastros da unidade {objUnit.Name} ({objCompany.Name}) " +
                $"referente a {sPeriodo}.\r\nTotal de cadastros no período: {objRows.Count}.\r\n\r\n" +
                "Mensagem automática do AccessWifi.";

            await _objEmailSender.SendAsync(objUnit.Email.Trim(), sSubject, sBody, objCsv, sFileName, objCancellationToken);

            _objLogger.LogInformation(
                "Relatório de {Count} cadastros ({Periodo}) enviado para {Email} (unidade {Unidade}, empresa {Slug}).",
                objRows.Count, sPeriodo, objUnit.Email, objUnit.Name, objCompany.Slug);
        }
    }
}
