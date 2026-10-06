using AccessWifiService;
using AccessWifiService.Campaigns;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Models.Campaigns;
using Models.DataBase;
using Models.Email;
using Models.Persistence;
using Models.Security;

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

objBuilder.Build().Run();
return 0;
