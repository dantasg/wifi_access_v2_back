using AccessWifi.Api.Controllers;
using AccessWifi.Api.Features;
using AccessWifi.Api.Features.Emails;
using Microsoft.AspNetCore.Mvc;
using Models.DataBase;
using Models.Persistence;

namespace AccessWifi.Api.Tests;

/// <summary>Correio eletrônico: quem vê o quê, ordem, filtros e paginação.</summary>
public class EmailsControllerTests
{
    private sealed class Scenario
    {
        public required Company Regional { get; init; }
        public required Company Other { get; init; }
        public required Unit Itaituba { get; init; }
        public required Unit Castanhal { get; init; }
    }

    private static Scenario BuildScenario(AppDbContext objDbContext)
    {
        Company objRegional = new Company { Name = "Lojas Regional", Slug = "regional" };
        Company objOther = new Company { Name = "Outra", Slug = "outra" };
        Unit objItaituba = new Unit { IDCompany = objRegional.Id, Name = "Itaituba", Slug = "itaituba" };
        Unit objCastanhal = new Unit { IDCompany = objRegional.Id, Name = "Castanhal", Slug = "castanhal" };
        Unit objOtherUnit = new Unit { IDCompany = objOther.Id, Name = "Da outra", Slug = "da-outra" };
        objDbContext.AddRange(objRegional, objOther, objItaituba, objCastanhal, objOtherUnit);
        // Admin da empresa inteira (o acesso é lido do banco pelo usuário do token).
        objDbContext.Users.Add(new AdminUser { Username = "admin.regional", PasswordHash = "hash", IDCompany = objRegional.Id });

        DateTime dtBase = new DateTime(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);
        objDbContext.SentEmails.AddRange(
            Email(objRegional, objItaituba, SentEmailKind.Report, "Relatório Itaituba", dtBase),
            Email(objRegional, objItaituba, SentEmailKind.Campaign, "Campanha Itaituba", dtBase.AddDays(2)),
            Email(objRegional, objCastanhal, SentEmailKind.Campaign, "Campanha Castanhal", dtBase.AddDays(1)),
            Email(objOther, objOtherUnit, SentEmailKind.Report, "Relatório da outra", dtBase.AddDays(3)));
        objDbContext.SaveChanges();
        return new Scenario { Regional = objRegional, Other = objOther, Itaituba = objItaituba, Castanhal = objCastanhal };
    }

    private static SentEmail Email(Company objCompany, Unit objUnit, string sKind, string sSubject, DateTime dtSentAt) =>
        new SentEmail
        {
            IDCompany = objCompany.Id, IDUnit = objUnit.Id, UnitName = objUnit.Name, Kind = sKind,
            ToEmail = $"gerente.{objUnit.Slug}@exemplo.com.br", Subject = sSubject, Body = $"Texto de {sSubject}",
            AttachmentName = "anexo", SentAt = dtSentAt,
        };

    private static EmailsController Controller(AppDbContext objDbContext, Guid? objCompanyId, string? sUsername = "admin.regional")
    {
        EmailsController objController = new EmailsController(objDbContext);
        TestHelpers.SetUser(objController, objCompanyId, sUsername);
        return objController;
    }

    private static SentEmailPageDto GetPage(ActionResult<SentEmailPageDto> objResult) =>
        Assert.IsType<SentEmailPageDto>(Assert.IsType<OkObjectResult>(objResult.Result).Value);

    [Fact]
    public async Task List_CompanyAdmin_SeesOnlyOwnNewestFirst()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Scenario objC = BuildScenario(objDbContext);

        SentEmailPageDto objPage = GetPage(await Controller(objDbContext, objC.Regional.Id)
            .List(null, null, null, 1, 25, CancellationToken.None));

        Assert.Equal(3, objPage.Total);
        Assert.Equal(
            new[] { "Campanha Itaituba", "Campanha Castanhal", "Relatório Itaituba" },
            objPage.Items.Select(item => item.Subject));
    }

    [Fact]
    public async Task List_UnitAndKindFiltersAndPaging()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Scenario objC = BuildScenario(objDbContext);
        EmailsController objController = Controller(objDbContext, objC.Regional.Id);

        SentEmailPageDto objItaituba = GetPage(await objController.List(null, "itaituba", null, 1, 25, CancellationToken.None));
        Assert.Equal(2, objItaituba.Total);

        SentEmailPageDto objReports = GetPage(await objController.List(null, null, SentEmailKind.Report, 1, 25, CancellationToken.None));
        Assert.Equal("Relatório Itaituba", Assert.Single(objReports.Items).Subject);

        SentEmailPageDto objSecond = GetPage(await objController.List(null, null, null, 2, 2, CancellationToken.None));
        Assert.Equal(3, objSecond.Total);
        Assert.Equal("Relatório Itaituba", Assert.Single(objSecond.Items).Subject);

        SentEmailPageDto objNothing = GetPage(await objController.List(null, "nao-existe", null, 1, 25, CancellationToken.None));
        Assert.Equal(0, objNothing.Total);
    }

    [Fact]
    public async Task List_SuperAdmin_NeedsCompanyAndSeesGivenOne()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        BuildScenario(objDbContext);
        EmailsController objController = Controller(objDbContext, null, "root");

        Assert.IsType<BadRequestObjectResult>((await objController.List(null, null, null, 1, 25, CancellationToken.None)).Result);
        SentEmailPageDto objOther = GetPage(await objController.List("outra", null, null, 1, 25, CancellationToken.None));
        Assert.Equal("Relatório da outra", Assert.Single(objOther.Items).Subject);
    }

    [Fact]
    public async Task List_UnitUser_SeesOnlyOwnUnits()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Scenario objC = BuildScenario(objDbContext);
        objDbContext.Users.Add(new AdminUser
        {
            Username = "loja.castanhal", PasswordHash = "hash", IDCompany = objC.Regional.Id, RestrictToUnits = true,
            Units = [new AdminUserUnit { IDUnit = objC.Castanhal.Id }],
        });
        objDbContext.SaveChanges();
        EmailsController objController = Controller(objDbContext, objC.Regional.Id, "loja.castanhal");

        SentEmailPageDto objPage = GetPage(await objController.List(null, null, null, 1, 25, CancellationToken.None));
        Assert.Equal("Campanha Castanhal", Assert.Single(objPage.Items).Subject);

        // O de outra loja não abre nem pelo endereço direto.
        Guid objFromItaituba = objDbContext.SentEmails.Single(email => email.Subject == "Campanha Itaituba").Id;
        Assert.IsType<NotFoundObjectResult>((await objController.Get(objFromItaituba, null, CancellationToken.None)).Result);
    }

    [Fact]
    public async Task Get_ReturnsTextAndOtherCompanyGives404()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Scenario objC = BuildScenario(objDbContext);
        EmailsController objController = Controller(objDbContext, objC.Regional.Id);
        Guid objFromRegional = objDbContext.SentEmails.Single(email => email.Subject == "Relatório Itaituba").Id;
        Guid objOtherUnit = objDbContext.SentEmails.Single(email => email.Subject == "Relatório da outra").Id;

        SentEmailDto objEmail = Assert.IsType<SentEmailDto>(
            Assert.IsType<OkObjectResult>((await objController.Get(objFromRegional, null, CancellationToken.None)).Result).Value);
        Assert.Equal("Texto de Relatório Itaituba", objEmail.Body);

        NotFoundObjectResult objNotFound = Assert.IsType<NotFoundObjectResult>(
            (await objController.Get(objOtherUnit, null, CancellationToken.None)).Result);
        Assert.Equal("E-mail não encontrado.", Assert.IsType<ErrorResponse>(objNotFound.Value).Error);
    }
}
