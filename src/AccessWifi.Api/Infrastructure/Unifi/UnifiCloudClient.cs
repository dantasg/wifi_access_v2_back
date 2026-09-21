using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Models.DataBase;
using Models.Security;

namespace AccessWifi.Api.Infrastructure.Unifi;

/// <summary>
/// Fala com a controladora pela nuvem da Ubiquiti (Site Manager Connector Proxy), para unidades
/// sem IP público nem DDNS. Nosso servidor chama api.ui.com e a Ubiquiti repassa o comando ao
/// console pelo túnel que o próprio equipamento mantém aberto — sem abrir porta, sem VPN e sem
/// senha de administrador (a autenticação é por chave de API).
///
/// São três chamadas por visitante: descobrir o site (só na primeira vez), achar o aparelho pelo
/// MAC e autorizar. Validado em campo na unidade Itaituba em 2026-09-20.
/// </summary>
public partial class UnifiCloudClient : IUnifiClient
{
    /// <summary>Nome do HttpClient nomeado registrado no Program.cs.</summary>
    public const string HttpClientName = "unifi-cloud";

    private const string ApiKeyHeader = "X-API-KEY";

    /// <summary>
    /// D6: o aparelho acabou de se conectar e pode ainda não constar na lista da controladora.
    /// Uma segunda tentativa curta resolve a corrida sem prender o visitante na tela.
    /// </summary>
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(1500);

    private static readonly JsonSerializerOptions s_objJsonOptions =
        new JsonSerializerOptions(JsonSerializerDefaults.Web);

    // O ConsoleId entra na URL: restringir o formato evita que um valor digitado errado (ou de
    // má-fé) escape do caminho previsto.
    [GeneratedRegex("^[A-Za-z0-9:_-]{10,120}$")]
    private static partial Regex ConsoleIdRegex();

    [GeneratedRegex("^[0-9a-f]{12}$")]
    private static partial Regex MacRegex();

    private readonly IHttpClientFactory _objHttpClientFactory;
    private readonly IEncryptor _objEncryptor;

    public UnifiCloudClient(IHttpClientFactory objHttpClientFactory, IEncryptor objEncryptor)
    {
        _objHttpClientFactory = objHttpClientFactory;
        _objEncryptor = objEncryptor;
    }

    private record PagedResponse<T>(List<T>? Data, int TotalCount);
    private record SiteItem(string Id, string? InternalReference, string? Name);
    private record ClientItem(string Id, string? MacAddress);
    private record AuthorizeGuestPayload(string Action, int TimeLimitMinutes);

    public async Task AuthorizeGuestAsync(
        CompanyUnifi objConfig, string sMac, int iAccessMinutes,
        CancellationToken objCancellationToken = default)
    {
        HttpClient objHttpClient = _objHttpClientFactory.CreateClient(HttpClientName);
        string sApiKey = ReadApiKey(objConfig);
        string sBasePath = BuildBasePath(objConfig);
        string sNormalizedMac = NormalizeMac(sMac);

        string sSiteId = await EnsureSiteIdAsync(
            objHttpClient, objConfig, sApiKey, sBasePath, objCancellationToken);

        string? sClientId = await FindClientIdAsync(
            objHttpClient, sApiKey, sBasePath, sSiteId, sNormalizedMac, objCancellationToken);
        if (sClientId is null)
        {
            await Task.Delay(RetryDelay, objCancellationToken);
            sClientId = await FindClientIdAsync(
                objHttpClient, sApiKey, sBasePath, sSiteId, sNormalizedMac, objCancellationToken);
        }

        if (sClientId is null)
        {
            throw new UnifiException(
                "Aparelho não encontrado na rede da unidade (ainda não apareceu na controladora).");
        }

        AuthorizeGuestPayload objPayload = new AuthorizeGuestPayload(
            Action: "AUTHORIZE_GUEST_ACCESS",
            TimeLimitMinutes: iAccessMinutes);

        using HttpRequestMessage objRequest = new HttpRequestMessage(
            HttpMethod.Post, $"{sBasePath}/sites/{sSiteId}/clients/{sClientId}/actions")
        {
            Content = JsonContent.Create(objPayload, options: s_objJsonOptions),
        };
        objRequest.Headers.Add(ApiKeyHeader, sApiKey);

        using HttpResponseMessage objResponse = await SendAsync(
            objHttpClient, objRequest, objCancellationToken);
        await EnsureSuccessAsync(objResponse, "autorizar o visitante", objCancellationToken);
    }

    public async Task<string> TestConnectionAsync(
        CompanyUnifi objConfig, CancellationToken objCancellationToken = default)
    {
        HttpClient objHttpClient = _objHttpClientFactory.CreateClient(HttpClientName);
        string sApiKey = ReadApiKey(objConfig);
        string sBasePath = BuildBasePath(objConfig);

        List<SiteItem> objSites = await ListSitesAsync(
            objHttpClient, sApiKey, sBasePath, objCancellationToken);
        string sSiteId = await EnsureSiteIdAsync(
            objHttpClient, objConfig, sApiKey, sBasePath, objCancellationToken);

        SiteItem? objSite = objSites.FirstOrDefault(site => site.Id == sSiteId);
        return $"Console respondeu pela nuvem. Site em uso: \"{objSite?.Name ?? sSiteId}\""
            + $" ({objSites.Count} site(s) no console).";
    }

    /// <summary>
    /// D3: usa o site já gravado na unidade; se não houver, descobre pela API e grava no objeto
    /// (quem chamou persiste). Com mais de um site, exige escolha manual — adivinhar seria pior.
    /// </summary>
    private async Task<string> EnsureSiteIdAsync(
        HttpClient objHttpClient, CompanyUnifi objConfig, string sApiKey, string sBasePath,
        CancellationToken objCancellationToken)
    {
        if (Guid.TryParse(objConfig.SiteId, out _))
        {
            return objConfig.SiteId;
        }

        List<SiteItem> objSites = await ListSitesAsync(
            objHttpClient, sApiKey, sBasePath, objCancellationToken);

        if (objSites.Count == 0)
        {
            throw new UnifiException("O console UniFi não devolveu nenhum site.");
        }

        if (objSites.Count > 1)
        {
            string sOpcoes = string.Join(", ", objSites.Select(site => $"{site.Name} ({site.Id})"));
            throw new UnifiException(
                $"O console tem mais de um site; informe qual usar na unidade. Opções: {sOpcoes}.");
        }

        objConfig.SiteId = objSites[0].Id;
        return objConfig.SiteId;
    }

    private async Task<List<SiteItem>> ListSitesAsync(
        HttpClient objHttpClient, string sApiKey, string sBasePath,
        CancellationToken objCancellationToken)
    {
        using HttpRequestMessage objRequest =
            new HttpRequestMessage(HttpMethod.Get, $"{sBasePath}/sites");
        objRequest.Headers.Add(ApiKeyHeader, sApiKey);

        using HttpResponseMessage objResponse = await SendAsync(
            objHttpClient, objRequest, objCancellationToken);
        await EnsureSuccessAsync(objResponse, "listar os sites do console", objCancellationToken);

        PagedResponse<SiteItem>? objPage = await ReadJsonAsync<PagedResponse<SiteItem>>(
            objResponse, objCancellationToken);
        return objPage?.Data ?? [];
    }

    private async Task<string?> FindClientIdAsync(
        HttpClient objHttpClient, string sApiKey, string sBasePath, string sSiteId,
        string sMac, CancellationToken objCancellationToken)
    {
        // O MAC já passou pelo NormalizeMac, então não há como escapar da expressão de filtro.
        string sFilter = Uri.EscapeDataString($"macAddress.eq('{sMac}')");

        using HttpRequestMessage objRequest = new HttpRequestMessage(
            HttpMethod.Get, $"{sBasePath}/sites/{sSiteId}/clients?filter={sFilter}");
        objRequest.Headers.Add(ApiKeyHeader, sApiKey);

        using HttpResponseMessage objResponse = await SendAsync(
            objHttpClient, objRequest, objCancellationToken);
        await EnsureSuccessAsync(objResponse, "procurar o aparelho na rede", objCancellationToken);

        PagedResponse<ClientItem>? objPage = await ReadJsonAsync<PagedResponse<ClientItem>>(
            objResponse, objCancellationToken);
        return objPage?.Data?.FirstOrDefault()?.Id;
    }

    private string ReadApiKey(CompanyUnifi objConfig)
    {
        string sApiKey = _objEncryptor.Decrypt(objConfig.ApiKey) ?? "";
        if (string.IsNullOrWhiteSpace(sApiKey))
        {
            throw new UnifiException("Chave de API da nuvem UniFi não configurada para esta unidade.");
        }
        return sApiKey;
    }

    private static string BuildBasePath(CompanyUnifi objConfig)
    {
        string sConsoleId = objConfig.ConsoleId?.Trim() ?? "";
        if (!ConsoleIdRegex().IsMatch(sConsoleId))
        {
            throw new UnifiException("Console da nuvem UniFi não configurado (ou em formato inválido).");
        }
        return $"v1/connector/consoles/{sConsoleId}/proxy/network/integration/v1";
    }

    /// <summary>Aceita "aa:bb:...", "AA-BB-..." ou "aabb..." e devolve no formato da UniFi.</summary>
    private static string NormalizeMac(string sMac)
    {
        string sOnlyHex = new string((sMac ?? "").Where(Uri.IsHexDigit).ToArray()).ToLowerInvariant();

        if (!MacRegex().IsMatch(sOnlyHex))
        {
            throw new UnifiException("MAC do aparelho em formato inválido.");
        }

        return string.Join(':', Enumerable.Range(0, 6).Select(i => sOnlyHex.Substring(i * 2, 2)));
    }

    private static async Task<HttpResponseMessage> SendAsync(
        HttpClient objHttpClient, HttpRequestMessage objRequest,
        CancellationToken objCancellationToken)
    {
        try
        {
            return await objHttpClient.SendAsync(objRequest, objCancellationToken);
        }
        catch (HttpRequestException objException)
        {
            throw new UnifiException("Não foi possível falar com a nuvem da UniFi.", objException);
        }
        catch (TaskCanceledException objException)
            when (!objCancellationToken.IsCancellationRequested)
        {
            throw new UnifiException("A nuvem da UniFi demorou demais para responder.", objException);
        }
    }

    private static async Task<T?> ReadJsonAsync<T>(
        HttpResponseMessage objResponse, CancellationToken objCancellationToken)
    {
        try
        {
            return await objResponse.Content.ReadFromJsonAsync<T>(
                s_objJsonOptions, objCancellationToken);
        }
        catch (JsonException objException)
        {
            throw new UnifiException("A nuvem da UniFi devolveu uma resposta inesperada.", objException);
        }
    }

    /// <summary>
    /// D11: cada erro tem causa e solução diferentes, então a mensagem precisa distinguir —
    /// todos estes foram observados no teste real com a controladora da Itaituba.
    /// </summary>
    private static async Task EnsureSuccessAsync(
        HttpResponseMessage objResponse, string sAcao, CancellationToken objCancellationToken)
    {
        if (objResponse.IsSuccessStatusCode)
        {
            return;
        }

        string sBody = await objResponse.Content.ReadAsStringAsync(objCancellationToken);

        string sMessage = objResponse.StatusCode switch
        {
            HttpStatusCode.Unauthorized =>
                "Chave de API da nuvem UniFi inválida ou revogada.",
            HttpStatusCode.Forbidden =>
                "A chave de API não alcança este console UniFi (confira o escopo da chave).",
            HttpStatusCode.UnprocessableEntity
                when sBody.Contains("not-guest", StringComparison.OrdinalIgnoreCase) =>
                "A rede da unidade não está configurada como rede de visitantes (Hotspot).",
            HttpStatusCode.TooManyRequests =>
                "Limite de chamadas da nuvem UniFi atingido; tente de novo em instantes.",
            _ => $"A nuvem da UniFi recusou {sAcao} (HTTP {(int)objResponse.StatusCode}).",
        };

        throw new UnifiException(sMessage);
    }
}
