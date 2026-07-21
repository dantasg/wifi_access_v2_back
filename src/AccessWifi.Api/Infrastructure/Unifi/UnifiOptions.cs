namespace AccessWifi.Api.Infrastructure.Unifi;

/// <summary>Seção "Unifi" do appsettings — dados de acesso à controladora.</summary>
public class UnifiOptions
{
    public const string SectionName = "Unifi";

    /// <summary>Ex.: https://192.168.1.1 (inclua :8443 se for controller clássico).</summary>
    public string Host { get; set; } = "";

    public string Site { get; set; } = "default";
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";

    /// <summary>true: UDM/Cloud Gateway (UniFi OS) | false: controller clássico.</summary>
    public bool UnifiOs { get; set; } = true;

    /// <summary>UDM usa certificado self-signed — em geral fica false.</summary>
    public bool VerifySsl { get; set; }

    /// <summary>Tempo de liberação do guest, em minutos.</summary>
    public int AccessMinutes { get; set; } = 1440;
}
