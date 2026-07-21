namespace AccessWifi.Api.Infrastructure.Security;

/// <summary>Seção "Admin" do appsettings — a senha fica sempre em hash bcrypt, nunca em texto puro.</summary>
public class AdminOptions
{
    public const string SectionName = "Admin";

    public string Username { get; set; } = "";
    public string PasswordHash { get; set; } = "";
}
