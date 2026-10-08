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

    private sealed class Cenario
    {
        public required Company Company { get; init; }
        public required Unit Itaituba { get; init; }
        public required Unit Castanhal { get; init; }
    }

    /// <summary>Meio-dia de Belém do dia informado, em UTC.</summary>
    private static DateTime Dia(int iMes, int iDia, int iHora = 12) =>
        TimeZoneInfo.ConvertTimeToUtc(new DateTime(2026, iMes, iDia, iHora, 0, 0, DateTimeKind.Unspecified), s_objBelem);

    private static Cenario Montar(AppDbContext objDb)
    {
        Company objCompany = new Company { Name = "Lojas Regional", Slug = "regional" };
        Company objOutra = new Company { Name = "Outra Rede", Slug = "outra" };
        Unit objItaituba = new Unit { IDCompany = objCompany.Id, Name = "Itaituba", Slug = "itaituba" };
        Unit objCastanhal = new Unit { IDCompany = objCompany.Id, Name = "Castanhal", Slug = "castanhal" };
        Unit objDaOutra = new Unit { IDCompany = objOutra.Id, Name = "Loja da outra", Slug = "loja-outra" };
        objDb.Companies.AddRange(objCompany, objOutra);
        objDb.Units.AddRange(objItaituba, objCastanhal, objDaOutra);

        Customer objC1 = Cliente(objCompany, "93000000001", Dia(9, 25), Dia(10, 5), 3, new DateOnly(1990, 5, 10), "@c1");
        Customer objC2 = Cliente(objCompany, "93000000002", Dia(10, 3), Dia(10, 7), 2, new DateOnly(2000, 1, 1), "");
        Customer objC3 = Cliente(objCompany, "93000000003", Dia(10, 4), Dia(10, 4), 1, null, "");
        Customer objC4 = Cliente(objCompany, "93000000004", Dia(8, 1), Dia(10, 6), 6, new DateOnly(1960, 3, 3), "@c4");
        Customer objDeOutra = Cliente(objOutra, "93000000009", Dia(10, 2), Dia(10, 2), 1, null, "@x");
        objDb.Customers.AddRange(objC1, objC2, objC3, objC4, objDeOutra);

        objDb.CustomerUnits.AddRange(
            Link(objC1, objItaituba, Dia(9, 25), Dia(10, 5)),
            Link(objC2, objItaituba, Dia(10, 3), Dia(10, 7)),
            Link(objC3, objCastanhal, Dia(10, 4), Dia(10, 4)),
            Link(objC4, objItaituba, Dia(8, 1), Dia(8, 1)),
            Link(objC4, objCastanhal, Dia(10, 6), Dia(10, 6)),
            Link(objDeOutra, objDaOutra, Dia(10, 2), Dia(10, 2)));

        objDb.Visits.AddRange(
            Conexao(objItaituba, objC1, 9, 25, 9, bNovo: true, sAp: "8c:30:66:4e:9b:58"),
            Conexao(objItaituba, objC1, 10, 2, 9, sAp: "8c:30:66:4e:9b:58"),
            Conexao(objItaituba, null, 10, 2, 15, sAp: "8c:30:66:4e:9b:58"),
            Conexao(objItaituba, objC2, 10, 3, 15, bNovo: true, sAp: "8c:30:66:4e:9b:58"),
            Conexao(objCastanhal, objC3, 10, 4, 18, bNovo: true, sAp: "9c:05:d6:73:bb:20"),
            Conexao(objItaituba, objC1, 10, 5, 9, sAp: "8c:30:66:4e:9b:58"),
            Conexao(objCastanhal, objC4, 10, 6, 18, bNovo: true, sAp: "9c:05:d6:73:bb:20"),
            Conexao(objItaituba, objC2, 10, 7, 15, sAp: ""),
            Conexao(objDaOutra, objDeOutra, 10, 2, 10, bNovo: true, sAp: ""));

        objDb.UnitDevices.Add(new UnitDevice
        {
            IDUnit = objCastanhal.Id, Mac = "9c:05:d6:73:bb:20", Name = "UK Ultra", Model = "UK Ultra", SyncedAt = DateTime.UtcNow,
        });
        objDb.SaveChanges();
        return new Cenario { Company = objCompany, Itaituba = objItaituba, Castanhal = objCastanhal };
    }

    private static Customer Cliente(
        Company objCompany, string sPhone, DateTime dtPrimeira, DateTime dtUltima, int iVisitas, DateOnly? dtNascimento,
        string sInstagram) =>
        new Customer
        {
            IDCompany = objCompany.Id,
            Phone = sPhone,
            Name = "Cliente",
            Instagram = sInstagram,
            BirthDate = dtNascimento,
            FirstVisitAt = dtPrimeira,
            FirstVisitDate = CompanyTimeZone.Today(s_objBelem, dtPrimeira),
            LastVisitAt = dtUltima,
            LastVisitDate = CompanyTimeZone.Today(s_objBelem, dtUltima),
            VisitCount = iVisitas,
        };

    private static CustomerUnit Link(Customer objCustomer, Unit objUnit, DateTime dtPrimeira, DateTime dtUltima) =>
        new CustomerUnit { IDCustomer = objCustomer.Id, IDUnit = objUnit.Id, FirstVisitAt = dtPrimeira, LastVisitAt = dtUltima };

    private static Visit Conexao(
        Unit objUnit, Customer? objCustomer, int iMes, int iDia, int iHora, bool bNovo = false, string sAp = "") =>
        new Visit
        {
            IDUnit = objUnit.Id,
            IDCustomer = objCustomer?.Id,
            At = Dia(iMes, iDia, iHora),
            LocalDate = new DateOnly(2026, iMes, iDia),
            LocalHour = iHora,
            NewInCompany = bNovo,
            NewInUnit = bNovo,
            Ap = sAp,
        };

    private static async Task<DashboardDto> PedirAsync(
        DashboardController objController, string? sCompany = null, string? sUnit = null,
        string? sFrom = "2026-10-01", string? sTo = "2026-10-10")
    {
        ActionResult<DashboardDto> objResult = await objController.Get(sCompany, sUnit, sFrom, sTo, CancellationToken.None);
        return Assert.IsType<DashboardDto>(Assert.IsType<OkObjectResult>(objResult.Result).Value);
    }

    // ---------------------------------------------------------------- visão empresa

    [Fact]
    public async Task VisaoEmpresa_CartoesEComparacao()
    {
        using AppDbContext objDb = TestHelpers.CreateDbContext();
        Cenario objCenario = Montar(objDb);
        DashboardController objController = new DashboardController(objDb);
        TestHelpers.SetCompanyUser(objController, objDb, objCenario.Company.Id);

        DashboardDto objDash = await PedirAsync(objController);

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
    public async Task VisaoEmpresa_PorDia_Hora_DiaDaSemana()
    {
        using AppDbContext objDb = TestHelpers.CreateDbContext();
        Cenario objCenario = Montar(objDb);
        DashboardController objController = new DashboardController(objDb);
        TestHelpers.SetCompanyUser(objController, objDb, objCenario.Company.Id);

        DashboardDto objDash = await PedirAsync(objController);

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
    public async Task VisaoEmpresa_TabelaPorUnidade()
    {
        using AppDbContext objDb = TestHelpers.CreateDbContext();
        Cenario objCenario = Montar(objDb);
        DashboardController objController = new DashboardController(objDb);
        TestHelpers.SetCompanyUser(objController, objDb, objCenario.Company.Id);

        DashboardDto objDash = await PedirAsync(objController);

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
    public async Task VisaoUnidade_SoAUnidade_ComPontosDeAcesso()
    {
        using AppDbContext objDb = TestHelpers.CreateDbContext();
        Cenario objCenario = Montar(objDb);
        DashboardController objController = new DashboardController(objDb);
        TestHelpers.SetCompanyUser(objController, objDb, objCenario.Company.Id);

        DashboardDto objDash = await PedirAsync(objController, sUnit: "castanhal");

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
    public async Task VisaoUnidade_ApSemNome_EConexaoSemAp()
    {
        using AppDbContext objDb = TestHelpers.CreateDbContext();
        Cenario objCenario = Montar(objDb);
        DashboardController objController = new DashboardController(objDb);
        TestHelpers.SetCompanyUser(objController, objDb, objCenario.Company.Id);

        DashboardDto objDash = await PedirAsync(objController, sUnit: "itaituba");

        Assert.Equal(
            [("8c:30:66:4e:9b:58", "Ponto de acesso não identificado", 4), ("", "Sem ponto de acesso informado", 1)],
            objDash.AccessPoints.Select(ap => (ap.Mac, ap.Name, ap.Connections)));
    }

    // ---------------------------------------------------------------- acesso

    [Fact]
    public async Task UsuarioDeUnidade_SoVeAsUnidadesDele()
    {
        using AppDbContext objDb = TestHelpers.CreateDbContext();
        Cenario objCenario = Montar(objDb);
        DashboardController objController = new DashboardController(objDb);
        TestHelpers.SetCompanyUser(objController, objDb, objCenario.Company.Id, "gerente-castanhal", objCenario.Castanhal.Id);

        DashboardDto objDash = await PedirAsync(objController);
        Assert.Equal(["castanhal"], objDash.Units.Select(unit => unit.Slug));
        Assert.Equal(2, objDash.Kpis.Connections.Current);
        Assert.Equal(2, objDash.Kpis.CustomerBase);

        ActionResult<DashboardDto> objOutra = await objController.Get(null, "itaituba", "2026-10-01", "2026-10-10", CancellationToken.None);
        Assert.IsType<NotFoundObjectResult>(objOutra.Result);
    }

    [Fact]
    public async Task AdminDaEmpresa_NaoEscolheOutraEmpresa()
    {
        using AppDbContext objDb = TestHelpers.CreateDbContext();
        Cenario objCenario = Montar(objDb);
        DashboardController objController = new DashboardController(objDb);
        TestHelpers.SetCompanyUser(objController, objDb, objCenario.Company.Id);

        DashboardDto objDash = await PedirAsync(objController, sCompany: "outra");

        Assert.Equal("regional", objDash.Company.Slug);
    }

    [Fact]
    public async Task SuperAdmin_PrecisaDaEmpresa()
    {
        using AppDbContext objDb = TestHelpers.CreateDbContext();
        Montar(objDb);
        DashboardController objController = new DashboardController(objDb);
        TestHelpers.SetUser(objController, null, "root");

        ActionResult<DashboardDto> objSem = await objController.Get(null, null, null, null, CancellationToken.None);
        Assert.IsType<BadRequestObjectResult>(objSem.Result);

        DashboardDto objDash = await PedirAsync(objController, sCompany: "outra");
        Assert.Equal(1, objDash.Kpis.Connections.Current);
        Assert.Equal(["loja-outra"], objDash.Units.Select(unit => unit.Slug));
    }

    // ---------------------------------------------------------------- clientes

    [Fact]
    public async Task FaixaEtaria_Frequencia_Instagram()
    {
        using AppDbContext objDb = TestHelpers.CreateDbContext();
        Cenario objCenario = Montar(objDb);
        DashboardController objController = new DashboardController(objDb);
        TestHelpers.SetCompanyUser(objController, objDb, objCenario.Company.Id);

        DashboardDto objDash = await PedirAsync(objController);

        // C2 (2000) 25–26 anos; C1 (1990) 35–36; C4 (1960) 60+; C3 sem nascimento.
        Assert.Equal([0, 0, 1, 1, 0, 1], objDash.AgeBands.Select(band => band.Count));
        Assert.Equal("Menos de 18", objDash.AgeBands[0].Label);
        // Visitas na empresa: C3 = 1; C2 = 2; C1 = 3; C4 = 6.
        Assert.Equal([1, 1, 1, 1], objDash.Frequency.Select(band => band.Count));
        Assert.Equal(2, objDash.Extras.WithInstagram);
    }

    [Fact]
    public async Task Aniversariantes_DoMesCorrente()
    {
        using AppDbContext objDb = TestHelpers.CreateDbContext();
        Cenario objCenario = Montar(objDb);
        DateOnly dtHoje = CompanyTimeZone.Today(s_objBelem, DateTime.UtcNow);
        Customer objAniversariante = objDb.Customers.Single(customer => customer.Phone == "93000000003");
        objAniversariante.BirthDate = new DateOnly(1995, dtHoje.Month, 1);
        objDb.SaveChanges();
        DashboardController objController = new DashboardController(objDb);
        TestHelpers.SetCompanyUser(objController, objDb, objCenario.Company.Id);

        DashboardDto objDash = await PedirAsync(objController);

        Assert.True(objDash.Extras.BirthdaysThisMonth >= 1);
        Assert.Equal(
            objDb.Customers.Count(customer => customer.IDCompany == objCenario.Company.Id
                && customer.BirthDate != null && customer.BirthDate.Value.Month == dtHoje.Month),
            objDash.Extras.BirthdaysThisMonth);
    }

    [Fact]
    public async Task Campanhas_SoClientesDaVisao_ESemSimulacao()
    {
        using AppDbContext objDb = TestHelpers.CreateDbContext();
        Cenario objCenario = Montar(objDb);
        Customer objC1 = objDb.Customers.Single(customer => customer.Phone == "93000000001");
        Customer objC3 = objDb.Customers.Single(customer => customer.Phone == "93000000003");
        CampaignRun objRun = new CampaignRun { IDCompany = objCenario.Company.Id, LocalDate = new DateOnly(2026, 10, 5) };
        CampaignRun objSimulada = new CampaignRun { IDCompany = objCenario.Company.Id, LocalDate = new DateOnly(2026, 10, 6), Simulation = true };
        objDb.CampaignRuns.AddRange(objRun, objSimulada);
        objDb.CampaignRecipients.AddRange(
            new CampaignRecipient { IDRun = objRun.Id, IDCustomer = objC1.Id, IDUnit = objCenario.Itaituba.Id, Status = CampaignRecipientStatus.Sent },
            new CampaignRecipient { IDRun = objRun.Id, IDCustomer = objC3.Id, IDUnit = objCenario.Castanhal.Id, Status = CampaignRecipientStatus.Failed },
            new CampaignRecipient { IDRun = objSimulada.Id, IDCustomer = objC1.Id, IDUnit = objCenario.Itaituba.Id, Status = CampaignRecipientStatus.Sent });
        objDb.SaveChanges();
        DashboardController objController = new DashboardController(objDb);
        TestHelpers.SetCompanyUser(objController, objDb, objCenario.Company.Id);

        DashboardDto objEmpresa = await PedirAsync(objController);
        DashboardDto objItaituba = await PedirAsync(objController, sUnit: "itaituba");

        Assert.Equal((1, 1, 1), (objEmpresa.Extras.CampaignRuns, objEmpresa.Extras.CampaignSent, objEmpresa.Extras.CampaignFailed));
        Assert.Equal((1, 1, 0), (objItaituba.Extras.CampaignRuns, objItaituba.Extras.CampaignSent, objItaituba.Extras.CampaignFailed));
    }

    // ---------------------------------------------------------------- período

    [Fact]
    public async Task SemDatas_UltimosTrintaDiasAteHoje()
    {
        using AppDbContext objDb = TestHelpers.CreateDbContext();
        Cenario objCenario = Montar(objDb);
        DashboardController objController = new DashboardController(objDb);
        TestHelpers.SetCompanyUser(objController, objDb, objCenario.Company.Id);

        DashboardDto objDash = await PedirAsync(objController, sFrom: null, sTo: null);

        DateOnly dtHoje = CompanyTimeZone.Today(s_objBelem, DateTime.UtcNow);
        Assert.Equal(dtHoje, objDash.Period.To);
        Assert.Equal(dtHoje.AddDays(-29), objDash.Period.From);
        Assert.Equal(30, objDash.Daily.Count);
    }

    [Theory]
    [InlineData("2026-10-10", "2026-10-01", "depois da final")]
    [InlineData("10/01/2026", "2026-10-10", "Data inicial inválida")]
    [InlineData("2026-10-01", "ontem", "Data final inválida")]
    [InlineData("2024-01-01", "2026-10-10", "até 2 anos")]
    public async Task PeriodoInvalido_400(string sFrom, string sTo, string sTrecho)
    {
        using AppDbContext objDb = TestHelpers.CreateDbContext();
        Cenario objCenario = Montar(objDb);
        DashboardController objController = new DashboardController(objDb);
        TestHelpers.SetCompanyUser(objController, objDb, objCenario.Company.Id);

        ActionResult<DashboardDto> objResult = await objController.Get(null, null, sFrom, sTo, CancellationToken.None);

        ErrorResponse objErro = Assert.IsType<ErrorResponse>(Assert.IsType<BadRequestObjectResult>(objResult.Result).Value);
        Assert.Contains(sTrecho, objErro.Error);
    }

    [Fact]
    public async Task SemNenhumaConexao_ZerosSemQuebrar()
    {
        using AppDbContext objDb = TestHelpers.CreateDbContext();
        Company objCompany = new Company { Name = "Nova", Slug = "nova" };
        objDb.Companies.Add(objCompany);
        objDb.Units.Add(new Unit { IDCompany = objCompany.Id, Name = "Loja", Slug = "loja" });
        objDb.SaveChanges();
        DashboardController objController = new DashboardController(objDb);
        TestHelpers.SetCompanyUser(objController, objDb, objCompany.Id);

        DashboardDto objDash = await PedirAsync(objController);

        Assert.Null(objDash.VisitsSince);
        Assert.Equal(0, objDash.Kpis.Connections.Current);
        Assert.Null(objDash.Kpis.ReturnRate.Current);
        Assert.All(objDash.AgeBands, band => Assert.Equal(0, band.Count));
        Assert.Single(objDash.Units);
    }
}
