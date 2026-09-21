using System.Net;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using AccessWifi.Api.Infrastructure.Persistence;
using AccessWifi.Api.Infrastructure.Security;
using AccessWifi.Api.Infrastructure.Unifi;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Models.Persistence;
using Models.Security;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
ConfigurationManager objConfiguration = builder.Configuration;

// ------------------------------------------------------------------ Options
builder.Services.Configure<AdminOptions>(objConfiguration.GetSection(AdminOptions.SectionName));
// JWT: falha rápida no boot se o segredo for vazio ou fraco (< 32 bytes), evitando subir
// com uma chave que permitiria forjar tokens de admin.
builder.Services.AddOptions<JwtOptions>()
    .Bind(objConfiguration.GetSection(JwtOptions.SectionName))
    .Validate(
        objOptions => !string.IsNullOrWhiteSpace(objOptions.Secret)
            && Encoding.UTF8.GetByteCount(objOptions.Secret) >= 32,
        "Jwt:Secret é obrigatório e deve ter no mínimo 32 bytes (defina via env/user-secrets).")
    .ValidateOnStart();

// Cabeçalhos encaminhados pelo reverse proxy (IP real do cliente e esquema http/https).
// Sem KnownProxies configurados, o ASP.NET ignora os headers (padrão seguro): configure
// "ForwardedHeaders:KnownProxies" com o IP do seu proxy para o rate limit e o HTTPS
// redirect enxergarem o cliente real.
builder.Services.Configure<ForwardedHeadersOptions>(objForwardedOptions =>
{
    objForwardedOptions.ForwardedHeaders =
        ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    objForwardedOptions.KnownProxies.Clear();
    objForwardedOptions.KnownIPNetworks.Clear();
    string[] arrKnownProxies =
        objConfiguration.GetSection("ForwardedHeaders:KnownProxies").Get<string[]>() ?? [];
    foreach (string sProxy in arrKnownProxies)
    {
        if (IPAddress.TryParse(sProxy, out IPAddress? objProxyIp))
        {
            objForwardedOptions.KnownProxies.Add(objProxyIp);
        }
    }
});

// ------------------------------------------------------------- EF Core / Postgres
string sConnectionString = objConfiguration.GetConnectionString("Default")
    ?? throw new InvalidOperationException("ConnectionStrings:Default não configurada.");
builder.Services.AddDbContext<AppDbContext>(objDbOptions => objDbOptions.UseNpgsql(sConnectionString));

// ----------------------------------------------------------------------- CORS
// Libera só a origem do front (configurável por env FrontOrigin) e apenas os métodos/headers
// que a aplicação usa — sem AllowAny.
string sFrontOrigin = objConfiguration["FrontOrigin"] ?? "http://localhost:5173";
builder.Services.AddCors(objCorsOptions => objCorsOptions.AddDefaultPolicy(objPolicy => objPolicy
    .WithOrigins(sFrontOrigin)
    .WithMethods("GET", "POST", "PUT")
    // ngrok-skip-browser-warning: o front envia sempre (pula o aviso do ngrok free).
    // Sem ele na lista, o preflight falha quando a API é exposta por túnel.
    .WithHeaders("Content-Type", "Authorization", "ngrok-skip-browser-warning")));

// ------------------------------------------------------------------- JWT (admin)
JwtOptions objJwtOptions = objConfiguration.GetSection(JwtOptions.SectionName).Get<JwtOptions>()
    ?? new JwtOptions();
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(objBearerOptions =>
    {
        // Sem renomear claims na entrada: "role" e "companyId" chegam com esses nomes
        // aos controllers (senão o RoleClaimType abaixo não casa e [Authorize(Roles)] dá 403).
        objBearerOptions.MapInboundClaims = false;
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
            // Papéis multi empresa: [Authorize(Roles = "superadmin")] lê a claim "role".
            RoleClaimType = ClaimsExtensions.ClaimRole,
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
    // O portal chama o prepare uma vez ao abrir; 20/min por IP folga para vários aparelhos atrás
    // do mesmo NAT da loja sem abrir espaço para usar a rota como sonda da controladora.
    objLimiterOptions.AddPolicy("authorize-prepare", objHttpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            objHttpContext.Connection.RemoteIpAddress ?? IPAddress.Loopback,
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 20,
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
// Cifragem em repouso (senha da UniFi/SMTP). A chave vem de Encryption:Key (env/user-secrets);
// resolvida sob demanda para não exigir a chave no design-time das migrations.
builder.Services.AddSingleton<IEncryptor>(
    _ => new AesGcmEncryptor(objConfiguration["Encryption:Key"]));
// A config da controladora vem por unidade (banco). No modo local o client é criado por chamada
// (cada unidade tem host e política de certificado próprios); no modo nuvem o destino é sempre
// api.ui.com, então vale um HttpClient nomeado e reaproveitado.
builder.Services.AddHttpClient(UnifiCloudClient.HttpClientName, objUnifiHttpClient =>
{
    objUnifiHttpClient.BaseAddress = new Uri("https://api.ui.com/");
    // D8: a Ubiquiti corta a chamada repassada em 25 s; 20 s deixa margem para respondermos antes.
    objUnifiHttpClient.Timeout = TimeSpan.FromSeconds(20);
});
// Guarda o ID do aparelho adiantado pelo /authorize/prepare (ver UnifiCloudClient.PrepareAsync).
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<UnifiLocalClient>();
builder.Services.AddSingleton<UnifiCloudClient>();
// O router escolhe o caminho pelo Mode da unidade — os controllers não precisam saber qual é.
builder.Services.AddSingleton<IUnifiClient, UnifiClientRouter>();

builder.Services.AddControllers().AddJsonOptions(objJsonOptions =>
    objJsonOptions.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull);

WebApplication app = builder.Build();

// Bootstrap: cria o super admin vindo da configuração quando ainda não há usuários.
using (IServiceScope objScope = app.Services.CreateScope())
{
    await DbSeeder.SeedAsync(objScope.ServiceProvider);
}

// Primeiro na pipeline: adota o IP/esquema reais vindos do proxy (quando confiável).
app.UseForwardedHeaders();

if (!app.Environment.IsDevelopment())
{
    // HSTS instrui o navegador a só usar HTTPS. O header só trafega sobre HTTPS, então é inócuo
    // em HTTP — seguro habilitar sempre em produção.
    app.UseHsts();
}

// Redirecionar HTTP→HTTPS fica atrás de flag ("Security:EnforceHttpsRedirect"): habilite só
// quando o proxy encaminhar corretamente o X-Forwarded-Proto (senão pode gerar loop de redirect).
if (objConfiguration.GetValue("Security:EnforceHttpsRedirect", false))
{
    app.UseHttpsRedirection();
}

// Cabeçalhos de segurança (defesa em profundidade; a API não serve HTML, mas custam pouco).
app.Use(async (objContext, objNext) =>
{
    objContext.Response.Headers["X-Content-Type-Options"] = "nosniff";
    objContext.Response.Headers["Referrer-Policy"] = "no-referrer";
    objContext.Response.Headers["X-Frame-Options"] = "DENY";
    await objNext();
});

app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.MapControllers();

app.Run();
