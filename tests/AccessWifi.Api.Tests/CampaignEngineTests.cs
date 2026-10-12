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
    private static readonly DateTime s_dtNine = new DateTime(2026, 9, 29, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly s_dtToday = new DateOnly(2026, 9, 29);
    private const string UnitEmail = "gerente.itaituba@regional.com.br";

    private sealed class Scenario
    {
        public required AppDbContext Db { get; init; }
        public required Company Company { get; init; }
        public required Unit Unit { get; init; }
        public FakeEmailSender Email { get; } = new FakeEmailSender();
    }

    private static Scenario CreateScenario(AppDbContext objDbContext, params string[] arrKinds)
    {
        Company objCompany = new Company { Name = "Lojas Regional", Slug = "regional", TimeZone = "America/Belem" };
        Unit objUnit = new Unit { IDCompany = objCompany.Id, Name = "Itaituba", Slug = "itaituba", Email = UnitEmail };
        objDbContext.AddRange(objCompany, objUnit);
        foreach (string sKind in arrKinds.Length > 0 ? arrKinds : CampaignKind.All.ToArray())
        {
            objDbContext.CompanyCampaignKinds.Add(new CompanyCampaignKind { IDCompany = objCompany.Id, Kind = sKind });
        }
        objDbContext.SaveChanges();
        return new Scenario { Db = objDbContext, Company = objCompany, Unit = objUnit };
    }

    private static Customer AddCustomer(
        Scenario objScenario, string sPhone, string sName = "Cliente",
        DateOnly? dtBirth = null, DateOnly? dtFirstVisit = null, DateTime? dtLastVisit = null,
        int iVisits = 1, string sInstagram = "", Unit? objUnit = null)
    {
        DateOnly dtFirst = dtFirstVisit ?? new DateOnly(2026, 1, 10);
        DateTime dtLast = dtLastVisit ?? new DateTime(2026, 9, 1, 15, 0, 0, DateTimeKind.Utc);
        Customer objCustomer = new Customer
        {
            IDCompany = objScenario.Company.Id,
            Phone = sPhone,
            Name = sName,
            Instagram = sInstagram,
            BirthDate = dtBirth,
            FirstVisitDate = dtFirst,
            FirstVisitAt = dtFirst.ToDateTime(new TimeOnly(15, 0), DateTimeKind.Utc),
            LastVisitAt = dtLast,
            LastVisitDate = DateOnly.FromDateTime(dtLast),
            VisitCount = iVisits,
            IDLastUnit = (objUnit ?? objScenario.Unit).Id,
        };
        objScenario.Db.Customers.Add(objCustomer);
        objScenario.Db.CustomerUnits.Add(new CustomerUnit
        {
            IDCustomer = objCustomer.Id, IDUnit = (objUnit ?? objScenario.Unit).Id, FirstVisitAt = dtLast, LastVisitAt = dtLast,
        });
        objScenario.Db.SaveChanges();
        return objCustomer;
    }

    private static Campaign AddCampaign(
        Scenario objScenario, string sKind, CampaignConfig objConfig, DateTime? dtNextRun = null, string? sName = null)
    {
        Campaign objCampaign = new Campaign
        {
            IDCompany = objScenario.Company.Id,
            Kind = sKind,
            Name = sName ?? CampaignKind.Label(sKind),
            ConfigJson = objConfig.ToJson(),
            NextRunAt = dtNextRun ?? s_dtNine,
        };
        objScenario.Db.Campaigns.Add(objCampaign);
        objScenario.Db.CampaignVersions.Add(new CampaignVersion
        {
            IDCampaign = objCampaign.Id, Number = 1, Name = objCampaign.Name, ConfigJson = objCampaign.ConfigJson,
        });
        objScenario.Db.SaveChanges();
        return objCampaign;
    }

    private static CampaignEngine CreateEngine(Scenario objScenario, int iBatch = 2)
    {
        return new CampaignEngine(
            objScenario.Db,
            objScenario.Email,
            Options.Create(new CampaignEngineOptions
            {
                TickSeconds = 5, SelectionChunkSize = iBatch, EmailMaxAttempts = 3, EmailRetryMinutes = 5,
            }),
            NullLogger<CampaignEngine>.Instance);
    }

    private static CampaignConfig MessageConfig(string sText = "Oi {primeiro_nome}!") =>
        new CampaignConfig { Message = sText, SendTime = "09:00" };

    private static List<CampaignRecipient> RecipientsOf(AppDbContext objDbContext, Guid objCampaignId) =>
        objDbContext.CampaignRecipients
            .Where(recipient => objDbContext.CampaignRuns.Any(run => run.Id == recipient.IDRun && run.IDCampaign == objCampaignId))
            .OrderBy(recipient => recipient.Id)
            .ToList();

    // ----------------------------------------------------------------- Agenda

    [Fact]
    public async Task Schedule_OnTime_CreatesRunAndSchedulesNextDay()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Scenario objScenario = CreateScenario(objDbContext);
        Campaign objCampaign = AddCampaign(objScenario, CampaignKind.Birthday, MessageConfig());
        CampaignEngine objEngine = CreateEngine(objScenario);

        await objEngine.ScheduleDueAsync(s_dtNine.AddSeconds(3));
        await objEngine.ScheduleDueAsync(s_dtNine.AddSeconds(8)); // outra volta: não duplica

        CampaignRun objRun = Assert.Single(objDbContext.CampaignRuns);
        Assert.Equal(CampaignRunStatus.Selecting, objRun.Status);
        Assert.Equal(s_dtToday, objRun.LocalDate);
        Assert.Equal(s_dtNine, objRun.ScheduledFor);
        Assert.Equal(s_dtNine.AddDays(1), objDbContext.Campaigns.Single(campaign => campaign.Id == objCampaign.Id).NextRunAt);
    }

    [Fact]
    public async Task Schedule_TimeMovedLaterSameDay_DoesNotFireAgain()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Scenario objScenario = CreateScenario(objDbContext);
        Campaign objCampaign = AddCampaign(objScenario, CampaignKind.Birthday, MessageConfig());
        CampaignEngine objEngine = CreateEngine(objScenario);
        await objEngine.ScheduleDueAsync(s_dtNine.AddSeconds(3));

        // Depois do disparo das 09:00, o horário vira 15:00 do mesmo dia.
        Campaign objEdited = objDbContext.Campaigns.Single(campaign => campaign.Id == objCampaign.Id);
        objEdited.ConfigJson = (MessageConfig() with { SendTime = "15:00" }).ToJson();
        objEdited.NextRunAt = s_dtNine.AddHours(6);
        objDbContext.SaveChanges();
        await objEngine.ScheduleDueAsync(s_dtNine.AddHours(6).AddSeconds(3));

        Assert.Single(objDbContext.CampaignRuns); // cada dia é uma execução
        Assert.Equal(s_dtNine.AddHours(6).AddDays(1),
            objDbContext.Campaigns.Single(campaign => campaign.Id == objCampaign.Id).NextRunAt);
    }

    [Fact]
    public async Task Schedule_ServiceDownAndDayChanged_RecordsMissedAndFiresTodaysTime()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Scenario objScenario = CreateScenario(objDbContext);
        AddCampaign(objScenario, CampaignKind.Birthday, MessageConfig(), dtNextRun: s_dtNine.AddDays(-1));

        // Volta às 10:00 de Belém do dia 29: o de ontem se perdeu; o de hoje (09:00) ainda vale.
        await CreateEngine(objScenario).ScheduleDueAsync(s_dtNine.AddHours(1));

        List<CampaignRun> objRuns = objDbContext.CampaignRuns.OrderBy(run => run.ScheduledFor).ToList();
        Assert.Equal(2, objRuns.Count);
        Assert.Equal(CampaignRunStatus.Missed, objRuns[0].Status);
        Assert.Equal(CampaignRunStatus.Selecting, objRuns[1].Status);
        Assert.Equal(s_dtToday, objRuns[1].LocalDate);
    }

    [Fact]
    public async Task Schedule_KindDisabledForCompany_DoesNotFireAndHasNoSchedule()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Scenario objScenario = CreateScenario(objDbContext, CampaignKind.Filtered); // aniversário não liberado
        Campaign objCampaign = AddCampaign(objScenario, CampaignKind.Birthday, MessageConfig());

        await CreateEngine(objScenario).ScheduleDueAsync(s_dtNine.AddSeconds(3));

        Assert.Empty(objDbContext.CampaignRuns);
        Assert.Null(objDbContext.Campaigns.Single(campaign => campaign.Id == objCampaign.Id).NextRunAt);
    }

    // ---------------------------------------------------------- Quem recebe

    [Fact]
    public async Task Birthday_OnTuesday_ListsTodayToSaturday_WithAgeOnBirthday()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Scenario objScenario = CreateScenario(objDbContext);
        AddCustomer(objScenario, "91900000001", "Ana Beatriz", dtBirth: new DateOnly(1998, 9, 29)); // hoje (terça)
        AddCustomer(objScenario, "91900000002", "Bruno", dtBirth: new DateOnly(1990, 10, 3));       // sábado
        AddCustomer(objScenario, "91900000003", "Carla", dtBirth: new DateOnly(1995, 9, 28));       // segunda: já passou
        AddCustomer(objScenario, "91900000004", "Davi", dtBirth: new DateOnly(1995, 10, 4));        // domingo: próxima semana
        AddCustomer(objScenario, "91900000005", "Edu");                                              // sem nascimento válido
        Campaign objCampaign = AddCampaign(objScenario, CampaignKind.Birthday,
            MessageConfig("Parabéns, {primeiro_nome}! {idade} anos com a {empresa} ({unidade})."));

        await CreateEngine(objScenario).TickAsync(s_dtNine.AddSeconds(3));

        List<CampaignRecipient> objList = RecipientsOf(objDbContext, objCampaign.Id);
        Assert.Equal(new[] { "91900000001", "91900000002" }, objList.Select(recipient => recipient.Phone).Order());
        CampaignRecipient objAna = objList.Single(recipient => recipient.Phone == "91900000001");
        Assert.Equal("Parabéns, Ana! 28 anos com a Lojas Regional (Itaituba).", objAna.Message);
        Assert.Equal(new DateOnly(2026, 9, 29), objAna.EventDate);
        Assert.Equal("ter, 29/09 · 28 anos", objAna.Info);
        // Bruno faz 36 no sábado: a mensagem já vai com a idade que ele completa.
        CampaignRecipient objBruno = objList.Single(recipient => recipient.Phone == "91900000002");
        Assert.Equal("Parabéns, Bruno! 36 anos com a Lojas Regional (Itaituba).", objBruno.Message);
        Assert.Equal("sáb, 03/10 · 36 anos", objBruno.Info);
        Assert.All(objList, recipient => Assert.Equal(CampaignRecipientStatus.Sent, recipient.Status));
        CampaignRun objRun = objDbContext.CampaignRuns.Single();
        Assert.Equal(CampaignRunStatus.Completed, objRun.Status);
        Assert.Equal(2, objRun.SentCount);
        Assert.False(objRun.Simulation);
    }

    [Fact]
    public async Task Birthday_OnMonday_AlsoTakesSunday_AndDoesNotFireOnSunday()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Scenario objScenario = CreateScenario(objDbContext);
        AddCustomer(objScenario, "91900000001", "Domingo", dtBirth: new DateOnly(1990, 10, 4));
        AddCustomer(objScenario, "91900000002", "Sexta", dtBirth: new DateOnly(1990, 10, 9));
        AddCustomer(objScenario, "91900000003", "Outra semana", dtBirth: new DateOnly(1990, 10, 11));
        // Sábado, 03/10 às 09:00: o próximo disparo pula o domingo e cai na segunda, 05/10.
        DateTime dtSaturday = new DateTime(2026, 10, 3, 12, 0, 0, DateTimeKind.Utc);
        Campaign objCampaign = AddCampaign(objScenario, CampaignKind.Birthday, MessageConfig(), dtNextRun: dtSaturday);
        CampaignEngine objEngine = CreateEngine(objScenario);

        await objEngine.TickAsync(dtSaturday.AddSeconds(3));
        DateTime dtMonday = new DateTime(2026, 10, 5, 12, 0, 0, DateTimeKind.Utc);
        Assert.Equal(dtMonday, objDbContext.Campaigns.Single(campaign => campaign.Id == objCampaign.Id).NextRunAt);
        await objEngine.TickAsync(dtMonday.AddSeconds(3));

        CampaignRun objMondayRun = objDbContext.CampaignRuns.Single(run => run.LocalDate == new DateOnly(2026, 10, 5));
        List<CampaignRecipient> objList = objDbContext.CampaignRecipients.Where(recipient => recipient.IDRun == objMondayRun.Id).ToList();
        Assert.Equal(new[] { "91900000001", "91900000002" }, objList.Select(recipient => recipient.Phone).Order());
        Assert.Equal(2, objDbContext.CampaignRuns.Count()); // sábado e segunda; domingo não
    }

    [Fact]
    public void Birthday_WeekRange()
    {
        Assert.Equal((new DateOnly(2026, 10, 4), new DateOnly(2026, 10, 10)), CampaignCalendar.BirthdayRange(new DateOnly(2026, 10, 5))); // segunda
        Assert.Equal((new DateOnly(2026, 10, 6), new DateOnly(2026, 10, 10)), CampaignCalendar.BirthdayRange(new DateOnly(2026, 10, 6))); // terça
        Assert.Equal((new DateOnly(2026, 10, 10), new DateOnly(2026, 10, 10)), CampaignCalendar.BirthdayRange(new DateOnly(2026, 10, 10))); // sábado
        Assert.Equal((new DateOnly(2026, 10, 4), new DateOnly(2026, 10, 10)), CampaignCalendar.BirthdayRange(new DateOnly(2026, 10, 4))); // domingo
        // Virada de ano: terça 29/12 a sábado 02/01.
        Assert.Equal(new DateOnly(2027, 1, 2), CampaignCalendar.BirthdayInRange(new DateOnly(1990, 1, 2), new DateOnly(2026, 12, 29)));
    }

    [Fact]
    public async Task Birthday_BornFeb29_ReceivesOnFeb28InNonLeapYear()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Scenario objScenario = CreateScenario(objDbContext);
        AddCustomer(objScenario, "91900000001", "Leap", dtBirth: new DateOnly(2000, 2, 29));
        // Quarta, 28/02/2029 (ano não bissexto).
        DateTime dtFeb28 = new DateTime(2029, 2, 28, 12, 0, 0, DateTimeKind.Utc);
        Campaign objCampaign = AddCampaign(objScenario, CampaignKind.Birthday, MessageConfig(), dtNextRun: dtFeb28);

        await CreateEngine(objScenario).TickAsync(dtFeb28.AddSeconds(3));

        Assert.Single(RecipientsOf(objDbContext, objCampaign.Id));
    }

    [Fact]
    public async Task SignupAnniversary_CompletesYearToday_ButNotSignedUpToday()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Scenario objScenario = CreateScenario(objDbContext);
        AddCustomer(objScenario, "91900000001", "Um ano", dtFirstVisit: new DateOnly(2025, 9, 29));
        AddCustomer(objScenario, "91900000002", "Hoje", dtFirstVisit: s_dtToday);
        Campaign objCampaign = AddCampaign(objScenario, CampaignKind.SignupAnniversary,
            MessageConfig("{anos_de_cadastro} ano(s) conosco"));

        await CreateEngine(objScenario).TickAsync(s_dtNine.AddSeconds(3));

        CampaignRecipient objRecipient = Assert.Single(RecipientsOf(objDbContext, objCampaign.Id));
        Assert.Equal("1 ano(s) conosco", objRecipient.Message);
        Assert.Equal("1 ano de cadastro", objRecipient.Info);
    }

    [Fact]
    public async Task WeMissYou_OncePerAbsence()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Scenario objScenario = CreateScenario(objDbContext);
        Customer objMissing = AddCustomer(objScenario, "91900000001", "Sumido", dtLastVisit: s_dtNine.AddDays(-40));
        AddCustomer(objScenario, "91900000002", "Frequente", dtLastVisit: s_dtNine.AddDays(-3));
        Campaign objCampaign = AddCampaign(objScenario, CampaignKind.WeMissYou, MessageConfig() with { AbsenceDays = 30 });
        CampaignEngine objEngine = CreateEngine(objScenario);

        await objEngine.TickAsync(s_dtNine.AddSeconds(3));
        await objEngine.TickAsync(s_dtNine.AddDays(1).AddSeconds(3)); // dia seguinte: já recebeu nesta ausência

        CampaignRecipient objRecipient = Assert.Single(RecipientsOf(objDbContext, objCampaign.Id));
        Assert.Equal(objMissing.Id, objRecipient.IDCustomer);

        // Voltou e sumiu de novo por mais de 30 dias: recebe outra vez.
        Customer objAgain = objDbContext.Customers.Single(customer => customer.Id == objMissing.Id);
        objAgain.LastVisitAt = s_dtNine.AddDays(2);
        objDbContext.SaveChanges();
        DateTime dtAfter = s_dtNine.AddDays(40);
        Campaign objScheduled = objDbContext.Campaigns.Single(campaign => campaign.Id == objCampaign.Id);
        objScheduled.NextRunAt = dtAfter;
        objDbContext.SaveChanges();
        await objEngine.TickAsync(dtAfter.AddSeconds(3));

        // O sumido recebe a 2ª (nova ausência). O "frequente", que também passou de 30 dias sem vir, recebe a 1ª.
        List<CampaignRecipient> objAll = RecipientsOf(objDbContext, objCampaign.Id);
        Assert.Equal(2, objAll.Count(recipient => recipient.IDCustomer == objMissing.Id));
        Assert.Equal(3, objAll.Count);
    }

    [Fact]
    public async Task FrequentCustomer_EachMilestoneOnlyOnce()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Scenario objScenario = CreateScenario(objDbContext);
        Customer objFive = AddCustomer(objScenario, "91900000001", "Cinco", iVisits: 5);
        AddCustomer(objScenario, "91900000002", "Quatro", iVisits: 4);
        Campaign objCampaign = AddCampaign(objScenario, CampaignKind.FrequentCustomer, MessageConfig() with { VisitMilestone = 5 });
        CampaignEngine objEngine = CreateEngine(objScenario);

        await objEngine.TickAsync(s_dtNine.AddSeconds(3));
        await objEngine.TickAsync(s_dtNine.AddDays(1).AddSeconds(3)); // mesmo marco: não repete

        CampaignRecipient objFirst = Assert.Single(RecipientsOf(objDbContext, objCampaign.Id));
        Assert.Equal(5, objFirst.Milestone);

        objDbContext.Customers.Single(customer => customer.Id == objFive.Id).VisitCount = 10;
        objDbContext.SaveChanges();
        await objEngine.TickAsync(s_dtNine.AddDays(2).AddSeconds(3));

        Assert.Equal(new int?[] { 5, 10 }, RecipientsOf(objDbContext, objCampaign.Id).Select(recipient => recipient.Milestone));
    }

    [Fact]
    public async Task Filtered_AppliesFiltersOnEachRun()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Scenario objScenario = CreateScenario(objDbContext);
        AddCustomer(objScenario, "91900000001", "Jovem com Insta", dtBirth: new DateOnly(2000, 5, 1), iVisits: 3, sInstagram: "@j");
        AddCustomer(objScenario, "91900000002", "Jovem sem Insta", dtBirth: new DateOnly(2000, 5, 1), iVisits: 3);
        AddCustomer(objScenario, "91900000003", "Mais velho", dtBirth: new DateOnly(1970, 5, 1), iVisits: 3, sInstagram: "@m");
        AddCustomer(objScenario, "91900000004", "Pouca visita", dtBirth: new DateOnly(2000, 5, 1), iVisits: 1, sInstagram: "@p");
        CampaignConfig objConfig = MessageConfig() with
        {
            Schedule = new CampaignScheduleConfig { Recurrence = CampaignRecurrence.Daily, StartDate = s_dtToday },
            Filters = new CampaignFilters
            {
                UnitIds = [objScenario.Unit.Id], AgeMin = 18, AgeMax = 30, VisitsMin = 2, HasInstagram = true,
            },
        };
        Campaign objCampaign = AddCampaign(objScenario, CampaignKind.Filtered, objConfig);
        CampaignEngine objEngine = CreateEngine(objScenario);

        await objEngine.TickAsync(s_dtNine.AddSeconds(3));
        Assert.Equal("91900000001", Assert.Single(RecipientsOf(objDbContext, objCampaign.Id)).Phone);

        // Cliente novo, que passa nos filtros, entra já na execução do dia seguinte (D16).
        AddCustomer(objScenario, "91900000005", "Novo", dtBirth: new DateOnly(2001, 1, 1), iVisits: 2, sInstagram: "@n");
        await objEngine.TickAsync(s_dtNine.AddDays(1).AddSeconds(3));

        Assert.Equal(3, RecipientsOf(objDbContext, objCampaign.Id).Count); // 1 do 1º dia + 2 do 2º
    }

    [Fact]
    public async Task Filtered_DoesNotRepeatForRecentRecipients()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Scenario objScenario = CreateScenario(objDbContext);
        AddCustomer(objScenario, "91900000001");
        CampaignConfig objConfig = MessageConfig() with
        {
            Schedule = new CampaignScheduleConfig { Recurrence = CampaignRecurrence.Daily, StartDate = s_dtToday },
            ResendAfterDays = 7,
        };
        Campaign objCampaign = AddCampaign(objScenario, CampaignKind.Filtered, objConfig);
        CampaignEngine objEngine = CreateEngine(objScenario);

        for (int iDay = 0; iDay <= 8; iDay++)
        {
            await objEngine.TickAsync(s_dtNine.AddDays(iDay).AddSeconds(3));
        }

        // Recebeu no dia 0; do dia 1 ao 7 ainda está dentro dos 7 dias; no dia 8 recebe de novo.
        Assert.Equal(9, objDbContext.CampaignRuns.Count());
        Assert.Equal(2, RecipientsOf(objDbContext, objCampaign.Id).Count);
    }

    // -------------------------------------------------- Limite de 1 por dia (D11)

    [Fact]
    public async Task DailyLimit_SameTime_BirthdayWinsAndFilteredIgnores()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Scenario objScenario = CreateScenario(objDbContext);
        AddCustomer(objScenario, "91900000001", "Aniversariante", dtBirth: new DateOnly(1998, 9, 29));
        AddCustomer(objScenario, "91900000002", "Outro");
        Campaign objFiltered = AddCampaign(objScenario, CampaignKind.Filtered, MessageConfig() with
        {
            Schedule = new CampaignScheduleConfig { Recurrence = CampaignRecurrence.Once, StartDate = s_dtToday },
        }, sName: "Promo");
        Campaign objBirthday = AddCampaign(objScenario, CampaignKind.Birthday, MessageConfig());

        await CreateEngine(objScenario).TickAsync(s_dtNine.AddSeconds(3));

        Assert.Equal(CampaignRecipientStatus.Sent, Assert.Single(RecipientsOf(objDbContext, objBirthday.Id)).Status);
        List<CampaignRecipient> objFromPromo = RecipientsOf(objDbContext, objFiltered.Id);
        Assert.Equal(2, objFromPromo.Count);
        CampaignRecipient objIgnored = objFromPromo.Single(recipient => recipient.Phone == "91900000001");
        Assert.Equal(CampaignRecipientStatus.Ignored, objIgnored.Status);
        Assert.Contains("Limite do dia", objIgnored.Reason);
        Assert.Equal(1, objDbContext.CampaignRuns.Single(run => run.IDCampaign == objFiltered.Id).IgnoredCount);
    }

    [Fact]
    public async Task DailyLimit_MoreImportantArrivesLater_ReplacesLessImportantPending()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Scenario objScenario = CreateScenario(objDbContext);
        AddCustomer(objScenario, "91900000001", "Aniversariante", dtBirth: new DateOnly(1998, 9, 29));
        Campaign objFiltered = AddCampaign(objScenario, CampaignKind.Filtered, MessageConfig() with
        {
            SendTime = "08:00",
            Schedule = new CampaignScheduleConfig { Recurrence = CampaignRecurrence.Once, StartDate = s_dtToday },
        }, dtNextRun: s_dtNine.AddHours(-1), sName: "Promo");
        Campaign objBirthday = AddCampaign(objScenario, CampaignKind.Birthday, MessageConfig());
        CampaignEngine objEngine = CreateEngine(objScenario);

        // 08:00: a promoção escolhe o cliente, mas ainda não enviou.
        await objEngine.ScheduleDueAsync(s_dtNine.AddHours(-1).AddSeconds(3));
        Guid objRunPromo = objDbContext.CampaignRuns.Single().Id;
        await objEngine.SelectRecipientsAsync(objRunPromo, s_dtNine.AddHours(-1).AddSeconds(3));
        // 09:00: o aniversário chega e ocupa o lugar.
        await objEngine.ScheduleDueAsync(s_dtNine.AddSeconds(3));
        Guid objBirthdayRun = objDbContext.CampaignRuns.Single(run => run.IDCampaign == objBirthday.Id).Id;
        await objEngine.SelectRecipientsAsync(objBirthdayRun, s_dtNine.AddSeconds(3));

        CampaignRecipient objFromPromo = Assert.Single(RecipientsOf(objDbContext, objFiltered.Id));
        Assert.Equal(CampaignRecipientStatus.Ignored, objFromPromo.Status);
        Assert.Contains("Substituída", objFromPromo.Reason);
        Assert.Equal(CampaignRecipientStatus.Pending, Assert.Single(RecipientsOf(objDbContext, objBirthday.Id)).Status);
    }

    // ------------------------------------------- Pausar, retomar, cancelar, retomar

    private static async Task<Guid> RunWithFivePendingAsync(Scenario objScenario, CampaignEngine objEngine)
    {
        for (int iCustomer = 1; iCustomer <= 5; iCustomer++)
        {
            AddCustomer(objScenario, $"9190000000{iCustomer}");
        }
        AddCampaign(objScenario, CampaignKind.Filtered, MessageConfig() with
        {
            Schedule = new CampaignScheduleConfig { Recurrence = CampaignRecurrence.Once, StartDate = s_dtToday },
        });
        await objEngine.ScheduleDueAsync(s_dtNine.AddSeconds(3));
        Guid objRunId = objScenario.Db.CampaignRuns.Single().Id;
        await objEngine.SelectRecipientsAsync(objRunId, s_dtNine.AddSeconds(3));
        return objRunId;
    }

    [Fact]
    public async Task Pause_SendsNothing_AndResumeSendsOneEmailWithAll()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Scenario objScenario = CreateScenario(objDbContext);
        CampaignEngine objEngine = CreateEngine(objScenario);
        Guid objRunId = await RunWithFivePendingAsync(objScenario, objEngine);

        objDbContext.CampaignRuns.Single(run => run.Id == objRunId).Status = CampaignRunStatus.Paused;
        objDbContext.SaveChanges();
        Assert.Equal(0, await objEngine.ProcessRunAsync(objRunId, s_dtNine.AddSeconds(5))); // pausada: não anda
        Assert.Empty(objScenario.Email.Sent);

        objDbContext.CampaignRuns.Single(run => run.Id == objRunId).Status = CampaignRunStatus.Running;
        objDbContext.SaveChanges();
        Assert.Equal(5, await objEngine.ProcessRunAsync(objRunId, s_dtNine.AddSeconds(10)));

        CampaignRun objRun = objDbContext.CampaignRuns.Single(run => run.Id == objRunId);
        Assert.Equal(CampaignRunStatus.Completed, objRun.Status);
        Assert.Equal(5, objRun.SentCount);
        Assert.Single(objScenario.Email.Sent);
        Assert.All(objDbContext.CampaignRecipients, recipient => Assert.Equal(CampaignRecipientStatus.Sent, recipient.Status));
    }

    [Fact]
    public async Task Cancel_PendingBecomeCancelled_AndNoEmailIsSent()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Scenario objScenario = CreateScenario(objDbContext);
        objScenario.Email.FailTimes = 1; // a primeira tentativa falha: o e-mail fica esperando a próxima
        CampaignEngine objEngine = CreateEngine(objScenario);
        Guid objRunId = await RunWithFivePendingAsync(objScenario, objEngine);
        await objEngine.ProcessRunAsync(objRunId, s_dtNine.AddSeconds(5));

        objDbContext.CampaignRuns.Single(run => run.Id == objRunId).Status = CampaignRunStatus.Cancelled;
        objDbContext.SaveChanges();
        await objEngine.TickAsync(s_dtNine.AddMinutes(10));

        CampaignRun objRun = objDbContext.CampaignRuns.Single(run => run.Id == objRunId);
        Assert.Equal(5, objRun.CancelledCount);
        Assert.Empty(objScenario.Email.Sent);
        Assert.DoesNotContain(objDbContext.CampaignRecipients, recipient => recipient.Status == CampaignRecipientStatus.Pending);
        Assert.Equal(CampaignDeliveryStatus.Cancelled, objDbContext.CampaignDeliveries.Single().Status);
    }

    // ------------------------------------------------- E-mail para a unidade (D17)

    [Fact]
    public async Task Delivery_OneEmailPerUnit_WithItsCustomersPdf()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Scenario objScenario = CreateScenario(objDbContext);
        Unit objSantarem = new Unit { IDCompany = objScenario.Company.Id, Name = "Santarém", Slug = "santarem", Email = "gerente.stm@regional.com.br" };
        objDbContext.Units.Add(objSantarem);
        objDbContext.SaveChanges();
        AddCustomer(objScenario, "93991230001", "Ana", dtBirth: new DateOnly(1998, 9, 29), sInstagram: "https://www.instagram.com/ana.souza");
        AddCustomer(objScenario, "93991230002", "Bia", dtBirth: new DateOnly(1998, 10, 1));
        AddCustomer(objScenario, "93991230003", "Caio", dtBirth: new DateOnly(1998, 9, 30), objUnit: objSantarem);
        AddCampaign(objScenario, CampaignKind.Birthday,
            MessageConfig("Feliz aniversário, {primeiro_nome}! A {empresa} ({unidade}) deseja tudo de bom."));

        await CreateEngine(objScenario).TickAsync(s_dtNine.AddSeconds(3));

        Assert.Equal(2, objScenario.Email.Sent.Count);
        FakeEmailSender.Email objItaituba = objScenario.Email.Sent.Single(email => email.To == UnitEmail);
        Assert.Contains("Itaituba", objItaituba.Subject);
        Assert.Contains("2 clientes", objItaituba.Subject);
        // A mensagem com o que é igual para todos já preenchido; o que muda por cliente fica entre chaves.
        Assert.Contains("Feliz aniversário, {primeiro_nome}! A Lojas Regional (Itaituba) deseja tudo de bom.", objItaituba.Body);
        Assert.Contains("A Lojas Regional (Santarém)", objScenario.Email.Sent.Single(email => email.To == "gerente.stm@regional.com.br").Body);
        Assert.Equal("campanha-aniversario-itaituba-2026-09-29.pdf", objItaituba.AttachmentName);
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(objItaituba.Attachment!, 0, 4));
        Assert.Contains("1 cliente", objScenario.Email.Sent.Single(email => email.To == "gerente.stm@regional.com.br").Subject);

        List<CampaignDelivery> objDeliveries = objDbContext.CampaignDeliveries.OrderBy(delivery => delivery.UnitName).ToList();
        Assert.Equal(new[] { "Itaituba", "Santarém" }, objDeliveries.Select(delivery => delivery.UnitName));
        Assert.All(objDeliveries, delivery => Assert.Equal(CampaignDeliveryStatus.Sent, delivery.Status));
        Assert.Equal(new[] { 2, 1 }, objDeliveries.Select(delivery => delivery.RecipientCount));
        CampaignRecipient objAna = objDbContext.CampaignRecipients.Single(recipient => recipient.Name == "Ana");
        Assert.Equal("ana.souza", objAna.Instagram);
        Assert.Equal(objScenario.Unit.Id, objAna.IDUnit);
    }

    // ------------------------------------------------- Correio eletrônico

    [Fact]
    public async Task Mail_CampaignDelivery_IsRecordedAndPdfIsRebuiltByScreen()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Scenario objScenario = CreateScenario(objDbContext);
        AddCustomer(objScenario, "93991230001", "Ana", dtBirth: new DateOnly(1998, 9, 29));
        AddCampaign(objScenario, CampaignKind.Birthday, MessageConfig());

        await CreateEngine(objScenario).TickAsync(s_dtNine.AddSeconds(3));

        FakeEmailSender.Email objSent = Assert.Single(objScenario.Email.Sent);
        SentEmail objRecord = Assert.Single(objDbContext.SentEmails.AsNoTracking());
        Assert.Equal(SentEmailKind.Campaign, objRecord.Kind);
        Assert.Equal(objScenario.Company.Id, objRecord.IDCompany);
        Assert.Equal(objScenario.Unit.Id, objRecord.IDUnit);
        Assert.Equal(UnitEmail, objRecord.ToEmail);
        Assert.Equal(objSent.Subject, objRecord.Subject);
        Assert.Equal(objSent.Body, objRecord.Body);
        Assert.Equal(objSent.AttachmentName, objRecord.AttachmentName);
        Assert.Equal(objDbContext.CampaignRuns.Single().Id, objRecord.IDCampaignRun);

        // A tela remonta o PDF da execução (o mesmo arquivo, com o logo e as cores de hoje).
        AccessWifi.Api.Controllers.EmailsController objController = new(objDbContext);
        TestHelpers.SetUser(objController, null, "root");
        IActionResult objAttachment = await objController.Attachment(objRecord.Id, "regional", CancellationToken.None);
        FileContentResult objPdf = Assert.IsType<FileContentResult>(objAttachment);
        Assert.Equal("application/pdf", objPdf.ContentType);
        Assert.Equal(objSent.AttachmentName, objPdf.FileDownloadName);
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(objPdf.FileContents, 0, 4));

        // Depois que a LGPD apaga a lista da execução, o PDF não tem mais como ser remontado.
        objDbContext.CampaignRecipients.RemoveRange(objDbContext.CampaignRecipients);
        objDbContext.SaveChanges();
        NotFoundObjectResult objWithoutList = Assert.IsType<NotFoundObjectResult>(
            await objController.Attachment(objRecord.Id, "regional", CancellationToken.None));
        Assert.Contains("retenção", Assert.IsType<AccessWifi.Api.Features.ErrorResponse>(objWithoutList.Value).Error);
    }

    [Fact]
    public async Task Mail_FailedDelivery_OnlyRecordedWhenSent()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Scenario objScenario = CreateScenario(objDbContext);
        objScenario.Email.FailTimes = 1;
        AddCustomer(objScenario, "91900000001", "Ana", dtBirth: new DateOnly(1998, 9, 29));
        AddCampaign(objScenario, CampaignKind.Birthday, MessageConfig());
        CampaignEngine objEngine = CreateEngine(objScenario);

        await objEngine.TickAsync(s_dtNine.AddSeconds(3));
        Assert.Empty(objDbContext.SentEmails.AsNoTracking());

        await objEngine.TickAsync(s_dtNine.AddMinutes(6));
        Assert.Single(objDbContext.SentEmails.AsNoTracking());
    }

    [Fact]
    public async Task Delivery_UnitWithoutEmail_CustomersMarkedFailedWithReason()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Scenario objScenario = CreateScenario(objDbContext);
        objDbContext.Units.Single().Email = "";
        objDbContext.SaveChanges();
        AddCustomer(objScenario, "91900000001", "Ana", dtBirth: new DateOnly(1998, 9, 29));
        AddCampaign(objScenario, CampaignKind.Birthday, MessageConfig());

        await CreateEngine(objScenario).TickAsync(s_dtNine.AddSeconds(3));

        Assert.Empty(objScenario.Email.Sent);
        CampaignDelivery objDelivery = objDbContext.CampaignDeliveries.Single();
        Assert.Equal(CampaignDeliveryStatus.Failed, objDelivery.Status);
        Assert.Contains("não tem e-mail", objDelivery.Error);
        CampaignRecipient objAna = objDbContext.CampaignRecipients.Single();
        Assert.Equal(CampaignRecipientStatus.Failed, objAna.Status);
        Assert.Contains("não tem e-mail", objAna.Reason);
        CampaignRun objRun = objDbContext.CampaignRuns.Single();
        Assert.Equal(CampaignRunStatus.Completed, objRun.Status);
        Assert.Equal(1, objRun.FailedCount);
    }

    [Fact]
    public async Task Delivery_SmtpFails_RetriesAfter5Minutes()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Scenario objScenario = CreateScenario(objDbContext);
        objScenario.Email.FailTimes = 1;
        AddCustomer(objScenario, "91900000001", "Ana", dtBirth: new DateOnly(1998, 9, 29));
        AddCampaign(objScenario, CampaignKind.Birthday, MessageConfig());
        CampaignEngine objEngine = CreateEngine(objScenario);

        await objEngine.TickAsync(s_dtNine.AddSeconds(3));
        CampaignDelivery objWaiting = objDbContext.CampaignDeliveries.AsNoTracking().Single();
        Assert.Equal(CampaignDeliveryStatus.Pending, objWaiting.Status);
        Assert.Equal(1, objWaiting.Attempts);
        Assert.Equal(s_dtNine.AddSeconds(3).AddMinutes(5), objWaiting.NextAttemptAt);
        Assert.Equal(CampaignRunStatus.Running, objDbContext.CampaignRuns.AsNoTracking().Single().Status);

        await objEngine.TickAsync(s_dtNine.AddMinutes(2)); // antes da hora: não tenta
        Assert.Empty(objScenario.Email.Sent);
        await objEngine.TickAsync(s_dtNine.AddMinutes(6));

        Assert.Single(objScenario.Email.Sent);
        Assert.Equal(CampaignDeliveryStatus.Sent, objDbContext.CampaignDeliveries.AsNoTracking().Single().Status);
        Assert.Equal(CampaignRunStatus.Completed, objDbContext.CampaignRuns.AsNoTracking().Single().Status);
    }

    [Fact]
    public async Task Delivery_ThreeFailures_GivesUpAndMarksCustomersWithReason()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Scenario objScenario = CreateScenario(objDbContext);
        objScenario.Email.FailTimes = 10;
        AddCustomer(objScenario, "91900000001", "Ana", dtBirth: new DateOnly(1998, 9, 29));
        AddCampaign(objScenario, CampaignKind.Birthday, MessageConfig());
        CampaignEngine objEngine = CreateEngine(objScenario);

        foreach (int iMinute in new[] { 0, 6, 12, 18 })
        {
            await objEngine.TickAsync(s_dtNine.AddMinutes(iMinute).AddSeconds(3));
        }

        CampaignDelivery objDelivery = objDbContext.CampaignDeliveries.AsNoTracking().Single();
        Assert.Equal(CampaignDeliveryStatus.Failed, objDelivery.Status);
        Assert.Equal(3, objDelivery.Attempts);
        Assert.Contains("SMTP fora do ar", objDelivery.Error);
        CampaignRecipient objAna = objDbContext.CampaignRecipients.AsNoTracking().Single();
        Assert.Equal(CampaignRecipientStatus.Failed, objAna.Status);
        Assert.Contains("não saiu", objAna.Reason);
        Assert.Equal(CampaignRunStatus.Completed, objDbContext.CampaignRuns.AsNoTracking().Single().Status);
    }

    [Fact]
    public async Task Selection_InterruptedMidway_ResumesWithoutRepeatingAnyone()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Scenario objScenario = CreateScenario(objDbContext);
        CampaignEngine objEngine = CreateEngine(objScenario);
        Guid objRunId = await RunWithFivePendingAsync(objScenario, objEngine);

        // Simula a queda: a execução volta para "selecionando" com 5 já gravados, e a seleção roda de novo.
        CampaignRun objRun = objDbContext.CampaignRuns.Single(run => run.Id == objRunId);
        objRun.Status = CampaignRunStatus.Selecting;
        objDbContext.SaveChanges();
        await objEngine.SelectRecipientsAsync(objRunId, s_dtNine.AddMinutes(1));

        Assert.Equal(5, objDbContext.CampaignRecipients.Count(recipient => recipient.IDRun == objRunId));
        Assert.Equal(5, objDbContext.CampaignRuns.Single(run => run.Id == objRunId).TotalCount);
    }

    [Fact]
    public async Task Version_RunUsesVersionItWasCreatedWith()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Scenario objScenario = CreateScenario(objDbContext);
        AddCustomer(objScenario, "91900000001", "Ana", dtBirth: new DateOnly(1998, 9, 29));
        Campaign objCampaign = AddCampaign(objScenario, CampaignKind.Birthday, MessageConfig("Versão 1"));
        CampaignEngine objEngine = CreateEngine(objScenario);
        await objEngine.ScheduleDueAsync(s_dtNine.AddSeconds(3));

        // Editada entre a criação da execução e a seleção (D13).
        Campaign objEdited = objDbContext.Campaigns.Single(campaign => campaign.Id == objCampaign.Id);
        objEdited.ConfigJson = MessageConfig("Versão 2").ToJson();
        objEdited.CurrentVersion = 2;
        objDbContext.CampaignVersions.Add(new CampaignVersion
        {
            IDCampaign = objCampaign.Id, Number = 2, ConfigJson = objEdited.ConfigJson,
        });
        objDbContext.SaveChanges();
        await objEngine.TickAsync(s_dtNine.AddSeconds(8));

        Assert.Equal("Versão 1", Assert.Single(RecipientsOf(objDbContext, objCampaign.Id)).Message);
    }

    // ------------------------------------------------- Reenvio (botão "Reenviar" da unidade)

    /// <summary>
    /// O disparo do teste roda no relógio de 29/09; o pedido de reenvio usa o relógio de verdade. A campanha
    /// para de agendar, para o tick de agora só mandar o reenvio.
    /// </summary>
    private static (CampaignRun Run, CampaignDelivery Original) FinishedRun(Scenario objScenario)
    {
        Campaign objCampaign = objScenario.Db.Campaigns.Single();
        objCampaign.Status = CampaignStatus.Paused;
        objCampaign.NextRunAt = null;
        objScenario.Db.SaveChanges();
        return (objScenario.Db.CampaignRuns.AsNoTracking().Single(),
            objScenario.Db.CampaignDeliveries.AsNoTracking().Single());
    }

    private static AccessWifi.Api.Controllers.CampaignsController ResendController(Scenario objScenario)
    {
        AccessWifi.Api.Controllers.CampaignsController objController = new(objScenario.Db);
        TestHelpers.SetCompanyUser(objController, objScenario.Db, objScenario.Company.Id, "gerente");
        return objController;
    }

    [Fact]
    public async Task Resend_UnitHadNoEmail_GoesToTodaysEmail_AndRunNumbersStay()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Scenario objScenario = CreateScenario(objDbContext);
        objDbContext.Units.Single().Email = "";
        objDbContext.SaveChanges();
        AddCustomer(objScenario, "91900000001", "Ana", dtBirth: new DateOnly(1998, 9, 29));
        AddCampaign(objScenario, CampaignKind.Birthday, MessageConfig());
        CampaignEngine objEngine = CreateEngine(objScenario);
        await objEngine.TickAsync(s_dtNine.AddSeconds(3));
        (CampaignRun objRun, CampaignDelivery objOriginal) = FinishedRun(objScenario);
        Assert.Equal(CampaignDeliveryStatus.Failed, objOriginal.Status);

        // O e-mail da unidade foi cadastrado depois do disparo; o reenvio vai para ele.
        objDbContext.Units.Single().Email = "novo.gerente@regional.com.br";
        objDbContext.SaveChanges();
        AccessWifi.Api.Controllers.CampaignsController objController = ResendController(objScenario);
        ActionResult<AccessWifi.Api.Features.Campaigns.CampaignDeliveryDto> objResult =
            await objController.ResendDelivery(objRun.Id, objOriginal.Id, null, CancellationToken.None);
        AccessWifi.Api.Features.Campaigns.CampaignDeliveryDto objResend =
            Assert.IsType<AccessWifi.Api.Features.Campaigns.CampaignDeliveryDto>(Assert.IsType<OkObjectResult>(objResult.Result).Value);
        Assert.True(objResend.IsResend);
        Assert.Equal("novo.gerente@regional.com.br", objResend.Email);
        Assert.Equal("gerente", objResend.RequestedBy);
        Assert.Equal(CampaignDeliveryStatus.Pending, objResend.Status);
        // Clique duplo: o segundo pedido espera o primeiro sair.
        BadRequestObjectResult objTwice = Assert.IsType<BadRequestObjectResult>(
            (await objController.ResendDelivery(objRun.Id, objOriginal.Id, null, CancellationToken.None)).Result);
        Assert.Contains("Aguarde", Assert.IsType<AccessWifi.Api.Features.ErrorResponse>(objTwice.Value).Error);

        await objEngine.TickAsync(DateTime.UtcNow.AddSeconds(1));

        FakeEmailSender.Email objSent = Assert.Single(objScenario.Email.Sent);
        Assert.Equal("novo.gerente@regional.com.br", objSent.To);
        Assert.Contains("Itaituba", objSent.Subject);
        Assert.Equal(objOriginal.FileName, objSent.AttachmentName);
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(objSent.Attachment!, 0, 4));
        CampaignDelivery objDone = objDbContext.CampaignDeliveries.AsNoTracking().Single(delivery => delivery.IsResend);
        Assert.Equal(CampaignDeliveryStatus.Sent, objDone.Status);
        Assert.Equal(1, objDone.RecipientCount);

        // O disparo e os números da execução ficam como foram.
        Assert.Equal(CampaignDeliveryStatus.Failed,
            objDbContext.CampaignDeliveries.AsNoTracking().Single(delivery => !delivery.IsResend).Status);
        CampaignRun objAfter = objDbContext.CampaignRuns.AsNoTracking().Single();
        Assert.Equal(1, objAfter.FailedCount);
        Assert.Equal(0, objAfter.SentCount);
        Assert.Equal(CampaignRecipientStatus.Failed, objDbContext.CampaignRecipients.AsNoTracking().Single().Status);

        // Registrado como reenvio: Correio eletrônico (com o PDF remontado), histórico e detalhe da execução.
        SentEmail objRecord = Assert.Single(objDbContext.SentEmails.AsNoTracking());
        Assert.Equal(SentEmailKind.CampaignResend, objRecord.Kind);
        Assert.Equal("novo.gerente@regional.com.br", objRecord.ToEmail);
        AccessWifi.Api.Controllers.EmailsController objEmails = new(objDbContext);
        TestHelpers.SetUser(objEmails, null, "root");
        Assert.IsType<FileContentResult>(await objEmails.Attachment(objRecord.Id, "regional", CancellationToken.None));
        CampaignEvent objEvent = Assert.Single(objDbContext.CampaignEvents.AsNoTracking()
            .Where(evt => evt.Action == CampaignEventAction.DeliveryResent));
        Assert.Equal("gerente", objEvent.Username);
        Assert.Equal(objRun.Id, objEvent.IDRun);
        List<AccessWifi.Api.Features.Campaigns.CampaignDeliveryDto> objList = Assert.IsType<List<AccessWifi.Api.Features.Campaigns.CampaignDeliveryDto>>(
            Assert.IsType<OkObjectResult>((await objController.Deliveries(objRun.Id, null, CancellationToken.None)).Result).Value);
        Assert.Equal(new[] { false, true }, objList.Select(delivery => delivery.IsResend));
    }

    [Fact]
    public async Task Resend_OfCancelledRun_SmtpFails_RetriesLikeTheDispatch()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Scenario objScenario = CreateScenario(objDbContext);
        AddCustomer(objScenario, "91900000001", "Ana", dtBirth: new DateOnly(1998, 9, 29));
        AddCampaign(objScenario, CampaignKind.Birthday, MessageConfig());
        CampaignEngine objEngine = CreateEngine(objScenario);
        await objEngine.TickAsync(s_dtNine.AddSeconds(3));
        (CampaignRun objRun, CampaignDelivery objOriginal) = FinishedRun(objScenario);
        // Execução que terminou cancelada: o reenvio do que já tinha saído também vale.
        objDbContext.CampaignRuns.Single().Status = CampaignRunStatus.Cancelled;
        objDbContext.SaveChanges();
        objScenario.Email.Sent.Clear();
        objScenario.Email.FailTimes = 1;

        Assert.IsType<OkObjectResult>(
            (await ResendController(objScenario).ResendDelivery(objRun.Id, objOriginal.Id, null, CancellationToken.None)).Result);
        DateTime dtNow = DateTime.UtcNow.AddSeconds(1);
        await objEngine.TickAsync(dtNow);

        CampaignDelivery objWaiting = objDbContext.CampaignDeliveries.AsNoTracking().Single(delivery => delivery.IsResend);
        Assert.Equal(CampaignDeliveryStatus.Pending, objWaiting.Status);
        Assert.Equal(1, objWaiting.Attempts);
        Assert.Equal(dtNow.AddMinutes(5), objWaiting.NextAttemptAt);
        Assert.DoesNotContain(objDbContext.SentEmails.AsNoTracking(), email => email.Kind == SentEmailKind.CampaignResend);

        await objEngine.TickAsync(dtNow.AddMinutes(6));

        Assert.Equal(UnitEmail, Assert.Single(objScenario.Email.Sent).To);
        Assert.Equal(CampaignDeliveryStatus.Sent,
            objDbContext.CampaignDeliveries.AsNoTracking().Single(delivery => delivery.IsResend).Status);
        Assert.Equal(CampaignRunStatus.Cancelled, objDbContext.CampaignRuns.AsNoTracking().Single().Status);
        Assert.Equal(1, objDbContext.CampaignRuns.AsNoTracking().Single().SentCount);
    }
}
