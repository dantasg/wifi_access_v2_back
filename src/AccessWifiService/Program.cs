using AccessWifiService;
using AccessWifiService.Campaigns;
using AccessWifiService.SiteContacts;
using AccessWifiService.WhatsApp;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Models.Campaigns;
using Models.DataBase;
using Models.Email;
using Models.Persistence;
using Models.Security;
using Models.WhatsApp;

// Teste do PDF de campanha, sem banco nem serviço: o publicar-producao.sh roda no servidor antes de trocar
// a versão, para saber se o gerador (biblioteca nativa + fonte embutida) funciona lá.
if (args.Contains("--testar-pdf"))
{
    try
    {
        byte[] arrPdf = CampaignPdf.Build(new CampaignPdfData(
            "Empresa", "Unidade", "Aniversário", CampaignKind.Birthday, new DateOnly(2026, 10, 12),
            "Feliz aniversário, {primeiro_nome}! 🎉", null, new ThemeColors(),
            [new CampaignPdfRow("Cliente Teste", "93991234567", "cliente", "seg, 12/10 · 30 anos", true, "Feliz aniversário!")]));
        Console.WriteLine($"PDF de campanha ok ({arrPdf.Length} bytes)");
        return 0;
    }
    catch (Exception objException)
    {
        Console.WriteLine($"PDF de campanha falhou: {objException}");
        return 1;
    }
}

// ContentRoot no diretório do binário: como serviço (Windows/systemd) o diretório de
// trabalho não é o da aplicação, então o appsettings.json precisa ser localizado por aqui.
HostApplicationBuilder objBuilder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory,
});

// Roda como serviço no Windows (SCM) e no Linux (systemd).
objBuilder.Services.AddWindowsService(objOptions => objOptions.ServiceName = "AccessWifiService");
objBuilder.Services.AddSystemd();

// Banco compartilhado com a API (mesma lib Models / mesmo AppDbContext).
string sConnectionString = objBuilder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException("ConnectionStrings:Default não configurada.");
objBuilder.Services.AddDbContext<AppDbContext>(objDbOptions => objDbOptions.UseNpgsql(sConnectionString));

// Config global (SMTP etc.) vem da tabela Configuration, não do appsettings.
// Cifragem em repouso (senha do SMTP). Mesma chave Encryption:Key usada pela API.
objBuilder.Services.AddSingleton<IEncryptor>(
    _ => new AesGcmEncryptor(objBuilder.Configuration["Encryption:Key"]));

objBuilder.Services.AddScoped<ConfigurationReader>();
objBuilder.Services.AddScoped<IEmailSender, SmtpEmailSender>();
objBuilder.Services.AddScoped<ReportService>();
objBuilder.Services.AddScoped<LeadRetentionService>();

objBuilder.Services.AddHostedService<SrvWifiService>();

// Campanhas: agendador + execução. Cada execução manda, por unidade, um e-mail com o PDF dos clientes (D17).
objBuilder.Services.Configure<CampaignEngineOptions>(
    objBuilder.Configuration.GetSection(CampaignEngineOptions.SectionName));
objBuilder.Services.AddScoped<CampaignEngine>();
objBuilder.Services.AddHostedService<CampaignWorker>();

// Contatos do site institucional: aviso por e-mail de cada pedido novo (PROPOSTA_SITE.md, D10).
objBuilder.Services.Configure<SiteContactOptions>(
    objBuilder.Configuration.GetSection(SiteContactOptions.SectionName));
objBuilder.Services.AddScoped<SiteContactNotifier>();
objBuilder.Services.AddHostedService<SiteContactWorker>();

// WhatsApp pela API oficial da Meta (PROPOSTA_WHATSAPP.md): os modelos de mensagem de cada campanha vão para a
// Meta sozinhos e, com o envio automático ligado, a execução manda a mensagem a cada cliente antes do e-mail ao
// gestor; tudo com as credenciais de cada empresa. Um HttpClient só, com conexões renovadas a cada 5 minutos.
WhatsAppOptions objWhatsAppOptions = objBuilder.Configuration.GetSection(WhatsAppOptions.SectionName).Get<WhatsAppOptions>()
    ?? new WhatsAppOptions();
HttpClient objWhatsAppHttpClient = new HttpClient(new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(5) })
{
    Timeout = TimeSpan.FromSeconds(20),
};
objBuilder.Services.AddSingleton(new WhatsAppCloudClient(objWhatsAppHttpClient, objWhatsAppOptions));
objBuilder.Services.AddSingleton(
    objBuilder.Configuration.GetSection(WhatsAppTemplateOptions.SectionName).Get<WhatsAppTemplateOptions>()
    ?? new WhatsAppTemplateOptions());
objBuilder.Services.AddScoped<WhatsAppTemplateService>();
objBuilder.Services.AddSingleton(
    objBuilder.Configuration.GetSection(CampaignWhatsAppOptions.SectionName).Get<CampaignWhatsAppOptions>()
    ?? new CampaignWhatsAppOptions());
objBuilder.Services.AddScoped<CampaignWhatsAppSender>();
objBuilder.Services.AddScoped<WhatsAppAlertService>();
objBuilder.Services.AddHostedService<WhatsAppTemplateWorker>();

objBuilder.Build().Run();
return 0;
