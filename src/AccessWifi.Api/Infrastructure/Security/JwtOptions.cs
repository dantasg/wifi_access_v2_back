namespace AccessWifi.Api.Infrastructure.Security;

public class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Secret { get; set; } = "";

    /// <summary>Duração do access token (curto), em minutos.</summary>
    public int AccessTokenMinutes { get; set; } = 60;

    /// <summary>Duração do refresh token (longo), em dias.</summary>
    public int RefreshTokenDays { get; set; } = 14;

    public string Issuer { get; set; } = "accesswifi";
    public string Audience { get; set; } = "accesswifi-admin";
}
