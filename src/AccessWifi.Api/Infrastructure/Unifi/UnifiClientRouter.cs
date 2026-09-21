using Models.DataBase;

namespace AccessWifi.Api.Infrastructure.Unifi;

/// <summary>
/// Escolhe como falar com a controladora conforme o modo da unidade (D1): direto na controladora
/// ou pela nuvem da Ubiquiti. É este que fica registrado como <see cref="IUnifiClient"/>, de forma
/// que os controllers não precisam saber qual caminho está em uso.
/// </summary>
public class UnifiClientRouter : IUnifiClient
{
    private readonly UnifiLocalClient _objLocalClient;
    private readonly UnifiCloudClient _objCloudClient;

    public UnifiClientRouter(UnifiLocalClient objLocalClient, UnifiCloudClient objCloudClient)
    {
        _objLocalClient = objLocalClient;
        _objCloudClient = objCloudClient;
    }

    public Task AuthorizeGuestAsync(
        CompanyUnifi objConfig, string sMac, int iAccessMinutes,
        CancellationToken objCancellationToken = default)
    {
        return Resolve(objConfig)
            .AuthorizeGuestAsync(objConfig, sMac, iAccessMinutes, objCancellationToken);
    }

    public Task<string> TestConnectionAsync(
        CompanyUnifi objConfig, CancellationToken objCancellationToken = default)
    {
        return Resolve(objConfig).TestConnectionAsync(objConfig, objCancellationToken);
    }

    /// <summary>Qualquer valor que não seja "Cloud" cai no modo local — o padrão histórico.</summary>
    private IUnifiClient Resolve(CompanyUnifi objConfig)
    {
        return string.Equals(objConfig.Mode, UnifiMode.Cloud, StringComparison.OrdinalIgnoreCase)
            ? _objCloudClient
            : _objLocalClient;
    }
}
