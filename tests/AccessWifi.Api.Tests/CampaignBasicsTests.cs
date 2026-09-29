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
    public async Task RegisterVisit_PrimeiraConexao_CriaClientePeloTelefoneComNascimentoEmData()
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
    public async Task RegisterVisit_MesmoDiaContaUmaVisita_DiaSeguinteContaOutra()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Guid objCompanyId = Guid.NewGuid(), objUnitId = Guid.NewGuid();
        DateTime dtDia1 = new DateTime(2026, 9, 29, 13, 0, 0, DateTimeKind.Utc);

        foreach (DateTime dtConexao in new[] { dtDia1, dtDia1.AddHours(3), dtDia1.AddDays(1) })
        {
            await CustomerDirectory.RegisterVisitAsync(
                objDbContext, objCompanyId, s_objBelem, objUnitId, "Ana", "", "91988881234", "", dtConexao);
            await objDbContext.SaveChangesAsync();
        }

        Customer objCustomer = Assert.Single(objDbContext.Customers);
        Assert.Equal(2, objCustomer.VisitCount);
        Assert.Equal(dtDia1.AddDays(1), objCustomer.LastVisitAt);
    }

    [Fact]
    public async Task RegisterVisit_MesmoTelefoneEmOutraLoja_EOMesmoCliente()
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
    public void ParseBirthDate_DataInvalida_FicaNula(string sBirth)
    {
        Assert.Null(CustomerDirectory.ParseBirthDate(sBirth, new DateOnly(2026, 9, 29)));
    }

    [Fact]
    public async Task RegisterVisit_TelefoneCurto_NaoCriaCliente()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();

        Customer? objCustomer = await CustomerDirectory.RegisterVisitAsync(
            objDbContext, Guid.NewGuid(), s_objBelem, Guid.NewGuid(), "Ana", "", "98888", "", DateTime.UtcNow);

        Assert.Null(objCustomer);
    }

    [Fact]
    public async Task Backfill_JuntaOsCadastrosDoMesmoTelefoneEmUmCliente()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Company objCompany = new Company { Name = "Regional", Slug = "regional" };
        Unit objLoja1 = new Unit { IDCompany = objCompany.Id, Name = "Loja 1", Slug = "l1" };
        Unit objLoja2 = new Unit { IDCompany = objCompany.Id, Name = "Loja 2", Slug = "l2" };
        objDbContext.AddRange(objCompany, objLoja1, objLoja2);
        DateTime dtBase = new DateTime(2026, 9, 21, 12, 0, 0, DateTimeKind.Utc);
        objDbContext.Leads.AddRange(
            new Lead { IDUnit = objLoja1.Id, Nome = "Ana", Telefone = "(91) 98888-1234", Nascimento = "12/03/1998", Mac = "a", CreatedAt = dtBase, Timestamp = dtBase },
            new Lead { IDUnit = objLoja2.Id, Nome = "Ana B.", Telefone = "91988881234", Nascimento = "", Mac = "b", CreatedAt = dtBase.AddDays(2), Timestamp = dtBase.AddDays(3) },
            new Lead { IDUnit = objLoja1.Id, Nome = "Bruno", Telefone = "(91) 97777-0000", Nascimento = "31/02/1990", Mac = "c", CreatedAt = dtBase, Timestamp = dtBase });
        objDbContext.SaveChanges();

        int iCriados = await CustomerDirectory.BackfillIfEmptyAsync(objDbContext, dtBase.AddDays(5));

        Assert.Equal(2, iCriados);
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

    private static CampaignConfig Filtrada(string sRecurrence, DateOnly dtStart, DateOnly? dtEnd = null, int[]? arrDays = null) =>
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
    public void Calendario_DeSistema_TodoDiaNoHorarioDaEmpresa()
    {
        CampaignConfig objConfig = new CampaignConfig { Message = "Oi", SendTime = "09:00" };

        // 07:00 em Belém (10:00 UTC): ainda hoje, às 09:00 de Belém = 12:00 UTC.
        DateTime? dtHoje = CampaignCalendar.NextOccurrence(
            CampaignKind.Birthday, objConfig, s_objBelem, new DateTime(2026, 9, 29, 10, 0, 0, DateTimeKind.Utc));
        // Exatamente no horário: o próximo é amanhã.
        DateTime? dtAmanha = CampaignCalendar.NextOccurrence(
            CampaignKind.Birthday, objConfig, s_objBelem, new DateTime(2026, 9, 29, 12, 0, 0, DateTimeKind.Utc));

        Assert.Equal(new DateTime(2026, 9, 29, 12, 0, 0, DateTimeKind.Utc), dtHoje);
        Assert.Equal(new DateTime(2026, 9, 30, 12, 0, 0, DateTimeKind.Utc), dtAmanha);
    }

    [Fact]
    public void Calendario_UmaVez_DisparaNaDataEDepoisAcaba()
    {
        CampaignConfig objConfig = Filtrada(CampaignRecurrence.Once, new DateOnly(2026, 10, 5));
        DateTime dtAgora = new DateTime(2026, 9, 29, 12, 0, 0, DateTimeKind.Utc);

        DateTime? dtDisparo = CampaignCalendar.NextOccurrence(CampaignKind.Filtered, objConfig, s_objBelem, dtAgora);

        Assert.Equal(new DateTime(2026, 10, 5, 12, 0, 0, DateTimeKind.Utc), dtDisparo);
        Assert.Null(CampaignCalendar.NextOccurrence(CampaignKind.Filtered, objConfig, s_objBelem, dtDisparo!.Value));
    }

    [Fact]
    public void Calendario_Semanal_SoNosDiasEscolhidos()
    {
        // Segunda (1) e sexta (5). 29/09/2026 é terça.
        CampaignConfig objConfig = Filtrada(CampaignRecurrence.Weekly, new DateOnly(2026, 9, 29), arrDays: [1, 5]);

        DateTime? dtProximo = CampaignCalendar.NextOccurrence(
            CampaignKind.Filtered, objConfig, s_objBelem, new DateTime(2026, 9, 29, 13, 0, 0, DateTimeKind.Utc));

        Assert.Equal(new DateTime(2026, 10, 2, 12, 0, 0, DateTimeKind.Utc), dtProximo); // sexta, 02/10
    }

    [Fact]
    public void Calendario_MensalNoDia31_EmMesCurtoVaiParaOUltimoDia()
    {
        CampaignConfig objConfig = Filtrada(CampaignRecurrence.Monthly, new DateOnly(2027, 1, 31));

        DateTime? dtProximo = CampaignCalendar.NextOccurrence(
            CampaignKind.Filtered, objConfig, s_objBelem, new DateTime(2027, 1, 31, 13, 0, 0, DateTimeKind.Utc));

        Assert.Equal(new DateTime(2027, 2, 28, 12, 0, 0, DateTimeKind.Utc), dtProximo);
    }

    [Fact]
    public void Calendario_Anual29DeFevereiro_EmAnoNaoBissextoVaiPara28()
    {
        CampaignConfig objConfig = Filtrada(CampaignRecurrence.Yearly, new DateOnly(2028, 2, 29));

        DateTime? dtProximo = CampaignCalendar.NextOccurrence(
            CampaignKind.Filtered, objConfig, s_objBelem, new DateTime(2028, 3, 1, 0, 0, 0, DateTimeKind.Utc));

        Assert.Equal(new DateTime(2029, 2, 28, 12, 0, 0, DateTimeKind.Utc), dtProximo);
    }

    [Fact]
    public void Calendario_DepoisDaDataDeFim_NaoTemMaisDisparo()
    {
        CampaignConfig objConfig = Filtrada(
            CampaignRecurrence.Daily, new DateOnly(2026, 10, 1), dtEnd: new DateOnly(2026, 10, 2));

        Assert.Null(CampaignCalendar.NextOccurrence(
            CampaignKind.Filtered, objConfig, s_objBelem, new DateTime(2026, 10, 2, 12, 0, 0, DateTimeKind.Utc)));
    }

    // ---------------------------------------------------------------- Mensagem

    [Fact]
    public void Mensagem_PreencheOsCampos()
    {
        string sMensagem = CampaignMessage.Render(
            "Oi, {primeiro_nome}! A {empresa} ({unidade}) deseja parabéns pelos {idade} anos — {nome}.",
            new CampaignMessageData("Ana Beatriz Souza", "Lojas Regional", "Itaituba", 28, 1));

        Assert.Equal(
            "Oi, Ana! A Lojas Regional (Itaituba) deseja parabéns pelos 28 anos — Ana Beatriz Souza.", sMensagem);
    }

    [Fact]
    public void Idade_NascidoEm29DeFevereiro_FazAniversarioEm28EmAnoNaoBissexto()
    {
        DateOnly dtNascimento = new DateOnly(2000, 2, 29);

        Assert.Equal(26, CampaignMessage.AgeOn(dtNascimento, new DateOnly(2026, 2, 28)));
        Assert.Equal(25, CampaignMessage.AgeOn(dtNascimento, new DateOnly(2026, 2, 27)));
    }
}
