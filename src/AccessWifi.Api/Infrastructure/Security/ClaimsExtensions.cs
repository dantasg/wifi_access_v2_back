using System.Security.Claims;

namespace AccessWifi.Api.Infrastructure.Security;

public static class ClaimsExtensions
{
    public const string RoleSuperAdmin = "superadmin";
    public const string RoleAdmin = "admin";
    public const string ClaimRole = "role";
    public const string ClaimCompanyId = "companyId";

    public static bool IsSuperAdmin(this ClaimsPrincipal objUser)
    {
        return objUser.IsInRole(RoleSuperAdmin);
    }

    public static Guid? GetCompanyId(this ClaimsPrincipal objUser)
    {
        string? sValue = objUser.FindFirstValue(ClaimCompanyId);
        return Guid.TryParse(sValue, out Guid objCompanyId) ? objCompanyId : null;
    }
}
