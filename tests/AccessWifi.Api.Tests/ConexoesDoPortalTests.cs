using AccessWifi.Api.Controllers;
using AccessWifi.Api.Features.Authorize;
using AccessWifi.Api.Infrastructure.Unifi;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Models.DataBase;
using Models.Persistence;

namespace AccessWifi.Api.Tests;

/// <summary>
/// Registro de cada conexão liberada para o dashboard (PROPOSTA_DASHBOARD.md, D3). Nada disso pode mudar o
/// que o visitante vê: a resposta e o redirecionamento continuam iguais, e o cliente continua sendo gravado.
/// </summary>
public class ConexoesDoPortalTests
{
    private class FakeUnifiClient : IUnifiClient
    {
        public bool Falhar { get; set; }

        public Task AuthorizeGuestAsync(
            CompanyUnifi objConfig, string sMac, int iAccessMinutes, CancellationToken objCancellationToken = default)
        {
            if (Falhar)
            {
                throw new UnifiException("Simulação de falha.");
            }
            return Task.CompletedTask;
        }

        public Task<string> TestConnectionAsync(CompanyUnifi objConfig, CancellationToken objCancellationToken = default) =>
            Task.FromResult("ok");
    }

    private static (Unit Itaituba, Unit Castanhal) CreateLojas(AppDbContext objDb)
    {
        Company objCompany = new Company { Name = "Lojas Regional", Slug = "regional" };
        objDb.Companies.Add(objCompany);
        Unit objItaituba = new Unit { IDCompany = objCompany.Id, Name = "Itaituba", Slug = "itaituba" };
        Unit objCastanhal = new Unit { IDCompany = objCompany.Id, Name = "Castanhal", Slug = "castanhal" };
        objDb.Units.AddRange(objItaituba, objCastanhal);
        objDb.SaveChanges();
        return (objItaituba, objCastanhal);
    }

    private static AuthorizeRequest Pedido(
        string sUnit, string sMac = "aa:bb:cc:dd:ee:01", string sTelefone = "(93) 98888-1234",
        string? sAp = "8C-30-66-4E-9B-58") =>
        new AuthorizeRequest(
            Nome: "Ana Teste", Instagram: "@ana", Telefone: sTelefone, Nascimento: "10/05/1990",
            Consentimento: true, Unit: sUnit, Mac: sMac, Ap: sAp, Ssid: "PIX REGIONAL",
            Url: "http://www.msftconnecttest.com/redirect");

    private static async Task<ActionResult<AuthorizeResponse>> ConectarAsync(
        AppDbContext objDb, AuthorizeRequest objPedido, bool bFalhar = false)
    {
        AuthorizeController objController = new AuthorizeController(
            objDb, new FakeUnifiClient { Falhar = bFalhar }, NullLogger<AuthorizeController>.Instance);
        return await objController.Post(objPedido, CancellationToken.None);
    }

    [Fact]
    public async Task PrimeiraConexao_GravaUmaConexaoNova_NoFusoDaEmpresa()
    {
        using AppDbContext objDb = TestHelpers.CreateDbContext();
        (Unit objItaituba, _) = CreateLojas(objDb);

        ActionResult<AuthorizeResponse> objResult = await ConectarAsync(objDb, Pedido("itaituba"));

        AuthorizeResponse objResposta = Assert.IsType<AuthorizeResponse>(Assert.IsType<OkObjectResult>(objResult.Result).Value);
        Assert.True(objResposta.Authorized);
        Assert.Equal("http://www.msftconnecttest.com/redirect", objResposta.Redirect);

        Visit objVisit = Assert.Single(objDb.Visits);
        Customer objCustomer = Assert.Single(objDb.Customers);
        Assert.Equal(objItaituba.Id, objVisit.IDUnit);
        Assert.Equal(objCustomer.Id, objVisit.IDCustomer);
        Assert.True(objVisit.NewInCompany);
        Assert.True(objVisit.NewInUnit);
        Assert.Equal("8c:30:66:4e:9b:58", objVisit.Ap);

        DateTime dtBelem = TimeZoneInfo.ConvertTimeFromUtc(
            DateTime.SpecifyKind(objVisit.At, DateTimeKind.Utc), CompanyTimeZone.Resolve("America/Belem"));
        Assert.Equal(DateOnly.FromDateTime(dtBelem), objVisit.LocalDate);
        Assert.Equal(dtBelem.Hour, objVisit.LocalHour);
    }

    [Fact]
    public async Task Voltou_NaMesmaLoja_NaoEhNovo()
    {
        using AppDbContext objDb = TestHelpers.CreateDbContext();
        CreateLojas(objDb);

        await ConectarAsync(objDb, Pedido("itaituba"));
        await ConectarAsync(objDb, Pedido("itaituba"));

        List<Visit> objVisits = objDb.Visits.OrderBy(visit => visit.Id).ToList();
        Assert.Equal(2, objVisits.Count);
        Assert.Equal(objVisits[0].IDCustomer, objVisits[1].IDCustomer);
        Assert.False(objVisits[1].NewInCompany);
        Assert.False(objVisits[1].NewInUnit);
    }

    [Fact]
    public async Task OutraLojaDaEmpresa_NovoNaUnidade_MasNaoNaEmpresa()
    {
        using AppDbContext objDb = TestHelpers.CreateDbContext();
        (_, Unit objCastanhal) = CreateLojas(objDb);

        await ConectarAsync(objDb, Pedido("itaituba"));
        await ConectarAsync(objDb, Pedido("castanhal", sMac: "aa:bb:cc:dd:ee:02"));

        Visit objNaCastanhal = objDb.Visits.Single(visit => visit.IDUnit == objCastanhal.Id);
        Assert.False(objNaCastanhal.NewInCompany);
        Assert.True(objNaCastanhal.NewInUnit);
        Assert.Single(objDb.Customers);
    }

    [Fact]
    public async Task UnifiRecusou_NaoContaConexao_MasOClienteContinuaGravado()
    {
        using AppDbContext objDb = TestHelpers.CreateDbContext();
        CreateLojas(objDb);

        ActionResult<AuthorizeResponse> objResult = await ConectarAsync(objDb, Pedido("itaituba"), bFalhar: true);

        Assert.Equal(502, Assert.IsType<ObjectResult>(objResult.Result).StatusCode);
        Assert.Empty(objDb.Visits);
        Assert.Single(objDb.Customers);
    }

    [Fact]
    public async Task TelefoneQueNaoIdentifica_ContaPeloAparelho()
    {
        using AppDbContext objDb = TestHelpers.CreateDbContext();
        CreateLojas(objDb);

        await ConectarAsync(objDb, Pedido("itaituba", sTelefone: "123"));
        await ConectarAsync(objDb, Pedido("itaituba", sTelefone: "123"));

        List<Visit> objVisits = objDb.Visits.OrderBy(visit => visit.Id).ToList();
        Assert.Equal(2, objVisits.Count);
        Assert.All(objVisits, visit => Assert.Null(visit.IDCustomer));
        Assert.True(objVisits[0].NewInCompany && objVisits[0].NewInUnit);
        Assert.False(objVisits[1].NewInCompany || objVisits[1].NewInUnit);
        Assert.Empty(objDb.Customers);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("nao-e-mac")]
    public async Task ApAusenteOuInvalido_GravaVazio(string? sAp)
    {
        using AppDbContext objDb = TestHelpers.CreateDbContext();
        CreateLojas(objDb);

        await ConectarAsync(objDb, Pedido("itaituba", sAp: sAp));

        Assert.Equal("", Assert.Single(objDb.Visits).Ap);
    }
}
