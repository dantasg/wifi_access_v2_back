using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using AccessWifi.Api.Infrastructure.Security;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace AccessWifi.Api.Tests;

public class TokenServiceTests
{
    private static readonly JwtOptions s_objJwtOptions = new JwtOptions
    {
        Secret = "segredo-de-teste-3f9a1c7e5b2d8046a1e9c3b7d5f20486",
        ExpiresInHours = 8,
        Issuer = "accesswifi",
        Audience = "accesswifi-admin",
    };

    private static TokenService CreateService()
    {
        return new TokenService(Options.Create(s_objJwtOptions));
    }

    [Fact]
    public void GenerateToken_TokenValidaComAMesmaChaveIssuerEAudience()
    {
        string sToken = CreateService().GenerateToken("admin");

        TokenValidationParameters objParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = s_objJwtOptions.Issuer,
            ValidateAudience = true,
            ValidAudience = s_objJwtOptions.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(s_objJwtOptions.Secret)),
            ValidateLifetime = true,
        };

        ClaimsPrincipal objPrincipal = new JwtSecurityTokenHandler()
            .ValidateToken(sToken, objParameters, out SecurityToken _);

        Assert.NotNull(objPrincipal);
    }

    [Fact]
    public void GenerateToken_CarregaUsernameNoSubEExpiracaoConfigurada()
    {
        string sToken = CreateService().GenerateToken("admin");

        JwtSecurityToken objToken = new JwtSecurityTokenHandler().ReadJwtToken(sToken);

        Assert.Equal("admin", objToken.Claims.First(claim => claim.Type == "sub").Value);
        Assert.True(objToken.ValidTo > DateTime.UtcNow.AddHours(7));
        Assert.True(objToken.ValidTo <= DateTime.UtcNow.AddHours(9));
    }
}
