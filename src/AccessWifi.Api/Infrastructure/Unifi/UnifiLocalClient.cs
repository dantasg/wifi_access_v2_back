using System.Net;
using System.Text.Json;
using Models.DataBase;
using Models.Security;

namespace AccessWifi.Api.Infrastructure.Unifi;

/// <summary>
/// Fala direto com a controladora da unidade (API clássica, com usuário e senha de admin).
/// Exige que o servidor alcance o equipamento pela rede — ou seja, IP público ou DDNS.
/// Para unidades sem isso, ver <see cref="UnifiCloudClient"/>.
/// </summary>
public class UnifiLocalClient : IUnifiClient
{
    private static readonly JsonSerializerOptions s_objJsonOptions =
        new JsonSerializerOptions(JsonSerializerDefaults.Web);

    private readonly IEncryptor _objEncryptor;

    public UnifiLocalClient(IEncryptor objEncryptor)
    {
        _objEncryptor = objEncryptor;
    }

    private record LoginPayload(string Username, string Password);
    private record AuthorizeGuestPayload(string Cmd, string Mac, int Minutes);

    public async Task AuthorizeGuestAsync(
        CompanyUnifi objConfig, string sMac, int iAccessMinutes,
        CancellationToken objCancellationToken = default)
    {
        using HttpClientHandler objHandler = CreateHandler(objConfig);
        using HttpClient objHttpClient = CreateHttpClient(objConfig, objHandler);

        string? sCsrfToken = await LoginAsync(objHttpClient, objConfig, objCancellationToken);

        string sAuthorizePath = objConfig.UnifiOs
            ? $"/proxy/network/api/s/{objConfig.Site}/cmd/stamgr"
            : $"/api/s/{objConfig.Site}/cmd/stamgr";

        AuthorizeGuestPayload objPayload = new AuthorizeGuestPayload(
            Cmd: "authorize-guest",
            Mac: sMac.ToLowerInvariant(),
            Minutes: iAccessMinutes);

        using HttpRequestMessage objRequest = new HttpRequestMessage(HttpMethod.Post, sAuthorizePath)
        {
            Content = JsonContent.Create(objPayload, options: s_objJsonOptions),
        };
        if (!string.IsNullOrEmpty(sCsrfToken))
        {
            objRequest.Headers.Add("X-CSRF-Token", sCsrfToken);
        }

        HttpResponseMessage objResponse;
        try
        {
            objResponse = await objHttpClient.SendAsync(objRequest, objCancellationToken);
        }
        catch (HttpRequestException objException)
        {
            throw new UnifiException("Não foi possível falar com a controladora UniFi.", objException);
        }

        using (objResponse)
        {
            if (!objResponse.IsSuccessStatusCode)
            {
                throw new UnifiException(
                    $"Controladora recusou a autorização (HTTP {(int)objResponse.StatusCode}).");
            }
        }
    }

    public async Task<string> TestConnectionAsync(
        CompanyUnifi objConfig, CancellationToken objCancellationToken = default)
    {
        using HttpClientHandler objHandler = CreateHandler(objConfig);
        using HttpClient objHttpClient = CreateHttpClient(objConfig, objHandler);

        await LoginAsync(objHttpClient, objConfig, objCancellationToken);

        return $"Login na controladora funcionou (site \"{objConfig.Site}\").";
    }

    private static HttpClientHandler CreateHandler(CompanyUnifi objConfig)
    {
        HttpClientHandler objHandler = new HttpClientHandler
        {
            UseCookies = true,
            CookieContainer = new CookieContainer(),
        };
        if (!objConfig.VerifySsl)
        {
            // UDM/Cloud Gateway usam certificado self-signed.
            objHandler.ServerCertificateCustomValidationCallback = (_, _, _, _) => true;
        }
        return objHandler;
    }

    private static HttpClient CreateHttpClient(CompanyUnifi objConfig, HttpClientHandler objHandler)
    {
        if (string.IsNullOrWhiteSpace(objConfig.Host) ||
            !Uri.TryCreate(objConfig.Host, UriKind.Absolute, out Uri? objBaseAddress))
        {
            throw new UnifiException("Controladora UniFi não configurada para esta unidade.");
        }

        return new HttpClient(objHandler)
        {
            BaseAddress = objBaseAddress,
            Timeout = TimeSpan.FromSeconds(15),
        };
    }

    private async Task<string?> LoginAsync(
        HttpClient objHttpClient, CompanyUnifi objConfig, CancellationToken objCancellationToken)
    {
        string sLoginPath = objConfig.UnifiOs ? "/api/auth/login" : "/api/login";
        // A senha é guardada cifrada no banco; decifra só na hora de falar com a controladora.
        string sPassword = _objEncryptor.Decrypt(objConfig.Password) ?? "";
        LoginPayload objPayload = new LoginPayload(objConfig.Username, sPassword);

        HttpResponseMessage objResponse;
        try
        {
            objResponse = await objHttpClient.PostAsJsonAsync(
                sLoginPath, objPayload, s_objJsonOptions, objCancellationToken);
        }
        catch (HttpRequestException objException)
        {
            throw new UnifiException("Não foi possível falar com a controladora UniFi.", objException);
        }

        using (objResponse)
        {
            if (!objResponse.IsSuccessStatusCode)
            {
                throw new UnifiException(
                    $"Login na controladora falhou (HTTP {(int)objResponse.StatusCode}).");
            }

            objResponse.Headers.TryGetValues("x-csrf-token", out IEnumerable<string>? objCsrfValues);
            return objCsrfValues?.FirstOrDefault();
        }
    }
}
