using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace AccessWifi.Api.Infrastructure.Security;

/// <summary>Gera o JWT devolvido no POST /admin/login.</summary>
public class TokenService
{
    private readonly JwtOptions _objJwtOptions;

    public TokenService(IOptions<JwtOptions> objOptions)
    {
        _objJwtOptions = objOptions.Value;
    }

    public string GenerateToken(string sUsername)
    {
        SymmetricSecurityKey objSecurityKey =
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_objJwtOptions.Secret));
        SigningCredentials objSigningCredentials =
            new SigningCredentials(objSecurityKey, SecurityAlgorithms.HmacSha256);

        Claim[] objClaims =
        [
            new Claim(JwtRegisteredClaimNames.Sub, sUsername),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
        ];

        JwtSecurityToken objToken = new JwtSecurityToken(
            issuer: _objJwtOptions.Issuer,
            audience: _objJwtOptions.Audience,
            claims: objClaims,
            expires: DateTime.UtcNow.AddHours(_objJwtOptions.ExpiresInHours),
            signingCredentials: objSigningCredentials);

        return new JwtSecurityTokenHandler().WriteToken(objToken);
    }
}
