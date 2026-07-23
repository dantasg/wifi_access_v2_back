namespace AccessWifi.Api.Infrastructure.Security;

public class AdminOptions
{
    public const string SectionName = "Admin";

    public string Username { get; set; } = "";
    public string PasswordHash { get; set; } = "";
}
