using AccessWifiService;
using Microsoft.Extensions.Logging.Abstractions;
using Models.DataBase;
using Models.Persistence;

namespace AccessWifi.Api.Tests;

/// <summary>Relatório mensal de cadastros: um por unidade, no e-mail dela (D24).</summary>
public class ReportServiceTests
{
    private static Company AddCompany(AppDbContext objDbContext, string sSlug, int iSendDay, bool bActive = true)
    {
        Company objCompany = new Company
        {
            Name = sSlug,
            Slug = sSlug,
            ReportSendDay = iSendDay,
            Active = bActive,
        };
        objDbContext.Companies.Add(objCompany);
        objDbContext.SaveChanges();
        return objCompany;
    }

    private static Unit AddUnit(
        AppDbContext objDbContext, Guid objCompanyId, string sSlug, string sEmail = "", bool bActive = true)
    {
        Unit objUnit = new Unit { IDCompany = objCompanyId, Name = sSlug, Slug = sSlug, Email = sEmail, Active = bActive };
        objDbContext.Units.Add(objUnit);
        objDbContext.SaveChanges();
        return objUnit;
    }

    private static void AddLead(AppDbContext objDbContext, Guid objUnitId, DateTime dtCreatedAt)
    {
        objDbContext.Leads.Add(new Lead
        {
            IDUnit = objUnitId,
            Nome = "Fulano",
            CreatedAt = dtCreatedAt,
            Timestamp = dtCreatedAt,
        });
        objDbContext.SaveChanges();
    }

    private static int LinhasDoCsv(byte[]? arrCsv) =>
        System.Text.Encoding.UTF8.GetString(arrCsv!).Split('\n', StringSplitOptions.RemoveEmptyEntries).Length;

    private static ReportService CreateService(AppDbContext objDbContext, FakeEmailSender objSender) =>
        new ReportService(objDbContext, objSender, NullLogger<ReportService>.Instance);

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
    public async Task SendDueReports_CadaUnidadeComEmailRecebeSoOsCadastrosDela()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Company objRegional = AddCompany(objDbContext, "regional", iSendDay: 1);
        Unit objItaituba = AddUnit(objDbContext, objRegional.Id, "itaituba", "gerente.itb@regional.com.br");
        Unit objSantarem = AddUnit(objDbContext, objRegional.Id, "santarem", "gerente.stm@regional.com.br");
        Unit objSemEmail = AddUnit(objDbContext, objRegional.Id, "altamira");
        Company objOutroDia = AddCompany(objDbContext, "outra", iSendDay: 15);
        AddUnit(objDbContext, objOutroDia.Id, "outra-loja", "z@z.com");

        // Itaituba: 2 em julho + 1 em junho e 1 em agosto (fora). Santarém: 1 em julho. Altamira: 1 (sem e-mail).
        AddLead(objDbContext, objItaituba.Id, new DateTime(2026, 7, 5, 12, 0, 0, DateTimeKind.Utc));
        AddLead(objDbContext, objItaituba.Id, new DateTime(2026, 7, 20, 12, 0, 0, DateTimeKind.Utc));
        AddLead(objDbContext, objItaituba.Id, new DateTime(2026, 6, 30, 12, 0, 0, DateTimeKind.Utc));
        AddLead(objDbContext, objItaituba.Id, new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc));
        AddLead(objDbContext, objSantarem.Id, new DateTime(2026, 7, 9, 12, 0, 0, DateTimeKind.Utc));
        AddLead(objDbContext, objSemEmail.Id, new DateTime(2026, 7, 9, 12, 0, 0, DateTimeKind.Utc));
        FakeEmailSender objSender = new FakeEmailSender();

        await CreateService(objDbContext, objSender).SendDueReportsAsync(new DateTime(2026, 8, 1), CancellationToken.None);

        Assert.Equal(2, objSender.Enviados.Count);
        FakeEmailSender.Email objItb = objSender.Enviados.Single(email => email.To == "gerente.itb@regional.com.br");
        Assert.Equal(3, LinhasDoCsv(objItb.Attachment)); // cabeçalho + 2 de julho
        Assert.Contains("itaituba", objItb.Subject);
        Assert.Equal("cadastros-regional-itaituba-2026-07.csv", objItb.AttachmentName);
        FakeEmailSender.Email objStm = objSender.Enviados.Single(email => email.To == "gerente.stm@regional.com.br");
        Assert.Equal(2, LinhasDoCsv(objStm.Attachment));
    }

    [Fact]
    public async Task SendDueReports_LeadCadastradoNoMesMasReconectadoDepois_ContaNoMesDoCadastro()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Company objCompany = AddCompany(objDbContext, "regional", iSendDay: 1);
        Unit objUnit = AddUnit(objDbContext, objCompany.Id, "itaituba", "gerente@regional.com.br");

        // Cadastro em julho, mas o aparelho reconectou em agosto (Timestamp movido pelo upsert).
        objDbContext.Leads.Add(new Lead
        {
            IDUnit = objUnit.Id,
            Nome = "Fulano",
            CreatedAt = new DateTime(2026, 7, 10, 12, 0, 0, DateTimeKind.Utc),
            Timestamp = new DateTime(2026, 8, 3, 9, 0, 0, DateTimeKind.Utc),
        });
        objDbContext.SaveChanges();
        FakeEmailSender objSender = new FakeEmailSender();

        await CreateService(objDbContext, objSender).SendDueReportsAsync(new DateTime(2026, 8, 1), CancellationToken.None);

        Assert.Equal(2, LinhasDoCsv(Assert.Single(objSender.Enviados).Attachment));
    }

    [Fact]
    public async Task SendDueReports_NaoReenviaNoMesmoMes_EMarcaAUnidade()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        // Referência = hoje em UTC (alinha com o carimbo LastReportSentAt = UtcNow).
        DateTime dtHoje = DateTime.UtcNow.Date;
        Company objCompany = AddCompany(objDbContext, "regional", iSendDay: dtHoje.Day);
        Unit objUnit = AddUnit(objDbContext, objCompany.Id, "itaituba", "gerente@regional.com.br");
        FakeEmailSender objSender = new FakeEmailSender();
        ReportService objService = CreateService(objDbContext, objSender);

        await objService.SendDueReportsAsync(dtHoje, CancellationToken.None);
        await objService.SendDueReportsAsync(dtHoje, CancellationToken.None);

        Assert.Single(objSender.Enviados);
        Assert.NotNull(objDbContext.Units.Single(unit => unit.Id == objUnit.Id).LastReportSentAt);
    }

    [Fact]
    public async Task SendDueReports_MesSeguinte_EnviaDeNovoMesmoComLastReportSentAtAntigo()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Company objCompany = AddCompany(objDbContext, "regional", iSendDay: 1);
        Unit objUnit = AddUnit(objDbContext, objCompany.Id, "itaituba", "gerente@regional.com.br");
        // Já enviou em julho; em 01/08 deve enviar de novo (referente a julho).
        objUnit.LastReportSentAt = new DateTime(2026, 7, 1, 8, 0, 0, DateTimeKind.Utc);
        objDbContext.SaveChanges();
        FakeEmailSender objSender = new FakeEmailSender();

        await CreateService(objDbContext, objSender).SendDueReportsAsync(new DateTime(2026, 8, 1), CancellationToken.None);

        Assert.Single(objSender.Enviados);
    }

    [Fact]
    public async Task SendDueReports_EmpresaOuUnidadeInativa_NaoEnvia()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Company objInativa = AddCompany(objDbContext, "inativa", iSendDay: 1, bActive: false);
        AddUnit(objDbContext, objInativa.Id, "loja-a", "a@a.com");
        Company objAtiva = AddCompany(objDbContext, "ativa", iSendDay: 1);
        AddUnit(objDbContext, objAtiva.Id, "loja-b", "b@b.com", bActive: false);
        FakeEmailSender objSender = new FakeEmailSender();

        await CreateService(objDbContext, objSender).SendDueReportsAsync(new DateTime(2026, 8, 1), CancellationToken.None);

        Assert.Empty(objSender.Enviados);
    }

    [Fact]
    public async Task SendDueReports_EmailFalhou_NaoMarcaComoEnviadoESegueParaAsOutras()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Company objCompany = AddCompany(objDbContext, "regional", iSendDay: 1);
        Unit objPrimeira = AddUnit(objDbContext, objCompany.Id, "a-primeira", "a@regional.com.br");
        Unit objSegunda = AddUnit(objDbContext, objCompany.Id, "b-segunda", "b@regional.com.br");
        FakeEmailSender objSender = new FakeEmailSender { FalharVezes = 1 }; // a primeira (ordem por nome) falha

        await CreateService(objDbContext, objSender).SendDueReportsAsync(new DateTime(2026, 8, 1), CancellationToken.None);

        Assert.Equal("b@regional.com.br", Assert.Single(objSender.Enviados).To);
        Assert.Null(objDbContext.Units.Single(unit => unit.Id == objPrimeira.Id).LastReportSentAt);
        Assert.NotNull(objDbContext.Units.Single(unit => unit.Id == objSegunda.Id).LastReportSentAt);
        // Correio eletrônico: só o que saiu.
        Assert.Equal("b@regional.com.br", Assert.Single(objDbContext.SentEmails).ToEmail);
    }

    [Fact]
    public async Task Correio_RelatorioEnviado_FicaRegistradoEOCsvERemontadoDoMes()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Company objCompany = AddCompany(objDbContext, "regional", iSendDay: 1);
        Unit objUnit = AddUnit(objDbContext, objCompany.Id, "itaituba", "gerente.itb@regional.com.br");
        AddLead(objDbContext, objUnit.Id, new DateTime(2026, 7, 10, 15, 0, 0, DateTimeKind.Utc));
        AddLead(objDbContext, objUnit.Id, new DateTime(2026, 7, 20, 15, 0, 0, DateTimeKind.Utc));
        AddLead(objDbContext, objUnit.Id, new DateTime(2026, 8, 2, 15, 0, 0, DateTimeKind.Utc)); // fora do mês
        FakeEmailSender objSender = new FakeEmailSender();

        await CreateService(objDbContext, objSender).SendDueReportsAsync(new DateTime(2026, 8, 1), CancellationToken.None);

        FakeEmailSender.Email objEnviado = Assert.Single(objSender.Enviados);
        SentEmail objRegistro = Assert.Single(objDbContext.SentEmails);
        Assert.Equal(SentEmailKind.Report, objRegistro.Kind);
        Assert.Equal(objUnit.Id, objRegistro.IDUnit);
        Assert.Equal(objEnviado.Subject, objRegistro.Subject);
        Assert.Equal(objEnviado.Body, objRegistro.Body);
        Assert.Equal(new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc), objRegistro.PeriodStart);
        Assert.Equal(new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc), objRegistro.PeriodEnd);

        AccessWifi.Api.Controllers.EmailsController objController = new(objDbContext);
        TestHelpers.SetUser(objController, null, "root");
        Microsoft.AspNetCore.Mvc.FileContentResult objCsv = Assert.IsType<Microsoft.AspNetCore.Mvc.FileContentResult>(
            await objController.Attachment(objRegistro.Id, "regional", CancellationToken.None));
        Assert.Equal(objEnviado.AttachmentName, objCsv.FileDownloadName);
        Assert.Equal(LinhasDoCsv(objEnviado.Attachment), LinhasDoCsv(objCsv.FileContents)); // cabeçalho + 2 cadastros
    }
}
