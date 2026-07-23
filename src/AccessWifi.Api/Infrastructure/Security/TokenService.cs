using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using AccessWifi.Api.Features.Admin;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Models.DataBase;

namespace AccessWifi.Api.Infrastructure.Security;

public class TokenService
{
    private readonly JwtOptions _objJwtOptions;

    public TokenService(IOptions<JwtOptions> objOptions)
    {
        _objJwtOptions = objOptions.Value;
    }

    public string GenerateToken(AdminUser objUser)
    {
        SymmetricSecurityKey objSecurityKey =
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_objJwtOptions.Secret));
        SigningCredentials objSigningCredentials =
            new SigningCredentials(objSecurityKey, SecurityAlgorithms.HmacSha256);

        List<Claim> objClaims =
        [
            new Claim(JwtRegisteredClaimNames.Sub, objUser.Username),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new Claim(
                ClaimsExtensions.ClaimRole,
                objUser.IDCompany is null ? ClaimsExtensions.RoleSuperAdmin : ClaimsExtensions.RoleAdmin),
        ];
        if (objUser.IDCompany is not null)
        {
            objClaims.Add(new Claim(ClaimsExtensions.ClaimCompanyId, objUser.IDCompany.Value.ToString()));
        }

        JwtSecurityToken objToken = new JwtSecurityToken(
            issuer: _objJwtOptions.Issuer,
            audience: _objJwtOptions.Audience,
            claims: objClaims,
            expires: DateTime.UtcNow.AddHours(_objJwtOptions.ExpiresInHours),
            signingCredentials: objSigningCredentials);

        return new JwtSecurityTokenHandler().WriteToken(objToken);
    }
}
