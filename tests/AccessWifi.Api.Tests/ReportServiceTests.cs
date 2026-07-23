using AccessWifiService;
using Microsoft.Extensions.Logging.Abstractions;
using Models.DataBase;
using Models.Persistence;

namespace AccessWifi.Api.Tests;

public class ReportServiceTests
{
    /// <summary>Dublê do envio: guarda o que seria enviado.</summary>
    private class FakeEmailSender : IEmailSender
    {
        public List<(string To, string Subject, byte[]? Attachment)> Enviados { get; } = new();

        public Task SendAsync(
            string sToEmail, string sSubject, string sBody,
            byte[]? objAttachment, string? sAttachmentName, CancellationToken objCancellationToken = default)
        {
            Enviados.Add((sToEmail, sSubject, objAttachment));
            return Task.CompletedTask;
        }
    }

    private static Company AddCompany(
        AppDbContext objDbContext, string sSlug, string? sReportEmail, int iSendDay, bool bActive = true)
    {
        Company objCompany = new Company
        {
            Name = sSlug,
            Slug = sSlug,
            ReportEmail = sReportEmail,
            ReportSendDay = iSendDay,
            Active = bActive,
        };
        objDbContext.Companies.Add(objCompany);
        objDbContext.SaveChanges();
        return objCompany;
    }

    private static void AddLead(AppDbContext objDbContext, Guid objCompanyId, DateTime dtTimestamp)
    {
        objDbContext.Leads.Add(new Lead
        {
            IDCompany = objCompanyId,
            Nome = "Fulano",
            Timestamp = dtTimestamp,
        });
        objDbContext.SaveChanges();
    }

    [Fact]
    public void PreviousMonthRangeUtc_PrimeiroDeAgosto_DevolveJulhoCompleto()
    {
        (DateTime dtStart, DateTime dtEnd) =
            ReportSchedule.PreviousMonthRangeUtc(new DateTime(2026, 8, 1));

        Assert.Equal(new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc), dtStart);
        Assert.Equal(new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc), dtEnd);
    }

    [Fact]
    public void PreviousMonthRangeUtc_Janeiro_VoltaParaDezembroDoAnoAnterior()
    {
        (DateTime dtStart, DateTime dtEnd) =
            ReportSchedule.PreviousMonthRangeUtc(new DateTime(2026, 1, 10));

        Assert.Equal(new DateTime(2025, 12, 1, 0, 0, 0, DateTimeKind.Utc), dtStart);
        Assert.Equal(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), dtEnd);
    }

    [Fact]
    public async Task SendDueReports_EnviaSoParaEmpresaComDiaBatendoEEmail_ComLeadsDoMesAnterior()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        DateTime dtHoje = new DateTime(2026, 8, 1);

        Company objComEmail = AddCompany(objDbContext, "doce", "rel@doce.com.br", iSendDay: 1);
        Company objSemEmail = AddCompany(objDbContext, "outra", null, iSendDay: 1);
        Company objOutroDia = AddCompany(objDbContext, "terceira", "z@z.com", iSendDay: 15);

        // 2 leads em julho (mês anterior) + 1 em junho + 1 em agosto (fora do período).
        AddLead(objDbContext, objComEmail.Id, new DateTime(2026, 7, 5, 12, 0, 0, DateTimeKind.Utc));
        AddLead(objDbContext, objComEmail.Id, new DateTime(2026, 7, 20, 12, 0, 0, DateTimeKind.Utc));
        AddLead(objDbContext, objComEmail.Id, new DateTime(2026, 6, 30, 12, 0, 0, DateTimeKind.Utc));
        AddLead(objDbContext, objComEmail.Id, new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc));

        FakeEmailSender objSender = new FakeEmailSender();
        ReportService objService = new ReportService(
            objDbContext, objSender, NullLogger<ReportService>.Instance);

        await objService.SendDueReportsAsync(dtHoje, CancellationToken.None);

        // Só a empresa com e-mail e dia 1 recebe.
        (string sTo, string sSubject, byte[]? objAttachment) = Assert.Single(objSender.Enviados);
        Assert.Equal("rel@doce.com.br", sTo);
        // CSV: cabeçalho + 2 linhas de julho = 3 linhas.
        string sCsv = System.Text.Encoding.UTF8.GetString(objAttachment!);
        int iLinhas = sCsv.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length;
        Assert.Equal(3, iLinhas);
    }

    [Fact]
    public async Task SendDueReports_NaoReenviaNoMesmoMes_ELancaLastReportSentAt()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        // Referência = hoje em UTC (alinha com o carimbo LastReportSentAt = UtcNow).
        DateTime dtHoje = DateTime.UtcNow.Date;
        Company objCompany = AddCompany(objDbContext, "doce", "rel@doce.com.br", iSendDay: dtHoje.Day);

        FakeEmailSender objSender = new FakeEmailSender();
        ReportService objService = new ReportService(
            objDbContext, objSender, NullLogger<ReportService>.Instance);

        await objService.SendDueReportsAsync(dtHoje, CancellationToken.None);
        await objService.SendDueReportsAsync(dtHoje, CancellationToken.None);

        Assert.Single(objSender.Enviados);
        Assert.NotNull(objDbContext.Companies.Single(c => c.Id == objCompany.Id).LastReportSentAt);
    }

    [Fact]
    public async Task SendDueReports_MesSeguinte_EnviaDeNovoMesmoComLastReportSentAtAntigo()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Company objCompany = AddCompany(objDbContext, "doce", "rel@doce.com.br", iSendDay: 1);
        // Já enviou em julho; em 01/08 deve enviar de novo (referente a julho).
        objCompany.LastReportSentAt = new DateTime(2026, 7, 1, 8, 0, 0, DateTimeKind.Utc);
        objDbContext.SaveChanges();

        FakeEmailSender objSender = new FakeEmailSender();
        ReportService objService = new ReportService(
            objDbContext, objSender, NullLogger<ReportService>.Instance);

        await objService.SendDueReportsAsync(new DateTime(2026, 8, 1), CancellationToken.None);

        Assert.Single(objSender.Enviados);
    }

    [Fact]
    public async Task SendDueReports_EmpresaInativa_NaoEnvia()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        AddCompany(objDbContext, "doce", "rel@doce.com.br", iSendDay: 1, bActive: false);

        FakeEmailSender objSender = new FakeEmailSender();
        ReportService objService = new ReportService(
            objDbContext, objSender, NullLogger<ReportService>.Instance);

        await objService.SendDueReportsAsync(new DateTime(2026, 8, 1), CancellationToken.None);

        Assert.Empty(objSender.Enviados);
    }
}
