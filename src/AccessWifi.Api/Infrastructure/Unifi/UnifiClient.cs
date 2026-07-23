using System.Net;
using System.Text.Json;
using AccessWifi.Api.Features.Companies;
using Models.DataBase;

namespace AccessWifi.Api.Infrastructure.Unifi;

public class UnifiClient : IUnifiClient
{
    private static readonly JsonSerializerOptions s_objJsonOptions =
        new JsonSerializerOptions(JsonSerializerDefaults.Web);

    private record LoginPayload(string Username, string Password);
    private record AuthorizeGuestPayload(string Cmd, string Mac, int Minutes);

    public async Task AuthorizeGuestAsync(
        CompanyUnifi objConfig, string sMac, int iAccessMinutes,
        CancellationToken objCancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(objConfig.Host) ||
            !Uri.TryCreate(objConfig.Host, UriKind.Absolute, out Uri? objBaseAddress))
        {
            throw new UnifiException("Controladora UniFi não configurada para esta empresa.");
        }

        using HttpClientHandler objHandler = new HttpClientHandler
        {
            UseCookies = true,
            CookieContainer = new CookieContainer(),
        };
        if (!objConfig.VerifySsl)
        {
            // UDM/Cloud Gateway usam certificado self-signed.
            objHandler.ServerCertificateCustomValidationCallback = (_, _, _, _) => true;
        }
        using HttpClient objHttpClient = new HttpClient(objHandler)
        {
            BaseAddress = objBaseAddress,
            Timeout = TimeSpan.FromSeconds(15),
        };

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

    private static async Task<string?> LoginAsync(
        HttpClient objHttpClient, CompanyUnifi objConfig, CancellationToken objCancellationToken)
    {
        string sLoginPath = objConfig.UnifiOs ? "/api/auth/login" : "/api/login";
        LoginPayload objPayload = new LoginPayload(objConfig.Username, objConfig.Password);

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
