using AccessWifi.Api.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Models.Campaigns;
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

            // Campanhas: monta a base de clientes a partir dos cadastros que já existiam (só na
            // primeira vez, com a tabela vazia). Depois disso, cada conexão atualiza o cliente.
            int iCustomers = await CustomerDirectory.BackfillIfEmptyAsync(objDbContext, DateTime.UtcNow);
            if (iCustomers > 0)
            {
                objLogger.LogInformation("Seed: {Count} cliente(s) montados a partir dos cadastros.", iCustomers);
            }
        }
        catch (Exception objException)
        {
            // Banco fora do ar/não migrado não deve impedir a API de subir.
            objLogger.LogWarning(objException, "Seed pulado: banco indisponível ou não migrado.");
        }
    }
}
