using AccessWifi.Api.Infrastructure.Unifi;
using Microsoft.Extensions.Caching.Memory;
using Models.DataBase;

namespace AccessWifi.Api.Tests;

/// <summary>
/// D1: os dois modos convivem e quem decide é o campo Mode da unidade. Os testes identificam qual
/// cliente atendeu pela mensagem de erro de configuração vazia — cada um reclama de algo diferente,
/// então não é preciso simular a rede para saber por onde a chamada foi.
/// </summary>
public class UnifiClientRouterTests
{
    private class StubHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string sName) => new HttpClient();
    }

    private static UnifiClientRouter CreateRouter()
    {
        return new UnifiClientRouter(
            new UnifiLocalClient(TestHelpers.CreateEncryptor()),
            new UnifiCloudClient(
                new StubHttpClientFactory(), TestHelpers.CreateEncryptor(),
                new MemoryCache(new MemoryCacheOptions())));
    }

    [Fact]
    public async Task ModoCloud_UsaOClienteDaNuvem()
    {
        CompanyUnifi objConfig = new CompanyUnifi { Mode = UnifiMode.Cloud };

        UnifiException objException = await Assert.ThrowsAsync<UnifiException>(
            () => CreateRouter().AuthorizeGuestAsync(objConfig, "36:9d:94:1e:aa:10", 60));

        // Só o cliente da nuvem fala em "nuvem UniFi"; o local reclama da "Controladora".
        Assert.Contains("da nuvem UniFi", objException.Message);
    }

    [Theory]
    [InlineData("Local")]
    [InlineData("")]
    [InlineData("qualquer-coisa")]
    public async Task ModoDiferenteDeCloud_UsaOClienteLocal(string sMode)
    {
        // Qualquer valor inesperado cai no local, que é o comportamento histórico — nunca na nuvem.
        CompanyUnifi objConfig = new CompanyUnifi { Mode = sMode };

        UnifiException objException = await Assert.ThrowsAsync<UnifiException>(
            () => CreateRouter().AuthorizeGuestAsync(objConfig, "36:9d:94:1e:aa:10", 60));

        Assert.Contains("Controladora UniFi não configurada", objException.Message);
    }

    [Fact]
    public async Task ModoCloudEmMaiusculasOuMinusculas_ContinuaIndoParaANuvem()
    {
        CompanyUnifi objConfig = new CompanyUnifi { Mode = "cloud" };

        UnifiException objException = await Assert.ThrowsAsync<UnifiException>(
            () => CreateRouter().TestConnectionAsync(objConfig));

        // Só o cliente da nuvem fala em "nuvem UniFi"; o local reclama da "Controladora".
        Assert.Contains("da nuvem UniFi", objException.Message);
    }

    [Fact]
    public async Task Prepare_ModoLocal_ConcluiSemFazerNada()
    {
        // D6: no modo local a autorização já é uma chamada direta pelo MAC, não há o que adiantar.
        CompanyUnifi objConfig = new CompanyUnifi { Mode = UnifiMode.Local };

        await CreateRouter().PrepareAsync(objConfig, "36:9d:94:1e:aa:10");
    }
}
