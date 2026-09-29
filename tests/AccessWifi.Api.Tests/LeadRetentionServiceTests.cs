using AccessWifiService;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Models.DataBase;
using Models.Persistence;

namespace AccessWifi.Api.Tests;

public class LeadRetentionServiceTests
{
    private static readonly DateTime s_dtNow = new DateTime(2026, 7, 25, 0, 0, 0, DateTimeKind.Utc);

    private static LeadRetentionService CreateService(
        AppDbContext objDbContext, int iMonths = 24, int iRecipientMonths = 12)
    {
        IConfiguration objConfig = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Retention:LeadMonths"] = iMonths.ToString(),
                ["Retention:CampaignRecipientMonths"] = iRecipientMonths.ToString(),
            })
            .Build();
        return new LeadRetentionService(objDbContext, objConfig, NullLogger<LeadRetentionService>.Instance);
    }

    private static void AddLead(AppDbContext objDbContext, DateTime dtCreatedAt, DateTime dtLastVisit)
    {
        objDbContext.Leads.Add(new Lead
        {
            IDUnit = Guid.NewGuid(), Nome = "Fulano", CreatedAt = dtCreatedAt, Timestamp = dtLastVisit,
        });
        objDbContext.SaveChanges();
    }

    [Fact]
    public async Task Purge_ContaDaUltimaVisita_QuemContinuaVoltandoNaoEApagado()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        // Cadastrou há 3 anos, mas voltou mês passado: fica (D4).
        AddLead(objDbContext, s_dtNow.AddYears(-3), s_dtNow.AddMonths(-1));
        // Cadastrou há 3 anos e sumiu há 25 meses: sai.
        AddLead(objDbContext, s_dtNow.AddYears(-3), s_dtNow.AddMonths(-25));
        // 23 meses sem voltar: ainda dentro do prazo.
        AddLead(objDbContext, s_dtNow.AddMonths(-23), s_dtNow.AddMonths(-23));

        await CreateService(objDbContext).PurgeExpiredLeadsAsync(s_dtNow, CancellationToken.None);

        Assert.Equal(2, objDbContext.Leads.Count());
        Assert.DoesNotContain(objDbContext.Leads, lead => lead.Timestamp < s_dtNow.AddMonths(-24));
    }

    [Fact]
    public async Task Purge_ApagaClienteQueFicou24MesesSemVoltar()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Guid objCompanyId = Guid.NewGuid();
        objDbContext.Customers.AddRange(
            new Customer { IDCompany = objCompanyId, Phone = "91988880001", LastVisitAt = s_dtNow.AddMonths(-25) },
            new Customer { IDCompany = objCompanyId, Phone = "91988880002", LastVisitAt = s_dtNow.AddMonths(-2) });
        objDbContext.SaveChanges();

        await CreateService(objDbContext).PurgeExpiredLeadsAsync(s_dtNow, CancellationToken.None);

        Customer objFicou = Assert.Single(objDbContext.Customers);
        Assert.Equal("91988880002", objFicou.Phone);
    }

    [Fact]
    public async Task Purge_ApagaDestinatariosDeExecucoesComMaisDe12MesesEMantemOResumo()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        CampaignRun objVelha = new CampaignRun { IDCampaign = Guid.NewGuid(), CreatedAt = s_dtNow.AddMonths(-13), TotalCount = 1 };
        CampaignRun objNova = new CampaignRun { IDCampaign = Guid.NewGuid(), CreatedAt = s_dtNow.AddMonths(-2), TotalCount = 1 };
        objDbContext.CampaignRuns.AddRange(objVelha, objNova);
        objDbContext.CampaignRecipients.AddRange(
            new CampaignRecipient { IDRun = objVelha.Id, IDCustomer = Guid.NewGuid(), Phone = "1" },
            new CampaignRecipient { IDRun = objNova.Id, IDCustomer = Guid.NewGuid(), Phone = "2" });
        objDbContext.SaveChanges();

        await CreateService(objDbContext).PurgeExpiredLeadsAsync(s_dtNow, CancellationToken.None);

        CampaignRecipient objFicou = Assert.Single(objDbContext.CampaignRecipients);
        Assert.Equal(objNova.Id, objFicou.IDRun);
        // O resumo (a execução, com os números) fica (D16).
        Assert.Equal(2, objDbContext.CampaignRuns.Count());
    }

    [Fact]
    public async Task Purge_PrazoZero_NaoApagaNada()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        AddLead(objDbContext, s_dtNow.AddYears(-5), s_dtNow.AddYears(-5));

        await CreateService(objDbContext, iMonths: 0).PurgeExpiredLeadsAsync(s_dtNow, CancellationToken.None);

        Assert.Single(objDbContext.Leads);
    }
}
