using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace AccessWifi.Api.Infrastructure.Unifi;

/// <summary>
/// Cliente HTTP da controladora UniFi (portado do protótipo v1 em Node).
/// Faz login a cada autorização — o cookie de sessão fica no CookieContainer do handler
/// e o header x-csrf-token é repassado quando a controladora o envia (UniFi OS).
/// </summary>
public class UnifiClient : IUnifiClient
{
    private static readonly JsonSerializerOptions s_objJsonOptions =
        new JsonSerializerOptions(JsonSerializerDefaults.Web);

    private readonly HttpClient _objHttpClient;
    private readonly UnifiOptions _objUnifiOptions;

    private record LoginPayload(string Username, string Password);
    private record AuthorizeGuestPayload(string Cmd, string Mac, int Minutes);

    public UnifiClient(HttpClient objHttpClient, IOptions<UnifiOptions> objOptions)
    {
        _objHttpClient = objHttpClient;
        _objUnifiOptions = objOptions.Value;
    }

    public async Task AuthorizeGuestAsync(
        string sMac, int? iAccessMinutes = null, CancellationToken objCancellationToken = default)
    {
        string? sCsrfToken = await LoginAsync(objCancellationToken);

        string sAuthorizePath = _objUnifiOptions.UnifiOs
            ? $"/proxy/network/api/s/{_objUnifiOptions.Site}/cmd/stamgr"
            : $"/api/s/{_objUnifiOptions.Site}/cmd/stamgr";

        AuthorizeGuestPayload objPayload = new AuthorizeGuestPayload(
            Cmd: "authorize-guest",
            Mac: sMac.ToLowerInvariant(),
            Minutes: iAccessMinutes ?? _objUnifiOptions.AccessMinutes);

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
            objResponse = await _objHttpClient.SendAsync(objRequest, objCancellationToken);
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

    /// <summary>Autentica na controladora e devolve o x-csrf-token, quando presente.</summary>
    private async Task<string?> LoginAsync(CancellationToken objCancellationToken)
    {
        string sLoginPath = _objUnifiOptions.UnifiOs ? "/api/auth/login" : "/api/login";
        LoginPayload objPayload = new LoginPayload(_objUnifiOptions.Username, _objUnifiOptions.Password);

        HttpResponseMessage objResponse;
        try
        {
            objResponse = await _objHttpClient.PostAsJsonAsync(
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
