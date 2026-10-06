using Models.DataBase;
using AccessWifi.Api.Controllers;
using AccessWifi.Api.Features;
using AccessWifi.Api.Features.Companies;
using Models.Persistence;
using Microsoft.AspNetCore.Mvc;

namespace AccessWifi.Api.Tests;

public class CompaniesControllerTests
{
    private static CreateCompanyRequest CreateRequest(string sSlug = "regional", int? iReportSendDay = 1)
    {
        return new CreateCompanyRequest(
            Name: "Lojas Regional",
            Slug: sSlug,
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
        Assert.Equal("regional", objCompany.Slug);
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
            await objController.Create(CreateRequest(sSlug: "Lojas Regional!"), CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(objResult.Result);
        Assert.Empty(objDbContext.Companies);
    }

    [Fact]
    public async Task Create_GravaODiaDoRelatorio()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        CompaniesController objController = new CompaniesController(objDbContext);

        ActionResult<CompanyDto> objResult = await objController.Create(
            CreateRequest(iReportSendDay: 10), CancellationToken.None);

        CompanyDto objDto = Assert.IsType<CompanyDto>(Assert.IsType<OkObjectResult>(objResult.Result).Value);
        Assert.Equal(10, objDto.ReportSendDay);
        Assert.Equal(10, objDbContext.Companies.Single().ReportSendDay);
    }

    [Fact]
    public async Task Create_SemDia_UsaPadraoDia1()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        CompaniesController objController = new CompaniesController(objDbContext);

        ActionResult<CompanyDto> objResult = await objController.Create(
            CreateRequest(iReportSendDay: null), CancellationToken.None);

        CompanyDto objDto = Assert.IsType<CompanyDto>(Assert.IsType<OkObjectResult>(objResult.Result).Value);
        Assert.Equal(1, objDto.ReportSendDay);
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
    public async Task Update_AtualizaODiaDoRelatorio()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        CompaniesController objController = new CompaniesController(objDbContext);
        await objController.Create(CreateRequest(), CancellationToken.None);
        Guid objCompanyId = objDbContext.Companies.Single().Id;

        UpdateCompanyRequest objUpdate = new UpdateCompanyRequest(Name: "Regional", Active: true, ReportSendDay: 5);

        await objController.Update(objCompanyId, objUpdate, CancellationToken.None);

        Company objCompany = objDbContext.Companies.Single();
        Assert.Equal("Regional", objCompany.Name);
        Assert.Equal(5, objCompany.ReportSendDay);
    }
}
