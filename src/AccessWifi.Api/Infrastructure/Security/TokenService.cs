using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
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

    /// <summary>Hash (SHA-256, base64) do token bruto — o que guardamos no banco.</summary>
    public static string HashRefreshToken(string sRawToken)
    {
        byte[] arrHash = SHA256.HashData(Encoding.UTF8.GetBytes(sRawToken));
        return Convert.ToBase64String(arrHash);
    }

    /// <summary>
    /// Cria um refresh token: devolve o valor bruto (vai para o cliente) e a entidade a
    /// persistir (guardando só o hash e a expiração).
    /// </summary>
    public (string RawToken, RefreshToken Entity) CreateRefreshToken(Guid objUserId)
    {
        string sRawToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        RefreshToken objEntity = new RefreshToken
        {
            IDUser = objUserId,
            TokenHash = HashRefreshToken(sRawToken),
            ExpiresAt = DateTime.UtcNow.AddDays(_objJwtOptions.RefreshTokenDays),
        };
        return (sRawToken, objEntity);
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
            expires: DateTime.UtcNow.AddMinutes(_objJwtOptions.AccessTokenMinutes),
            signingCredentials: objSigningCredentials);

        return new JwtSecurityTokenHandler().WriteToken(objToken);
    }
}
