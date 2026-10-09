using Models.Campaigns;
using Models.DataBase;
using Models.Persistence;

namespace AccessWifi.Api.Tests;

/// <summary>Base de clientes, calendário e mensagem das campanhas.</summary>
public class CampaignBasicsTests
{
    private static readonly TimeZoneInfo s_objBelem = CompanyTimeZone.Resolve("America/Belem");

    // ---------------------------------------------------------------- Clientes

    [Fact]
    public async Task RegisterVisit_FirstVisit_CreatesCustomerByPhoneWithBirthDate()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Guid objCompanyId = Guid.NewGuid(), objUnitId = Guid.NewGuid();
        DateTime dtNow = new DateTime(2026, 9, 29, 15, 0, 0, DateTimeKind.Utc);

        Customer? objCustomer = await CustomerDirectory.RegisterVisitAsync(
            objDbContext, objCompanyId, s_objBelem, objUnitId,
            "Ana Beatriz", "@ana", "(91) 98888-1234", "12/03/1998", dtNow);
        await objDbContext.SaveChangesAsync();

        Assert.NotNull(objCustomer);
        Assert.Equal("91988881234", objCustomer.Phone);
        Assert.Equal(new DateOnly(1998, 3, 12), objCustomer.BirthDate);
        Assert.Equal(1, objCustomer.VisitCount);
        Assert.Equal(new DateOnly(2026, 9, 29), objCustomer.FirstVisitDate);
        Assert.Single(objDbContext.CustomerUnits);
    }

    [Fact]
    public async Task RegisterVisit_SameDayCountsOneVisit_NextDayCountsAnother()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Guid objCompanyId = Guid.NewGuid(), objUnitId = Guid.NewGuid();
        DateTime dtDay1 = new DateTime(2026, 9, 29, 13, 0, 0, DateTimeKind.Utc);

        foreach (DateTime dtVisit in new[] { dtDay1, dtDay1.AddHours(3), dtDay1.AddDays(1) })
        {
            await CustomerDirectory.RegisterVisitAsync(
                objDbContext, objCompanyId, s_objBelem, objUnitId, "Ana", "", "91988881234", "", dtVisit);
            await objDbContext.SaveChangesAsync();
        }

        Customer objCustomer = Assert.Single(objDbContext.Customers);
        Assert.Equal(2, objCustomer.VisitCount);
        Assert.Equal(dtDay1.AddDays(1), objCustomer.LastVisitAt);
    }

    [Fact]
    public async Task RegisterVisit_SamePhoneInAnotherStore_IsSameCustomer()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Guid objCompanyId = Guid.NewGuid();
        DateTime dtNow = new DateTime(2026, 9, 29, 13, 0, 0, DateTimeKind.Utc);

        await CustomerDirectory.RegisterVisitAsync(
            objDbContext, objCompanyId, s_objBelem, Guid.NewGuid(), "Ana", "", "(91) 98888-1234", "", dtNow);
        await objDbContext.SaveChangesAsync();
        await CustomerDirectory.RegisterVisitAsync(
            objDbContext, objCompanyId, s_objBelem, Guid.NewGuid(), "", "", "91 988881234", "", dtNow);
        await objDbContext.SaveChangesAsync();

        Customer objCustomer = Assert.Single(objDbContext.Customers);
        // Nome em branco na segunda conexão não apaga o que já se sabia.
        Assert.Equal("Ana", objCustomer.Name);
        Assert.Equal(2, objDbContext.CustomerUnits.Count());
    }

    [Theory]
    [InlineData("31/02/1990")] // não existe
    [InlineData("02/10/2027")] // no futuro
    [InlineData("01/01/1850")] // antes de 1900
    [InlineData("1990-03-12")] // formato errado
    public void ParseBirthDate_InvalidDate_StaysNull(string sBirth)
    {
        Assert.Null(CustomerDirectory.ParseBirthDate(sBirth, new DateOnly(2026, 9, 29)));
    }

    [Fact]
    public async Task RegisterVisit_ShortPhone_DoesNotCreateCustomer()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();

        Customer? objCustomer = await CustomerDirectory.RegisterVisitAsync(
            objDbContext, Guid.NewGuid(), s_objBelem, Guid.NewGuid(), "Ana", "", "98888", "", DateTime.UtcNow);

        Assert.Null(objCustomer);
    }

    [Fact]
    public async Task Backfill_MergesSignupsWithSamePhoneIntoOneCustomer()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Company objCompany = new Company { Name = "Regional", Slug = "regional" };
        Unit objStore1 = new Unit { IDCompany = objCompany.Id, Name = "Loja 1", Slug = "l1" };
        Unit objStore2 = new Unit { IDCompany = objCompany.Id, Name = "Loja 2", Slug = "l2" };
        objDbContext.AddRange(objCompany, objStore1, objStore2);
        DateTime dtBase = new DateTime(2026, 9, 21, 12, 0, 0, DateTimeKind.Utc);
        objDbContext.Leads.AddRange(
            new Lead { IDUnit = objStore1.Id, Name = "Ana", Phone = "(91) 98888-1234", BirthDate = "12/03/1998", Mac = "a", CreatedAt = dtBase, Timestamp = dtBase },
            new Lead { IDUnit = objStore2.Id, Name = "Ana B.", Phone = "91988881234", BirthDate = "", Mac = "b", CreatedAt = dtBase.AddDays(2), Timestamp = dtBase.AddDays(3) },
            new Lead { IDUnit = objStore1.Id, Name = "Bruno", Phone = "(91) 97777-0000", BirthDate = "31/02/1990", Mac = "c", CreatedAt = dtBase, Timestamp = dtBase });
        objDbContext.SaveChanges();

        int iCreated = await CustomerDirectory.BackfillIfEmptyAsync(objDbContext, dtBase.AddDays(5));

        Assert.Equal(2, iCreated);
        Customer objAna = objDbContext.Customers.Single(customer => customer.Phone == "91988881234");
        Assert.Equal("Ana B.", objAna.Name); // o mais recente
        Assert.Equal(new DateOnly(1998, 3, 12), objAna.BirthDate); // o último válido
        Assert.Equal(dtBase, objAna.FirstVisitAt);
        Assert.Equal(dtBase.AddDays(3), objAna.LastVisitAt);
        Assert.Equal(3, objAna.VisitCount); // dias 21, 23 e 24
        Assert.Equal(2, objDbContext.CustomerUnits.Count(link => link.IDCustomer == objAna.Id));
        Assert.Null(objDbContext.Customers.Single(customer => customer.Phone == "91977770000").BirthDate);

        // Só roda com a tabela vazia.
        Assert.Equal(0, await CustomerDirectory.BackfillIfEmptyAsync(objDbContext, dtBase.AddDays(5)));
    }

    // -------------------------------------------------------------- Calendário

    private static CampaignConfig Filtered(string sRecurrence, DateOnly dtStart, DateOnly? dtEnd = null, int[]? arrDays = null) =>
        new CampaignConfig
        {
            Message = "Oi",
            SendTime = "09:00",
            Schedule = new CampaignScheduleConfig
            {
                Recurrence = sRecurrence, StartDate = dtStart, EndDate = dtEnd, DaysOfWeek = arrDays,
            },
        };

    [Fact]
    public void Calendar_System_EveryDayAtCompanyTime()
    {
        CampaignConfig objConfig = new CampaignConfig { Message = "Oi", SendTime = "09:00" };

        // 07:00 em Belém (10:00 UTC): ainda hoje, às 09:00 de Belém = 12:00 UTC.
        DateTime? dtToday = CampaignCalendar.NextOccurrence(
            CampaignKind.Birthday, objConfig, s_objBelem, new DateTime(2026, 9, 29, 10, 0, 0, DateTimeKind.Utc));
        // Exatamente no horário: o próximo é amanhã.
        DateTime? dtTomorrow = CampaignCalendar.NextOccurrence(
            CampaignKind.Birthday, objConfig, s_objBelem, new DateTime(2026, 9, 29, 12, 0, 0, DateTimeKind.Utc));

        Assert.Equal(new DateTime(2026, 9, 29, 12, 0, 0, DateTimeKind.Utc), dtToday);
        Assert.Equal(new DateTime(2026, 9, 30, 12, 0, 0, DateTimeKind.Utc), dtTomorrow);
    }

    [Fact]
    public void Calendar_Once_FiresOnDateThenEnds()
    {
        CampaignConfig objConfig = Filtered(CampaignRecurrence.Once, new DateOnly(2026, 10, 5));
        DateTime dtNow = new DateTime(2026, 9, 29, 12, 0, 0, DateTimeKind.Utc);

        DateTime? dtFire = CampaignCalendar.NextOccurrence(CampaignKind.Filtered, objConfig, s_objBelem, dtNow);

        Assert.Equal(new DateTime(2026, 10, 5, 12, 0, 0, DateTimeKind.Utc), dtFire);
        Assert.Null(CampaignCalendar.NextOccurrence(CampaignKind.Filtered, objConfig, s_objBelem, dtFire!.Value));
    }

    [Fact]
    public void Calendar_Weekly_OnlyOnChosenDays()
    {
        // Segunda (1) e sexta (5). 29/09/2026 é terça.
        CampaignConfig objConfig = Filtered(CampaignRecurrence.Weekly, new DateOnly(2026, 9, 29), arrDays: [1, 5]);

        DateTime? dtNext = CampaignCalendar.NextOccurrence(
            CampaignKind.Filtered, objConfig, s_objBelem, new DateTime(2026, 9, 29, 13, 0, 0, DateTimeKind.Utc));

        Assert.Equal(new DateTime(2026, 10, 2, 12, 0, 0, DateTimeKind.Utc), dtNext); // sexta, 02/10
    }

    [Fact]
    public void Calendar_MonthlyOnDay31_InShortMonthGoesToLastDay()
    {
        CampaignConfig objConfig = Filtered(CampaignRecurrence.Monthly, new DateOnly(2027, 1, 31));

        DateTime? dtNext = CampaignCalendar.NextOccurrence(
            CampaignKind.Filtered, objConfig, s_objBelem, new DateTime(2027, 1, 31, 13, 0, 0, DateTimeKind.Utc));

        Assert.Equal(new DateTime(2027, 2, 28, 12, 0, 0, DateTimeKind.Utc), dtNext);
    }

    [Fact]
    public void Calendar_YearlyFeb29_InNonLeapYearGoesTo28()
    {
        CampaignConfig objConfig = Filtered(CampaignRecurrence.Yearly, new DateOnly(2028, 2, 29));

        DateTime? dtNext = CampaignCalendar.NextOccurrence(
            CampaignKind.Filtered, objConfig, s_objBelem, new DateTime(2028, 3, 1, 0, 0, 0, DateTimeKind.Utc));

        Assert.Equal(new DateTime(2029, 2, 28, 12, 0, 0, DateTimeKind.Utc), dtNext);
    }

    [Fact]
    public void Calendar_AfterEndDate_HasNoMoreRuns()
    {
        CampaignConfig objConfig = Filtered(
            CampaignRecurrence.Daily, new DateOnly(2026, 10, 1), dtEnd: new DateOnly(2026, 10, 2));

        Assert.Null(CampaignCalendar.NextOccurrence(
            CampaignKind.Filtered, objConfig, s_objBelem, new DateTime(2026, 10, 2, 12, 0, 0, DateTimeKind.Utc)));
    }

    // ---------------------------------------------------------------- Mensagem

    [Fact]
    public void Message_FillsFields()
    {
        string sMessage = CampaignMessage.Render(
            "Oi, {primeiro_nome}! A {empresa} ({unidade}) deseja parabéns pelos {idade} anos — {nome}.",
            new CampaignMessageData("Ana Beatriz Souza", "Lojas Regional", "Itaituba", 28, 1));

        Assert.Equal(
            "Oi, Ana! A Lojas Regional (Itaituba) deseja parabéns pelos 28 anos — Ana Beatriz Souza.", sMessage);
    }

    [Fact]
    public void Age_BornFeb29_HasBirthdayOnFeb28InNonLeapYear()
    {
        DateOnly dtBirth = new DateOnly(2000, 2, 29);

        Assert.Equal(26, CampaignMessage.AgeOn(dtBirth, new DateOnly(2026, 2, 28)));
        Assert.Equal(25, CampaignMessage.AgeOn(dtBirth, new DateOnly(2026, 2, 27)));
    }
}
