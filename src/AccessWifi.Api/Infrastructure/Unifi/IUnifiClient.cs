namespace AccessWifi.Api.Infrastructure.Unifi;

/// <summary>Isola a controladora UniFi atrás de interface (facilita teste e troca de implementação).</summary>
public interface IUnifiClient
{
    /// <summary>Autoriza o MAC do visitante na controladora (cmd authorize-guest).</summary>
    /// <param name="iAccessMinutes">Tempo de liberação; null = usar o Unifi:AccessMinutes do appsettings.</param>
    /// <exception cref="UnifiException">Login ou autorização falharam.</exception>
    Task AuthorizeGuestAsync(
        string sMac, int? iAccessMinutes = null, CancellationToken objCancellationToken = default);
}
