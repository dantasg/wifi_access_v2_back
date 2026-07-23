namespace AccessWifi.Api.Infrastructure.Security;

public class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Secret { get; set; } = "";
    public int ExpiresInHours { get; set; } = 8;
    public string Issuer { get; set; } = "accesswifi";
    public string Audience { get; set; } = "accesswifi-admin";
}
