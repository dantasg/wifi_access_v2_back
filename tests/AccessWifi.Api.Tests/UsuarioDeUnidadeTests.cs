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
public class UsuarioDeUnidadeTests
{
    private sealed record Cenario(Company Empresa, Company Outra, Unit Itaituba, Unit Castanhal, Unit DaOutra);

    private static Cenario Montar(AppDbContext objDbContext)
    {
        Company objEmpresa = new Company { Name = "Lojas Regional", Slug = "regional", TimeZone = "America/Belem" };
        Company objOutra = new Company { Name = "Outra", Slug = "outra" };
        objDbContext.Companies.AddRange(objEmpresa, objOutra);
        Unit objItaituba = new Unit { IDCompany = objEmpresa.Id, Name = "Itaituba", Slug = "itaituba" };
        Unit objCastanhal = new Unit { IDCompany = objEmpresa.Id, Name = "Castanhal", Slug = "castanhal" };
        Unit objDaOutra = new Unit { IDCompany = objOutra.Id, Name = "Da outra", Slug = "da-outra" };
        objDbContext.Units.AddRange(objItaituba, objCastanhal, objDaOutra);
        objDbContext.Leads.AddRange(
            new Lead { IDUnit = objItaituba.Id, Nome = "Cliente de Itaituba" },
            new Lead { IDUnit = objCastanhal.Id, Nome = "Cliente de Castanhal" });
        objDbContext.SaveChanges();
        return new Cenario(objEmpresa, objOutra, objItaituba, objCastanhal, objDaOutra);
    }

    private static TokenService CreateTokenService() =>
        new TokenService(Options.Create(new JwtOptions { Secret = "segredo-de-teste-3f9a1c7e5b2d8046a1e9c3b7d5f20486" }));

    private static T Ok<T>(ActionResult<T> objResult) =>
        Assert.IsType<T>(Assert.IsType<OkObjectResult>(objResult.Result).Value);

    private static string Erro<T>(ActionResult<T> objResult) =>
        Assert.IsType<ErrorResponse>(Assert.IsType<BadRequestObjectResult>(objResult.Result).Value).Error;

    // ------------------------------------------------------------- Login e leitura

    [Fact]
    public async Task Login_UsuarioDeUnidade_DevolveAsUnidadesDele()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Cenario objC = Montar(objDbContext);
        objDbContext.Users.Add(new AdminUser
        {
            Username = "loja.itaituba", PasswordHash = BCrypt.Net.BCrypt.HashPassword("senha-forte"),
            IDCompany = objC.Empresa.Id, RestrictToUnits = true,
            Units = [new AdminUserUnit { IDUnit = objC.Itaituba.Id }],
        });
        objDbContext.Users.Add(new AdminUser
        {
            Username = "gerente.geral", PasswordHash = BCrypt.Net.BCrypt.HashPassword("senha-forte"),
            IDCompany = objC.Empresa.Id,
        });
        objDbContext.SaveChanges();
        AdminController objController = new AdminController(objDbContext, CreateTokenService());

        LoginResponse objLoja = Ok(await objController.Login(
            new LoginRequest("loja.itaituba", "senha-forte"), CancellationToken.None));
        LoginResponse objGeral = Ok(await objController.Login(
            new LoginRequest("gerente.geral", "senha-forte"), CancellationToken.None));

        UserUnitDto objUnidade = Assert.Single(objLoja.Units!);
        Assert.Equal("itaituba", objUnidade.Slug);
        Assert.Equal(ClaimsExtensions.RoleAdmin, objLoja.Role);
        Assert.Null(objGeral.Units);
    }

    [Fact]
    public async Task Leads_UsuarioDeUnidade_VeSoAsUnidadesDele()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Cenario objC = Montar(objDbContext);
        AdminController objController = new AdminController(objDbContext, CreateTokenService());
        TestHelpers.SetCompanyUser(objController, objDbContext, objC.Empresa.Id, "loja.itaituba", objC.Itaituba.Id);

        List<LeadDto> objTodos = Ok(await objController.GetLeads(null, null, CancellationToken.None));
        List<LeadDto> objOutraLoja = Ok(await objController.GetLeads(null, "castanhal", CancellationToken.None));

        Assert.Equal("Cliente de Itaituba", Assert.Single(objTodos).Nome);
        Assert.Empty(objOutraLoja);
    }

    [Fact]
    public async Task Leads_UsuarioDeUnidadeSemUnidades_NaoVeNada()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Cenario objC = Montar(objDbContext);
        AdminController objController = new AdminController(objDbContext, CreateTokenService());
        AdminUser objUser = TestHelpers.SetCompanyUser(objController, objDbContext, objC.Empresa.Id, "sem.loja");
        objUser.RestrictToUnits = true; // perdeu a última unidade: não vira admin da empresa
        objDbContext.SaveChanges();

        Assert.Empty(Ok(await objController.GetLeads(null, null, CancellationToken.None)));
    }

    [Fact]
    public async Task Unidades_UsuarioDeUnidade_RecebeSoAsDele()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Cenario objC = Montar(objDbContext);
        UnitsController objController = new UnitsController(
            objDbContext, TestHelpers.CreateEncryptor(), null!, Microsoft.Extensions.Logging.Abstractions.NullLogger<UnitsController>.Instance);
        TestHelpers.SetCompanyUser(objController, objDbContext, objC.Empresa.Id, "loja.castanhal", objC.Castanhal.Id);

        List<UnitDto> objUnidades = Ok(await objController.GetAll(null, CancellationToken.None));

        Assert.Equal("castanhal", Assert.Single(objUnidades).Slug);
    }

    // ------------------------------------------------------------------ Usuários

    [Fact]
    public async Task Usuarios_AdminDaEmpresa_CriaUsuarioDeUnidadeNaPropriaEmpresa()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Cenario objC = Montar(objDbContext);
        UsersController objController = new UsersController(objDbContext);
        TestHelpers.SetCompanyUser(objController, objDbContext, objC.Empresa.Id, "gerente.geral");

        // Mesmo pedindo outra empresa, o usuário fica na empresa de quem cria.
        UserDto objNovo = Ok(await objController.Create(new CreateUserRequest(
            "Loja.Itaituba", "senha-forte", objC.Outra.Id, true, [objC.Itaituba.Id, objC.Itaituba.Id]),
            CancellationToken.None));

        Assert.Equal("loja.itaituba", objNovo.Username);
        Assert.Equal(objC.Empresa.Id, objNovo.IDCompany);
        Assert.True(objNovo.RestrictToUnits);
        Assert.Equal("Itaituba", Assert.Single(objNovo.Units).Name);
        Assert.Single(objDbContext.UserUnits);
    }

    [Fact]
    public async Task Usuarios_UnidadeDeOutraEmpresaOuNenhuma_Recusa()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Cenario objC = Montar(objDbContext);
        UsersController objController = new UsersController(objDbContext);
        TestHelpers.SetCompanyUser(objController, objDbContext, objC.Empresa.Id, "gerente.geral");

        Assert.Equal("Unidade não encontrada nesta empresa.", Erro(await objController.Create(
            new CreateUserRequest("loja.x", "senha-forte", null, true, [objC.DaOutra.Id]), CancellationToken.None)));
        Assert.Equal("Escolha ao menos uma unidade.", Erro(await objController.Create(
            new CreateUserRequest("loja.y", "senha-forte", null, true, []), CancellationToken.None)));
    }

    [Fact]
    public async Task Usuarios_SuperAdminSemEmpresa_NaoPodeSerDeUnidade()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Cenario objC = Montar(objDbContext);
        UsersController objController = new UsersController(objDbContext);
        TestHelpers.SetUser(objController, null, "root");

        Assert.Equal("Usuário de unidade precisa de uma empresa.", Erro(await objController.Create(
            new CreateUserRequest("loja.z", "senha-forte", null, true, [objC.Itaituba.Id]), CancellationToken.None)));
    }

    [Fact]
    public async Task Usuarios_AdminDaEmpresa_ListaSoOsDaEmpresaENaoMexeEmOutra()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Cenario objC = Montar(objDbContext);
        AdminUser objDeFora = new AdminUser { Username = "de.fora", PasswordHash = "hash", IDCompany = objC.Outra.Id };
        AdminUser objRoot = new AdminUser { Username = "root", PasswordHash = "hash" };
        objDbContext.Users.AddRange(objDeFora, objRoot);
        objDbContext.SaveChanges();
        UsersController objController = new UsersController(objDbContext);
        TestHelpers.SetCompanyUser(objController, objDbContext, objC.Empresa.Id, "gerente.geral");

        List<UserDto> objLista = Ok(await objController.GetAll("outra", CancellationToken.None));
        ActionResult<UserDto> objOutraEmpresa = await objController.Update(
            objDeFora.Id, new UpdateUserRequest(Active: false), CancellationToken.None);
        ActionResult<UserDto> objSuper = await objController.Update(
            objRoot.Id, new UpdateUserRequest(Active: false), CancellationToken.None);

        Assert.Equal("gerente.geral", Assert.Single(objLista).Username);
        Assert.IsType<NotFoundObjectResult>(objOutraEmpresa.Result);
        Assert.IsType<NotFoundObjectResult>(objSuper.Result);
        Assert.True(objDbContext.Users.Single(user => user.Username == "de.fora").Active);
    }

    [Fact]
    public async Task Usuarios_UsuarioDeUnidade_NaoGerenciaUsuarios()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Cenario objC = Montar(objDbContext);
        UsersController objController = new UsersController(objDbContext);
        AdminUser objEu = TestHelpers.SetCompanyUser(objController, objDbContext, objC.Empresa.Id, "loja.itaituba", objC.Itaituba.Id);

        Assert.IsType<ForbidResult>((await objController.GetAll(null, CancellationToken.None)).Result);
        Assert.IsType<ForbidResult>((await objController.Create(
            new CreateUserRequest("outro", "senha-forte", null), CancellationToken.None)).Result);
        Assert.IsType<ForbidResult>((await objController.Update(
            objEu.Id, new UpdateUserRequest(RestrictToUnits: false), CancellationToken.None)).Result);
        Assert.True(objDbContext.Users.Single(user => user.Id == objEu.Id).RestrictToUnits);
    }

    [Fact]
    public async Task Usuarios_TrocarUnidadesEVoltarParaEmpresaToda()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Cenario objC = Montar(objDbContext);
        UsersController objController = new UsersController(objDbContext);
        TestHelpers.SetCompanyUser(objController, objDbContext, objC.Empresa.Id, "gerente.geral");
        UserDto objNovo = Ok(await objController.Create(new CreateUserRequest(
            "loja.itaituba", "senha-forte", null, true, [objC.Itaituba.Id]), CancellationToken.None));

        UserDto objDuas = Ok(await objController.Update(objNovo.Id,
            new UpdateUserRequest(RestrictToUnits: true, UnitIds: [objC.Itaituba.Id, objC.Castanhal.Id]),
            CancellationToken.None));
        UserDto objEmpresaToda = Ok(await objController.Update(objNovo.Id,
            new UpdateUserRequest(RestrictToUnits: false), CancellationToken.None));

        Assert.Equal(["Castanhal", "Itaituba"], objDuas.Units.Select(unit => unit.Name).ToArray());
        Assert.False(objEmpresaToda.RestrictToUnits);
        Assert.Empty(objEmpresaToda.Units);
        Assert.Empty(objDbContext.UserUnits);
        Assert.True(objEmpresaToda.Active);
    }

    [Fact]
    public async Task Usuarios_NaoMudaOProprioAcesso()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Cenario objC = Montar(objDbContext);
        UsersController objController = new UsersController(objDbContext);
        AdminUser objEu = TestHelpers.SetCompanyUser(objController, objDbContext, objC.Empresa.Id, "gerente.geral");

        Assert.Equal("Você não pode mudar o próprio acesso.", Erro(await objController.Update(objEu.Id,
            new UpdateUserRequest(RestrictToUnits: true, UnitIds: [objC.Itaituba.Id]), CancellationToken.None)));
    }

    // ----------------------------------------------------------------- Campanhas

    [Fact]
    public async Task Campanhas_UsuarioDeUnidade_VeSoOsDestinatariosEnviosEPdfDaUnidadeDele()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Cenario objC = Montar(objDbContext);
        objDbContext.CompanyCampaignKinds.Add(new CompanyCampaignKind { IDCompany = objC.Empresa.Id, Kind = CampaignKind.Birthday });
        objDbContext.SaveChanges();
        CampaignsController objAdmin = new CampaignsController(objDbContext);
        TestHelpers.SetCompanyUser(objAdmin, objDbContext, objC.Empresa.Id, "gerente.geral");
        CampaignDetailDto objCampanha = Ok(await objAdmin.Create(new SaveCampaignRequest(
            CampaignKind.Birthday, "", new CampaignConfig { Message = "Parabéns, {primeiro_nome}!", SendTime = "09:00" }),
            null, CancellationToken.None));

        CampaignRun objRun = new CampaignRun
        {
            IDCampaign = objCampanha.Id, IDCompany = objC.Empresa.Id, Status = CampaignRunStatus.Completed,
            IDCampaignVersion = objDbContext.CampaignVersions.Single().Id, VersionNumber = 1,
            LocalDate = new DateOnly(2026, 10, 12), TotalCount = 3, SentCount = 3,
        };
        objDbContext.CampaignRuns.Add(objRun);
        foreach ((Unit objUnit, string sNome) in new[] { (objC.Itaituba, "Ana"), (objC.Castanhal, "Bia"), (objC.Castanhal, "Carla") })
        {
            Customer objCliente = new Customer { IDCompany = objC.Empresa.Id, Phone = "939" + sNome.Length + sNome, Name = sNome, IDLastUnit = objUnit.Id };
            objDbContext.Customers.Add(objCliente);
            objDbContext.CampaignRecipients.Add(new CampaignRecipient
            {
                IDRun = objRun.Id, IDCustomer = objCliente.Id, IDUnit = objUnit.Id, Phone = objCliente.Phone,
                Name = sNome, Message = "Parabéns, " + sNome + "!", Status = CampaignRecipientStatus.Sent,
                EventDate = new DateOnly(2026, 10, 12),
            });
        }
        CampaignDelivery objEnvioItaituba = NovoEnvio(objRun, objC.Itaituba, 1);
        CampaignDelivery objEnvioCastanhal = NovoEnvio(objRun, objC.Castanhal, 2);
        objDbContext.CampaignDeliveries.AddRange(objEnvioItaituba, objEnvioCastanhal);
        objDbContext.SaveChanges();

        CampaignsController objLoja = new CampaignsController(objDbContext);
        TestHelpers.SetCompanyUser(objLoja, objDbContext, objC.Empresa.Id, "loja.itaituba", objC.Itaituba.Id);

        CampaignRunDto objExecucao = Ok(await objLoja.Run(objRun.Id, null, CancellationToken.None));
        CampaignRunDto objDaLista = Assert.Single(Ok(await objLoja.Runs(objCampanha.Id, null, CancellationToken.None)));
        CampaignSummaryDto objResumo = Assert.Single(Ok(await objLoja.GetAll(null, CancellationToken.None)));
        PagedDto<CampaignRecipientDto> objDestinatarios = Ok(await objLoja.Recipients(
            objRun.Id, null, null, 1, 50, CancellationToken.None));
        CampaignDeliveryDto objEnvio = Assert.Single(Ok(await objLoja.Deliveries(objRun.Id, null, CancellationToken.None)));
        IActionResult objPdfDaOutra = await objLoja.DeliveryPdf(objRun.Id, objEnvioCastanhal.Id, null, CancellationToken.None);
        FileContentResult objCsv = Assert.IsType<FileContentResult>(
            await objLoja.RecipientsCsv(objRun.Id, null, CancellationToken.None));

        Assert.Equal(1, objExecucao.Total);
        Assert.Equal(1, objExecucao.Sent);
        Assert.Equal(1, objDaLista.Total);
        Assert.Equal(1, objResumo.LastRun!.Total);
        Assert.Equal("Ana", Assert.Single(objDestinatarios.Items).Name);
        Assert.Equal(1, objDestinatarios.Total);
        Assert.Equal("Itaituba", objEnvio.UnitName);
        Assert.IsType<NotFoundObjectResult>(objPdfDaOutra);
        string sCsv = System.Text.Encoding.UTF8.GetString(objCsv.FileContents);
        Assert.Contains("Ana", sCsv);
        Assert.DoesNotContain("Bia", sCsv);

        // O admin da empresa continua vendo tudo.
        Assert.Equal(3, Ok(await objAdmin.Run(objRun.Id, null, CancellationToken.None)).Total);
        Assert.Equal(2, Ok(await objAdmin.Deliveries(objRun.Id, null, CancellationToken.None)).Count);
    }

    [Fact]
    public async Task Campanhas_Previa_UsuarioDeUnidadeContaSoClientesDaUnidadeDele()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Cenario objC = Montar(objDbContext);
        objDbContext.Customers.AddRange(
            new Customer { IDCompany = objC.Empresa.Id, Phone = "93900000001", Name = "Ana Itaituba", IDLastUnit = objC.Itaituba.Id, VisitCount = 5 },
            new Customer { IDCompany = objC.Empresa.Id, Phone = "93900000002", Name = "Bia Castanhal", IDLastUnit = objC.Castanhal.Id, VisitCount = 6 });
        objDbContext.SaveChanges();
        CampaignsController objLoja = new CampaignsController(objDbContext);
        TestHelpers.SetCompanyUser(objLoja, objDbContext, objC.Empresa.Id, "loja.itaituba", objC.Itaituba.Id);

        AudiencePreviewDto objPrevia = Ok(await objLoja.Preview(new AudiencePreviewRequest(
            CampaignKind.FrequentCustomer, new CampaignConfig { Message = "Oi, {primeiro_nome}!", VisitMilestone = 5 }),
            null, CancellationToken.None));

        Assert.Equal(1, objPrevia.Count);
        Assert.Equal("Ana Itaituba", objPrevia.SampleName);
    }

    private static CampaignDelivery NovoEnvio(CampaignRun objRun, Unit objUnit, int iClientes) => new CampaignDelivery
    {
        IDRun = objRun.Id, IDUnit = objUnit.Id, UnitName = objUnit.Name, Email = objUnit.Slug + "@exemplo.com.br",
        Status = CampaignDeliveryStatus.Sent, RecipientCount = iClientes, Attempts = 1,
        FileName = "campanha-aniversario-" + objUnit.Slug + "-2026-10-12.pdf", SentAt = DateTime.UtcNow,
    };
}
