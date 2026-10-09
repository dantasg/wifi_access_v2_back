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
    private sealed class Cenario
    {
        public required Company Regional { get; init; }
        public required Company Outra { get; init; }
        public required Unit Itaituba { get; init; }
        public required Unit Castanhal { get; init; }
    }

    private static Cenario Montar(AppDbContext objDbContext)
    {
        Company objRegional = new Company { Name = "Lojas Regional", Slug = "regional" };
        Company objOutra = new Company { Name = "Outra", Slug = "outra" };
        Unit objItaituba = new Unit { IDCompany = objRegional.Id, Name = "Itaituba", Slug = "itaituba" };
        Unit objCastanhal = new Unit { IDCompany = objRegional.Id, Name = "Castanhal", Slug = "castanhal" };
        Unit objDaOutra = new Unit { IDCompany = objOutra.Id, Name = "Da outra", Slug = "da-outra" };
        objDbContext.AddRange(objRegional, objOutra, objItaituba, objCastanhal, objDaOutra);
        // Admin da empresa inteira (o acesso é lido do banco pelo usuário do token).
        objDbContext.Users.Add(new AdminUser { Username = "admin.regional", PasswordHash = "hash", IDCompany = objRegional.Id });

        DateTime dtBase = new DateTime(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);
        objDbContext.SentEmails.AddRange(
            Email(objRegional, objItaituba, SentEmailKind.Report, "Relatório Itaituba", dtBase),
            Email(objRegional, objItaituba, SentEmailKind.Campaign, "Campanha Itaituba", dtBase.AddDays(2)),
            Email(objRegional, objCastanhal, SentEmailKind.Campaign, "Campanha Castanhal", dtBase.AddDays(1)),
            Email(objOutra, objDaOutra, SentEmailKind.Report, "Relatório da outra", dtBase.AddDays(3)));
        objDbContext.SaveChanges();
        return new Cenario { Regional = objRegional, Outra = objOutra, Itaituba = objItaituba, Castanhal = objCastanhal };
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

    private static SentEmailPageDto Pagina(ActionResult<SentEmailPageDto> objResult) =>
        Assert.IsType<SentEmailPageDto>(Assert.IsType<OkObjectResult>(objResult.Result).Value);

    [Fact]
    public async Task List_AdminDaEmpresa_VeSoOsDelaDoMaisNovoParaOMaisVelho()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Cenario objC = Montar(objDbContext);

        SentEmailPageDto objPagina = Pagina(await Controller(objDbContext, objC.Regional.Id)
            .List(null, null, null, 1, 25, CancellationToken.None));

        Assert.Equal(3, objPagina.Total);
        Assert.Equal(
            new[] { "Campanha Itaituba", "Campanha Castanhal", "Relatório Itaituba" },
            objPagina.Items.Select(item => item.Subject));
    }

    [Fact]
    public async Task List_FiltrosDeUnidadeETipoEPaginacao()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Cenario objC = Montar(objDbContext);
        EmailsController objController = Controller(objDbContext, objC.Regional.Id);

        SentEmailPageDto objItaituba = Pagina(await objController.List(null, "itaituba", null, 1, 25, CancellationToken.None));
        Assert.Equal(2, objItaituba.Total);

        SentEmailPageDto objRelatorios = Pagina(await objController.List(null, null, SentEmailKind.Report, 1, 25, CancellationToken.None));
        Assert.Equal("Relatório Itaituba", Assert.Single(objRelatorios.Items).Subject);

        SentEmailPageDto objSegunda = Pagina(await objController.List(null, null, null, 2, 2, CancellationToken.None));
        Assert.Equal(3, objSegunda.Total);
        Assert.Equal("Relatório Itaituba", Assert.Single(objSegunda.Items).Subject);

        SentEmailPageDto objNada = Pagina(await objController.List(null, "nao-existe", null, 1, 25, CancellationToken.None));
        Assert.Equal(0, objNada.Total);
    }

    [Fact]
    public async Task List_SuperAdmin_PrecisaDaEmpresaEVeAIndicada()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Montar(objDbContext);
        EmailsController objController = Controller(objDbContext, null, "root");

        Assert.IsType<BadRequestObjectResult>((await objController.List(null, null, null, 1, 25, CancellationToken.None)).Result);
        SentEmailPageDto objOutra = Pagina(await objController.List("outra", null, null, 1, 25, CancellationToken.None));
        Assert.Equal("Relatório da outra", Assert.Single(objOutra.Items).Subject);
    }

    [Fact]
    public async Task List_UsuarioDeUnidade_VeSoOsDasUnidadesDele()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Cenario objC = Montar(objDbContext);
        objDbContext.Users.Add(new AdminUser
        {
            Username = "loja.castanhal", PasswordHash = "hash", IDCompany = objC.Regional.Id, RestrictToUnits = true,
            Units = [new AdminUserUnit { IDUnit = objC.Castanhal.Id }],
        });
        objDbContext.SaveChanges();
        EmailsController objController = Controller(objDbContext, objC.Regional.Id, "loja.castanhal");

        SentEmailPageDto objPagina = Pagina(await objController.List(null, null, null, 1, 25, CancellationToken.None));
        Assert.Equal("Campanha Castanhal", Assert.Single(objPagina.Items).Subject);

        // O de outra loja não abre nem pelo endereço direto.
        Guid objDeItaituba = objDbContext.SentEmails.Single(email => email.Subject == "Campanha Itaituba").Id;
        Assert.IsType<NotFoundObjectResult>((await objController.Get(objDeItaituba, null, CancellationToken.None)).Result);
    }

    [Fact]
    public async Task Get_DevolveOTextoEDeOutraEmpresaDa404()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Cenario objC = Montar(objDbContext);
        EmailsController objController = Controller(objDbContext, objC.Regional.Id);
        Guid objDaRegional = objDbContext.SentEmails.Single(email => email.Subject == "Relatório Itaituba").Id;
        Guid objDaOutra = objDbContext.SentEmails.Single(email => email.Subject == "Relatório da outra").Id;

        SentEmailDto objEmail = Assert.IsType<SentEmailDto>(
            Assert.IsType<OkObjectResult>((await objController.Get(objDaRegional, null, CancellationToken.None)).Result).Value);
        Assert.Equal("Texto de Relatório Itaituba", objEmail.Body);

        NotFoundObjectResult objNotFound = Assert.IsType<NotFoundObjectResult>(
            (await objController.Get(objDaOutra, null, CancellationToken.None)).Result);
        Assert.Equal("E-mail não encontrado.", Assert.IsType<ErrorResponse>(objNotFound.Value).Error);
    }
}
