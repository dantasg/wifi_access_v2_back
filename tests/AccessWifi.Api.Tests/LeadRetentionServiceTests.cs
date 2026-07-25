using AccessWifiService;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Models.DataBase;
using Models.Persistence;

namespace AccessWifi.Api.Tests;

public class LeadRetentionServiceTests
{
    private static LeadRetentionService CreateService(AppDbContext objDbContext, int iMonths)
    {
        IConfiguration objConfig = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Retention:LeadMonths"] = iMonths.ToString(),
            })
            .Build();
        return new LeadRetentionService(objDbContext, objConfig, NullLogger<LeadRetentionService>.Instance);
    }

    private static void AddLead(AppDbContext objDbContext, Guid objUnitId, DateTime dtCreatedAt)
    {
        objDbContext.Leads.Add(new Lead { IDUnit = objUnitId, Nome = "Fulano", CreatedAt = dtCreatedAt });
        objDbContext.SaveChanges();
    }

    [Fact]
    public async Task Purge_RemoveApenasLeadsMaisVelhosQueOPrazo()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Guid objUnitId = Guid.NewGuid();
        DateTime dtNow = new DateTime(2026, 7, 25, 0, 0, 0, DateTimeKind.Utc);

        // 1 ano de retenção: > 12 meses sai, dentro de 12 meses fica.
        AddLead(objDbContext, objUnitId, dtNow.AddMonths(-13)); // expira
        AddLead(objDbContext, objUnitId, dtNow.AddMonths(-6));  // fica
        AddLead(objDbContext, objUnitId, dtNow.AddDays(-1));    // fica

        LeadRetentionService objService = CreateService(objDbContext, iMonths: 12);
        await objService.PurgeExpiredLeadsAsync(dtNow, CancellationToken.None);

        Assert.Equal(2, objDbContext.Leads.Count());
        Assert.DoesNotContain(objDbContext.Leads, lead => lead.CreatedAt < dtNow.AddMonths(-12));
    }

    [Fact]
    public async Task Purge_PrazoZero_NaoApagaNada()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        DateTime dtNow = DateTime.UtcNow;
        AddLead(objDbContext, Guid.NewGuid(), dtNow.AddYears(-5));

        LeadRetentionService objService = CreateService(objDbContext, iMonths: 0);
        await objService.PurgeExpiredLeadsAsync(dtNow, CancellationToken.None);

        Assert.Single(objDbContext.Leads);
    }
}
