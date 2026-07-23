using AccessWifi.Api.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Models.DataBase;
using Models.Persistence;

namespace AccessWifi.Api.Infrastructure.Persistence;

/// <summary>
/// Semeadura mínima para a aplicação subir: apenas o super admin vindo da configuração.
/// Empresas, unidades e usuários de empresa são criados pelo super admin via API.
/// </summary>
public static class DbSeeder
{
    public static async Task SeedAsync(IServiceProvider objServices)
    {
        AppDbContext objDbContext = objServices.GetRequiredService<AppDbContext>();
        AdminOptions objAdminOptions = objServices.GetRequiredService<IOptions<AdminOptions>>().Value;
        ILogger objLogger = objServices.GetRequiredService<ILoggerFactory>().CreateLogger("DbSeeder");

        try
        {
            if (!await objDbContext.Users.AnyAsync() &&
                !string.IsNullOrWhiteSpace(objAdminOptions.PasswordHash))
            {
                objDbContext.Users.Add(new AdminUser
                {
                    Username = objAdminOptions.Username,
                    PasswordHash = objAdminOptions.PasswordHash,
                    IDCompany = null, // super admin
                });
                objLogger.LogInformation("Seed: super admin '{Username}' criado.", objAdminOptions.Username);

                await objDbContext.SaveChangesAsync();
            }
        }
        catch (Exception objException)
        {
            // Banco fora do ar/não migrado não deve impedir a API de subir.
            objLogger.LogWarning(objException, "Seed pulado: banco indisponível ou não migrado.");
        }
    }
}
