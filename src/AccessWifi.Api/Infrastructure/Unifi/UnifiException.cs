namespace AccessWifi.Api.Infrastructure.Unifi;

public class UnifiException : Exception
{
    public UnifiException(string sMessage) : base(sMessage) { }
    public UnifiException(string sMessage, Exception objInner) : base(sMessage, objInner) { }
}
