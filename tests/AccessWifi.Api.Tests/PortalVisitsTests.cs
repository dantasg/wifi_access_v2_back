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
public class PortalVisitsTests
{
    private class FakeUnifiClient : IUnifiClient
    {
        public bool Fail { get; set; }

        public Task AuthorizeGuestAsync(
            CompanyUnifi objConfig, string sMac, int iAccessMinutes, CancellationToken objCancellationToken = default)
        {
            if (Fail)
            {
                throw new UnifiException("Simulação de falha.");
            }
            return Task.CompletedTask;
        }

        public Task<string> TestConnectionAsync(CompanyUnifi objConfig, CancellationToken objCancellationToken = default) =>
            Task.FromResult("ok");
    }

    private static (Unit Itaituba, Unit Castanhal) CreateStores(AppDbContext objDb)
    {
        Company objCompany = new Company { Name = "Lojas Regional", Slug = "regional" };
        objDb.Companies.Add(objCompany);
        Unit objItaituba = new Unit { IDCompany = objCompany.Id, Name = "Itaituba", Slug = "itaituba" };
        Unit objCastanhal = new Unit { IDCompany = objCompany.Id, Name = "Castanhal", Slug = "castanhal" };
        objDb.Units.AddRange(objItaituba, objCastanhal);
        objDb.SaveChanges();
        return (objItaituba, objCastanhal);
    }

    private static AuthorizeRequest MakeRequest(
        string sUnit, string sMac = "aa:bb:cc:dd:ee:01", string sPhone = "(93) 98888-1234",
        string? sAp = "8C-30-66-4E-9B-58") =>
        new AuthorizeRequest(
            Name: "Ana Teste", Instagram: "@ana", Phone: sPhone, BirthDate: "10/05/1990",
            Consent: true, Unit: sUnit, Mac: sMac, Ap: sAp, Ssid: "PIX REGIONAL",
            Url: "http://www.msftconnecttest.com/redirect");

    private static async Task<ActionResult<AuthorizeResponse>> ConnectAsync(
        AppDbContext objDb, AuthorizeRequest objRequest, bool bFail = false)
    {
        AuthorizeController objController = new AuthorizeController(
            objDb, new FakeUnifiClient { Fail = bFail }, NullLogger<AuthorizeController>.Instance);
        return await objController.Post(objRequest, CancellationToken.None);
    }

    [Fact]
    public async Task FirstVisit_SavesNewVisit_InCompanyTimeZone()
    {
        using AppDbContext objDb = TestHelpers.CreateDbContext();
        (Unit objItaituba, _) = CreateStores(objDb);

        ActionResult<AuthorizeResponse> objResult = await ConnectAsync(objDb, MakeRequest("itaituba"));

        AuthorizeResponse objResponse = Assert.IsType<AuthorizeResponse>(Assert.IsType<OkObjectResult>(objResult.Result).Value);
        Assert.True(objResponse.Authorized);
        Assert.Equal("http://www.msftconnecttest.com/redirect", objResponse.Redirect);

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
    public async Task Returned_SameStore_IsNotNew()
    {
        using AppDbContext objDb = TestHelpers.CreateDbContext();
        CreateStores(objDb);

        await ConnectAsync(objDb, MakeRequest("itaituba"));
        await ConnectAsync(objDb, MakeRequest("itaituba"));

        List<Visit> objVisits = objDb.Visits.OrderBy(visit => visit.Id).ToList();
        Assert.Equal(2, objVisits.Count);
        Assert.Equal(objVisits[0].IDCustomer, objVisits[1].IDCustomer);
        Assert.False(objVisits[1].NewInCompany);
        Assert.False(objVisits[1].NewInUnit);
    }

    [Fact]
    public async Task OtherStoreOfCompany_NewInUnit_ButNotInCompany()
    {
        using AppDbContext objDb = TestHelpers.CreateDbContext();
        (_, Unit objCastanhal) = CreateStores(objDb);

        await ConnectAsync(objDb, MakeRequest("itaituba"));
        await ConnectAsync(objDb, MakeRequest("castanhal", sMac: "aa:bb:cc:dd:ee:02"));

        Visit objInCastanhal = objDb.Visits.Single(visit => visit.IDUnit == objCastanhal.Id);
        Assert.False(objInCastanhal.NewInCompany);
        Assert.True(objInCastanhal.NewInUnit);
        Assert.Single(objDb.Customers);
    }

    [Fact]
    public async Task UnifiRejected_DoesNotCountVisit_ButCustomerStaysSaved()
    {
        using AppDbContext objDb = TestHelpers.CreateDbContext();
        CreateStores(objDb);

        ActionResult<AuthorizeResponse> objResult = await ConnectAsync(objDb, MakeRequest("itaituba"), bFail: true);

        Assert.Equal(502, Assert.IsType<ObjectResult>(objResult.Result).StatusCode);
        Assert.Empty(objDb.Visits);
        Assert.Single(objDb.Customers);
    }

    [Fact]
    public async Task UnidentifiablePhone_CountsByDevice()
    {
        using AppDbContext objDb = TestHelpers.CreateDbContext();
        CreateStores(objDb);

        await ConnectAsync(objDb, MakeRequest("itaituba", sPhone: "123"));
        await ConnectAsync(objDb, MakeRequest("itaituba", sPhone: "123"));

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
    public async Task ApMissingOrInvalid_SavesEmpty(string? sAp)
    {
        using AppDbContext objDb = TestHelpers.CreateDbContext();
        CreateStores(objDb);

        await ConnectAsync(objDb, MakeRequest("itaituba", sAp: sAp));

        Assert.Equal("", Assert.Single(objDb.Visits).Ap);
    }
}
