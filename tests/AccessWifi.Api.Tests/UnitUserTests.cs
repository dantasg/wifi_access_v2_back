using AccessWifi.Api.Controllers;
using AccessWifi.Api.Features;
using AccessWifi.Api.Features.Admin;
using AccessWifi.Api.Features.Campaigns;
using AccessWifi.Api.Features.Leads;
using AccessWifi.Api.Features.Units;
using AccessWifi.Api.Infrastructure.Security;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Models.Campaigns;
using Models.DataBase;
using Models.Persistence;

namespace AccessWifi.Api.Tests;

/// <summary>
/// Usuário de unidade: vê só as unidades dele (cadastros, unidades e campanhas) e não gerencia
/// usuários. O admin da empresa gerencia os usuários da própria empresa.
/// </summary>
public class UnitUserTests
{
    private sealed record Scenario(Company Company, Company Other, Unit Itaituba, Unit Castanhal, Unit OtherUnit);

    private static Scenario BuildScenario(AppDbContext objDbContext)
    {
        Company objCompany = new Company { Name = "Lojas Regional", Slug = "regional", TimeZone = "America/Belem" };
        Company objOther = new Company { Name = "Outra", Slug = "outra" };
        objDbContext.Companies.AddRange(objCompany, objOther);
        Unit objItaituba = new Unit { IDCompany = objCompany.Id, Name = "Itaituba", Slug = "itaituba" };
        Unit objCastanhal = new Unit { IDCompany = objCompany.Id, Name = "Castanhal", Slug = "castanhal" };
        Unit objOtherUnit = new Unit { IDCompany = objOther.Id, Name = "Da outra", Slug = "da-outra" };
        objDbContext.Units.AddRange(objItaituba, objCastanhal, objOtherUnit);
        objDbContext.Leads.AddRange(
            new Lead { IDUnit = objItaituba.Id, Name = "Cliente de Itaituba" },
            new Lead { IDUnit = objCastanhal.Id, Name = "Cliente de Castanhal" });
        objDbContext.SaveChanges();
        return new Scenario(objCompany, objOther, objItaituba, objCastanhal, objOtherUnit);
    }

    private static TokenService CreateTokenService() =>
        new TokenService(Options.Create(new JwtOptions { Secret = "segredo-de-teste-3f9a1c7e5b2d8046a1e9c3b7d5f20486" }));

    private static T Ok<T>(ActionResult<T> objResult) =>
        Assert.IsType<T>(Assert.IsType<OkObjectResult>(objResult.Result).Value);

    private static string ErrorMessage<T>(ActionResult<T> objResult) =>
        Assert.IsType<ErrorResponse>(Assert.IsType<BadRequestObjectResult>(objResult.Result).Value).Error;

    // ------------------------------------------------------------- Login e leitura

    [Fact]
    public async Task Login_UnitUser_ReturnsOwnUnits()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Scenario objC = BuildScenario(objDbContext);
        objDbContext.Users.Add(new AdminUser
        {
            Username = "loja.itaituba", PasswordHash = BCrypt.Net.BCrypt.HashPassword("senha-forte"),
            IDCompany = objC.Company.Id, RestrictToUnits = true,
            Units = [new AdminUserUnit { IDUnit = objC.Itaituba.Id }],
        });
        objDbContext.Users.Add(new AdminUser
        {
            Username = "gerente.geral", PasswordHash = BCrypt.Net.BCrypt.HashPassword("senha-forte"),
            IDCompany = objC.Company.Id,
        });
        objDbContext.SaveChanges();
        AdminController objController = new AdminController(objDbContext, CreateTokenService());

        LoginResponse objStore = Ok(await objController.Login(
            new LoginRequest("loja.itaituba", "senha-forte"), CancellationToken.None));
        LoginResponse objGeneral = Ok(await objController.Login(
            new LoginRequest("gerente.geral", "senha-forte"), CancellationToken.None));

        UserUnitDto objUnit = Assert.Single(objStore.Units!);
        Assert.Equal("itaituba", objUnit.Slug);
        Assert.Equal(ClaimsExtensions.RoleAdmin, objStore.Role);
        Assert.Null(objGeneral.Units);
    }

    [Fact]
    public async Task Leads_UnitUser_SeesOnlyOwnUnits()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Scenario objC = BuildScenario(objDbContext);
        AdminController objController = new AdminController(objDbContext, CreateTokenService());
        TestHelpers.SetCompanyUser(objController, objDbContext, objC.Company.Id, "loja.itaituba", objC.Itaituba.Id);

        List<LeadDto> objAll = Ok(await objController.GetLeads(null, null, CancellationToken.None));
        List<LeadDto> objOtherStore = Ok(await objController.GetLeads(null, "castanhal", CancellationToken.None));

        Assert.Equal("Cliente de Itaituba", Assert.Single(objAll).Name);
        Assert.Empty(objOtherStore);
    }

    [Fact]
    public async Task Leads_UnitUserWithoutUnits_SeesNothing()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Scenario objC = BuildScenario(objDbContext);
        AdminController objController = new AdminController(objDbContext, CreateTokenService());
        AdminUser objUser = TestHelpers.SetCompanyUser(objController, objDbContext, objC.Company.Id, "sem.loja");
        objUser.RestrictToUnits = true; // perdeu a última unidade: não vira admin da empresa
        objDbContext.SaveChanges();

        Assert.Empty(Ok(await objController.GetLeads(null, null, CancellationToken.None)));
    }

    [Fact]
    public async Task Units_UnitUser_GetsOnlyOwn()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Scenario objC = BuildScenario(objDbContext);
        UnitsController objController = new UnitsController(
            objDbContext, TestHelpers.CreateEncryptor(), null!, Microsoft.Extensions.Logging.Abstractions.NullLogger<UnitsController>.Instance);
        TestHelpers.SetCompanyUser(objController, objDbContext, objC.Company.Id, "loja.castanhal", objC.Castanhal.Id);

        List<UnitDto> objUnits = Ok(await objController.GetAll(null, CancellationToken.None));

        Assert.Equal("castanhal", Assert.Single(objUnits).Slug);
    }

    // ------------------------------------------------------------------ Usuários

    [Fact]
    public async Task Users_CompanyAdmin_CreatesUnitUserInOwnCompany()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Scenario objC = BuildScenario(objDbContext);
        UsersController objController = new UsersController(objDbContext);
        TestHelpers.SetCompanyUser(objController, objDbContext, objC.Company.Id, "gerente.geral");

        // Mesmo pedindo outra empresa, o usuário fica na empresa de quem cria.
        UserDto objNew = Ok(await objController.Create(new CreateUserRequest(
            "Loja.Itaituba", "senha-forte", objC.Other.Id, true, [objC.Itaituba.Id, objC.Itaituba.Id]),
            CancellationToken.None));

        Assert.Equal("loja.itaituba", objNew.Username);
        Assert.Equal(objC.Company.Id, objNew.IDCompany);
        Assert.True(objNew.RestrictToUnits);
        Assert.Equal("Itaituba", Assert.Single(objNew.Units).Name);
        Assert.Single(objDbContext.UserUnits);
    }

    [Fact]
    public async Task Users_UnitOfOtherCompanyOrNone_Rejects()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Scenario objC = BuildScenario(objDbContext);
        UsersController objController = new UsersController(objDbContext);
        TestHelpers.SetCompanyUser(objController, objDbContext, objC.Company.Id, "gerente.geral");

        Assert.Equal("Unidade não encontrada nesta empresa.", ErrorMessage(await objController.Create(
            new CreateUserRequest("loja.x", "senha-forte", null, true, [objC.OtherUnit.Id]), CancellationToken.None)));
        Assert.Equal("Escolha ao menos uma unidade.", ErrorMessage(await objController.Create(
            new CreateUserRequest("loja.y", "senha-forte", null, true, []), CancellationToken.None)));
    }

    [Fact]
    public async Task Users_SuperAdminWithoutCompany_CannotBeUnitUser()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Scenario objC = BuildScenario(objDbContext);
        UsersController objController = new UsersController(objDbContext);
        TestHelpers.SetUser(objController, null, "root");

        Assert.Equal("Usuário de unidade precisa de uma empresa.", ErrorMessage(await objController.Create(
            new CreateUserRequest("loja.z", "senha-forte", null, true, [objC.Itaituba.Id]), CancellationToken.None)));
    }

    [Fact]
    public async Task Users_CompanyAdmin_ListsOnlyOwnCompanyAndCannotChangeOther()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Scenario objC = BuildScenario(objDbContext);
        AdminUser objOutside = new AdminUser { Username = "de.fora", PasswordHash = "hash", IDCompany = objC.Other.Id };
        AdminUser objRoot = new AdminUser { Username = "root", PasswordHash = "hash" };
        objDbContext.Users.AddRange(objOutside, objRoot);
        objDbContext.SaveChanges();
        UsersController objController = new UsersController(objDbContext);
        TestHelpers.SetCompanyUser(objController, objDbContext, objC.Company.Id, "gerente.geral");

        List<UserDto> objList = Ok(await objController.GetAll("outra", CancellationToken.None));
        ActionResult<UserDto> objOtherCompany = await objController.Update(
            objOutside.Id, new UpdateUserRequest(Active: false), CancellationToken.None);
        ActionResult<UserDto> objSuper = await objController.Update(
            objRoot.Id, new UpdateUserRequest(Active: false), CancellationToken.None);

        Assert.Equal("gerente.geral", Assert.Single(objList).Username);
        Assert.IsType<NotFoundObjectResult>(objOtherCompany.Result);
        Assert.IsType<NotFoundObjectResult>(objSuper.Result);
        Assert.True(objDbContext.Users.Single(user => user.Username == "de.fora").Active);
    }

    [Fact]
    public async Task Users_UnitUser_CannotManageUsers()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Scenario objC = BuildScenario(objDbContext);
        UsersController objController = new UsersController(objDbContext);
        AdminUser objMe = TestHelpers.SetCompanyUser(objController, objDbContext, objC.Company.Id, "loja.itaituba", objC.Itaituba.Id);

        Assert.IsType<ForbidResult>((await objController.GetAll(null, CancellationToken.None)).Result);
        Assert.IsType<ForbidResult>((await objController.Create(
            new CreateUserRequest("outro", "senha-forte", null), CancellationToken.None)).Result);
        Assert.IsType<ForbidResult>((await objController.Update(
            objMe.Id, new UpdateUserRequest(RestrictToUnits: false), CancellationToken.None)).Result);
        Assert.True(objDbContext.Users.Single(user => user.Id == objMe.Id).RestrictToUnits);
    }

    [Fact]
    public async Task Users_ChangeUnitsAndBackToWholeCompany()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Scenario objC = BuildScenario(objDbContext);
        UsersController objController = new UsersController(objDbContext);
        TestHelpers.SetCompanyUser(objController, objDbContext, objC.Company.Id, "gerente.geral");
        UserDto objNew = Ok(await objController.Create(new CreateUserRequest(
            "loja.itaituba", "senha-forte", null, true, [objC.Itaituba.Id]), CancellationToken.None));

        UserDto objWithTwo = Ok(await objController.Update(objNew.Id,
            new UpdateUserRequest(RestrictToUnits: true, UnitIds: [objC.Itaituba.Id, objC.Castanhal.Id]),
            CancellationToken.None));
        UserDto objWholeCompany = Ok(await objController.Update(objNew.Id,
            new UpdateUserRequest(RestrictToUnits: false), CancellationToken.None));

        Assert.Equal(["Castanhal", "Itaituba"], objWithTwo.Units.Select(unit => unit.Name).ToArray());
        Assert.False(objWholeCompany.RestrictToUnits);
        Assert.Empty(objWholeCompany.Units);
        Assert.Empty(objDbContext.UserUnits);
        Assert.True(objWholeCompany.Active);
    }

    [Fact]
    public async Task Users_CannotChangeOwnAccess()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Scenario objC = BuildScenario(objDbContext);
        UsersController objController = new UsersController(objDbContext);
        AdminUser objMe = TestHelpers.SetCompanyUser(objController, objDbContext, objC.Company.Id, "gerente.geral");

        Assert.Equal("Você não pode mudar o próprio acesso.", ErrorMessage(await objController.Update(objMe.Id,
            new UpdateUserRequest(RestrictToUnits: true, UnitIds: [objC.Itaituba.Id]), CancellationToken.None)));
    }

    // ----------------------------------------------------------------- Campanhas

    [Fact]
    public async Task Campaigns_UnitUser_SeesOnlyOwnUnitRecipientsDeliveriesAndPdf()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Scenario objC = BuildScenario(objDbContext);
        objDbContext.CompanyCampaignKinds.Add(new CompanyCampaignKind { IDCompany = objC.Company.Id, Kind = CampaignKind.Birthday });
        objDbContext.SaveChanges();
        CampaignsController objAdmin = new CampaignsController(objDbContext);
        TestHelpers.SetCompanyUser(objAdmin, objDbContext, objC.Company.Id, "gerente.geral");
        CampaignDetailDto objCampaign = Ok(await objAdmin.Create(new SaveCampaignRequest(
            CampaignKind.Birthday, "", new CampaignConfig { Message = "Parabéns, {primeiro_nome}!", SendTime = "09:00" }),
            null, CancellationToken.None));

        CampaignRun objRun = new CampaignRun
        {
            IDCampaign = objCampaign.Id, IDCompany = objC.Company.Id, Status = CampaignRunStatus.Completed,
            IDCampaignVersion = objDbContext.CampaignVersions.Single().Id, VersionNumber = 1,
            LocalDate = new DateOnly(2026, 10, 12), TotalCount = 3, SentCount = 3,
        };
        objDbContext.CampaignRuns.Add(objRun);
        foreach ((Unit objUnit, string sName) in new[] { (objC.Itaituba, "Ana"), (objC.Castanhal, "Bia"), (objC.Castanhal, "Carla") })
        {
            Customer objCustomer = new Customer { IDCompany = objC.Company.Id, Phone = "939" + sName.Length + sName, Name = sName, IDLastUnit = objUnit.Id };
            objDbContext.Customers.Add(objCustomer);
            objDbContext.CampaignRecipients.Add(new CampaignRecipient
            {
                IDRun = objRun.Id, IDCustomer = objCustomer.Id, IDUnit = objUnit.Id, Phone = objCustomer.Phone,
                Name = sName, Message = "Parabéns, " + sName + "!", Status = CampaignRecipientStatus.Sent,
                EventDate = new DateOnly(2026, 10, 12),
            });
        }
        CampaignDelivery objItaitubaDelivery = NewDelivery(objRun, objC.Itaituba, 1);
        CampaignDelivery objCastanhalDelivery = NewDelivery(objRun, objC.Castanhal, 2);
        objDbContext.CampaignDeliveries.AddRange(objItaitubaDelivery, objCastanhalDelivery);
        objDbContext.SaveChanges();

        CampaignsController objStore = new CampaignsController(objDbContext);
        TestHelpers.SetCompanyUser(objStore, objDbContext, objC.Company.Id, "loja.itaituba", objC.Itaituba.Id);

        CampaignRunDto objRunDto = Ok(await objStore.Run(objRun.Id, null, CancellationToken.None));
        CampaignRunDto objFromList = Assert.Single(Ok(await objStore.Runs(objCampaign.Id, null, CancellationToken.None)));
        CampaignSummaryDto objSummary = Assert.Single(Ok(await objStore.GetAll(null, CancellationToken.None)));
        PagedDto<CampaignRecipientDto> objRecipients = Ok(await objStore.Recipients(
            objRun.Id, null, null, 1, 50, CancellationToken.None));
        CampaignDeliveryDto objDelivery = Assert.Single(Ok(await objStore.Deliveries(objRun.Id, null, CancellationToken.None)));
        IActionResult objOtherPdf = await objStore.DeliveryPdf(objRun.Id, objCastanhalDelivery.Id, null, CancellationToken.None);
        FileContentResult objCsv = Assert.IsType<FileContentResult>(
            await objStore.RecipientsCsv(objRun.Id, null, CancellationToken.None));

        Assert.Equal(1, objRunDto.Total);
        Assert.Equal(1, objRunDto.Sent);
        Assert.Equal(1, objFromList.Total);
        Assert.Equal(1, objSummary.LastRun!.Total);
        Assert.Equal("Ana", Assert.Single(objRecipients.Items).Name);
        Assert.Equal(1, objRecipients.Total);
        Assert.Equal("Itaituba", objDelivery.UnitName);
        Assert.IsType<NotFoundObjectResult>(objOtherPdf);
        string sCsv = System.Text.Encoding.UTF8.GetString(objCsv.FileContents);
        Assert.Contains("Ana", sCsv);
        Assert.DoesNotContain("Bia", sCsv);

        // O admin da empresa continua vendo tudo.
        Assert.Equal(3, Ok(await objAdmin.Run(objRun.Id, null, CancellationToken.None)).Total);
        Assert.Equal(2, Ok(await objAdmin.Deliveries(objRun.Id, null, CancellationToken.None)).Count);
    }

    [Fact]
    public async Task Campaigns_Preview_UnitUserCountsOnlyOwnUnitCustomers()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Scenario objC = BuildScenario(objDbContext);
        objDbContext.Customers.AddRange(
            new Customer { IDCompany = objC.Company.Id, Phone = "93900000001", Name = "Ana Itaituba", IDLastUnit = objC.Itaituba.Id, VisitCount = 5 },
            new Customer { IDCompany = objC.Company.Id, Phone = "93900000002", Name = "Bia Castanhal", IDLastUnit = objC.Castanhal.Id, VisitCount = 6 });
        objDbContext.SaveChanges();
        CampaignsController objStore = new CampaignsController(objDbContext);
        TestHelpers.SetCompanyUser(objStore, objDbContext, objC.Company.Id, "loja.itaituba", objC.Itaituba.Id);

        AudiencePreviewDto objPreview = Ok(await objStore.Preview(new AudiencePreviewRequest(
            CampaignKind.FrequentCustomer, new CampaignConfig { Message = "Oi, {primeiro_nome}!", VisitMilestone = 5 }),
            null, CancellationToken.None));

        Assert.Equal(1, objPreview.Count);
        Assert.Equal("Ana Itaituba", objPreview.SampleName);
    }

    private static CampaignDelivery NewDelivery(CampaignRun objRun, Unit objUnit, int iCustomers) => new CampaignDelivery
    {
        IDRun = objRun.Id, IDUnit = objUnit.Id, UnitName = objUnit.Name, Email = objUnit.Slug + "@exemplo.com.br",
        Status = CampaignDeliveryStatus.Sent, RecipientCount = iCustomers, Attempts = 1,
        FileName = "campanha-aniversario-" + objUnit.Slug + "-2026-10-12.pdf", SentAt = DateTime.UtcNow,
    };
}
