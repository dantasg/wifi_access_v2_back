using AccessWifi.Api.Controllers;
using AccessWifi.Api.Features;
using AccessWifi.Api.Features.Campaigns;
using AccessWifi.Api.Features.Companies;
using Microsoft.AspNetCore.Mvc;
using Models.Campaigns;
using Models.DataBase;
using Models.Persistence;

namespace AccessWifi.Api.Tests;

public class CampaignsControllerTests
{
    private static Company CreateCompany(AppDbContext objDbContext, string sSlug = "regional", params string[] arrKinds)
    {
        Company objCompany = new Company { Name = "Lojas Regional", Slug = sSlug, TimeZone = "America/Belem" };
        objDbContext.Companies.Add(objCompany);
        foreach (string sKind in arrKinds)
        {
            objDbContext.CompanyCampaignKinds.Add(new CompanyCampaignKind { IDCompany = objCompany.Id, Kind = sKind });
        }
        objDbContext.SaveChanges();
        return objCompany;
    }

    private static CampaignsController CreateController(AppDbContext objDbContext, Guid? objCompanyId, string sUser = "gerente")
    {
        CampaignsController objController = new CampaignsController(objDbContext);
        if (objCompanyId is Guid objId)
        {
            TestHelpers.SetCompanyUser(objController, objDbContext, objId, sUser);
        }
        else
        {
            TestHelpers.SetUser(objController, null, sUser);
        }
        return objController;
    }

    private static CampaignConfig Birthday(string sText = "Parabéns, {primeiro_nome}!") =>
        new CampaignConfig { Message = sText, SendTime = "09:00" };

    private static T Ok<T>(ActionResult<T> objResult) =>
        Assert.IsType<T>(Assert.IsType<OkObjectResult>(objResult.Result).Value);

    private static string ErrorMessage<T>(ActionResult<T> objResult) =>
        Assert.IsType<ErrorResponse>(Assert.IsType<BadRequestObjectResult>(objResult.Result).Value).Error;

    [Fact]
    public async Task Create_KindNotEnabledForCompany_Rejects()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Company objCompany = CreateCompany(objDbContext, arrKinds: CampaignKind.WeMissYou);

        ActionResult<CampaignDetailDto> objResult = await CreateController(objDbContext, objCompany.Id).Create(
            new SaveCampaignRequest(CampaignKind.Birthday, "", Birthday()), null, CancellationToken.None);

        Assert.Contains("não está liberada", ErrorMessage(objResult));
        Assert.Empty(objDbContext.Campaigns);
    }

    [Fact]
    public async Task Create_CreatesActiveWithVersion1AndSchedule_AndSystemOnlyOnePerCompany()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Company objCompany = CreateCompany(objDbContext, arrKinds: CampaignKind.Birthday);
        CampaignsController objController = CreateController(objDbContext, objCompany.Id);

        CampaignDetailDto objCreated = Ok(await objController.Create(
            new SaveCampaignRequest(CampaignKind.Birthday, "", Birthday()), null, CancellationToken.None));

        Assert.Equal("Aniversário", objCreated.Name); // sem nome: o do tipo
        Assert.Equal(CampaignStatus.Active, objCreated.Status);
        Assert.NotNull(objCreated.NextRunAt);
        CampaignVersion objVersion = Assert.Single(objDbContext.CampaignVersions);
        Assert.Equal(1, objVersion.Number);
        Assert.Equal("gerente", objVersion.Username);

        ActionResult<CampaignDetailDto> objSecond = await objController.Create(
            new SaveCampaignRequest(CampaignKind.Birthday, "Outra", Birthday()), null, CancellationToken.None);
        Assert.Contains("já tem a campanha", ErrorMessage(objSecond));
    }

    [Fact]
    public async Task Create_Instagram_RejectsUntilConfirmedWithMeta()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Company objCompany = CreateCompany(objDbContext, arrKinds: CampaignKind.Birthday);

        ActionResult<CampaignDetailDto> objResult = await CreateController(objDbContext, objCompany.Id).Create(
            new SaveCampaignRequest(CampaignKind.Birthday, "", Birthday() with { Channel = CampaignChannel.Instagram }),
            null, CancellationToken.None);

        Assert.Contains("Instagram", ErrorMessage(objResult));
    }

    [Fact]
    public async Task Create_Filtered_ComingSoon_RejectsEvenIfEnabledBefore()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        // Liberação antiga, de antes da filtrada virar "em breve" (D23).
        Company objCompany = CreateCompany(objDbContext, arrKinds: CampaignKind.Filtered);
        CampaignConfig objConfig = new CampaignConfig
        {
            Message = "Oi",
            SendTime = "09:00",
            Schedule = new CampaignScheduleConfig { Recurrence = CampaignRecurrence.Daily, StartDate = new DateOnly(2030, 1, 1) },
        };

        ActionResult<CampaignDetailDto> objResult = await CreateController(objDbContext, objCompany.Id).Create(
            new SaveCampaignRequest(CampaignKind.Filtered, "Promo", objConfig), null, CancellationToken.None);

        Assert.Contains("em breve", ErrorMessage(objResult));
        Assert.Empty(objDbContext.Campaigns);
    }

    [Fact]
    public async Task Update_CreatesVersionWithWhatAndWhoChanged_NoChangeCreatesNone()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Company objCompany = CreateCompany(objDbContext, arrKinds: CampaignKind.Birthday);
        CampaignDetailDto objCreated = Ok(await CreateController(objDbContext, objCompany.Id, "ana").Create(
            new SaveCampaignRequest(CampaignKind.Birthday, "", Birthday()), null, CancellationToken.None));
        CampaignsController objBruno = CreateController(objDbContext, objCompany.Id, "bruno");

        CampaignDetailDto objEdited = Ok(await objBruno.Update(objCreated.Id,
            new SaveCampaignRequest(null, "Aniversário", Birthday("Feliz aniversário!") with { SendTime = "10:00" }),
            null, CancellationToken.None));
        await objBruno.Update(objCreated.Id,
            new SaveCampaignRequest(null, "Aniversário", Birthday("Feliz aniversário!") with { SendTime = "10:00" }),
            null, CancellationToken.None);

        Assert.Equal(2, objEdited.CurrentVersion);
        List<CampaignVersionDto> objVersions = Ok(await objBruno.Versions(objCreated.Id, null, CancellationToken.None));
        Assert.Equal(2, objVersions.Count); // a segunda edição, igual, não gerou versão
        Assert.Equal("bruno", objVersions[0].Username);
        Assert.Contains("Horário: 09:00 → 10:00", objVersions[0].Changes);
        Assert.Contains("Mensagem alterada", objVersions[0].Changes);
        Assert.Equal("ana", objVersions[1].Username);
    }

    [Fact]
    public async Task PauseAndResume_RemovesAndAddsToSchedule_AndRecordsWhoDidIt()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Company objCompany = CreateCompany(objDbContext, arrKinds: CampaignKind.Birthday);
        CampaignsController objController = CreateController(objDbContext, objCompany.Id);
        CampaignDetailDto objCreated = Ok(await objController.Create(
            new SaveCampaignRequest(CampaignKind.Birthday, "", Birthday()), null, CancellationToken.None));

        CampaignDetailDto objPaused = Ok(await objController.Pause(objCreated.Id, null, CancellationToken.None));
        Assert.Equal(CampaignStatus.Paused, objPaused.Status);
        Assert.Null(objPaused.NextRunAt);

        CampaignDetailDto objResumed = Ok(await objController.Resume(objCreated.Id, null, CancellationToken.None));
        Assert.Equal(CampaignStatus.Active, objResumed.Status);
        Assert.NotNull(objResumed.NextRunAt);

        List<CampaignEventDto> objEvents = Ok(await objController.Events(objCreated.Id, null, CancellationToken.None));
        Assert.Equal(
            new[] { CampaignEventAction.Resumed, CampaignEventAction.Paused, CampaignEventAction.Created },
            objEvents.Select(evt => evt.Action).ToArray());
        Assert.All(objEvents, evt => Assert.Null(evt.RunDate));
    }

    [Fact]
    public async Task Preview_CountsTodaysRecipientsAndBuildsSampleMessage()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Company objCompany = CreateCompany(objDbContext, arrKinds: CampaignKind.Filtered);
        foreach (string sName in new[] { "Ana Souza", "Bruno Lima" })
        {
            objDbContext.Customers.Add(new Customer
            {
                IDCompany = objCompany.Id, Phone = Guid.NewGuid().ToString("N")[..11], Name = sName, VisitCount = 3,
                LastVisitAt = DateTime.UtcNow,
            });
        }
        objDbContext.SaveChanges();

        AudiencePreviewDto objPreview = Ok(await CreateController(objDbContext, objCompany.Id).Preview(
            new AudiencePreviewRequest(CampaignKind.Filtered, new CampaignConfig
            {
                Message = "Oi {primeiro_nome}, da {empresa}",
                Filters = new CampaignFilters { VisitsMin = 2 },
            }),
            null, CancellationToken.None));

        Assert.Equal(2, objPreview.Count);
        Assert.StartsWith("Oi ", objPreview.SampleMessage);
        Assert.EndsWith(", da Lojas Regional", objPreview.SampleMessage);
    }

    [Fact]
    public async Task OtherCompany_CannotSeeOrChangeCampaign()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Company objRegional = CreateCompany(objDbContext, "regional", CampaignKind.Birthday);
        Company objSample = CreateCompany(objDbContext, "exemplo", CampaignKind.Birthday);
        CampaignDetailDto objFromRegional = Ok(await CreateController(objDbContext, objRegional.Id).Create(
            new SaveCampaignRequest(CampaignKind.Birthday, "", Birthday()), null, CancellationToken.None));
        CampaignsController objSampleAdmin = CreateController(objDbContext, objSample.Id);

        Assert.IsType<NotFoundObjectResult>((await objSampleAdmin.Get(objFromRegional.Id, null, CancellationToken.None)).Result);
        Assert.IsType<NotFoundObjectResult>((await objSampleAdmin.Pause(objFromRegional.Id, null, CancellationToken.None)).Result);
        // Mesmo mandando ?company=regional: o admin de empresa fica preso à própria.
        Assert.IsType<NotFoundObjectResult>((await objSampleAdmin.Get(objFromRegional.Id, "regional", CancellationToken.None)).Result);
    }

    [Fact]
    public async Task SuperAdmin_MustSayCompany()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Company objCompany = CreateCompany(objDbContext, arrKinds: CampaignKind.Birthday);
        CampaignsController objSuper = CreateController(objDbContext, null, "root");

        Assert.IsType<BadRequestObjectResult>((await objSuper.GetAll(null, CancellationToken.None)).Result);
        List<CampaignCatalogItemDto> objCatalog = Ok(await objSuper.Catalog(objCompany.Slug, CancellationToken.None));
        Assert.True(objCatalog.Single(item => item.Kind == CampaignKind.Birthday).Enabled);
        CampaignCatalogItemDto objFiltered = objCatalog.Single(item => item.Kind == CampaignKind.Filtered);
        Assert.False(objFiltered.Enabled);
        Assert.False(objFiltered.Available); // "em breve" (D23)
        Assert.DoesNotContain(objCatalog, item => item.Kind == "Welcome"); // saiu do sistema (D22)
    }

    [Fact]
    public async Task History_ShowsEmailsPerUnit_AndDownloadsPdfAgain()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Company objCompany = CreateCompany(objDbContext, arrKinds: CampaignKind.Birthday);
        Unit objUnit = new Unit { IDCompany = objCompany.Id, Name = "Itaituba", Slug = "itaituba", Email = "gerente@regional.com.br" };
        Customer objCustomer = new Customer { IDCompany = objCompany.Id, Phone = "93991234567", Name = "Ana Souza", IDLastUnit = objUnit.Id };
        objDbContext.AddRange(objUnit, objCustomer);
        objDbContext.SaveChanges();
        CampaignsController objController = CreateController(objDbContext, objCompany.Id);
        CampaignDetailDto objCampaign = Ok(await objController.Create(
            new SaveCampaignRequest(CampaignKind.Birthday, "", Birthday()), null, CancellationToken.None));
        CampaignRun objRun = new CampaignRun
        {
            IDCampaign = objCampaign.Id, IDCompany = objCompany.Id, Status = CampaignRunStatus.Completed,
            IDCampaignVersion = objDbContext.CampaignVersions.Single().Id, VersionNumber = 1,
            LocalDate = new DateOnly(2026, 10, 12),
        };
        CampaignDelivery objDelivery = new CampaignDelivery
        {
            IDRun = objRun.Id, IDUnit = objUnit.Id, UnitName = "Itaituba", Email = "gerente@regional.com.br",
            Status = CampaignDeliveryStatus.Sent, RecipientCount = 1, Attempts = 1,
            FileName = "campanha-aniversario-itaituba-2026-10-12.pdf", SentAt = DateTime.UtcNow,
        };
        objDbContext.AddRange(objRun, objDelivery, new CampaignRecipient
        {
            IDRun = objRun.Id, IDCustomer = objCustomer.Id, IDUnit = objUnit.Id, Phone = objCustomer.Phone,
            Name = objCustomer.Name, Message = "Parabéns, Ana!", Info = "seg, 12/10 · 30 anos",
            EventDate = new DateOnly(2026, 10, 12), Status = CampaignRecipientStatus.Sent,
        });
        objDbContext.SaveChanges();

        CampaignDeliveryDto objDeliveryDto = Assert.Single(Ok(await objController.Deliveries(objRun.Id, null, CancellationToken.None)));
        Assert.Equal("gerente@regional.com.br", objDeliveryDto.Email);
        Assert.Equal(CampaignDeliveryStatus.Sent, objDeliveryDto.Status);
        CampaignRecipientDto objRecipient = Assert.Single(Ok(await objController.Recipients(
            objRun.Id, null, null, 1, 50, CancellationToken.None)).Items);
        Assert.Equal("Itaituba", objRecipient.Unit);
        Assert.Equal("seg, 12/10 · 30 anos", objRecipient.Info);

        FileContentResult objPdf = Assert.IsType<FileContentResult>(
            await objController.DeliveryPdf(objRun.Id, objDeliveryDto.Id, null, CancellationToken.None));
        Assert.Equal("application/pdf", objPdf.ContentType);
        Assert.Equal("campanha-aniversario-itaituba-2026-10-12.pdf", objPdf.FileDownloadName);
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(objPdf.FileContents, 0, 4));
    }

    [Fact]
    public async Task Run_PauseResumeCancel_OnlyInRightStatuses()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Company objCompany = CreateCompany(objDbContext, arrKinds: CampaignKind.Birthday);
        CampaignsController objController = CreateController(objDbContext, objCompany.Id);
        CampaignDetailDto objCampaign = Ok(await objController.Create(
            new SaveCampaignRequest(CampaignKind.Birthday, "", Birthday()), null, CancellationToken.None));
        CampaignRun objRun = new CampaignRun
        {
            IDCampaign = objCampaign.Id, IDCompany = objCompany.Id, Status = CampaignRunStatus.Running,
            IDCampaignVersion = objDbContext.CampaignVersions.Single().Id, VersionNumber = 1,
            LocalDate = new DateOnly(2026, 9, 30),
        };
        objDbContext.CampaignRuns.Add(objRun);
        objDbContext.SaveChanges();

        Assert.Contains("retomar", ErrorMessage(await objController.ResumeRun(objRun.Id, null, CancellationToken.None)));
        Assert.Equal(CampaignRunStatus.Paused, Ok(await objController.PauseRun(objRun.Id, null, CancellationToken.None)).Status);
        Assert.Equal(CampaignRunStatus.Running, Ok(await objController.ResumeRun(objRun.Id, null, CancellationToken.None)).Status);
        Assert.Equal(CampaignRunStatus.Cancelled, Ok(await objController.CancelRun(objRun.Id, null, CancellationToken.None)).Status);
        Assert.Contains("já terminou", ErrorMessage(await objController.CancelRun(objRun.Id, null, CancellationToken.None)));

        Assert.Equal(3, objDbContext.CampaignEvents.Count(evt => evt.IDRun == objRun.Id));

        // O histórico diz de qual dia é a execução pausada/retomada/cancelada.
        List<CampaignEventDto> objEvents = Ok(await objController.Events(objCampaign.Id, null, CancellationToken.None));
        Assert.All(objEvents.Where(evt => evt.RunId == objRun.Id),
            evt => Assert.Equal(new DateOnly(2026, 9, 30), evt.RunDate));
        Assert.Null(objEvents.Single(evt => evt.Action == CampaignEventAction.Created).RunDate);
    }
}

public class CompanyCampaignSettingsTests
{
    [Fact]
    public async Task Update_TogglesKindsAndTimeZone_AndDisablingRemovesCampaignFromSchedule()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Company objCompany = new Company { Name = "Regional", Slug = "regional" };
        objDbContext.Companies.Add(objCompany);
        objDbContext.CompanyCampaignKinds.Add(new CompanyCampaignKind { IDCompany = objCompany.Id, Kind = CampaignKind.Birthday });
        Campaign objCampaign = new Campaign
        {
            IDCompany = objCompany.Id, Kind = CampaignKind.Birthday, Name = "Aniversário",
            ConfigJson = new CampaignConfig { Message = "Oi", SendTime = "09:00" }.ToJson(),
            NextRunAt = DateTime.UtcNow.AddHours(1),
        };
        objDbContext.Campaigns.Add(objCampaign);
        objDbContext.SaveChanges();
        CompaniesController objController = new CompaniesController(objDbContext);
        TestHelpers.SetUser(objController, null, "root");

        ActionResult<CompanyDto> objResult = await objController.Update(objCompany.Id,
            new UpdateCompanyRequest("Regional", true, null, "America/Manaus", [CampaignKind.WeMissYou, CampaignKind.SignupAnniversary]),
            CancellationToken.None);

        CompanyDto objDto = Assert.IsType<CompanyDto>(Assert.IsType<OkObjectResult>(objResult.Result).Value);
        Assert.Equal("America/Manaus", objDto.TimeZone);
        Assert.Equal(new[] { CampaignKind.SignupAnniversary, CampaignKind.WeMissYou }, objDto.CampaignKinds);
        Assert.Equal("root", objDbContext.CompanyCampaignKinds.Single(kind => kind.Kind == CampaignKind.WeMissYou).EnabledBy);
        // Aniversário foi desligado: a campanha dele sai da agenda.
        Assert.Null(objDbContext.Campaigns.Single().NextRunAt);
    }

    [Fact]
    public async Task Update_InvalidTimeZoneOrKind_Rejects()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Company objCompany = new Company { Name = "Regional", Slug = "regional" };
        objDbContext.Companies.Add(objCompany);
        objDbContext.SaveChanges();
        CompaniesController objController = new CompaniesController(objDbContext);
        TestHelpers.SetUser(objController, null, "root");

        ActionResult<CompanyDto> objZone = await objController.Update(objCompany.Id,
            new UpdateCompanyRequest("Regional", true, null, "Lua/Crateras"), CancellationToken.None);
        ActionResult<CompanyDto> objKind = await objController.Update(objCompany.Id,
            new UpdateCompanyRequest("Regional", true, null, null, ["Inventado"]), CancellationToken.None);
        ActionResult<CompanyDto> objWelcome = await objController.Update(objCompany.Id,
            new UpdateCompanyRequest("Regional", true, null, null, ["Welcome"]), CancellationToken.None);
        ActionResult<CompanyDto> objFiltered = await objController.Update(objCompany.Id,
            new UpdateCompanyRequest("Regional", true, null, null, [CampaignKind.Filtered]), CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(objZone.Result);
        Assert.IsType<BadRequestObjectResult>(objKind.Result);
        Assert.IsType<BadRequestObjectResult>(objWelcome.Result); // saiu do sistema (D22)
        Assert.Contains("em breve", Assert.IsType<ErrorResponse>(
            Assert.IsType<BadRequestObjectResult>(objFiltered.Result).Value).Error); // D23
        Assert.Empty(objDbContext.CompanyCampaignKinds);
    }
}
