using Models.DataBase;
using AccessWifi.Api.Controllers;
using AccessWifi.Api.Features;
using AccessWifi.Api.Features.Companies;
using Models.Persistence;
using Microsoft.AspNetCore.Mvc;

namespace AccessWifi.Api.Tests;

public class CompaniesControllerTests
{
    private static CreateCompanyRequest CreateRequest(
        string sSlug = "doce", string? sReportEmail = "relatorio@doce.com.br", int? iReportSendDay = 1)
    {
        return new CreateCompanyRequest(
            Name: "Dôce Cafeteria",
            Slug: sSlug,
            ReportEmail: sReportEmail,
            ReportSendDay: iReportSendDay);
    }

    [Fact]
    public async Task Create_ComDadosValidos_Cria()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        CompaniesController objController = new CompaniesController(objDbContext);

        ActionResult<CompanyDto> objResult =
            await objController.Create(CreateRequest(), CancellationToken.None);

        OkObjectResult objOk = Assert.IsType<OkObjectResult>(objResult.Result);
        CompanyDto objCompany = Assert.IsType<CompanyDto>(objOk.Value);
        Assert.Equal("doce", objCompany.Slug);
        Assert.Single(objDbContext.Companies);
    }

    [Fact]
    public async Task Create_SlugDuplicado_Retorna400()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        CompaniesController objController = new CompaniesController(objDbContext);
        await objController.Create(CreateRequest(), CancellationToken.None);

        ActionResult<CompanyDto> objResult =
            await objController.Create(CreateRequest(), CancellationToken.None);

        BadRequestObjectResult objBadRequest = Assert.IsType<BadRequestObjectResult>(objResult.Result);
        ErrorResponse objError = Assert.IsType<ErrorResponse>(objBadRequest.Value);
        Assert.Equal("Já existe uma empresa com esse slug.", objError.Error);
    }

    [Fact]
    public async Task Create_SlugInvalido_Retorna400()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        CompaniesController objController = new CompaniesController(objDbContext);

        ActionResult<CompanyDto> objResult =
            await objController.Create(CreateRequest(sSlug: "Dôce Café!"), CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(objResult.Result);
        Assert.Empty(objDbContext.Companies);
    }

    [Fact]
    public async Task Create_GravaCamposDoRelatorio()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        CompaniesController objController = new CompaniesController(objDbContext);

        ActionResult<CompanyDto> objResult = await objController.Create(
            CreateRequest(sReportEmail: "rel@doce.com.br", iReportSendDay: 10), CancellationToken.None);

        CompanyDto objDto = Assert.IsType<CompanyDto>(Assert.IsType<OkObjectResult>(objResult.Result).Value);
        Assert.Equal("rel@doce.com.br", objDto.ReportEmail);
        Assert.Equal(10, objDto.ReportSendDay);
        Company objCompany = objDbContext.Companies.Single();
        Assert.Equal("rel@doce.com.br", objCompany.ReportEmail);
        Assert.Equal(10, objCompany.ReportSendDay);
    }

    [Fact]
    public async Task Create_SemEmail_UsaPadraoDia1ESemRelatorio()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        CompaniesController objController = new CompaniesController(objDbContext);

        ActionResult<CompanyDto> objResult = await objController.Create(
            CreateRequest(sReportEmail: null, iReportSendDay: null), CancellationToken.None);

        CompanyDto objDto = Assert.IsType<CompanyDto>(Assert.IsType<OkObjectResult>(objResult.Result).Value);
        Assert.Null(objDto.ReportEmail);
        Assert.Equal(1, objDto.ReportSendDay);
    }

    [Fact]
    public async Task Create_EmailInvalido_Retorna400()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        CompaniesController objController = new CompaniesController(objDbContext);

        ActionResult<CompanyDto> objResult = await objController.Create(
            CreateRequest(sReportEmail: "nao-eh-email"), CancellationToken.None);

        BadRequestObjectResult objBadRequest = Assert.IsType<BadRequestObjectResult>(objResult.Result);
        Assert.Equal("E-mail do relatório inválido.", Assert.IsType<ErrorResponse>(objBadRequest.Value).Error);
        Assert.Empty(objDbContext.Companies);
    }

    [Fact]
    public async Task Create_DiaForaDoIntervalo_Retorna400()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        CompaniesController objController = new CompaniesController(objDbContext);

        ActionResult<CompanyDto> objResult = await objController.Create(
            CreateRequest(iReportSendDay: 31), CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(objResult.Result);
        Assert.Empty(objDbContext.Companies);
    }

    [Fact]
    public async Task Update_AtualizaCamposDoRelatorio()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        CompaniesController objController = new CompaniesController(objDbContext);
        await objController.Create(CreateRequest(), CancellationToken.None);
        Guid objCompanyId = objDbContext.Companies.Single().Id;

        UpdateCompanyRequest objUpdate = new UpdateCompanyRequest(
            Name: "Dôce", Active: true, ReportEmail: "novo@doce.com.br", ReportSendDay: 5);

        await objController.Update(objCompanyId, objUpdate, CancellationToken.None);

        Company objCompany = objDbContext.Companies.Single();
        Assert.Equal("novo@doce.com.br", objCompany.ReportEmail);
        Assert.Equal(5, objCompany.ReportSendDay);
    }
}
