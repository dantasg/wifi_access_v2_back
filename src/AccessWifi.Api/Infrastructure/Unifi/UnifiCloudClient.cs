using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Caching.Memory;
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
/// MAC e autorizar. A busca do aparelho é adiantada enquanto ele preenche o formulário
/// (PrepareAsync), então na hora do toque em "Conectar" sobra só a autorização.
/// Validado em campo na unidade Itaituba em 2026-09-20.
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

    /// <summary>
    /// Quanto tempo o ID do aparelho buscado pelo PrepareAsync fica guardado. Cobre com folga o
    /// tempo de preencher o formulário; o ID de um aparelho não muda nesse intervalo.
    /// </summary>
    private static readonly TimeSpan PreparedTtl = TimeSpan.FromMinutes(15);

    /// <summary>Limite da busca em segundo plano (não há requisição do visitante esperando).</summary>
    private static readonly TimeSpan PrepareTimeout = TimeSpan.FromSeconds(20);

    private readonly IHttpClientFactory _objHttpClientFactory;
    private readonly IEncryptor _objEncryptor;
    private readonly IMemoryCache _objCache;

    public UnifiCloudClient(
        IHttpClientFactory objHttpClientFactory, IEncryptor objEncryptor, IMemoryCache objCache)
    {
        _objHttpClientFactory = objHttpClientFactory;
        _objEncryptor = objEncryptor;
        _objCache = objCache;
    }

    private record PagedResponse<T>(List<T>? Data, int TotalCount);
    private record SiteItem(string Id, string? InternalReference, string? Name);
    private record ClientItem(string Id, string? MacAddress);
    private record AuthorizeGuestPayload(string Action, int TimeLimitMinutes);

    /// <summary>
    /// Adianta a busca do ID do aparelho enquanto o visitante preenche o formulário (medido em campo:
    /// cada ida ao console da loja leva 0,4–1 s, e o cliente leva 30–70 s no formulário). Guarda a
    /// busca — inclusive enquanto ainda está em andamento — para o AuthorizeGuestAsync só autorizar.
    /// Nunca lança: qualquer problema aqui só faz o /authorize buscar como sempre buscou.
    /// </summary>
    public Task PrepareAsync(
        CompanyUnifi objConfig, string sMac, CancellationToken objCancellationToken = default)
    {
        // Sem o site já conhecido não prepara: descobrir o site grava na entidade, e aqui (segundo
        // plano, depois da requisição) não há quem persista.
        if (!Guid.TryParse(objConfig.SiteId, out _))
        {
            return Task.CompletedTask;
        }

        string sApiKey, sBasePath, sNormalizedMac;
        try
        {
            sApiKey = ReadApiKey(objConfig);
            sBasePath = BuildBasePath(objConfig);
            sNormalizedMac = NormalizeMac(sMac);
        }
        catch (UnifiException)
        {
            return Task.CompletedTask; // configuração ou MAC inválidos: o /authorize reporta direito
        }

        string sKey = CacheKey(objConfig, sNormalizedMac);
        if (!_objCache.TryGetValue(sKey, out Task<string?>? _))
        {
            Task<string?> objBusca = FindClientIdDetachedAsync(sApiKey, sBasePath, objConfig.SiteId, sNormalizedMac);
            _objCache.Set(sKey, objBusca, PreparedTtl);
            // Resultado inútil não fica guardado: sem achar (ou com erro), o /authorize busca de novo.
            _ = objBusca.ContinueWith(objTask =>
            {
                _ = objTask.Exception; // marca a exceção como observada
                if (!objTask.IsCompletedSuccessfully || objTask.Result is null)
                {
                    _objCache.Remove(sKey);
                }
            }, TaskScheduler.Default);
        }

        return Task.CompletedTask;
    }

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
        string sKey = CacheKey(objConfig, sNormalizedMac);

        // Caminho rápido: o ID já foi buscado enquanto o visitante preenchia o formulário.
        string? sClientId = await TryGetPreparedClientIdAsync(sKey, objCancellationToken);
        bool bUsouPreparado = sClientId is not null;
        sClientId ??= await FindClientIdWithRetryAsync(
            objHttpClient, sApiKey, sBasePath, sSiteId, sNormalizedMac, objCancellationToken);

        HttpResponseMessage objResponse = await SendAuthorizeAsync(
            objHttpClient, sApiKey, sBasePath, sSiteId, sClientId, iAccessMinutes, objCancellationToken);

        if (bUsouPreparado && objResponse.StatusCode == HttpStatusCode.NotFound)
        {
            // O ID guardado não vale mais (o aparelho saiu e voltou, a controladora recriou o
            // registro…). Descarta e faz o caminho completo uma vez.
            objResponse.Dispose();
            _objCache.Remove(sKey);
            sClientId = await FindClientIdWithRetryAsync(
                objHttpClient, sApiKey, sBasePath, sSiteId, sNormalizedMac, objCancellationToken);
            objResponse = await SendAuthorizeAsync(
                objHttpClient, sApiKey, sBasePath, sSiteId, sClientId, iAccessMinutes, objCancellationToken);
        }

        using (objResponse)
        {
            await EnsureSuccessAsync(objResponse, "autorizar o visitante", objCancellationToken);
        }
    }

    private static string CacheKey(CompanyUnifi objConfig, string sNormalizedMac)
    {
        return $"unifi-cloud-client:{objConfig.ConsoleId.Trim()}:{objConfig.SiteId}:{sNormalizedMac}";
    }

    /// <summary>ID preparado, esperando a busca terminar se ela ainda estiver em andamento.</summary>
    private async Task<string?> TryGetPreparedClientIdAsync(string sKey, CancellationToken objCancellationToken)
    {
        if (!_objCache.TryGetValue(sKey, out Task<string?>? objBusca) || objBusca is null)
        {
            return null;
        }

        try
        {
            return await objBusca.WaitAsync(objCancellationToken);
        }
        catch (Exception objException) when ((objException is UnifiException or OperationCanceledException)
            && !objCancellationToken.IsCancellationRequested)
        {
            return null; // a preparação falhou: segue pelo caminho completo
        }
    }

    private async Task<string?> FindClientIdDetachedAsync(
        string sApiKey, string sBasePath, string sSiteId, string sNormalizedMac)
    {
        // Não usa o token da requisição: ela já terminou (a rota responde 202 na hora).
        using CancellationTokenSource objTimeout = new CancellationTokenSource(PrepareTimeout);
        HttpClient objHttpClient = _objHttpClientFactory.CreateClient(HttpClientName);
        return await FindClientIdAsync(
            objHttpClient, sApiKey, sBasePath, sSiteId, sNormalizedMac, objTimeout.Token);
    }

    private async Task<string> FindClientIdWithRetryAsync(
        HttpClient objHttpClient, string sApiKey, string sBasePath, string sSiteId,
        string sNormalizedMac, CancellationToken objCancellationToken)
    {
        string? sClientId = await FindClientIdAsync(
            objHttpClient, sApiKey, sBasePath, sSiteId, sNormalizedMac, objCancellationToken);
        if (sClientId is null)
        {
            await Task.Delay(RetryDelay, objCancellationToken);
            sClientId = await FindClientIdAsync(
                objHttpClient, sApiKey, sBasePath, sSiteId, sNormalizedMac, objCancellationToken);
        }

        return sClientId ?? throw new UnifiException(
            "Aparelho não encontrado na rede da unidade (ainda não apareceu na controladora).");
    }

    private static async Task<HttpResponseMessage> SendAuthorizeAsync(
        HttpClient objHttpClient, string sApiKey, string sBasePath, string sSiteId, string sClientId,
        int iAccessMinutes, CancellationToken objCancellationToken)
    {
        AuthorizeGuestPayload objPayload = new AuthorizeGuestPayload(
            Action: "AUTHORIZE_GUEST_ACCESS",
            TimeLimitMinutes: iAccessMinutes);

        using HttpRequestMessage objRequest = new HttpRequestMessage(
            HttpMethod.Post, $"{sBasePath}/sites/{sSiteId}/clients/{sClientId}/actions")
        {
            Content = JsonContent.Create(objPayload, options: s_objJsonOptions),
        };
        objRequest.Headers.Add(ApiKeyHeader, sApiKey);

        return await SendAsync(objHttpClient, objRequest, objCancellationToken);
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
