using AccessWifiService;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Models.Persistence;

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

// SMTP global do serviço + envio.
objBuilder.Services.Configure<SmtpOptions>(objBuilder.Configuration.GetSection(SmtpOptions.SectionName));
objBuilder.Services.AddSingleton<IEmailSender, SmtpEmailSender>();
objBuilder.Services.AddScoped<ReportService>();

objBuilder.Services.AddHostedService<SrvWifiService>();

objBuilder.Build().Run();
