namespace AccessWifi.Api.Infrastructure.Unifi;

/// <summary>Falha de comunicação/autorização com a controladora UniFi.</summary>
public class UnifiException : Exception
{
    public UnifiException(string sMessage) : base(sMessage) { }
    public UnifiException(string sMessage, Exception objInner) : base(sMessage, objInner) { }
}
