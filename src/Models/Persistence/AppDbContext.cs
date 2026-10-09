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
        public DbSet<Unit> Units => Set<Unit>();
        public DbSet<AdminUser> Users => Set<AdminUser>();
        public DbSet<AdminUserUnit> UserUnits => Set<AdminUserUnit>();
        public DbSet<UnitDevice> UnitDevices => Set<UnitDevice>();
        public DbSet<Visit> Visits => Set<Visit>();
        public DbSet<Lead> Leads => Set<Lead>();
        public DbSet<PortalSettings> PortalSettings => Set<PortalSettings>();
        public DbSet<Configuration> Configurations => Set<Configuration>();
        public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

        // ------------------------------------------------------------ Campanhas
        public DbSet<Customer> Customers => Set<Customer>();
        public DbSet<CustomerUnit> CustomerUnits => Set<CustomerUnit>();
        public DbSet<CompanyCampaignKind> CompanyCampaignKinds => Set<CompanyCampaignKind>();
        public DbSet<Campaign> Campaigns => Set<Campaign>();
        public DbSet<CampaignVersion> CampaignVersions => Set<CampaignVersion>();
        public DbSet<CampaignRun> CampaignRuns => Set<CampaignRun>();
        public DbSet<CampaignRecipient> CampaignRecipients => Set<CampaignRecipient>();
        public DbSet<CampaignDelivery> CampaignDeliveries => Set<CampaignDelivery>();
        public DbSet<CampaignEvent> CampaignEvents => Set<CampaignEvent>();

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
                objCompany.Property(company => company.TimeZone).HasMaxLength(60)
                    .HasDefaultValue(CompanyTimeZone.Default);
                objCompany.HasIndex(company => company.Slug).IsUnique();
            });

            objModelBuilder.Entity<Unit>(objUnit =>
            {
                objUnit.Property(unit => unit.Name).HasMaxLength(120);
                objUnit.Property(unit => unit.Slug).HasMaxLength(40);
                objUnit.HasIndex(unit => unit.Slug).IsUnique();
                objUnit.Property(unit => unit.PortalHost).HasMaxLength(200);
                // Único só entre as unidades que realmente usam host (o vazio pode repetir).
                objUnit.HasIndex(unit => unit.PortalHost)
                    .IsUnique()
                    .HasFilter("\"PortalHost\" <> ''");
                // Mesmo limite da URL "Geral" da empresa (PortalSettings.RedirectUrl).
                objUnit.Property(unit => unit.RedirectUrl).HasMaxLength(2048);
                objUnit.Property(unit => unit.Email).HasMaxLength(200);
                objUnit.Property(unit => unit.Ddd).HasMaxLength(2);
                objUnit.Property(unit => unit.DevicesSyncError).HasMaxLength(300);
                objUnit.HasOne<Company>()
                    .WithMany()
                    .HasForeignKey(unit => unit.IDCompany)
                    .OnDelete(DeleteBehavior.Restrict);
                objUnit.OwnsOne(unit => unit.Unifi, objUnifi =>
                {
                    objUnifi.Property(unifi => unifi.Mode).HasMaxLength(10);
                    objUnifi.Property(unifi => unifi.Host).HasMaxLength(200);
                    objUnifi.Property(unifi => unifi.Site).HasMaxLength(60);
                    objUnifi.Property(unifi => unifi.Username).HasMaxLength(100);
                    // Guardada cifrada (AES-GCM, base64) — maior que o texto puro.
                    objUnifi.Property(unifi => unifi.Password).HasMaxLength(512);
                    objUnifi.Property(unifi => unifi.ConsoleId).HasMaxLength(120);
                    // Cifrada, mesmo tratamento da senha.
                    objUnifi.Property(unifi => unifi.ApiKey).HasMaxLength(512);
                    objUnifi.Property(unifi => unifi.SiteId).HasMaxLength(60);
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

            objModelBuilder.Entity<UnitDevice>(objDevice =>
            {
                objDevice.HasKey(device => new { device.IDUnit, device.Mac });
                objDevice.Property(device => device.Mac).HasMaxLength(17);
                objDevice.Property(device => device.Name).HasMaxLength(120);
                objDevice.Property(device => device.Model).HasMaxLength(60);
                // A busca do portal é pelo MAC do ponto de acesso.
                objDevice.HasIndex(device => device.Mac);
                objDevice.HasOne<Unit>()
                    .WithMany()
                    .HasForeignKey(device => device.IDUnit)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            objModelBuilder.Entity<Visit>(objVisit =>
            {
                objVisit.Property(visit => visit.Ap).HasMaxLength(17);
                // O dashboard filtra por unidade e período (dia no fuso da empresa).
                objVisit.HasIndex(visit => new { visit.IDUnit, visit.LocalDate });
                objVisit.HasIndex(visit => visit.IDCustomer);
                objVisit.HasOne<Unit>()
                    .WithMany()
                    .HasForeignKey(visit => visit.IDUnit)
                    .OnDelete(DeleteBehavior.Cascade);
                // D8: a retenção apaga o cliente, a conexão fica (sem o vínculo) e os totais não mudam.
                objVisit.HasOne<Customer>()
                    .WithMany()
                    .HasForeignKey(visit => visit.IDCustomer)
                    .OnDelete(DeleteBehavior.SetNull);
            });

            objModelBuilder.Entity<AdminUserUnit>(objUserUnit =>
            {
                objUserUnit.ToTable("UserUnits");
                objUserUnit.HasKey(link => new { link.IDUser, link.IDUnit });
                objUserUnit.HasIndex(link => link.IDUnit);
                objUserUnit.HasOne<AdminUser>()
                    .WithMany(user => user.Units)
                    .HasForeignKey(link => link.IDUser)
                    .OnDelete(DeleteBehavior.Cascade);
                objUserUnit.HasOne<Unit>()
                    .WithMany()
                    .HasForeignKey(link => link.IDUnit)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            objModelBuilder.Entity<RefreshToken>(objRefreshToken =>
            {
                objRefreshToken.Property(token => token.TokenHash).HasMaxLength(64);
                objRefreshToken.HasIndex(token => token.TokenHash).IsUnique();
                objRefreshToken.HasIndex(token => token.IDUser);
                objRefreshToken.HasOne<AdminUser>()
                    .WithMany()
                    .HasForeignKey(token => token.IDUser)
                    .OnDelete(DeleteBehavior.Cascade);
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
                objLead.HasIndex(lead => new { lead.IDUnit, lead.Timestamp });
                // Um cadastro por aparelho em cada unidade: o /authorize faz upsert por esta chave.
                objLead.HasIndex(lead => new { lead.IDUnit, lead.Mac }).IsUnique();
                objLead.HasOne<Unit>()
                    .WithMany()
                    .HasForeignKey(lead => lead.IDUnit)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            objModelBuilder.Entity<PortalSettings>(objSettings =>
            {
                objSettings.Property(settings => settings.Ssid).HasMaxLength(32);
                objSettings.Property(settings => settings.RedirectUrl).HasMaxLength(2048);
                objSettings.Property(settings => settings.Ddd).HasMaxLength(2);
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

            ConfigureCampaigns(objModelBuilder);
        }

        private static void ConfigureCampaigns(ModelBuilder objModelBuilder)
        {
            objModelBuilder.Entity<Customer>(objCustomer =>
            {
                objCustomer.Property(customer => customer.Phone).HasMaxLength(20);
                objCustomer.Property(customer => customer.Name).HasMaxLength(200);
                objCustomer.Property(customer => customer.Instagram).HasMaxLength(100);
                // D1: um cliente por telefone dentro da empresa.
                objCustomer.HasIndex(customer => new { customer.IDCompany, customer.Phone }).IsUnique();
                objCustomer.HasIndex(customer => new { customer.IDCompany, customer.LastVisitAt });
                objCustomer.HasIndex(customer => new { customer.IDCompany, customer.FirstVisitDate });
                objCustomer.HasOne<Company>()
                    .WithMany()
                    .HasForeignKey(customer => customer.IDCompany)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            objModelBuilder.Entity<CustomerUnit>(objCustomerUnit =>
            {
                objCustomerUnit.HasKey(link => new { link.IDCustomer, link.IDUnit });
                objCustomerUnit.HasIndex(link => link.IDUnit);
                objCustomerUnit.HasOne<Customer>()
                    .WithMany()
                    .HasForeignKey(link => link.IDCustomer)
                    .OnDelete(DeleteBehavior.Cascade);
                objCustomerUnit.HasOne<Unit>()
                    .WithMany()
                    .HasForeignKey(link => link.IDUnit)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            objModelBuilder.Entity<CompanyCampaignKind>(objKind =>
            {
                objKind.HasKey(kind => new { kind.IDCompany, kind.Kind });
                objKind.Property(kind => kind.Kind).HasMaxLength(30);
                objKind.Property(kind => kind.EnabledBy).HasMaxLength(60);
                objKind.HasOne<Company>()
                    .WithMany()
                    .HasForeignKey(kind => kind.IDCompany)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            objModelBuilder.Entity<Campaign>(objCampaign =>
            {
                objCampaign.Property(campaign => campaign.Kind).HasMaxLength(30);
                objCampaign.Property(campaign => campaign.Name).HasMaxLength(120);
                objCampaign.Property(campaign => campaign.Status).HasMaxLength(20);
                objCampaign.Property(campaign => campaign.ConfigJson).HasColumnType("jsonb");
                objCampaign.HasIndex(campaign => campaign.IDCompany);
                // O agendador procura por aqui: "ativas com disparo vencido".
                objCampaign.HasIndex(campaign => new { campaign.Status, campaign.NextRunAt });
                objCampaign.HasOne<Company>()
                    .WithMany()
                    .HasForeignKey(campaign => campaign.IDCompany)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            objModelBuilder.Entity<CampaignVersion>(objVersion =>
            {
                objVersion.Property(version => version.Name).HasMaxLength(120);
                objVersion.Property(version => version.ConfigJson).HasColumnType("jsonb");
                objVersion.Property(version => version.Changes).HasMaxLength(2000);
                objVersion.Property(version => version.Username).HasMaxLength(60);
                objVersion.HasIndex(version => new { version.IDCampaign, version.Number }).IsUnique();
                objVersion.HasOne<Campaign>()
                    .WithMany()
                    .HasForeignKey(version => version.IDCampaign)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            objModelBuilder.Entity<CampaignRun>(objRun =>
            {
                objRun.Property(run => run.Status).HasMaxLength(20);
                objRun.Property(run => run.Error).HasMaxLength(1000);
                // Uma execução por campanha por dia (D1): nem reinício do serviço nem troca de horário
                // no mesmo dia duplicam o disparo.
                objRun.HasIndex(run => new { run.IDCampaign, run.LocalDate }).IsUnique();
                objRun.HasIndex(run => run.Status);
                objRun.HasIndex(run => new { run.IDCompany, run.LocalDate });
                objRun.HasOne<Campaign>()
                    .WithMany()
                    .HasForeignKey(run => run.IDCampaign)
                    .OnDelete(DeleteBehavior.Cascade);
                // NoAction (conferido no fim do comando): apagar a campanha leva versões e execuções
                // juntas sem depender da ordem da cascata.
                objRun.HasOne<CampaignVersion>()
                    .WithMany()
                    .HasForeignKey(run => run.IDCampaignVersion)
                    .OnDelete(DeleteBehavior.NoAction);
            });

            objModelBuilder.Entity<CampaignRecipient>(objRecipient =>
            {
                objRecipient.Property(recipient => recipient.Phone).HasMaxLength(20);
                objRecipient.Property(recipient => recipient.Name).HasMaxLength(200);
                objRecipient.Property(recipient => recipient.Message).HasMaxLength(4000);
                objRecipient.Property(recipient => recipient.Instagram).HasMaxLength(100);
                objRecipient.Property(recipient => recipient.Info).HasMaxLength(120);
                objRecipient.Property(recipient => recipient.Status).HasMaxLength(20);
                objRecipient.Property(recipient => recipient.Reason).HasMaxLength(300);
                // D1: um destinatário por cliente (= por telefone) em cada execução.
                objRecipient.HasIndex(recipient => new { recipient.IDRun, recipient.IDCustomer }).IsUnique();
                // Os lotes: "pendentes desta execução, em ordem".
                objRecipient.HasIndex(recipient => new { recipient.IDRun, recipient.Status, recipient.Id });
                objRecipient.HasIndex(recipient => recipient.IDCustomer);
                objRecipient.HasOne<CampaignRun>()
                    .WithMany()
                    .HasForeignKey(recipient => recipient.IDRun)
                    .OnDelete(DeleteBehavior.Cascade);
                // Cliente apagado pela retenção (D4) leva junto as mensagens dele; os números da
                // execução continuam no resumo.
                objRecipient.HasOne<Customer>()
                    .WithMany()
                    .HasForeignKey(recipient => recipient.IDCustomer)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            objModelBuilder.Entity<CampaignDelivery>(objDelivery =>
            {
                objDelivery.Property(delivery => delivery.UnitName).HasMaxLength(120);
                objDelivery.Property(delivery => delivery.Email).HasMaxLength(200);
                objDelivery.Property(delivery => delivery.Status).HasMaxLength(20);
                objDelivery.Property(delivery => delivery.Error).HasMaxLength(500);
                objDelivery.Property(delivery => delivery.FileName).HasMaxLength(200);
                // Uma entrega por unidade em cada execução.
                objDelivery.HasIndex(delivery => new { delivery.IDRun, delivery.IDUnit }).IsUnique();
                objDelivery.HasOne<CampaignRun>()
                    .WithMany()
                    .HasForeignKey(delivery => delivery.IDRun)
                    .OnDelete(DeleteBehavior.Cascade);
                // Unidade apagada: o histórico do envio fica, com o nome e o e-mail gravados.
                objDelivery.HasOne<Unit>()
                    .WithMany()
                    .HasForeignKey(delivery => delivery.IDUnit)
                    .OnDelete(DeleteBehavior.SetNull);
            });

            objModelBuilder.Entity<CampaignEvent>(objEvent =>
            {
                objEvent.Property(evt => evt.Action).HasMaxLength(40);
                objEvent.Property(evt => evt.Username).HasMaxLength(60);
                objEvent.HasIndex(evt => new { evt.IDCampaign, evt.CreatedAt });
                objEvent.HasOne<Campaign>()
                    .WithMany()
                    .HasForeignKey(evt => evt.IDCampaign)
                    .OnDelete(DeleteBehavior.Cascade);
            });
        }
    }
}
