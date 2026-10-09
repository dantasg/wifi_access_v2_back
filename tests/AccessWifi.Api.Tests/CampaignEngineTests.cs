using AccessWifiService.Campaigns;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Models.Campaigns;
using Models.DataBase;
using Models.Persistence;

namespace AccessWifi.Api.Tests;

/// <summary>Agenda, seleção e envio das campanhas (o motor que roda no AccessWifiService).</summary>
public class CampaignEngineTests
{
    // Terça, 29/09/2026 às 09:00 em Belém = 12:00 UTC.
    private static readonly DateTime s_dtNove = new DateTime(2026, 9, 29, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly s_dtHoje = new DateOnly(2026, 9, 29);
    private const string EmailDaUnidade = "gerente.itaituba@regional.com.br";

    private sealed class Cenario
    {
        public required AppDbContext Db { get; init; }
        public required Company Company { get; init; }
        public required Unit Unit { get; init; }
        public FakeEmailSender Email { get; } = new FakeEmailSender();
    }

    private static Cenario CreateScenario(AppDbContext objDbContext, params string[] arrKinds)
    {
        Company objCompany = new Company { Name = "Lojas Regional", Slug = "regional", TimeZone = "America/Belem" };
        Unit objUnit = new Unit { IDCompany = objCompany.Id, Name = "Itaituba", Slug = "itaituba", Email = EmailDaUnidade };
        objDbContext.AddRange(objCompany, objUnit);
        foreach (string sKind in arrKinds.Length > 0 ? arrKinds : CampaignKind.All.ToArray())
        {
            objDbContext.CompanyCampaignKinds.Add(new CompanyCampaignKind { IDCompany = objCompany.Id, Kind = sKind });
        }
        objDbContext.SaveChanges();
        return new Cenario { Db = objDbContext, Company = objCompany, Unit = objUnit };
    }

    private static Customer AddCustomer(
        Cenario objCenario, string sPhone, string sName = "Cliente",
        DateOnly? dtBirth = null, DateOnly? dtFirstVisit = null, DateTime? dtLastVisit = null,
        int iVisits = 1, string sInstagram = "", Unit? objUnit = null)
    {
        DateOnly dtFirst = dtFirstVisit ?? new DateOnly(2026, 1, 10);
        DateTime dtLast = dtLastVisit ?? new DateTime(2026, 9, 1, 15, 0, 0, DateTimeKind.Utc);
        Customer objCustomer = new Customer
        {
            IDCompany = objCenario.Company.Id,
            Phone = sPhone,
            Name = sName,
            Instagram = sInstagram,
            BirthDate = dtBirth,
            FirstVisitDate = dtFirst,
            FirstVisitAt = dtFirst.ToDateTime(new TimeOnly(15, 0), DateTimeKind.Utc),
            LastVisitAt = dtLast,
            LastVisitDate = DateOnly.FromDateTime(dtLast),
            VisitCount = iVisits,
            IDLastUnit = (objUnit ?? objCenario.Unit).Id,
        };
        objCenario.Db.Customers.Add(objCustomer);
        objCenario.Db.CustomerUnits.Add(new CustomerUnit
        {
            IDCustomer = objCustomer.Id, IDUnit = (objUnit ?? objCenario.Unit).Id, FirstVisitAt = dtLast, LastVisitAt = dtLast,
        });
        objCenario.Db.SaveChanges();
        return objCustomer;
    }

    private static Campaign AddCampaign(
        Cenario objCenario, string sKind, CampaignConfig objConfig, DateTime? dtNextRun = null, string? sName = null)
    {
        Campaign objCampaign = new Campaign
        {
            IDCompany = objCenario.Company.Id,
            Kind = sKind,
            Name = sName ?? CampaignKind.Label(sKind),
            ConfigJson = objConfig.ToJson(),
            NextRunAt = dtNextRun ?? s_dtNove,
        };
        objCenario.Db.Campaigns.Add(objCampaign);
        objCenario.Db.CampaignVersions.Add(new CampaignVersion
        {
            IDCampaign = objCampaign.Id, Number = 1, Name = objCampaign.Name, ConfigJson = objCampaign.ConfigJson,
        });
        objCenario.Db.SaveChanges();
        return objCampaign;
    }

    private static CampaignEngine CreateEngine(Cenario objCenario, int iBloco = 2)
    {
        return new CampaignEngine(
            objCenario.Db,
            objCenario.Email,
            Options.Create(new CampaignEngineOptions
            {
                TickSeconds = 5, SelectionChunkSize = iBloco, EmailMaxAttempts = 3, EmailRetryMinutes = 5,
            }),
            NullLogger<CampaignEngine>.Instance);
    }

    private static CampaignConfig Mensagem(string sTexto = "Oi {primeiro_nome}!") =>
        new CampaignConfig { Message = sTexto, SendTime = "09:00" };

    private static List<CampaignRecipient> RecipientsOf(AppDbContext objDbContext, Guid objCampaignId) =>
        objDbContext.CampaignRecipients
            .Where(recipient => objDbContext.CampaignRuns.Any(run => run.Id == recipient.IDRun && run.IDCampaign == objCampaignId))
            .OrderBy(recipient => recipient.Id)
            .ToList();

    // ----------------------------------------------------------------- Agenda

    [Fact]
    public async Task Agenda_NoHorario_CriaUmaExecucaoEAgendaOProximoDia()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Cenario objCenario = CreateScenario(objDbContext);
        Campaign objCampaign = AddCampaign(objCenario, CampaignKind.Birthday, Mensagem());
        CampaignEngine objEngine = CreateEngine(objCenario);

        await objEngine.ScheduleDueAsync(s_dtNove.AddSeconds(3));
        await objEngine.ScheduleDueAsync(s_dtNove.AddSeconds(8)); // outra volta: não duplica

        CampaignRun objRun = Assert.Single(objDbContext.CampaignRuns);
        Assert.Equal(CampaignRunStatus.Selecting, objRun.Status);
        Assert.Equal(s_dtHoje, objRun.LocalDate);
        Assert.Equal(s_dtNove, objRun.ScheduledFor);
        Assert.Equal(s_dtNove.AddDays(1), objDbContext.Campaigns.Single(campaign => campaign.Id == objCampaign.Id).NextRunAt);
    }

    [Fact]
    public async Task Agenda_HorarioMudadoParaMaisTardeNoMesmoDia_NaoDisparaDeNovo()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Cenario objCenario = CreateScenario(objDbContext);
        Campaign objCampaign = AddCampaign(objCenario, CampaignKind.Birthday, Mensagem());
        CampaignEngine objEngine = CreateEngine(objCenario);
        await objEngine.ScheduleDueAsync(s_dtNove.AddSeconds(3));

        // Depois do disparo das 09:00, o horário vira 15:00 do mesmo dia.
        Campaign objEditada = objDbContext.Campaigns.Single(campaign => campaign.Id == objCampaign.Id);
        objEditada.ConfigJson = (Mensagem() with { SendTime = "15:00" }).ToJson();
        objEditada.NextRunAt = s_dtNove.AddHours(6);
        objDbContext.SaveChanges();
        await objEngine.ScheduleDueAsync(s_dtNove.AddHours(6).AddSeconds(3));

        Assert.Single(objDbContext.CampaignRuns); // cada dia é uma execução
        Assert.Equal(s_dtNove.AddHours(6).AddDays(1),
            objDbContext.Campaigns.Single(campaign => campaign.Id == objCampaign.Id).NextRunAt);
    }

    [Fact]
    public async Task Agenda_ServicoForaEDiaVirou_RegistraPerdidaEDisparaOHorarioDeHoje()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Cenario objCenario = CreateScenario(objDbContext);
        AddCampaign(objCenario, CampaignKind.Birthday, Mensagem(), dtNextRun: s_dtNove.AddDays(-1));

        // Volta às 10:00 de Belém do dia 29: o de ontem se perdeu; o de hoje (09:00) ainda vale.
        await CreateEngine(objCenario).ScheduleDueAsync(s_dtNove.AddHours(1));

        List<CampaignRun> objRuns = objDbContext.CampaignRuns.OrderBy(run => run.ScheduledFor).ToList();
        Assert.Equal(2, objRuns.Count);
        Assert.Equal(CampaignRunStatus.Missed, objRuns[0].Status);
        Assert.Equal(CampaignRunStatus.Selecting, objRuns[1].Status);
        Assert.Equal(s_dtHoje, objRuns[1].LocalDate);
    }

    [Fact]
    public async Task Agenda_TipoDesligadoParaAEmpresa_NaoDisparaEFicaSemAgenda()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Cenario objCenario = CreateScenario(objDbContext, CampaignKind.Filtered); // aniversário não liberado
        Campaign objCampaign = AddCampaign(objCenario, CampaignKind.Birthday, Mensagem());

        await CreateEngine(objCenario).ScheduleDueAsync(s_dtNove.AddSeconds(3));

        Assert.Empty(objDbContext.CampaignRuns);
        Assert.Null(objDbContext.Campaigns.Single(campaign => campaign.Id == objCampaign.Id).NextRunAt);
    }

    // ---------------------------------------------------------- Quem recebe

    [Fact]
    public async Task Aniversario_NaTerca_ListaDeHojeAteSabado_ComAIdadeDoDiaDoAniversario()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Cenario objCenario = CreateScenario(objDbContext);
        AddCustomer(objCenario, "91900000001", "Ana Beatriz", dtBirth: new DateOnly(1998, 9, 29)); // hoje (terça)
        AddCustomer(objCenario, "91900000002", "Bruno", dtBirth: new DateOnly(1990, 10, 3));       // sábado
        AddCustomer(objCenario, "91900000003", "Carla", dtBirth: new DateOnly(1995, 9, 28));       // segunda: já passou
        AddCustomer(objCenario, "91900000004", "Davi", dtBirth: new DateOnly(1995, 10, 4));        // domingo: próxima semana
        AddCustomer(objCenario, "91900000005", "Edu");                                              // sem nascimento válido
        Campaign objCampaign = AddCampaign(objCenario, CampaignKind.Birthday,
            Mensagem("Parabéns, {primeiro_nome}! {idade} anos com a {empresa} ({unidade})."));

        await CreateEngine(objCenario).TickAsync(s_dtNove.AddSeconds(3));

        List<CampaignRecipient> objLista = RecipientsOf(objDbContext, objCampaign.Id);
        Assert.Equal(new[] { "91900000001", "91900000002" }, objLista.Select(recipient => recipient.Phone).Order());
        CampaignRecipient objAna = objLista.Single(recipient => recipient.Phone == "91900000001");
        Assert.Equal("Parabéns, Ana! 28 anos com a Lojas Regional (Itaituba).", objAna.Message);
        Assert.Equal(new DateOnly(2026, 9, 29), objAna.EventDate);
        Assert.Equal("ter, 29/09 · 28 anos", objAna.Info);
        // Bruno faz 36 no sábado: a mensagem já vai com a idade que ele completa.
        CampaignRecipient objBruno = objLista.Single(recipient => recipient.Phone == "91900000002");
        Assert.Equal("Parabéns, Bruno! 36 anos com a Lojas Regional (Itaituba).", objBruno.Message);
        Assert.Equal("sáb, 03/10 · 36 anos", objBruno.Info);
        Assert.All(objLista, recipient => Assert.Equal(CampaignRecipientStatus.Sent, recipient.Status));
        CampaignRun objRun = objDbContext.CampaignRuns.Single();
        Assert.Equal(CampaignRunStatus.Completed, objRun.Status);
        Assert.Equal(2, objRun.SentCount);
        Assert.False(objRun.Simulation);
    }

    [Fact]
    public async Task Aniversario_NaSegunda_PegaTambemODomingo_ENoDomingoNaoDispara()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Cenario objCenario = CreateScenario(objDbContext);
        AddCustomer(objCenario, "91900000001", "Domingo", dtBirth: new DateOnly(1990, 10, 4));
        AddCustomer(objCenario, "91900000002", "Sexta", dtBirth: new DateOnly(1990, 10, 9));
        AddCustomer(objCenario, "91900000003", "Outra semana", dtBirth: new DateOnly(1990, 10, 11));
        // Sábado, 03/10 às 09:00: o próximo disparo pula o domingo e cai na segunda, 05/10.
        DateTime dtSabado = new DateTime(2026, 10, 3, 12, 0, 0, DateTimeKind.Utc);
        Campaign objCampaign = AddCampaign(objCenario, CampaignKind.Birthday, Mensagem(), dtNextRun: dtSabado);
        CampaignEngine objEngine = CreateEngine(objCenario);

        await objEngine.TickAsync(dtSabado.AddSeconds(3));
        DateTime dtSegunda = new DateTime(2026, 10, 5, 12, 0, 0, DateTimeKind.Utc);
        Assert.Equal(dtSegunda, objDbContext.Campaigns.Single(campaign => campaign.Id == objCampaign.Id).NextRunAt);
        await objEngine.TickAsync(dtSegunda.AddSeconds(3));

        CampaignRun objDeSegunda = objDbContext.CampaignRuns.Single(run => run.LocalDate == new DateOnly(2026, 10, 5));
        List<CampaignRecipient> objLista = objDbContext.CampaignRecipients.Where(recipient => recipient.IDRun == objDeSegunda.Id).ToList();
        Assert.Equal(new[] { "91900000001", "91900000002" }, objLista.Select(recipient => recipient.Phone).Order());
        Assert.Equal(2, objDbContext.CampaignRuns.Count()); // sábado e segunda; domingo não
    }

    [Fact]
    public void Aniversario_FaixaDaSemana()
    {
        Assert.Equal((new DateOnly(2026, 10, 4), new DateOnly(2026, 10, 10)), CampaignCalendar.BirthdayRange(new DateOnly(2026, 10, 5))); // segunda
        Assert.Equal((new DateOnly(2026, 10, 6), new DateOnly(2026, 10, 10)), CampaignCalendar.BirthdayRange(new DateOnly(2026, 10, 6))); // terça
        Assert.Equal((new DateOnly(2026, 10, 10), new DateOnly(2026, 10, 10)), CampaignCalendar.BirthdayRange(new DateOnly(2026, 10, 10))); // sábado
        Assert.Equal((new DateOnly(2026, 10, 4), new DateOnly(2026, 10, 10)), CampaignCalendar.BirthdayRange(new DateOnly(2026, 10, 4))); // domingo
        // Virada de ano: terça 29/12 a sábado 02/01.
        Assert.Equal(new DateOnly(2027, 1, 2), CampaignCalendar.BirthdayInRange(new DateOnly(1990, 1, 2), new DateOnly(2026, 12, 29)));
    }

    [Fact]
    public async Task Aniversario_NascidoEm29DeFevereiro_RecebeEm28EmAnoNaoBissexto()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Cenario objCenario = CreateScenario(objDbContext);
        AddCustomer(objCenario, "91900000001", "Leap", dtBirth: new DateOnly(2000, 2, 29));
        // Quarta, 28/02/2029 (ano não bissexto).
        DateTime dtNove28 = new DateTime(2029, 2, 28, 12, 0, 0, DateTimeKind.Utc);
        Campaign objCampaign = AddCampaign(objCenario, CampaignKind.Birthday, Mensagem(), dtNextRun: dtNove28);

        await CreateEngine(objCenario).TickAsync(dtNove28.AddSeconds(3));

        Assert.Single(RecipientsOf(objDbContext, objCampaign.Id));
    }

    [Fact]
    public async Task AniversarioDeCadastro_QuemCompletaAnoHoje_MasNaoQuemSeCadastrouHoje()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Cenario objCenario = CreateScenario(objDbContext);
        AddCustomer(objCenario, "91900000001", "Um ano", dtFirstVisit: new DateOnly(2025, 9, 29));
        AddCustomer(objCenario, "91900000002", "Hoje", dtFirstVisit: s_dtHoje);
        Campaign objCampaign = AddCampaign(objCenario, CampaignKind.SignupAnniversary,
            Mensagem("{anos_de_cadastro} ano(s) conosco"));

        await CreateEngine(objCenario).TickAsync(s_dtNove.AddSeconds(3));

        CampaignRecipient objRecipient = Assert.Single(RecipientsOf(objDbContext, objCampaign.Id));
        Assert.Equal("1 ano(s) conosco", objRecipient.Message);
        Assert.Equal("1 ano de cadastro", objRecipient.Info);
    }

    [Fact]
    public async Task SentimosSuaFalta_UmaVezPorAusencia()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Cenario objCenario = CreateScenario(objDbContext);
        Customer objSumido = AddCustomer(objCenario, "91900000001", "Sumido", dtLastVisit: s_dtNove.AddDays(-40));
        AddCustomer(objCenario, "91900000002", "Frequente", dtLastVisit: s_dtNove.AddDays(-3));
        Campaign objCampaign = AddCampaign(objCenario, CampaignKind.WeMissYou, Mensagem() with { AbsenceDays = 30 });
        CampaignEngine objEngine = CreateEngine(objCenario);

        await objEngine.TickAsync(s_dtNove.AddSeconds(3));
        await objEngine.TickAsync(s_dtNove.AddDays(1).AddSeconds(3)); // dia seguinte: já recebeu nesta ausência

        CampaignRecipient objRecipient = Assert.Single(RecipientsOf(objDbContext, objCampaign.Id));
        Assert.Equal(objSumido.Id, objRecipient.IDCustomer);

        // Voltou e sumiu de novo por mais de 30 dias: recebe outra vez.
        Customer objDeNovo = objDbContext.Customers.Single(customer => customer.Id == objSumido.Id);
        objDeNovo.LastVisitAt = s_dtNove.AddDays(2);
        objDbContext.SaveChanges();
        DateTime dtDepois = s_dtNove.AddDays(40);
        Campaign objAgendada = objDbContext.Campaigns.Single(campaign => campaign.Id == objCampaign.Id);
        objAgendada.NextRunAt = dtDepois;
        objDbContext.SaveChanges();
        await objEngine.TickAsync(dtDepois.AddSeconds(3));

        // O sumido recebe a 2ª (nova ausência). O "frequente", que também passou de 30 dias sem vir, recebe a 1ª.
        List<CampaignRecipient> objTodos = RecipientsOf(objDbContext, objCampaign.Id);
        Assert.Equal(2, objTodos.Count(recipient => recipient.IDCustomer == objSumido.Id));
        Assert.Equal(3, objTodos.Count);
    }

    [Fact]
    public async Task ClienteFrequente_CadaMarcoUmaVezSo()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Cenario objCenario = CreateScenario(objDbContext);
        Customer objCinco = AddCustomer(objCenario, "91900000001", "Cinco", iVisits: 5);
        AddCustomer(objCenario, "91900000002", "Quatro", iVisits: 4);
        Campaign objCampaign = AddCampaign(objCenario, CampaignKind.FrequentCustomer, Mensagem() with { VisitMilestone = 5 });
        CampaignEngine objEngine = CreateEngine(objCenario);

        await objEngine.TickAsync(s_dtNove.AddSeconds(3));
        await objEngine.TickAsync(s_dtNove.AddDays(1).AddSeconds(3)); // mesmo marco: não repete

        CampaignRecipient objPrimeiro = Assert.Single(RecipientsOf(objDbContext, objCampaign.Id));
        Assert.Equal(5, objPrimeiro.Milestone);

        objDbContext.Customers.Single(customer => customer.Id == objCinco.Id).VisitCount = 10;
        objDbContext.SaveChanges();
        await objEngine.TickAsync(s_dtNove.AddDays(2).AddSeconds(3));

        Assert.Equal(new int?[] { 5, 10 }, RecipientsOf(objDbContext, objCampaign.Id).Select(recipient => recipient.Milestone));
    }

    [Fact]
    public async Task Filtrada_AplicaOsFiltrosEmCadaExecucao()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Cenario objCenario = CreateScenario(objDbContext);
        AddCustomer(objCenario, "91900000001", "Jovem com Insta", dtBirth: new DateOnly(2000, 5, 1), iVisits: 3, sInstagram: "@j");
        AddCustomer(objCenario, "91900000002", "Jovem sem Insta", dtBirth: new DateOnly(2000, 5, 1), iVisits: 3);
        AddCustomer(objCenario, "91900000003", "Mais velho", dtBirth: new DateOnly(1970, 5, 1), iVisits: 3, sInstagram: "@m");
        AddCustomer(objCenario, "91900000004", "Pouca visita", dtBirth: new DateOnly(2000, 5, 1), iVisits: 1, sInstagram: "@p");
        CampaignConfig objConfig = Mensagem() with
        {
            Schedule = new CampaignScheduleConfig { Recurrence = CampaignRecurrence.Daily, StartDate = s_dtHoje },
            Filters = new CampaignFilters
            {
                UnitIds = [objCenario.Unit.Id], AgeMin = 18, AgeMax = 30, VisitsMin = 2, HasInstagram = true,
            },
        };
        Campaign objCampaign = AddCampaign(objCenario, CampaignKind.Filtered, objConfig);
        CampaignEngine objEngine = CreateEngine(objCenario);

        await objEngine.TickAsync(s_dtNove.AddSeconds(3));
        Assert.Equal("91900000001", Assert.Single(RecipientsOf(objDbContext, objCampaign.Id)).Phone);

        // Cliente novo, que passa nos filtros, entra já na execução do dia seguinte (D16).
        AddCustomer(objCenario, "91900000005", "Novo", dtBirth: new DateOnly(2001, 1, 1), iVisits: 2, sInstagram: "@n");
        await objEngine.TickAsync(s_dtNove.AddDays(1).AddSeconds(3));

        Assert.Equal(3, RecipientsOf(objDbContext, objCampaign.Id).Count); // 1 do 1º dia + 2 do 2º
    }

    [Fact]
    public async Task Filtrada_NaoRepeteParaQuemRecebeuNosUltimosNDias()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Cenario objCenario = CreateScenario(objDbContext);
        AddCustomer(objCenario, "91900000001");
        CampaignConfig objConfig = Mensagem() with
        {
            Schedule = new CampaignScheduleConfig { Recurrence = CampaignRecurrence.Daily, StartDate = s_dtHoje },
            ResendAfterDays = 7,
        };
        Campaign objCampaign = AddCampaign(objCenario, CampaignKind.Filtered, objConfig);
        CampaignEngine objEngine = CreateEngine(objCenario);

        for (int iDia = 0; iDia <= 8; iDia++)
        {
            await objEngine.TickAsync(s_dtNove.AddDays(iDia).AddSeconds(3));
        }

        // Recebeu no dia 0; do dia 1 ao 7 ainda está dentro dos 7 dias; no dia 8 recebe de novo.
        Assert.Equal(9, objDbContext.CampaignRuns.Count());
        Assert.Equal(2, RecipientsOf(objDbContext, objCampaign.Id).Count);
    }

    // -------------------------------------------------- Limite de 1 por dia (D11)

    [Fact]
    public async Task LimiteDoDia_NoMesmoHorario_AniversarioVenceEAFiltradaIgnora()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Cenario objCenario = CreateScenario(objDbContext);
        AddCustomer(objCenario, "91900000001", "Aniversariante", dtBirth: new DateOnly(1998, 9, 29));
        AddCustomer(objCenario, "91900000002", "Outro");
        Campaign objFiltrada = AddCampaign(objCenario, CampaignKind.Filtered, Mensagem() with
        {
            Schedule = new CampaignScheduleConfig { Recurrence = CampaignRecurrence.Once, StartDate = s_dtHoje },
        }, sName: "Promo");
        Campaign objAniversario = AddCampaign(objCenario, CampaignKind.Birthday, Mensagem());

        await CreateEngine(objCenario).TickAsync(s_dtNove.AddSeconds(3));

        Assert.Equal(CampaignRecipientStatus.Sent, Assert.Single(RecipientsOf(objDbContext, objAniversario.Id)).Status);
        List<CampaignRecipient> objDaPromo = RecipientsOf(objDbContext, objFiltrada.Id);
        Assert.Equal(2, objDaPromo.Count);
        CampaignRecipient objIgnorado = objDaPromo.Single(recipient => recipient.Phone == "91900000001");
        Assert.Equal(CampaignRecipientStatus.Ignored, objIgnorado.Status);
        Assert.Contains("Limite do dia", objIgnorado.Reason);
        Assert.Equal(1, objDbContext.CampaignRuns.Single(run => run.IDCampaign == objFiltrada.Id).IgnoredCount);
    }

    [Fact]
    public async Task LimiteDoDia_MaisImportanteChegaDepois_SubstituiAPendenteDaMenosImportante()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Cenario objCenario = CreateScenario(objDbContext);
        AddCustomer(objCenario, "91900000001", "Aniversariante", dtBirth: new DateOnly(1998, 9, 29));
        Campaign objFiltrada = AddCampaign(objCenario, CampaignKind.Filtered, Mensagem() with
        {
            SendTime = "08:00",
            Schedule = new CampaignScheduleConfig { Recurrence = CampaignRecurrence.Once, StartDate = s_dtHoje },
        }, dtNextRun: s_dtNove.AddHours(-1), sName: "Promo");
        Campaign objAniversario = AddCampaign(objCenario, CampaignKind.Birthday, Mensagem());
        CampaignEngine objEngine = CreateEngine(objCenario);

        // 08:00: a promoção escolhe o cliente, mas ainda não enviou.
        await objEngine.ScheduleDueAsync(s_dtNove.AddHours(-1).AddSeconds(3));
        Guid objRunPromo = objDbContext.CampaignRuns.Single().Id;
        await objEngine.SelectRecipientsAsync(objRunPromo, s_dtNove.AddHours(-1).AddSeconds(3));
        // 09:00: o aniversário chega e ocupa o lugar.
        await objEngine.ScheduleDueAsync(s_dtNove.AddSeconds(3));
        Guid objRunAniversario = objDbContext.CampaignRuns.Single(run => run.IDCampaign == objAniversario.Id).Id;
        await objEngine.SelectRecipientsAsync(objRunAniversario, s_dtNove.AddSeconds(3));

        CampaignRecipient objDaPromo = Assert.Single(RecipientsOf(objDbContext, objFiltrada.Id));
        Assert.Equal(CampaignRecipientStatus.Ignored, objDaPromo.Status);
        Assert.Contains("Substituída", objDaPromo.Reason);
        Assert.Equal(CampaignRecipientStatus.Pending, Assert.Single(RecipientsOf(objDbContext, objAniversario.Id)).Status);
    }

    // ------------------------------------------- Pausar, retomar, cancelar, retomar

    private static async Task<Guid> RunComCincoPendentesAsync(Cenario objCenario, CampaignEngine objEngine)
    {
        for (int iCliente = 1; iCliente <= 5; iCliente++)
        {
            AddCustomer(objCenario, $"9190000000{iCliente}");
        }
        AddCampaign(objCenario, CampaignKind.Filtered, Mensagem() with
        {
            Schedule = new CampaignScheduleConfig { Recurrence = CampaignRecurrence.Once, StartDate = s_dtHoje },
        });
        await objEngine.ScheduleDueAsync(s_dtNove.AddSeconds(3));
        Guid objRunId = objCenario.Db.CampaignRuns.Single().Id;
        await objEngine.SelectRecipientsAsync(objRunId, s_dtNove.AddSeconds(3));
        return objRunId;
    }

    [Fact]
    public async Task Pausar_NaoMandaNada_ERetomarMandaUmEmailComTodos()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Cenario objCenario = CreateScenario(objDbContext);
        CampaignEngine objEngine = CreateEngine(objCenario);
        Guid objRunId = await RunComCincoPendentesAsync(objCenario, objEngine);

        objDbContext.CampaignRuns.Single(run => run.Id == objRunId).Status = CampaignRunStatus.Paused;
        objDbContext.SaveChanges();
        Assert.Equal(0, await objEngine.ProcessRunAsync(objRunId, s_dtNove.AddSeconds(5))); // pausada: não anda
        Assert.Empty(objCenario.Email.Enviados);

        objDbContext.CampaignRuns.Single(run => run.Id == objRunId).Status = CampaignRunStatus.Running;
        objDbContext.SaveChanges();
        Assert.Equal(5, await objEngine.ProcessRunAsync(objRunId, s_dtNove.AddSeconds(10)));

        CampaignRun objRun = objDbContext.CampaignRuns.Single(run => run.Id == objRunId);
        Assert.Equal(CampaignRunStatus.Completed, objRun.Status);
        Assert.Equal(5, objRun.SentCount);
        Assert.Single(objCenario.Email.Enviados);
        Assert.All(objDbContext.CampaignRecipients, recipient => Assert.Equal(CampaignRecipientStatus.Sent, recipient.Status));
    }

    [Fact]
    public async Task Cancelar_PendentesViramCancelados_ENenhumEmailSai()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Cenario objCenario = CreateScenario(objDbContext);
        objCenario.Email.FalharVezes = 1; // a primeira tentativa falha: o e-mail fica esperando a próxima
        CampaignEngine objEngine = CreateEngine(objCenario);
        Guid objRunId = await RunComCincoPendentesAsync(objCenario, objEngine);
        await objEngine.ProcessRunAsync(objRunId, s_dtNove.AddSeconds(5));

        objDbContext.CampaignRuns.Single(run => run.Id == objRunId).Status = CampaignRunStatus.Cancelled;
        objDbContext.SaveChanges();
        await objEngine.TickAsync(s_dtNove.AddMinutes(10));

        CampaignRun objRun = objDbContext.CampaignRuns.Single(run => run.Id == objRunId);
        Assert.Equal(5, objRun.CancelledCount);
        Assert.Empty(objCenario.Email.Enviados);
        Assert.DoesNotContain(objDbContext.CampaignRecipients, recipient => recipient.Status == CampaignRecipientStatus.Pending);
        Assert.Equal(CampaignDeliveryStatus.Cancelled, objDbContext.CampaignDeliveries.Single().Status);
    }

    // ------------------------------------------------- E-mail para a unidade (D17)

    [Fact]
    public async Task Envio_UmEmailPorUnidade_ComOPdfDosClientesDela()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Cenario objCenario = CreateScenario(objDbContext);
        Unit objSantarem = new Unit { IDCompany = objCenario.Company.Id, Name = "Santarém", Slug = "santarem", Email = "gerente.stm@regional.com.br" };
        objDbContext.Units.Add(objSantarem);
        objDbContext.SaveChanges();
        AddCustomer(objCenario, "93991230001", "Ana", dtBirth: new DateOnly(1998, 9, 29), sInstagram: "https://www.instagram.com/ana.souza");
        AddCustomer(objCenario, "93991230002", "Bia", dtBirth: new DateOnly(1998, 10, 1));
        AddCustomer(objCenario, "93991230003", "Caio", dtBirth: new DateOnly(1998, 9, 30), objUnit: objSantarem);
        AddCampaign(objCenario, CampaignKind.Birthday,
            Mensagem("Feliz aniversário, {primeiro_nome}! A {empresa} ({unidade}) deseja tudo de bom."));

        await CreateEngine(objCenario).TickAsync(s_dtNove.AddSeconds(3));

        Assert.Equal(2, objCenario.Email.Enviados.Count);
        FakeEmailSender.Email objItaituba = objCenario.Email.Enviados.Single(email => email.To == EmailDaUnidade);
        Assert.Contains("Itaituba", objItaituba.Subject);
        Assert.Contains("2 clientes", objItaituba.Subject);
        // A mensagem com o que é igual para todos já preenchido; o que muda por cliente fica entre chaves.
        Assert.Contains("Feliz aniversário, {primeiro_nome}! A Lojas Regional (Itaituba) deseja tudo de bom.", objItaituba.Body);
        Assert.Contains("A Lojas Regional (Santarém)", objCenario.Email.Enviados.Single(email => email.To == "gerente.stm@regional.com.br").Body);
        Assert.Equal("campanha-aniversario-itaituba-2026-09-29.pdf", objItaituba.AttachmentName);
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(objItaituba.Attachment!, 0, 4));
        Assert.Contains("1 cliente", objCenario.Email.Enviados.Single(email => email.To == "gerente.stm@regional.com.br").Subject);

        List<CampaignDelivery> objEnvios = objDbContext.CampaignDeliveries.OrderBy(delivery => delivery.UnitName).ToList();
        Assert.Equal(new[] { "Itaituba", "Santarém" }, objEnvios.Select(delivery => delivery.UnitName));
        Assert.All(objEnvios, delivery => Assert.Equal(CampaignDeliveryStatus.Sent, delivery.Status));
        Assert.Equal(new[] { 2, 1 }, objEnvios.Select(delivery => delivery.RecipientCount));
        CampaignRecipient objAna = objDbContext.CampaignRecipients.Single(recipient => recipient.Name == "Ana");
        Assert.Equal("ana.souza", objAna.Instagram);
        Assert.Equal(objCenario.Unit.Id, objAna.IDUnit);
    }

    // ------------------------------------------------- Correio eletrônico

    [Fact]
    public async Task Correio_EnvioDaCampanha_FicaRegistradoEOPdfERemontadoPelaTela()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Cenario objCenario = CreateScenario(objDbContext);
        AddCustomer(objCenario, "93991230001", "Ana", dtBirth: new DateOnly(1998, 9, 29));
        AddCampaign(objCenario, CampaignKind.Birthday, Mensagem());

        await CreateEngine(objCenario).TickAsync(s_dtNove.AddSeconds(3));

        FakeEmailSender.Email objEnviado = Assert.Single(objCenario.Email.Enviados);
        SentEmail objRegistro = Assert.Single(objDbContext.SentEmails.AsNoTracking());
        Assert.Equal(SentEmailKind.Campaign, objRegistro.Kind);
        Assert.Equal(objCenario.Company.Id, objRegistro.IDCompany);
        Assert.Equal(objCenario.Unit.Id, objRegistro.IDUnit);
        Assert.Equal(EmailDaUnidade, objRegistro.ToEmail);
        Assert.Equal(objEnviado.Subject, objRegistro.Subject);
        Assert.Equal(objEnviado.Body, objRegistro.Body);
        Assert.Equal(objEnviado.AttachmentName, objRegistro.AttachmentName);
        Assert.Equal(objDbContext.CampaignRuns.Single().Id, objRegistro.IDCampaignRun);

        // A tela remonta o PDF da execução (o mesmo arquivo, com o logo e as cores de hoje).
        AccessWifi.Api.Controllers.EmailsController objController = new(objDbContext);
        TestHelpers.SetUser(objController, null, "root");
        IActionResult objAnexo = await objController.Attachment(objRegistro.Id, "regional", CancellationToken.None);
        FileContentResult objPdf = Assert.IsType<FileContentResult>(objAnexo);
        Assert.Equal("application/pdf", objPdf.ContentType);
        Assert.Equal(objEnviado.AttachmentName, objPdf.FileDownloadName);
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(objPdf.FileContents, 0, 4));

        // Depois que a LGPD apaga a lista da execução, o PDF não tem mais como ser remontado.
        objDbContext.CampaignRecipients.RemoveRange(objDbContext.CampaignRecipients);
        objDbContext.SaveChanges();
        NotFoundObjectResult objSemLista = Assert.IsType<NotFoundObjectResult>(
            await objController.Attachment(objRegistro.Id, "regional", CancellationToken.None));
        Assert.Contains("retenção", Assert.IsType<AccessWifi.Api.Features.ErrorResponse>(objSemLista.Value).Error);
    }

    [Fact]
    public async Task Correio_EnvioQueFalhou_SoEntraQuandoSair()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Cenario objCenario = CreateScenario(objDbContext);
        objCenario.Email.FalharVezes = 1;
        AddCustomer(objCenario, "91900000001", "Ana", dtBirth: new DateOnly(1998, 9, 29));
        AddCampaign(objCenario, CampaignKind.Birthday, Mensagem());
        CampaignEngine objEngine = CreateEngine(objCenario);

        await objEngine.TickAsync(s_dtNove.AddSeconds(3));
        Assert.Empty(objDbContext.SentEmails.AsNoTracking());

        await objEngine.TickAsync(s_dtNove.AddMinutes(6));
        Assert.Single(objDbContext.SentEmails.AsNoTracking());
    }

    [Fact]
    public async Task Envio_UnidadeSemEmail_ClientesFicamComoFalhaComOMotivo()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Cenario objCenario = CreateScenario(objDbContext);
        objDbContext.Units.Single().Email = "";
        objDbContext.SaveChanges();
        AddCustomer(objCenario, "91900000001", "Ana", dtBirth: new DateOnly(1998, 9, 29));
        AddCampaign(objCenario, CampaignKind.Birthday, Mensagem());

        await CreateEngine(objCenario).TickAsync(s_dtNove.AddSeconds(3));

        Assert.Empty(objCenario.Email.Enviados);
        CampaignDelivery objEnvio = objDbContext.CampaignDeliveries.Single();
        Assert.Equal(CampaignDeliveryStatus.Failed, objEnvio.Status);
        Assert.Contains("não tem e-mail", objEnvio.Error);
        CampaignRecipient objAna = objDbContext.CampaignRecipients.Single();
        Assert.Equal(CampaignRecipientStatus.Failed, objAna.Status);
        Assert.Contains("não tem e-mail", objAna.Reason);
        CampaignRun objRun = objDbContext.CampaignRuns.Single();
        Assert.Equal(CampaignRunStatus.Completed, objRun.Status);
        Assert.Equal(1, objRun.FailedCount);
    }

    [Fact]
    public async Task Envio_SmtpFalha_TentaDeNovoDepoisDe5Minutos()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Cenario objCenario = CreateScenario(objDbContext);
        objCenario.Email.FalharVezes = 1;
        AddCustomer(objCenario, "91900000001", "Ana", dtBirth: new DateOnly(1998, 9, 29));
        AddCampaign(objCenario, CampaignKind.Birthday, Mensagem());
        CampaignEngine objEngine = CreateEngine(objCenario);

        await objEngine.TickAsync(s_dtNove.AddSeconds(3));
        CampaignDelivery objEsperando = objDbContext.CampaignDeliveries.AsNoTracking().Single();
        Assert.Equal(CampaignDeliveryStatus.Pending, objEsperando.Status);
        Assert.Equal(1, objEsperando.Attempts);
        Assert.Equal(s_dtNove.AddSeconds(3).AddMinutes(5), objEsperando.NextAttemptAt);
        Assert.Equal(CampaignRunStatus.Running, objDbContext.CampaignRuns.AsNoTracking().Single().Status);

        await objEngine.TickAsync(s_dtNove.AddMinutes(2)); // antes da hora: não tenta
        Assert.Empty(objCenario.Email.Enviados);
        await objEngine.TickAsync(s_dtNove.AddMinutes(6));

        Assert.Single(objCenario.Email.Enviados);
        Assert.Equal(CampaignDeliveryStatus.Sent, objDbContext.CampaignDeliveries.AsNoTracking().Single().Status);
        Assert.Equal(CampaignRunStatus.Completed, objDbContext.CampaignRuns.AsNoTracking().Single().Status);
    }

    [Fact]
    public async Task Envio_TresFalhas_DesisteEMarcaOsClientesComOMotivo()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Cenario objCenario = CreateScenario(objDbContext);
        objCenario.Email.FalharVezes = 10;
        AddCustomer(objCenario, "91900000001", "Ana", dtBirth: new DateOnly(1998, 9, 29));
        AddCampaign(objCenario, CampaignKind.Birthday, Mensagem());
        CampaignEngine objEngine = CreateEngine(objCenario);

        foreach (int iMinuto in new[] { 0, 6, 12, 18 })
        {
            await objEngine.TickAsync(s_dtNove.AddMinutes(iMinuto).AddSeconds(3));
        }

        CampaignDelivery objEnvio = objDbContext.CampaignDeliveries.AsNoTracking().Single();
        Assert.Equal(CampaignDeliveryStatus.Failed, objEnvio.Status);
        Assert.Equal(3, objEnvio.Attempts);
        Assert.Contains("SMTP fora do ar", objEnvio.Error);
        CampaignRecipient objAna = objDbContext.CampaignRecipients.AsNoTracking().Single();
        Assert.Equal(CampaignRecipientStatus.Failed, objAna.Status);
        Assert.Contains("não saiu", objAna.Reason);
        Assert.Equal(CampaignRunStatus.Completed, objDbContext.CampaignRuns.AsNoTracking().Single().Status);
    }

    [Fact]
    public async Task Selecao_InterrompidaNoMeio_RetomaSemRepetirNinguem()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Cenario objCenario = CreateScenario(objDbContext);
        CampaignEngine objEngine = CreateEngine(objCenario);
        Guid objRunId = await RunComCincoPendentesAsync(objCenario, objEngine);

        // Simula a queda: a execução volta para "selecionando" com 5 já gravados, e a seleção roda de novo.
        CampaignRun objRun = objDbContext.CampaignRuns.Single(run => run.Id == objRunId);
        objRun.Status = CampaignRunStatus.Selecting;
        objDbContext.SaveChanges();
        await objEngine.SelectRecipientsAsync(objRunId, s_dtNove.AddMinutes(1));

        Assert.Equal(5, objDbContext.CampaignRecipients.Count(recipient => recipient.IDRun == objRunId));
        Assert.Equal(5, objDbContext.CampaignRuns.Single(run => run.Id == objRunId).TotalCount);
    }

    [Fact]
    public async Task Versao_ExecucaoUsaAVersaoComQueFoiCriada()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Cenario objCenario = CreateScenario(objDbContext);
        AddCustomer(objCenario, "91900000001", "Ana", dtBirth: new DateOnly(1998, 9, 29));
        Campaign objCampaign = AddCampaign(objCenario, CampaignKind.Birthday, Mensagem("Versão 1"));
        CampaignEngine objEngine = CreateEngine(objCenario);
        await objEngine.ScheduleDueAsync(s_dtNove.AddSeconds(3));

        // Editada entre a criação da execução e a seleção (D13).
        Campaign objEditada = objDbContext.Campaigns.Single(campaign => campaign.Id == objCampaign.Id);
        objEditada.ConfigJson = Mensagem("Versão 2").ToJson();
        objEditada.CurrentVersion = 2;
        objDbContext.CampaignVersions.Add(new CampaignVersion
        {
            IDCampaign = objCampaign.Id, Number = 2, ConfigJson = objEditada.ConfigJson,
        });
        objDbContext.SaveChanges();
        await objEngine.TickAsync(s_dtNove.AddSeconds(8));

        Assert.Equal("Versão 1", Assert.Single(RecipientsOf(objDbContext, objCampaign.Id)).Message);
    }
}
