using Microsoft.EntityFrameworkCore;
using Models.DataBase;

namespace Models.Persistence
{
    /// <summary>
    /// Contexto de dados compartilhado entre a API e o AccessWifiService (ambos referenciam
    /// a lib Models para acessar as mesmas tabelas).
    /// </summary>
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> objOptions) : base(objOptions) { }

        public DbSet<Company> Companies => Set<Company>();
        public DbSet<AdminUser> Users => Set<AdminUser>();
        public DbSet<Lead> Leads => Set<Lead>();
        public DbSet<PortalSettings> PortalSettings => Set<PortalSettings>();
        public DbSet<Configuration> Configurations => Set<Configuration>();

        protected override void OnModelCreating(ModelBuilder objModelBuilder)
        {
            objModelBuilder.Entity<Configuration>(objConfiguration =>
            {
                objConfiguration.ToTable("Configuration");
                objConfiguration.HasKey(config => config.IDConfiguration);
                objConfiguration.Property(config => config.IDConfiguration).HasMaxLength(100);
            });

            objModelBuilder.Entity<Company>(objCompany =>
            {
                objCompany.Property(company => company.Name).HasMaxLength(120);
                objCompany.Property(company => company.Slug).HasMaxLength(40);
                objCompany.Property(company => company.ReportEmail).HasMaxLength(200);
                objCompany.HasIndex(company => company.Slug).IsUnique();
                objCompany.OwnsOne(company => company.Unifi, objUnifi =>
                {
                    objUnifi.Property(unifi => unifi.Host).HasMaxLength(200);
                    objUnifi.Property(unifi => unifi.Site).HasMaxLength(60);
                    objUnifi.Property(unifi => unifi.Username).HasMaxLength(100);
                    objUnifi.Property(unifi => unifi.Password).HasMaxLength(200);
                });
            });

            objModelBuilder.Entity<AdminUser>(objUser =>
            {
                objUser.Property(user => user.Username).HasMaxLength(60);
                objUser.HasIndex(user => user.Username).IsUnique();
                objUser.HasOne(user => user.Company)
                    .WithMany()
                    .HasForeignKey(user => user.IDCompany)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            objModelBuilder.Entity<Lead>(objLead =>
            {
                objLead.Property(lead => lead.Nome).HasMaxLength(200);
                objLead.Property(lead => lead.Instagram).HasMaxLength(100);
                objLead.Property(lead => lead.Telefone).HasMaxLength(20);
                objLead.Property(lead => lead.Nascimento).HasMaxLength(10);
                objLead.Property(lead => lead.Mac).HasMaxLength(17);
                objLead.Property(lead => lead.Ap).HasMaxLength(17);
                objLead.Property(lead => lead.Ssid).HasMaxLength(32);
                objLead.HasIndex(lead => new { lead.IDCompany, lead.Timestamp });
                objLead.HasOne<Company>()
                    .WithMany()
                    .HasForeignKey(lead => lead.IDCompany)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            objModelBuilder.Entity<PortalSettings>(objSettings =>
            {
                objSettings.Property(settings => settings.Ssid).HasMaxLength(32);
                objSettings.HasIndex(settings => settings.IDCompany).IsUnique();
                objSettings.HasOne<Company>()
                    .WithMany()
                    .HasForeignKey(settings => settings.IDCompany)
                    .OnDelete(DeleteBehavior.Cascade);
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
}
