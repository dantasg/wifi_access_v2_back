using AccessWifi.Api.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Models.DataBase;
using Models.Persistence;

namespace AccessWifi.Api.Infrastructure.Persistence;

public static class DbSeeder
{
    public static async Task SeedAsync(IServiceProvider objServices, IHostEnvironment objEnvironment)
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
            }

            if (objEnvironment.IsDevelopment() &&
                !await objDbContext.Companies.AnyAsync() &&
                !string.IsNullOrWhiteSpace(objAdminOptions.PasswordHash))
            {
                Company objCompany = new Company { Name = "Dôce Cafeteria", Slug = "doce" };
                objDbContext.Companies.Add(objCompany);
                objDbContext.Units.Add(new Unit
                {
                    IDCompany = objCompany.Id,
                    Name = "Matriz",
                    Slug = "doce-matriz",
                });
                objDbContext.Users.Add(new AdminUser
                {
                    Username = "admin",
                    PasswordHash = objAdminOptions.PasswordHash,
                    IDCompany = objCompany.Id,
                });
                objLogger.LogInformation(
                    "Seed dev: empresa 'doce', unidade 'doce-matriz' e usuário 'admin' criados.");
            }

            await objDbContext.SaveChangesAsync();
        }
        catch (Exception objException)
        {
            // Banco fora do ar/não migrado não deve impedir a API de subir.
            objLogger.LogWarning(objException, "Seed pulado: banco indisponível ou não migrado.");
        }
    }
}
