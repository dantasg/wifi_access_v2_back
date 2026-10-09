using AccessWifi.Api.Controllers;
using AccessWifi.Api.Features;
using AccessWifi.Api.Features.Dashboard;
using Microsoft.AspNetCore.Mvc;
using Models.DataBase;
using Models.Persistence;

namespace AccessWifi.Api.Tests;

/// <summary>
/// Números do dashboard (PROPOSTA_DASHBOARD.md §3) num cenário montado à mão, período de 01 a 10/10/2026
/// (o anterior é de 21 a 30/09). Fuso de Belém (UTC-3): as visitas são ao meio-dia local (15h UTC).
///
/// - C1: 1ª visita na Itaituba em 25/09 (período anterior); volta em 02/10 e 05/10.
/// - C2: novo na Itaituba em 03/10; volta em 07/10.
/// - C3: novo na Castanhal em 04/10; não volta.
/// - C4: cliente antigo da Itaituba (01/08), vai pela 1ª vez à Castanhal em 06/10.
/// - Uma conexão sem cliente (telefone que não identificou ninguém) na Itaituba em 02/10.
/// </summary>
public class DashboardTests
{
    private static readonly TimeZoneInfo s_objBelem = CompanyTimeZone.Resolve("America/Belem");

    private sealed class Scenario
    {
        public required Company Company { get; init; }
        public required Unit Itaituba { get; init; }
        public required Unit Castanhal { get; init; }
    }

    /// <summary>Meio-dia de Belém do dia informado, em UTC.</summary>
    private static DateTime DayAt(int iMonth, int iDay, int iHour = 12) =>
        TimeZoneInfo.ConvertTimeToUtc(new DateTime(2026, iMonth, iDay, iHour, 0, 0, DateTimeKind.Unspecified), s_objBelem);

    private static Scenario BuildScenario(AppDbContext objDb)
    {
        Company objCompany = new Company { Name = "Lojas Regional", Slug = "regional" };
        Company objOther = new Company { Name = "Outra Rede", Slug = "outra" };
        Unit objItaituba = new Unit { IDCompany = objCompany.Id, Name = "Itaituba", Slug = "itaituba" };
        Unit objCastanhal = new Unit { IDCompany = objCompany.Id, Name = "Castanhal", Slug = "castanhal" };
        Unit objOtherUnit = new Unit { IDCompany = objOther.Id, Name = "Loja da outra", Slug = "loja-outra" };
        objDb.Companies.AddRange(objCompany, objOther);
        objDb.Units.AddRange(objItaituba, objCastanhal, objOtherUnit);

        Customer objC1 = MakeCustomer(objCompany, "93000000001", DayAt(9, 25), DayAt(10, 5), 3, new DateOnly(1990, 5, 10), "@c1");
        Customer objC2 = MakeCustomer(objCompany, "93000000002", DayAt(10, 3), DayAt(10, 7), 2, new DateOnly(2000, 1, 1), "");
        Customer objC3 = MakeCustomer(objCompany, "93000000003", DayAt(10, 4), DayAt(10, 4), 1, null, "");
        Customer objC4 = MakeCustomer(objCompany, "93000000004", DayAt(8, 1), DayAt(10, 6), 6, new DateOnly(1960, 3, 3), "@c4");
        Customer objOtherCustomer = MakeCustomer(objOther, "93000000009", DayAt(10, 2), DayAt(10, 2), 1, null, "@x");
        objDb.Customers.AddRange(objC1, objC2, objC3, objC4, objOtherCustomer);

        objDb.CustomerUnits.AddRange(
            Link(objC1, objItaituba, DayAt(9, 25), DayAt(10, 5)),
            Link(objC2, objItaituba, DayAt(10, 3), DayAt(10, 7)),
            Link(objC3, objCastanhal, DayAt(10, 4), DayAt(10, 4)),
            Link(objC4, objItaituba, DayAt(8, 1), DayAt(8, 1)),
            Link(objC4, objCastanhal, DayAt(10, 6), DayAt(10, 6)),
            Link(objOtherCustomer, objOtherUnit, DayAt(10, 2), DayAt(10, 2)));

        objDb.Visits.AddRange(
            MakeVisit(objItaituba, objC1, 9, 25, 9, bNew: true, sAp: "8c:30:66:4e:9b:58"),
            MakeVisit(objItaituba, objC1, 10, 2, 9, sAp: "8c:30:66:4e:9b:58"),
            MakeVisit(objItaituba, null, 10, 2, 15, sAp: "8c:30:66:4e:9b:58"),
            MakeVisit(objItaituba, objC2, 10, 3, 15, bNew: true, sAp: "8c:30:66:4e:9b:58"),
            MakeVisit(objCastanhal, objC3, 10, 4, 18, bNew: true, sAp: "9c:05:d6:73:bb:20"),
            MakeVisit(objItaituba, objC1, 10, 5, 9, sAp: "8c:30:66:4e:9b:58"),
            MakeVisit(objCastanhal, objC4, 10, 6, 18, bNew: true, sAp: "9c:05:d6:73:bb:20"),
            MakeVisit(objItaituba, objC2, 10, 7, 15, sAp: ""),
            MakeVisit(objOtherUnit, objOtherCustomer, 10, 2, 10, bNew: true, sAp: ""));

        objDb.UnitDevices.Add(new UnitDevice
        {
            IDUnit = objCastanhal.Id, Mac = "9c:05:d6:73:bb:20", Name = "UK Ultra", Model = "UK Ultra", SyncedAt = DateTime.UtcNow,
        });
        objDb.SaveChanges();
        return new Scenario { Company = objCompany, Itaituba = objItaituba, Castanhal = objCastanhal };
    }

    private static Customer MakeCustomer(
        Company objCompany, string sPhone, DateTime dtFirst, DateTime dtLast, int iVisits, DateOnly? dtBirth,
        string sInstagram) =>
        new Customer
        {
            IDCompany = objCompany.Id,
            Phone = sPhone,
            Name = "Cliente",
            Instagram = sInstagram,
            BirthDate = dtBirth,
            FirstVisitAt = dtFirst,
            FirstVisitDate = CompanyTimeZone.Today(s_objBelem, dtFirst),
            LastVisitAt = dtLast,
            LastVisitDate = CompanyTimeZone.Today(s_objBelem, dtLast),
            VisitCount = iVisits,
        };

    private static CustomerUnit Link(Customer objCustomer, Unit objUnit, DateTime dtFirst, DateTime dtLast) =>
        new CustomerUnit { IDCustomer = objCustomer.Id, IDUnit = objUnit.Id, FirstVisitAt = dtFirst, LastVisitAt = dtLast };

    private static Visit MakeVisit(
        Unit objUnit, Customer? objCustomer, int iMonth, int iDay, int iHour, bool bNew = false, string sAp = "") =>
        new Visit
        {
            IDUnit = objUnit.Id,
            IDCustomer = objCustomer?.Id,
            At = DayAt(iMonth, iDay, iHour),
            LocalDate = new DateOnly(2026, iMonth, iDay),
            LocalHour = iHour,
            NewInCompany = bNew,
            NewInUnit = bNew,
            Ap = sAp,
        };

    private static async Task<DashboardDto> FetchAsync(
        DashboardController objController, string? sCompany = null, string? sUnit = null,
        string? sFrom = "2026-10-01", string? sTo = "2026-10-10")
    {
        ActionResult<DashboardDto> objResult = await objController.Get(sCompany, sUnit, sFrom, sTo, CancellationToken.None);
        return Assert.IsType<DashboardDto>(Assert.IsType<OkObjectResult>(objResult.Result).Value);
    }

    // ---------------------------------------------------------------- visão empresa

    [Fact]
    public async Task CompanyView_CardsAndComparison()
    {
        using AppDbContext objDb = TestHelpers.CreateDbContext();
        Scenario objScenario = BuildScenario(objDb);
        DashboardController objController = new DashboardController(objDb);
        TestHelpers.SetCompanyUser(objController, objDb, objScenario.Company.Id);

        DashboardDto objDash = await FetchAsync(objController);

        Assert.Null(objDash.Unit);
        Assert.Equal(new DashboardPeriodDto(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 10),
            new DateOnly(2026, 9, 21), new DateOnly(2026, 9, 30), 10), objDash.Period);
        Assert.Equal(new DashboardCompareDto(7, 1), objDash.Kpis.Connections);
        Assert.Equal(new DashboardCompareDto(2, 1), objDash.Kpis.NewCustomers);   // C2 e C3; antes, C1
        Assert.Equal(new DashboardCompareDto(4, 1), objDash.Kpis.Visitors);
        Assert.Equal(new DashboardCompareDto(3, 0), objDash.Kpis.Returning);      // C1, C2 (no dia 07) e C4
        Assert.Equal(new DashboardRateDto(75.0, 0.0), objDash.Kpis.ReturnRate);
        Assert.Equal(4, objDash.Kpis.CustomerBase);                               // nada da outra empresa
        Assert.Equal(new DateOnly(2026, 9, 25), objDash.VisitsSince);
    }

    [Fact]
    public async Task CompanyView_ByDay_Hour_Weekday()
    {
        using AppDbContext objDb = TestHelpers.CreateDbContext();
        Scenario objScenario = BuildScenario(objDb);
        DashboardController objController = new DashboardController(objDb);
        TestHelpers.SetCompanyUser(objController, objDb, objScenario.Company.Id);

        DashboardDto objDash = await FetchAsync(objController);

        Assert.Equal(10, objDash.Daily.Count);
        Assert.Equal(new DashboardDayDto(new DateOnly(2026, 10, 2), 2, 0, 1), objDash.Daily[1]);
        Assert.Equal(new DashboardDayDto(new DateOnly(2026, 10, 3), 1, 1, 0), objDash.Daily[2]);
        Assert.Equal(new DashboardDayDto(new DateOnly(2026, 10, 6), 1, 0, 1), objDash.Daily[5]);
        Assert.Equal(new DashboardDayDto(new DateOnly(2026, 10, 7), 1, 0, 1), objDash.Daily[6]);
        Assert.Equal(new DashboardDayDto(new DateOnly(2026, 10, 10), 0, 0, 0), objDash.Daily[9]);

        Assert.Equal(24, objDash.Hourly.Count);
        Assert.Equal(2, objDash.Hourly[9]);
        Assert.Equal(3, objDash.Hourly[15]);
        Assert.Equal(2, objDash.Hourly[18]);
        // 02/10/2026 é sexta (5): C1 e a conexão sem cliente.
        Assert.Equal(7, objDash.Weekday.Count);
        Assert.Equal(2, objDash.Weekday[(int)DayOfWeek.Friday]);
    }

    [Fact]
    public async Task CompanyView_TableByUnit()
    {
        using AppDbContext objDb = TestHelpers.CreateDbContext();
        Scenario objScenario = BuildScenario(objDb);
        DashboardController objController = new DashboardController(objDb);
        TestHelpers.SetCompanyUser(objController, objDb, objScenario.Company.Id);

        DashboardDto objDash = await FetchAsync(objController);

        Assert.Empty(objDash.AccessPoints);
        Assert.Equal(["itaituba", "castanhal"], objDash.Units.Select(unit => unit.Slug));
        DashboardUnitRowDto objIta = objDash.Units[0];
        Assert.Equal((5, 1, 2, 2, 100.0, 3), (objIta.Connections, objIta.NewCustomers, objIta.Visitors, objIta.Returning, objIta.ReturnRate, objIta.Customers));
        DashboardUnitRowDto objCas = objDash.Units[1];
        // C4 foi à Castanhal pela 1ª vez no período: novo NA UNIDADE e não voltou a ela.
        Assert.Equal((2, 2, 2, 0, 0.0, 2), (objCas.Connections, objCas.NewCustomers, objCas.Visitors, objCas.Returning, objCas.ReturnRate, objCas.Customers));
    }

    // ---------------------------------------------------------------- visão unidade

    [Fact]
    public async Task UnitView_OnlyTheUnit_WithAccessPoints()
    {
        using AppDbContext objDb = TestHelpers.CreateDbContext();
        Scenario objScenario = BuildScenario(objDb);
        DashboardController objController = new DashboardController(objDb);
        TestHelpers.SetCompanyUser(objController, objDb, objScenario.Company.Id);

        DashboardDto objDash = await FetchAsync(objController, sUnit: "castanhal");

        Assert.Equal("castanhal", objDash.Unit?.Slug);
        Assert.Equal(2, objDash.Kpis.Connections.Current);
        Assert.Equal(2, objDash.Kpis.NewCustomers.Current);  // C3 e C4 (1ª vez nesta loja)
        Assert.Equal(0, objDash.Kpis.Returning.Current);
        Assert.Equal(2, objDash.Kpis.CustomerBase);
        Assert.Empty(objDash.Units);
        DashboardApDto objAp = Assert.Single(objDash.AccessPoints);
        Assert.Equal(("9c:05:d6:73:bb:20", "UK Ultra", 2), (objAp.Mac, objAp.Name, objAp.Connections));
    }

    [Fact]
    public async Task UnitView_ApWithoutName_AndVisitWithoutAp()
    {
        using AppDbContext objDb = TestHelpers.CreateDbContext();
        Scenario objScenario = BuildScenario(objDb);
        DashboardController objController = new DashboardController(objDb);
        TestHelpers.SetCompanyUser(objController, objDb, objScenario.Company.Id);

        DashboardDto objDash = await FetchAsync(objController, sUnit: "itaituba");

        Assert.Equal(
            [("8c:30:66:4e:9b:58", "Ponto de acesso não identificado", 4), ("", "Sem ponto de acesso informado", 1)],
            objDash.AccessPoints.Select(ap => (ap.Mac, ap.Name, ap.Connections)));
    }

    // ---------------------------------------------------------------- acesso

    [Fact]
    public async Task UnitUser_SeesOnlyOwnUnits()
    {
        using AppDbContext objDb = TestHelpers.CreateDbContext();
        Scenario objScenario = BuildScenario(objDb);
        DashboardController objController = new DashboardController(objDb);
        TestHelpers.SetCompanyUser(objController, objDb, objScenario.Company.Id, "gerente-castanhal", objScenario.Castanhal.Id);

        DashboardDto objDash = await FetchAsync(objController);
        Assert.Equal(["castanhal"], objDash.Units.Select(unit => unit.Slug));
        Assert.Equal(2, objDash.Kpis.Connections.Current);
        Assert.Equal(2, objDash.Kpis.CustomerBase);

        ActionResult<DashboardDto> objOther = await objController.Get(null, "itaituba", "2026-10-01", "2026-10-10", CancellationToken.None);
        Assert.IsType<NotFoundObjectResult>(objOther.Result);
    }

    [Fact]
    public async Task CompanyAdmin_CannotPickAnotherCompany()
    {
        using AppDbContext objDb = TestHelpers.CreateDbContext();
        Scenario objScenario = BuildScenario(objDb);
        DashboardController objController = new DashboardController(objDb);
        TestHelpers.SetCompanyUser(objController, objDb, objScenario.Company.Id);

        DashboardDto objDash = await FetchAsync(objController, sCompany: "outra");

        Assert.Equal("regional", objDash.Company.Slug);
    }

    [Fact]
    public async Task SuperAdmin_NeedsCompany()
    {
        using AppDbContext objDb = TestHelpers.CreateDbContext();
        BuildScenario(objDb);
        DashboardController objController = new DashboardController(objDb);
        TestHelpers.SetUser(objController, null, "root");

        ActionResult<DashboardDto> objWithout = await objController.Get(null, null, null, null, CancellationToken.None);
        Assert.IsType<BadRequestObjectResult>(objWithout.Result);

        DashboardDto objDash = await FetchAsync(objController, sCompany: "outra");
        Assert.Equal(1, objDash.Kpis.Connections.Current);
        Assert.Equal(["loja-outra"], objDash.Units.Select(unit => unit.Slug));
    }

    // ---------------------------------------------------------------- clientes

    [Fact]
    public async Task AgeBand_Frequency_Instagram()
    {
        using AppDbContext objDb = TestHelpers.CreateDbContext();
        Scenario objScenario = BuildScenario(objDb);
        DashboardController objController = new DashboardController(objDb);
        TestHelpers.SetCompanyUser(objController, objDb, objScenario.Company.Id);

        DashboardDto objDash = await FetchAsync(objController);

        // C2 (2000) 25–26 anos; C1 (1990) 35–36; C4 (1960) 60+; C3 sem nascimento.
        Assert.Equal([0, 0, 1, 1, 0, 1], objDash.AgeBands.Select(band => band.Count));
        Assert.Equal("Menos de 18", objDash.AgeBands[0].Label);
        // Visitas na empresa: C3 = 1; C2 = 2; C1 = 3; C4 = 6.
        Assert.Equal([1, 1, 1, 1], objDash.Frequency.Select(band => band.Count));
        Assert.Equal(2, objDash.Extras.WithInstagram);
    }

    [Fact]
    public async Task Birthdays_OfCurrentMonth()
    {
        using AppDbContext objDb = TestHelpers.CreateDbContext();
        Scenario objScenario = BuildScenario(objDb);
        DateOnly dtToday = CompanyTimeZone.Today(s_objBelem, DateTime.UtcNow);
        Customer objBirthdayCustomer = objDb.Customers.Single(customer => customer.Phone == "93000000003");
        objBirthdayCustomer.BirthDate = new DateOnly(1995, dtToday.Month, 1);
        objDb.SaveChanges();
        DashboardController objController = new DashboardController(objDb);
        TestHelpers.SetCompanyUser(objController, objDb, objScenario.Company.Id);

        DashboardDto objDash = await FetchAsync(objController);

        Assert.True(objDash.Extras.BirthdaysThisMonth >= 1);
        Assert.Equal(
            objDb.Customers.Count(customer => customer.IDCompany == objScenario.Company.Id
                && customer.BirthDate != null && customer.BirthDate.Value.Month == dtToday.Month),
            objDash.Extras.BirthdaysThisMonth);
    }

    [Fact]
    public async Task Campaigns_OnlyViewCustomers_AndNoSimulation()
    {
        using AppDbContext objDb = TestHelpers.CreateDbContext();
        Scenario objScenario = BuildScenario(objDb);
        Customer objC1 = objDb.Customers.Single(customer => customer.Phone == "93000000001");
        Customer objC3 = objDb.Customers.Single(customer => customer.Phone == "93000000003");
        CampaignRun objRun = new CampaignRun { IDCompany = objScenario.Company.Id, LocalDate = new DateOnly(2026, 10, 5) };
        CampaignRun objSimulated = new CampaignRun { IDCompany = objScenario.Company.Id, LocalDate = new DateOnly(2026, 10, 6), Simulation = true };
        objDb.CampaignRuns.AddRange(objRun, objSimulated);
        objDb.CampaignRecipients.AddRange(
            new CampaignRecipient { IDRun = objRun.Id, IDCustomer = objC1.Id, IDUnit = objScenario.Itaituba.Id, Status = CampaignRecipientStatus.Sent },
            new CampaignRecipient { IDRun = objRun.Id, IDCustomer = objC3.Id, IDUnit = objScenario.Castanhal.Id, Status = CampaignRecipientStatus.Failed },
            new CampaignRecipient { IDRun = objSimulated.Id, IDCustomer = objC1.Id, IDUnit = objScenario.Itaituba.Id, Status = CampaignRecipientStatus.Sent });
        objDb.SaveChanges();
        DashboardController objController = new DashboardController(objDb);
        TestHelpers.SetCompanyUser(objController, objDb, objScenario.Company.Id);

        DashboardDto objCompany = await FetchAsync(objController);
        DashboardDto objItaituba = await FetchAsync(objController, sUnit: "itaituba");

        Assert.Equal((1, 1, 1), (objCompany.Extras.CampaignRuns, objCompany.Extras.CampaignSent, objCompany.Extras.CampaignFailed));
        Assert.Equal((1, 1, 0), (objItaituba.Extras.CampaignRuns, objItaituba.Extras.CampaignSent, objItaituba.Extras.CampaignFailed));
    }

    // ---------------------------------------------------------------- período

    [Fact]
    public async Task NoDates_LastThirtyDaysUntilToday()
    {
        using AppDbContext objDb = TestHelpers.CreateDbContext();
        Scenario objScenario = BuildScenario(objDb);
        DashboardController objController = new DashboardController(objDb);
        TestHelpers.SetCompanyUser(objController, objDb, objScenario.Company.Id);

        DashboardDto objDash = await FetchAsync(objController, sFrom: null, sTo: null);

        DateOnly dtToday = CompanyTimeZone.Today(s_objBelem, DateTime.UtcNow);
        Assert.Equal(dtToday, objDash.Period.To);
        Assert.Equal(dtToday.AddDays(-29), objDash.Period.From);
        Assert.Equal(30, objDash.Daily.Count);
    }

    [Theory]
    [InlineData("2026-10-10", "2026-10-01", "depois da final")]
    [InlineData("10/01/2026", "2026-10-10", "Data inicial inválida")]
    [InlineData("2026-10-01", "ontem", "Data final inválida")]
    [InlineData("2024-01-01", "2026-10-10", "até 2 anos")]
    public async Task InvalidPeriod_400(string sFrom, string sTo, string sSnippet)
    {
        using AppDbContext objDb = TestHelpers.CreateDbContext();
        Scenario objScenario = BuildScenario(objDb);
        DashboardController objController = new DashboardController(objDb);
        TestHelpers.SetCompanyUser(objController, objDb, objScenario.Company.Id);

        ActionResult<DashboardDto> objResult = await objController.Get(null, null, sFrom, sTo, CancellationToken.None);

        ErrorResponse objError = Assert.IsType<ErrorResponse>(Assert.IsType<BadRequestObjectResult>(objResult.Result).Value);
        Assert.Contains(sSnippet, objError.Error);
    }

    [Fact]
    public async Task NoVisitsAtAll_ZerosWithoutBreaking()
    {
        using AppDbContext objDb = TestHelpers.CreateDbContext();
        Company objCompany = new Company { Name = "Nova", Slug = "nova" };
        objDb.Companies.Add(objCompany);
        objDb.Units.Add(new Unit { IDCompany = objCompany.Id, Name = "Loja", Slug = "loja" });
        objDb.SaveChanges();
        DashboardController objController = new DashboardController(objDb);
        TestHelpers.SetCompanyUser(objController, objDb, objCompany.Id);

        DashboardDto objDash = await FetchAsync(objController);

        Assert.Null(objDash.VisitsSince);
        Assert.Equal(0, objDash.Kpis.Connections.Current);
        Assert.Null(objDash.Kpis.ReturnRate.Current);
        Assert.All(objDash.AgeBands, band => Assert.Equal(0, band.Count));
        Assert.Single(objDash.Units);
    }
}
