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
        public DbSet<SentEmail> SentEmails => Set<SentEmail>();
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

        // ------------------------------------------------------------ Temporária: avisos da UniFi (22/10/2026)
        public DbSet<UnifiWebhookCapture> UnifiWebhookCaptures => Set<UnifiWebhookCapture>();

        // ------------------------------------------------------------ Visitas pela UniFi (PROPOSTA_VISITAS_UNIFI.md)
        public DbSet<WifiPresence> WifiPresences => Set<WifiPresence>();
        public DbSet<UnitIgnoredDevice> UnitIgnoredDevices => Set<UnitIgnoredDevice>();

        // ------------------------------------------------------------ Site institucional (PROPOSTA_SITE.md)
        public DbSet<SiteContact> SiteContacts => Set<SiteContact>();

        // ------------------------------------------------------------ WhatsApp (PROPOSTA_WHATSAPP.md)
        public DbSet<WhatsAppAccountEvent> WhatsAppAccountEvents => Set<WhatsAppAccountEvent>();
        public DbSet<WhatsAppTemplate> WhatsAppTemplates => Set<WhatsAppTemplate>();
        public DbSet<WhatsAppMessage> WhatsAppMessages => Set<WhatsAppMessage>();
        public DbSet<WhatsAppMessageEvent> WhatsAppMessageEvents => Set<WhatsAppMessageEvent>();
        public DbSet<WhatsAppInboundMessage> WhatsAppInboundMessages => Set<WhatsAppInboundMessage>();
        public DbSet<WhatsAppWebhookRequest> WhatsAppWebhookRequests => Set<WhatsAppWebhookRequest>();
        public DbSet<WhatsAppAlert> WhatsAppAlerts => Set<WhatsAppAlert>();

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
                objCompany.Property(company => company.WhatsAppChangedBy).HasMaxLength(60);
                objCompany.OwnsOne(company => company.WhatsApp, objWhatsApp =>
                {
                    objWhatsApp.Property(whatsApp => whatsApp.PhoneNumberId).HasMaxLength(30);
                    objWhatsApp.Property(whatsApp => whatsApp.BusinessAccountId).HasMaxLength(30);
                    // Os três segredos vão cifrados (AES-GCM, base64), maiores que o texto puro.
                    objWhatsApp.Property(whatsApp => whatsApp.AccessToken).HasMaxLength(2048);
                    objWhatsApp.Property(whatsApp => whatsApp.AppSecret).HasMaxLength(512);
                    objWhatsApp.Property(whatsApp => whatsApp.VerifyToken).HasMaxLength(512);
                    objWhatsApp.Property(whatsApp => whatsApp.DisplayPhoneNumber).HasMaxLength(40);
                    objWhatsApp.Property(whatsApp => whatsApp.DisplayName).HasMaxLength(120);
                    objWhatsApp.Property(whatsApp => whatsApp.QualityRating).HasMaxLength(20);
                    objWhatsApp.Property(whatsApp => whatsApp.MessagingLimit).HasMaxLength(30);
                    objWhatsApp.Property(whatsApp => whatsApp.LastError).HasMaxLength(500);
                });
            });

            objModelBuilder.Entity<WhatsAppAccountEvent>(objEvent =>
            {
                objEvent.Property(evt => evt.Kind).HasMaxLength(30);
                objEvent.Property(evt => evt.Username).HasMaxLength(60);
                objEvent.Property(evt => evt.Details).HasMaxLength(1000);
                objEvent.Property(evt => evt.RequestJson).HasColumnType("jsonb");
                objEvent.Property(evt => evt.ResponseJson).HasColumnType("jsonb");
                // A tela mostra o histórico da empresa, do mais novo para o mais antigo.
                objEvent.HasIndex(evt => new { evt.IDCompany, evt.CreatedAt });
                objEvent.HasOne<Company>()
                    .WithMany()
                    .HasForeignKey(evt => evt.IDCompany)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            objModelBuilder.Entity<WhatsAppTemplate>(objTemplate =>
            {
                objTemplate.Property(template => template.Name).HasMaxLength(512);
                objTemplate.Property(template => template.Language).HasMaxLength(10);
                objTemplate.Property(template => template.Category).HasMaxLength(30);
                objTemplate.Property(template => template.BodyText).HasMaxLength(4000);
                objTemplate.Property(template => template.FooterText).HasMaxLength(60);
                objTemplate.Property(template => template.ParametersJson).HasColumnType("jsonb");
                objTemplate.Property(template => template.Status).HasMaxLength(20);
                objTemplate.Property(template => template.MetaStatus).HasMaxLength(30);
                objTemplate.Property(template => template.MetaTemplateId).HasMaxLength(30);
                objTemplate.Property(template => template.Reason).HasMaxLength(1000);
                objTemplate.Property(template => template.LastError).HasMaxLength(1000);
                // O nome é único por idioma na conta do WhatsApp da empresa (regra da Meta).
                objTemplate.HasIndex(template => new { template.IDCompany, template.Name, template.Language }).IsUnique();
                objTemplate.HasIndex(template => template.IDCampaign);
                objTemplate.HasOne<Company>()
                    .WithMany()
                    .HasForeignKey(template => template.IDCompany)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            objModelBuilder.Entity<WhatsAppMessage>(objMessage =>
            {
                objMessage.Property(message => message.Username).HasMaxLength(60);
                objMessage.Property(message => message.ToPhone).HasMaxLength(20);
                objMessage.Property(message => message.WaId).HasMaxLength(20);
                objMessage.Property(message => message.FromPhoneNumberId).HasMaxLength(30);
                objMessage.Property(message => message.FromDisplayPhone).HasMaxLength(40);
                objMessage.Property(message => message.TemplateName).HasMaxLength(512);
                objMessage.Property(message => message.TemplateLanguage).HasMaxLength(10);
                objMessage.Property(message => message.ParametersJson).HasColumnType("jsonb");
                objMessage.Property(message => message.Text).HasMaxLength(4000);
                objMessage.Property(message => message.RequestJson).HasColumnType("jsonb");
                objMessage.Property(message => message.ResponseJson).HasColumnType("jsonb");
                objMessage.Property(message => message.MetaMessageId).HasMaxLength(200);
                objMessage.Property(message => message.MetaMessageStatus).HasMaxLength(40);
                objMessage.Property(message => message.Status).HasMaxLength(20);
                objMessage.Property(message => message.ErrorMessage).HasMaxLength(1000);
                objMessage.Property(message => message.PricingCategory).HasMaxLength(40);
                objMessage.Property(message => message.PricingType).HasMaxLength(40);
                // Os avisos da Meta chegam pelo wamid.
                objMessage.HasIndex(message => message.MetaMessageId)
                    .IsUnique()
                    .HasFilter("\"MetaMessageId\" <> ''");
                objMessage.HasIndex(message => new { message.IDCompany, message.CreatedAt });
                objMessage.HasIndex(message => message.IDCampaignRun);
                objMessage.HasIndex(message => message.IDCampaignRecipient);
                objMessage.HasOne<Company>()
                    .WithMany()
                    .HasForeignKey(message => message.IDCompany)
                    .OnDelete(DeleteBehavior.Cascade);
                // D10: a mensagem vai embora junto com o cliente (retenção da LGPD).
                objMessage.HasOne<Customer>()
                    .WithMany()
                    .HasForeignKey(message => message.IDCustomer)
                    .OnDelete(DeleteBehavior.Cascade);
                // A lista da campanha é apagada antes (12 meses): a mensagem só perde o vínculo.
                objMessage.HasOne<CampaignRecipient>()
                    .WithMany()
                    .HasForeignKey(message => message.IDCampaignRecipient)
                    .OnDelete(DeleteBehavior.SetNull);
            });

            objModelBuilder.Entity<WhatsAppMessageEvent>(objEvent =>
            {
                objEvent.Property(evt => evt.Status).HasMaxLength(20);
                objEvent.Property(evt => evt.ErrorMessage).HasMaxLength(1000);
                objEvent.Property(evt => evt.PricingCategory).HasMaxLength(40);
                objEvent.Property(evt => evt.PricingType).HasMaxLength(40);
                objEvent.Property(evt => evt.PayloadJson).HasColumnType("jsonb");
                objEvent.HasIndex(evt => new { evt.IDWhatsAppMessage, evt.Status });
                // Vai embora junto com a mensagem (que vai junto com o cliente, D10).
                objEvent.HasOne<WhatsAppMessage>()
                    .WithMany()
                    .HasForeignKey(evt => evt.IDWhatsAppMessage)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            objModelBuilder.Entity<WhatsAppInboundMessage>(objInbound =>
            {
                objInbound.Property(inbound => inbound.MetaMessageId).HasMaxLength(200);
                objInbound.Property(inbound => inbound.FromPhone).HasMaxLength(20);
                objInbound.Property(inbound => inbound.ProfileName).HasMaxLength(200);
                objInbound.Property(inbound => inbound.Text).HasMaxLength(4096);
                objInbound.Property(inbound => inbound.ContextMessageId).HasMaxLength(200);
                objInbound.Property(inbound => inbound.PayloadJson).HasColumnType("jsonb");
                // A Meta repete avisos por até 7 dias: a mesma mensagem não entra duas vezes.
                objInbound.HasIndex(inbound => inbound.MetaMessageId).IsUnique();
                objInbound.HasIndex(inbound => new { inbound.IDCompany, inbound.SentAt });
                objInbound.HasIndex(inbound => inbound.IDCustomer);
                objInbound.HasOne<Company>()
                    .WithMany()
                    .HasForeignKey(inbound => inbound.IDCompany)
                    .OnDelete(DeleteBehavior.Cascade);
                // D10: a resposta vai embora junto com o cliente.
                objInbound.HasOne<Customer>()
                    .WithMany()
                    .HasForeignKey(inbound => inbound.IDCustomer)
                    .OnDelete(DeleteBehavior.Cascade);
                objInbound.HasOne<WhatsAppMessage>()
                    .WithMany()
                    .HasForeignKey(inbound => inbound.IDWhatsAppMessage)
                    .OnDelete(DeleteBehavior.SetNull);
            });

            objModelBuilder.Entity<WhatsAppWebhookRequest>(objRequest =>
            {
                objRequest.Property(request => request.Fields).HasMaxLength(300);
                objRequest.Property(request => request.Error).HasMaxLength(2000);
                objRequest.Property(request => request.RemoteIp).HasMaxLength(64);
                // A limpeza dos 90 dias (D12) e "último aviso recebido" na tela.
                objRequest.HasIndex(request => new { request.IDCompany, request.ReceivedAt });
                objRequest.HasIndex(request => request.ReceivedAt);
                objRequest.HasOne<Company>()
                    .WithMany()
                    .HasForeignKey(request => request.IDCompany)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            objModelBuilder.Entity<WhatsAppAlert>(objAlert =>
            {
                objAlert.Property(alert => alert.Key).HasMaxLength(200);
                objAlert.Property(alert => alert.Subject).HasMaxLength(300);
                objAlert.Property(alert => alert.Body).HasMaxLength(4000);
                objAlert.Property(alert => alert.Error).HasMaxLength(1000);
                objAlert.HasIndex(alert => new { alert.IDCompany, alert.Key, alert.CreatedAt });
                objAlert.HasIndex(alert => alert.SentAt);
                objAlert.HasOne<Company>()
                    .WithMany()
                    .HasForeignKey(alert => alert.IDCompany)
                    .OnDelete(DeleteBehavior.Cascade);
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
                objUnit.Property(unit => unit.AreaCode).HasMaxLength(2);
                objUnit.Property(unit => unit.StorePhone).HasMaxLength(11);
                objUnit.Property(unit => unit.DevicesSyncError).HasMaxLength(300);
                // O aviso da UniFi acha a unidade pela impressão do código do endereço.
                objUnit.Property(unit => unit.UnifiWebhookHash).HasMaxLength(64);
                objUnit.HasIndex(unit => unit.UnifiWebhookHash)
                    .IsUnique()
                    .HasFilter("\"UnifiWebhookHash\" <> ''");
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
                objVisit.Property(visit => visit.Source).HasConversion<string>().HasMaxLength(10);
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

            objModelBuilder.Entity<SentEmail>(objEmail =>
            {
                objEmail.Property(email => email.UnitName).HasMaxLength(120);
                objEmail.Property(email => email.Kind).HasMaxLength(20);
                objEmail.Property(email => email.ToEmail).HasMaxLength(200);
                objEmail.Property(email => email.Subject).HasMaxLength(400);
                objEmail.Property(email => email.AttachmentName).HasMaxLength(200);
                // A tela lista os mais novos primeiro, por empresa (e filtra por unidade).
                objEmail.HasIndex(email => new { email.IDCompany, email.SentAt });
                objEmail.HasIndex(email => email.IDUnit);
                objEmail.HasOne<Company>()
                    .WithMany()
                    .HasForeignKey(email => email.IDCompany)
                    .OnDelete(DeleteBehavior.Cascade);
                objEmail.HasOne<Unit>()
                    .WithMany()
                    .HasForeignKey(email => email.IDUnit)
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
                objLead.Property(lead => lead.Name).HasMaxLength(200);
                objLead.Property(lead => lead.Instagram).HasMaxLength(100);
                objLead.Property(lead => lead.Phone).HasMaxLength(20);
                objLead.Property(lead => lead.BirthDate).HasMaxLength(10);
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
                objSettings.Property(settings => settings.AreaCode).HasMaxLength(2);
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
                objRun.Property(run => run.WhatsAppError).HasMaxLength(1000);
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
                objRecipient.Property(recipient => recipient.WhatsAppStatus).HasMaxLength(20);
                objRecipient.Property(recipient => recipient.WhatsAppError).HasMaxLength(300);
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
                objDelivery.Property(delivery => delivery.RequestedBy).HasMaxLength(60);
                // Uma entrega do disparo por unidade em cada execução; os reenvios ficam de fora.
                objDelivery.HasIndex(delivery => new { delivery.IDRun, delivery.IDUnit })
                    .IsUnique()
                    .HasFilter("\"IsResend\" = false");
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

            objModelBuilder.Entity<UnifiWebhookCapture>(objCapture =>
            {
                objCapture.Property(capture => capture.Method).HasMaxLength(10);
                objCapture.Property(capture => capture.Label).HasMaxLength(60);
                objCapture.Property(capture => capture.QueryString).HasMaxLength(2000);
                objCapture.Property(capture => capture.HeadersJson).HasColumnType("jsonb");
                objCapture.Property(capture => capture.ContentType).HasMaxLength(200);
                objCapture.Property(capture => capture.BodyJson).HasColumnType("jsonb");
                objCapture.Property(capture => capture.RemoteIp).HasMaxLength(64);
                objCapture.HasIndex(capture => capture.ReceivedAt);
            });

            objModelBuilder.Entity<WifiPresence>(objPresence =>
            {
                objPresence.Property(presence => presence.Mac).HasMaxLength(17);
                objPresence.Property(presence => presence.Ap).HasMaxLength(17);
                objPresence.Property(presence => presence.Hostname).HasMaxLength(100);
                objPresence.Ignore(presence => presence.StaySeconds);
                // Um registro por aparelho, unidade e dia.
                objPresence.HasIndex(presence => new { presence.IDUnit, presence.Mac, presence.LocalDate }).IsUnique();
                // A tela da unidade lista os aparelhos dos últimos dias; a retenção apaga pelos mais antigos.
                objPresence.HasIndex(presence => new { presence.IDUnit, presence.LocalDate });
                objPresence.HasIndex(presence => presence.LastSeenAt);
                objPresence.HasOne<Unit>()
                    .WithMany()
                    .HasForeignKey(presence => presence.IDUnit)
                    .OnDelete(DeleteBehavior.Cascade);
                objPresence.HasOne<Customer>()
                    .WithMany()
                    .HasForeignKey(presence => presence.IDCustomer)
                    .OnDelete(DeleteBehavior.SetNull);
            });

            objModelBuilder.Entity<UnitIgnoredDevice>(objIgnored =>
            {
                objIgnored.HasKey(ignored => new { ignored.IDUnit, ignored.Mac });
                objIgnored.Property(ignored => ignored.Mac).HasMaxLength(17);
                objIgnored.Property(ignored => ignored.Label).HasMaxLength(100);
                objIgnored.Property(ignored => ignored.Username).HasMaxLength(60);
                objIgnored.HasOne<Unit>()
                    .WithMany()
                    .HasForeignKey(ignored => ignored.IDUnit)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            objModelBuilder.Entity<SiteContact>(objContact =>
            {
                objContact.Property(contact => contact.Name).HasMaxLength(100);
                objContact.Property(contact => contact.Company).HasMaxLength(120);
                objContact.Property(contact => contact.Phone).HasMaxLength(11);
                objContact.Property(contact => contact.Email).HasMaxLength(160);
                objContact.Property(contact => contact.City).HasMaxLength(80);
                objContact.Property(contact => contact.State).HasMaxLength(2);
                objContact.Property(contact => contact.Stores).HasMaxLength(10);
                objContact.Property(contact => contact.UsesUnifi).HasMaxLength(10);
                objContact.Property(contact => contact.Message).HasMaxLength(1000);
                objContact.Property(contact => contact.UtmSource).HasMaxLength(200);
                objContact.Property(contact => contact.UtmMedium).HasMaxLength(200);
                objContact.Property(contact => contact.UtmCampaign).HasMaxLength(200);
                objContact.Property(contact => contact.UtmTerm).HasMaxLength(200);
                objContact.Property(contact => contact.UtmContent).HasMaxLength(200);
                objContact.Property(contact => contact.Gclid).HasMaxLength(200);
                objContact.Property(contact => contact.Fbclid).HasMaxLength(200);
                objContact.Property(contact => contact.LandingPage).HasMaxLength(500);
                objContact.Property(contact => contact.Referrer).HasMaxLength(500);
                objContact.Property(contact => contact.Page).HasMaxLength(500);
                objContact.Property(contact => contact.Status).HasConversion<string>().HasMaxLength(12);
                objContact.Property(contact => contact.Notes).HasMaxLength(2000);
                objContact.Property(contact => contact.StatusChangedBy).HasMaxLength(60);
                objContact.Property(contact => contact.NotifyError).HasMaxLength(500);
                // A tela lista do mais novo para o mais antigo; a retenção apaga pelos mais antigos.
                objContact.HasIndex(contact => contact.CreatedAt);
                // O serviço procura os avisos que ainda não saíram.
                objContact.HasIndex(contact => contact.NotifiedAt);
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
