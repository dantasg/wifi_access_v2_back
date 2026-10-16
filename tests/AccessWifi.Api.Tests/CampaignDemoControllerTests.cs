using System.Reflection;
using System.Text;
using AccessWifi.Api.Controllers;
using AccessWifi.Api.Features;
using AccessWifi.Api.Features.Campaigns;
using AccessWifi.Api.Features.Emails;
using AccessWifi.Api.Infrastructure.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Models.DataBase;
using Models.Persistence;

namespace AccessWifi.Api.Tests;

/// <summary>Demonstração de campanha (PROPOSTA_DEMONSTRACAO_CAMPANHA.md): todos os clientes da unidade, na hora.</summary>
public class CampaignDemoControllerTests
{
    private sealed record Scenario(Company Company, Unit Matriz, Unit Filial, Unit Empty);

    private static Scenario Create(AppDbContext objDbContext)
    {
        Company objCompany = new Company { Name = "Lojão dos Plásticos", Slug = "lojao", TimeZone = "America/Belem" };
        Unit objMatriz = new Unit { IDCompany = objCompany.Id, Name = "Matriz", Slug = "lojao-matriz", Email = "gerente.matriz@lojao.com.br" };
        Unit objFilial = new Unit { IDCompany = objCompany.Id, Name = "Filial", Slug = "lojao-filial", Email = "" };
        Unit objEmpty = new Unit { IDCompany = objCompany.Id, Name = "Nova", Slug = "lojao-nova", Email = "nova@lojao.com.br" };
        Customer objAna = new Customer { IDCompany = objCompany.Id, Phone = "91991110001", Name = "Ana Souza", BirthDate = new DateOnly(1990, 3, 4) };
        Customer objBruno = new Customer { IDCompany = objCompany.Id, Phone = "91991110002", Name = "Bruno Lima", Instagram = "@bruno.lima" };
        Customer objCarla = new Customer { IDCompany = objCompany.Id, Phone = "91991110003", Name = "Carla Dias" };
        Customer objDavi = new Customer { IDCompany = objCompany.Id, Phone = "91991110004", Name = "Davi Melo" };
        objDbContext.AddRange(objCompany, objMatriz, objFilial, objEmpty, objAna, objBruno, objCarla, objDavi);
        DateTime dtVisit = new DateTime(2026, 10, 10, 15, 0, 0, DateTimeKind.Utc);
        foreach ((Customer objCustomer, Unit objUnit) in new[]
        {
            (objAna, objMatriz), (objBruno, objMatriz), (objDavi, objMatriz), (objCarla, objFilial), (objDavi, objFilial),
        })
        {
            objDbContext.CustomerUnits.Add(new CustomerUnit
            {
                IDCustomer = objCustomer.Id, IDUnit = objUnit.Id, FirstVisitAt = dtVisit, LastVisitAt = dtVisit,
            });
        }
        objDbContext.SaveChanges();
        return new Scenario(objCompany, objMatriz, objFilial, objEmpty);
    }

    private static CampaignDemoController CreateController(AppDbContext objDbContext, FakeEmailSender objSender)
    {
        CampaignDemoController objController = new CampaignDemoController(
            objDbContext, objSender, NullLogger<CampaignDemoController>.Instance);
        TestHelpers.SetUser(objController, null, "root");
        return objController;
    }

    private static T Ok<T>(ActionResult<T> objResult) => Assert.IsType<T>(Assert.IsType<OkObjectResult>(objResult.Result).Value);

    private static string Error<T>(ActionResult<T> objResult) =>
        Assert.IsType<ErrorResponse>(Assert.IsAssignableFrom<ObjectResult>(objResult.Result).Value).Error;

    [Fact]
    public void Demo_IsOnlyForTheSuperAdmin()
    {
        AuthorizeAttribute objAuthorize = Assert.Single(typeof(CampaignDemoController).GetCustomAttributes<AuthorizeAttribute>());
        Assert.Equal(ClaimsExtensions.RoleSuperAdmin, objAuthorize.Roles);
    }

    [Fact]
    public async Task Demo_SendsAllCustomersOfTheChosenUnit_ToItsEmail_WithoutTouchingCampaigns()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Scenario objScenario = Create(objDbContext);
        FakeEmailSender objSender = new FakeEmailSender();
        CampaignDemoController objController = CreateController(objDbContext, objSender);

        // A confirmação: cada unidade ativa, com o e-mail e quantos clientes vão no PDF; a mensagem padrão.
        CampaignDemoInfoDto objInfo = Ok(await objController.Get("lojao", CancellationToken.None));
        Assert.Equal("Todos os clientes", objInfo.CampaignName);
        Assert.EndsWith("Um abraço da equipe Lojão dos Plásticos.", objInfo.Message);
        Assert.Equal(["Filial", "Matriz", "Nova"], objInfo.Units.Select(unit => unit.Name));
        Assert.Equal([2, 3, 0], objInfo.Units.Select(unit => unit.Customers));
        Assert.Equal("", objInfo.Units[0].Email);

        // Unidade sem e-mail, sem clientes ou de outra empresa: não manda nada.
        Assert.Contains("não tem e-mail", Error(await objController.Send(objScenario.Filial.Id, "lojao", CancellationToken.None)));
        Assert.Contains("não tem clientes", Error(await objController.Send(objScenario.Empty.Id, "lojao", CancellationToken.None)));
        objDbContext.Companies.Add(new Company { Name = "Outra", Slug = "outra" });
        objDbContext.SaveChanges();
        Assert.IsType<NotFoundObjectResult>((await objController.Send(objScenario.Matriz.Id, "outra", CancellationToken.None)).Result);
        Assert.Empty(objSender.Sent);

        // SMTP fora do ar: avisa e não registra no Correio.
        objSender.FailTimes = 1;
        Assert.Contains("não saiu", Error(await objController.Send(objScenario.Matriz.Id, "lojao", CancellationToken.None)));
        Assert.Empty(objDbContext.SentEmails);

        CampaignDemoResultDto objResult = Ok(await objController.Send(objScenario.Matriz.Id, "lojao", CancellationToken.None));
        Assert.Equal(3, objResult.Customers);
        Assert.Equal("gerente.matriz@lojao.com.br", objResult.Email);

        // O e-mail sai igual ao de uma campanha, com o PDF de todos os clientes da unidade.
        FakeEmailSender.Email objEmail = Assert.Single(objSender.Sent);
        Assert.Equal("gerente.matriz@lojao.com.br", objEmail.To);
        Assert.StartsWith("Campanha Todos os clientes — Matriz — ", objEmail.Subject);
        Assert.EndsWith("(3 clientes)", objEmail.Subject);
        Assert.Contains("Olá, equipe da unidade Matriz!", objEmail.Body);
        Assert.Contains("Oi, {primeiro_nome}! Obrigado por usar o nosso Wi-Fi.", objEmail.Body);
        Assert.Matches(@"^campanha-todos-os-clientes-lojao-matriz-\d{4}-\d{2}-\d{2}\.pdf$", objEmail.AttachmentName);
        Assert.Equal("%PDF", Encoding.ASCII.GetString(objEmail.Attachment!, 0, 4));

        // No sistema: só uma linha no Correio, como Demonstração. Nenhuma execução nem destinatário de campanha.
        SentEmail objSent = Assert.Single(objDbContext.SentEmails);
        Assert.Equal(SentEmailKind.CampaignDemo, objSent.Kind);
        Assert.Equal(objScenario.Matriz.Id, objSent.IDUnit);
        Assert.Equal(objEmail.Subject, objSent.Subject);
        Assert.Empty(objDbContext.CampaignRuns);
        Assert.Empty(objDbContext.CampaignRecipients);
        Assert.Empty(objDbContext.CampaignDeliveries);

        // No Correio: filtra por Demonstração e remonta o PDF com os clientes de hoje.
        EmailsController objEmails = new EmailsController(objDbContext);
        TestHelpers.SetUser(objEmails, null, "root");
        ActionResult<SentEmailPageDto> objPage = await objEmails.List("lojao", null, SentEmailKind.CampaignDemo, 1, 25, CancellationToken.None);
        Assert.Single(Assert.IsType<SentEmailPageDto>(Assert.IsType<OkObjectResult>(objPage.Result).Value).Items);
        FileContentResult objPdf = Assert.IsType<FileContentResult>(await objEmails.Attachment(objSent.Id, "lojao", CancellationToken.None));
        Assert.Equal("application/pdf", objPdf.ContentType);
        Assert.Equal(objEmail.AttachmentName, objPdf.FileDownloadName);
    }
}
