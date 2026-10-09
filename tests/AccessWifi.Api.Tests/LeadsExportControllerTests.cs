using AccessWifi.Api.Controllers;
using AccessWifi.Api.Features;
using AccessWifi.Api.Features.Leads;
using Microsoft.AspNetCore.Mvc;
using Models.DataBase;
using Models.Persistence;
using Models.Reports;

namespace AccessWifi.Api.Tests;

/// <summary>PDF da tela de Leads: tema da empresa, limites e quem pode gerar.</summary>
public class LeadsExportControllerTests
{
    private static Company AddCompany(AppDbContext objDbContext)
    {
        Company objCompany = new Company { Name = "Lojas Nacional", Slug = "nacional" };
        objDbContext.Companies.Add(objCompany);
        objDbContext.Units.Add(new Unit { IDCompany = objCompany.Id, Name = "NACIONAL ADM", Slug = "nacional-adm" });
        objDbContext.PortalSettings.Add(new PortalSettings
        {
            IDCompany = objCompany.Id,
            Ssid = "NACIONAL",
            Colors = new ThemeColors { Brand = "#A41F24", BrandDark = "#7F171B" },
            Logo = "data:image/png;base64,isto-nao-e-uma-imagem", // logo ruim: o PDF sai com o nome da empresa
        });
        objDbContext.SaveChanges();
        return objCompany;
    }

    private static LeadsPdfRow Row(string sName, string sPhone = "(91) 98888-1234", string sUnit = "NACIONAL ADM") =>
        new LeadsPdfRow(sName, sPhone, "ana.souza", "12/03/1998", sUnit, "01/10/2026 10:00", "09/10/2026 11:00");

    private static LeadsExportController SuperAdmin(AppDbContext objDbContext)
    {
        LeadsExportController objController = new LeadsExportController(objDbContext);
        TestHelpers.SetUser(objController, null, "root");
        return objController;
    }

    [Fact]
    public async Task Pdf_WithRows_ReturnsPdfNamedAfterCompanyAndUnit()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        AddCompany(objDbContext);

        IActionResult objResult = await SuperAdmin(objDbContext).Pdf(
            new LeadsPdfRequest("nacional-adm", "Outubro / 2026", "ana", [Row("Ana Souza"), Row("Bia", "123")]),
            "nacional", CancellationToken.None);

        FileContentResult objPdf = Assert.IsType<FileContentResult>(objResult);
        Assert.Equal("application/pdf", objPdf.ContentType);
        Assert.Equal("cadastros-nacional-nacional-adm.pdf", objPdf.FileDownloadName);
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(objPdf.FileContents, 0, 4));
    }

    [Fact]
    public async Task Pdf_EmptyList_StillReturnsPdf()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        AddCompany(objDbContext);

        IActionResult objResult = await SuperAdmin(objDbContext).Pdf(
            new LeadsPdfRequest(null, null, null, []), "nacional", CancellationToken.None);

        FileContentResult objPdf = Assert.IsType<FileContentResult>(objResult);
        Assert.Equal("cadastros-nacional.pdf", objPdf.FileDownloadName);
    }

    [Fact]
    public async Task Pdf_SuperAdminWithoutCompany_Returns400()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        AddCompany(objDbContext);

        IActionResult objResult = await SuperAdmin(objDbContext).Pdf(
            new LeadsPdfRequest(null, null, null, [Row("Ana")]), null, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(objResult);
    }

    [Fact]
    public async Task Pdf_TooManyRows_Returns400WithHint()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        AddCompany(objDbContext);
        LeadsPdfRow[] arrRows = Enumerable.Range(0, LeadsExportController.MaxRows + 1).Select(i => Row($"Cliente {i}")).ToArray();

        IActionResult objResult = await SuperAdmin(objDbContext).Pdf(
            new LeadsPdfRequest(null, null, null, arrRows), "nacional", CancellationToken.None);

        BadRequestObjectResult objBadRequest = Assert.IsType<BadRequestObjectResult>(objResult);
        Assert.Contains("CSV", Assert.IsType<ErrorResponse>(objBadRequest.Value).Error);
    }

    [Fact]
    public void Build_ManyRowsAndMixedUnits_SpansSeveralPages()
    {
        LeadsPdfRow[] arrRows = Enumerable.Range(0, 300)
            .Select(i => Row($"Cliente {i}", sUnit: i % 2 == 0 ? "NACIONAL ADM" : "Outra loja"))
            .ToArray();

        byte[] arrPdf = LeadsPdf.Build(new LeadsPdfData(
            "Lojas Nacional", "Todas as unidades", LeadsPdf.AllPeriods, null, "09/10/2026 11:30",
            null, new ThemeColors(), arrRows));

        string sText = System.Text.Encoding.Latin1.GetString(arrPdf);
        int iPages = System.Text.RegularExpressions.Regex.Matches(sText, @"/Type\s*/Page\b").Count;
        Assert.True(iPages > 1, $"esperava mais de uma página, veio {iPages}");
    }
}
