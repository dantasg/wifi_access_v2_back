using System.Net;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using AccessWifi.Api.Infrastructure.Persistence;
using AccessWifi.Api.Infrastructure.Security;
using AccessWifi.Api.Infrastructure.Unifi;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
ConfigurationManager objConfiguration = builder.Configuration;

// ------------------------------------------------------------------ Options
builder.Services.Configure<AdminOptions>(objConfiguration.GetSection(AdminOptions.SectionName));
builder.Services.Configure<JwtOptions>(objConfiguration.GetSection(JwtOptions.SectionName));
builder.Services.Configure<UnifiOptions>(objConfiguration.GetSection(UnifiOptions.SectionName));

// ------------------------------------------------------------- EF Core / Postgres
string sConnectionString = objConfiguration.GetConnectionString("Default")
    ?? throw new InvalidOperationException("ConnectionStrings:Default não configurada.");
builder.Services.AddDbContext<AppDbContext>(objDbOptions => objDbOptions.UseNpgsql(sConnectionString));

// ----------------------------------------------------------------------- CORS
// Em dev o proxy do Vite evita CORS; em produção libere só a origem do front.
string sFrontOrigin = objConfiguration["FrontOrigin"] ?? "http://localhost:5173";
builder.Services.AddCors(objCorsOptions => objCorsOptions.AddDefaultPolicy(objPolicy => objPolicy
    .WithOrigins(sFrontOrigin)
    .WithMethods("GET", "POST", "PUT")
    .WithHeaders("Content-Type", "Authorization")));

// ------------------------------------------------------------------- JWT (admin)
JwtOptions objJwtOptions = objConfiguration.GetSection(JwtOptions.SectionName).Get<JwtOptions>()
    ?? new JwtOptions();
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(objBearerOptions =>
    {
        objBearerOptions.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = objJwtOptions.Issuer,
            ValidateAudience = true,
            ValidAudience = objJwtOptions.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(objJwtOptions.Secret)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(1),
        };
    });
builder.Services.AddAuthorization();

// -------------------------------------------------------------- Rate limiting
// Janelas fixas por IP: o portal é público, então o limite protege o banco e a controladora.
builder.Services.AddRateLimiter(objLimiterOptions =>
{
    objLimiterOptions.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    objLimiterOptions.AddPolicy("authorize", objHttpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            objHttpContext.Connection.RemoteIpAddress ?? IPAddress.Loopback,
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(1),
            }));
    objLimiterOptions.AddPolicy("admin-login", objHttpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            objHttpContext.Connection.RemoteIpAddress ?? IPAddress.Loopback,
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 5,
                Window = TimeSpan.FromMinutes(1),
            }));
});

// ---------------------------------------------------------------- Serviços da app
builder.Services.AddScoped<TokenService>();
builder.Services.AddHttpClient<IUnifiClient, UnifiClient>(
    (objServiceProvider, objHttpClient) =>
    {
        UnifiOptions objUnifiOptions =
            objServiceProvider.GetRequiredService<IOptions<UnifiOptions>>().Value;
        if (!string.IsNullOrWhiteSpace(objUnifiOptions.Host))
        {
            objHttpClient.BaseAddress = new Uri(objUnifiOptions.Host);
        }
        objHttpClient.Timeout = TimeSpan.FromSeconds(15);
    })
    .ConfigurePrimaryHttpMessageHandler(objServiceProvider =>
    {
        UnifiOptions objUnifiOptions =
            objServiceProvider.GetRequiredService<IOptions<UnifiOptions>>().Value;
        HttpClientHandler objHandler = new HttpClientHandler
        {
            UseCookies = true,
            CookieContainer = new CookieContainer(),
        };
        if (!objUnifiOptions.VerifySsl)
        {
            // UDM/Cloud Gateway usam certificado self-signed.
            objHandler.ServerCertificateCustomValidationCallback = (_, _, _, _) => true;
        }
        return objHandler;
    });

builder.Services.AddControllers().AddJsonOptions(objJsonOptions =>
    objJsonOptions.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull);

WebApplication app = builder.Build();

// HTTPS fica a cargo do reverse proxy (Nginx) em produção; em dev o Vite proxeia via http.
app.UseCors();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();
