using Models.DataBase;
using System.Security.Claims;
using Models.Persistence;
using AccessWifi.Api.Infrastructure.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AccessWifi.Api.Tests;

/// <summary>Apoio comum: banco InMemory isolado por teste e identidade simulada nos controllers.</summary>
public static class TestHelpers
{
    public static AppDbContext CreateDbContext()
    {
        DbContextOptions<AppDbContext> objOptions = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(objOptions);
    }

    /// <summary>Simula o JWT no controller: admin de empresa (com IDCompany) ou super admin (sem).</summary>
    public static void SetUser(ControllerBase objController, Guid? objCompanyId)
    {
        List<Claim> objClaims =
        [
            new Claim(
                ClaimsExtensions.ClaimRole,
                objCompanyId is null ? ClaimsExtensions.RoleSuperAdmin : ClaimsExtensions.RoleAdmin),
        ];
        if (objCompanyId is not null)
        {
            objClaims.Add(new Claim(ClaimsExtensions.ClaimCompanyId, objCompanyId.Value.ToString()));
        }

        ClaimsIdentity objIdentity = new ClaimsIdentity(
            objClaims, "Test", "sub", ClaimsExtensions.ClaimRole);
        objController.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(objIdentity) },
        };
    }
}
