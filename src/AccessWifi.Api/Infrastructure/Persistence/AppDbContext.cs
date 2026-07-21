using AccessWifi.Api.Features.Leads;
using AccessWifi.Api.Features.Settings;
using Microsoft.EntityFrameworkCore;

namespace AccessWifi.Api.Infrastructure.Persistence;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> objOptions) : base(objOptions) { }

    public DbSet<Lead> Leads => Set<Lead>();
    public DbSet<PortalSettings> PortalSettings => Set<PortalSettings>();

    protected override void OnModelCreating(ModelBuilder objModelBuilder)
    {
        objModelBuilder.Entity<Lead>(objLead =>
        {
            objLead.Property(lead => lead.Nome).HasMaxLength(200);
            objLead.Property(lead => lead.Instagram).HasMaxLength(100);
            objLead.Property(lead => lead.Telefone).HasMaxLength(20);
            objLead.Property(lead => lead.Nascimento).HasMaxLength(10);
            objLead.Property(lead => lead.Mac).HasMaxLength(17);
            objLead.Property(lead => lead.Ap).HasMaxLength(17);
            objLead.Property(lead => lead.Ssid).HasMaxLength(32);
            objLead.HasIndex(lead => lead.Timestamp);
        });

        objModelBuilder.Entity<PortalSettings>(objSettings =>
        {
            // Linha única: Id fixo, sem geração automática.
            objSettings.Property(settings => settings.Id).ValueGeneratedNever();
            objSettings.Property(settings => settings.Ssid).HasMaxLength(32);
            objSettings.OwnsOne(settings => settings.Colors, objColors =>
            {
                objColors.Property(colors => colors.Brand).HasMaxLength(7);
                objColors.Property(colors => colors.BrandDark).HasMaxLength(7);
                objColors.Property(colors => colors.Surface).HasMaxLength(7);
                objColors.Property(colors => colors.Card).HasMaxLength(7);
                objColors.Property(colors => colors.Field).HasMaxLength(7);
                objColors.Property(colors => colors.Ink).HasMaxLength(7);
                objColors.Property(colors => colors.Muted).HasMaxLength(7);
                objColors.Property(colors => colors.Line).HasMaxLength(7);
            });
        });
    }
}
