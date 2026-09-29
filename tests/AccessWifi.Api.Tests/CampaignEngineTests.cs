using AccessWifiService.Campaigns;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Models.Campaigns;
using Models.DataBase;
using Models.Persistence;

namespace AccessWifi.Api.Tests;

/// <summary>Agenda, seleção e execução das campanhas (o motor que roda no AccessWifiService).</summary>
public class CampaignEngineTests
{
    // 29/09/2026 às 09:00 em Belém = 12:00 UTC.
    private static readonly DateTime s_dtNove = new DateTime(2026, 9, 29, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly s_dtHoje = new DateOnly(2026, 9, 29);

    private sealed class Cenario
    {
        public required AppDbContext Db { get; init; }
        public required Company Company { get; init; }
        public required Unit Unit { get; init; }
    }

    private static Cenario CreateScenario(AppDbContext objDbContext, params string[] arrKinds)
    {
        Company objCompany = new Company { Name = "Lojas Regional", Slug = "regional", TimeZone = "America/Belem" };
        Unit objUnit = new Unit { IDCompany = objCompany.Id, Name = "Itaituba", Slug = "itaituba" };
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
        int iVisits = 1, string sInstagram = "")
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
            IDLastUnit = objCenario.Unit.Id,
        };
        objCenario.Db.Customers.Add(objCustomer);
        objCenario.Db.CustomerUnits.Add(new CustomerUnit
        {
            IDCustomer = objCustomer.Id, IDUnit = objCenario.Unit.Id, FirstVisitAt = dtLast, LastVisitAt = dtLast,
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

    private static CampaignEngine CreateEngine(AppDbContext objDbContext, int iPorMinuto = 60_000, int iBloco = 2)
    {
        return new CampaignEngine(
            objDbContext,
            new SimulatedMessageChannel(),
            Options.Create(new CampaignEngineOptions
            {
                MessagesPerMinute = iPorMinuto, TickSeconds = 5, SelectionChunkSize = iBloco,
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
        CampaignEngine objEngine = CreateEngine(objDbContext);

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
        CampaignEngine objEngine = CreateEngine(objDbContext);
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
        await CreateEngine(objDbContext).ScheduleDueAsync(s_dtNove.AddHours(1));

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

        await CreateEngine(objDbContext).ScheduleDueAsync(s_dtNove.AddSeconds(3));

        Assert.Empty(objDbContext.CampaignRuns);
        Assert.Null(objDbContext.Campaigns.Single(campaign => campaign.Id == objCampaign.Id).NextRunAt);
    }

    // ---------------------------------------------------------- Quem recebe

    [Fact]
    public async Task Aniversario_SoOsAniversariantesDoDia_ComAMensagemMontada()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Cenario objCenario = CreateScenario(objDbContext);
        AddCustomer(objCenario, "91900000001", "Ana Beatriz", dtBirth: new DateOnly(1998, 9, 29));
        AddCustomer(objCenario, "91900000002", "Bruno", dtBirth: new DateOnly(1990, 9, 30));
        AddCustomer(objCenario, "91900000003", "Carla"); // sem nascimento válido
        Campaign objCampaign = AddCampaign(objCenario, CampaignKind.Birthday,
            Mensagem("Parabéns, {primeiro_nome}! {idade} anos com a {empresa} ({unidade})."));

        await CreateEngine(objDbContext).TickAsync(s_dtNove.AddSeconds(3));

        CampaignRecipient objRecipient = Assert.Single(RecipientsOf(objDbContext, objCampaign.Id));
        Assert.Equal("91900000001", objRecipient.Phone);
        Assert.Equal("Parabéns, Ana! 28 anos com a Lojas Regional (Itaituba).", objRecipient.Message);
        Assert.Equal(CampaignRecipientStatus.Simulated, objRecipient.Status);
        CampaignRun objRun = objDbContext.CampaignRuns.Single();
        Assert.Equal(CampaignRunStatus.Completed, objRun.Status);
        Assert.Equal(1, objRun.SimulatedCount);
    }

    [Fact]
    public async Task Aniversario_NascidoEm29DeFevereiro_RecebeEm28EmAnoNaoBissexto()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Cenario objCenario = CreateScenario(objDbContext);
        AddCustomer(objCenario, "91900000001", "Leap", dtBirth: new DateOnly(2000, 2, 29));
        DateTime dtNove28 = new DateTime(2027, 2, 28, 12, 0, 0, DateTimeKind.Utc);
        Campaign objCampaign = AddCampaign(objCenario, CampaignKind.Birthday, Mensagem(), dtNextRun: dtNove28);

        await CreateEngine(objDbContext).TickAsync(dtNove28.AddSeconds(3));

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

        await CreateEngine(objDbContext).TickAsync(s_dtNove.AddSeconds(3));

        CampaignRecipient objRecipient = Assert.Single(RecipientsOf(objDbContext, objCampaign.Id));
        Assert.Equal("1 ano(s) conosco", objRecipient.Message);
    }

    [Fact]
    public async Task BoasVindas_QuemSeCadastrouOntem()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Cenario objCenario = CreateScenario(objDbContext);
        AddCustomer(objCenario, "91900000001", "Ontem", dtFirstVisit: s_dtHoje.AddDays(-1));
        AddCustomer(objCenario, "91900000002", "Antes", dtFirstVisit: s_dtHoje.AddDays(-2));
        Campaign objCampaign = AddCampaign(objCenario, CampaignKind.Welcome, Mensagem());

        await CreateEngine(objDbContext).TickAsync(s_dtNove.AddSeconds(3));

        Assert.Equal("91900000001", Assert.Single(RecipientsOf(objDbContext, objCampaign.Id)).Phone);
    }

    [Fact]
    public async Task SentimosSuaFalta_UmaVezPorAusencia()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Cenario objCenario = CreateScenario(objDbContext);
        Customer objSumido = AddCustomer(objCenario, "91900000001", "Sumido", dtLastVisit: s_dtNove.AddDays(-40));
        AddCustomer(objCenario, "91900000002", "Frequente", dtLastVisit: s_dtNove.AddDays(-3));
        Campaign objCampaign = AddCampaign(objCenario, CampaignKind.WeMissYou, Mensagem() with { AbsenceDays = 30 });
        CampaignEngine objEngine = CreateEngine(objDbContext);

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
        CampaignEngine objEngine = CreateEngine(objDbContext);

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
        CampaignEngine objEngine = CreateEngine(objDbContext);

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
        CampaignEngine objEngine = CreateEngine(objDbContext);

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

        await CreateEngine(objDbContext).TickAsync(s_dtNove.AddSeconds(3));

        Assert.Equal(CampaignRecipientStatus.Simulated, Assert.Single(RecipientsOf(objDbContext, objAniversario.Id)).Status);
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
        CampaignEngine objEngine = CreateEngine(objDbContext);

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
    public async Task Pausar_ParaNoLoteERetomar_ContinuaDeOndeParouSemRepetir()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Cenario objCenario = CreateScenario(objDbContext);
        CampaignEngine objEngine = CreateEngine(objDbContext);
        Guid objRunId = await RunComCincoPendentesAsync(objCenario, objEngine);

        Assert.Equal(2, await objEngine.ProcessRunAsync(objRunId, 2));
        objDbContext.CampaignRuns.Single(run => run.Id == objRunId).Status = CampaignRunStatus.Paused;
        objDbContext.SaveChanges();
        Assert.Equal(0, await objEngine.ProcessRunAsync(objRunId, 2)); // pausada: não anda

        objDbContext.CampaignRuns.Single(run => run.Id == objRunId).Status = CampaignRunStatus.Running;
        objDbContext.SaveChanges();
        Assert.Equal(2, await objEngine.ProcessRunAsync(objRunId, 2));
        Assert.Equal(1, await objEngine.ProcessRunAsync(objRunId, 2));

        CampaignRun objRun = objDbContext.CampaignRuns.Single(run => run.Id == objRunId);
        Assert.Equal(CampaignRunStatus.Completed, objRun.Status);
        Assert.Equal(5, objRun.SimulatedCount);
        Assert.All(objDbContext.CampaignRecipients, recipient => Assert.Equal(CampaignRecipientStatus.Simulated, recipient.Status));
    }

    [Fact]
    public async Task Cancelar_PendentesViramCancelados()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Cenario objCenario = CreateScenario(objDbContext);
        CampaignEngine objEngine = CreateEngine(objDbContext, iPorMinuto: 12); // 1 por volta
        Guid objRunId = await RunComCincoPendentesAsync(objCenario, objEngine);
        await objEngine.ProcessRunAsync(objRunId, 1);

        objDbContext.CampaignRuns.Single(run => run.Id == objRunId).Status = CampaignRunStatus.Cancelled;
        objDbContext.SaveChanges();
        await objEngine.TickAsync(s_dtNove.AddSeconds(10));

        CampaignRun objRun = objDbContext.CampaignRuns.Single(run => run.Id == objRunId);
        Assert.Equal(1, objRun.SimulatedCount);
        Assert.Equal(4, objRun.CancelledCount);
        Assert.DoesNotContain(objDbContext.CampaignRecipients, recipient => recipient.Status == CampaignRecipientStatus.Pending);
    }

    [Fact]
    public async Task Selecao_InterrompidaNoMeio_RetomaSemRepetirNinguem()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Cenario objCenario = CreateScenario(objDbContext);
        CampaignEngine objEngine = CreateEngine(objDbContext);
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
        CampaignEngine objEngine = CreateEngine(objDbContext);
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
