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
        TestHelpers.SetUser(objController, objCompanyId, sUser);
        return objController;
    }

    private static CampaignConfig Aniversario(string sTexto = "Parabéns, {primeiro_nome}!") =>
        new CampaignConfig { Message = sTexto, SendTime = "09:00" };

    private static T Ok<T>(ActionResult<T> objResult) =>
        Assert.IsType<T>(Assert.IsType<OkObjectResult>(objResult.Result).Value);

    private static string Erro<T>(ActionResult<T> objResult) =>
        Assert.IsType<ErrorResponse>(Assert.IsType<BadRequestObjectResult>(objResult.Result).Value).Error;

    [Fact]
    public async Task Create_TipoNaoLiberadoParaAEmpresa_Recusa()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Company objCompany = CreateCompany(objDbContext, arrKinds: CampaignKind.Filtered);

        ActionResult<CampaignDetailDto> objResult = await CreateController(objDbContext, objCompany.Id).Create(
            new SaveCampaignRequest(CampaignKind.Birthday, "", Aniversario()), null, CancellationToken.None);

        Assert.Contains("não está liberada", Erro(objResult));
        Assert.Empty(objDbContext.Campaigns);
    }

    [Fact]
    public async Task Create_CriaAtivaComVersao1EAgenda_ESistemaSoUmaPorEmpresa()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Company objCompany = CreateCompany(objDbContext, arrKinds: CampaignKind.Birthday);
        CampaignsController objController = CreateController(objDbContext, objCompany.Id);

        CampaignDetailDto objCriada = Ok(await objController.Create(
            new SaveCampaignRequest(CampaignKind.Birthday, "", Aniversario()), null, CancellationToken.None));

        Assert.Equal("Aniversário", objCriada.Name); // sem nome: o do tipo
        Assert.Equal(CampaignStatus.Active, objCriada.Status);
        Assert.NotNull(objCriada.NextRunAt);
        CampaignVersion objVersao = Assert.Single(objDbContext.CampaignVersions);
        Assert.Equal(1, objVersao.Number);
        Assert.Equal("gerente", objVersao.Username);

        ActionResult<CampaignDetailDto> objSegunda = await objController.Create(
            new SaveCampaignRequest(CampaignKind.Birthday, "Outra", Aniversario()), null, CancellationToken.None);
        Assert.Contains("já tem a campanha", Erro(objSegunda));
    }

    [Fact]
    public async Task Create_Instagram_RecusaAteConfirmarComAMeta()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Company objCompany = CreateCompany(objDbContext, arrKinds: CampaignKind.Birthday);

        ActionResult<CampaignDetailDto> objResult = await CreateController(objDbContext, objCompany.Id).Create(
            new SaveCampaignRequest(CampaignKind.Birthday, "", Aniversario() with { Channel = CampaignChannel.Instagram }),
            null, CancellationToken.None);

        Assert.Contains("Instagram", Erro(objResult));
    }

    [Fact]
    public async Task Create_FiltradaUmaVezNoPassado_Recusa()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Company objCompany = CreateCompany(objDbContext, arrKinds: CampaignKind.Filtered);
        CampaignConfig objConfig = new CampaignConfig
        {
            Message = "Oi",
            SendTime = "09:00",
            Schedule = new CampaignScheduleConfig { Recurrence = CampaignRecurrence.Once, StartDate = new DateOnly(2020, 1, 1) },
        };

        ActionResult<CampaignDetailDto> objResult = await CreateController(objDbContext, objCompany.Id).Create(
            new SaveCampaignRequest(CampaignKind.Filtered, "Promo", objConfig), null, CancellationToken.None);

        Assert.Contains("nenhum disparo daqui para frente", Erro(objResult));
    }

    [Fact]
    public async Task Update_GeraVersaoComOQueMudouEQuemMudou_SemMudancaNaoGera()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Company objCompany = CreateCompany(objDbContext, arrKinds: CampaignKind.Birthday);
        CampaignDetailDto objCriada = Ok(await CreateController(objDbContext, objCompany.Id, "ana").Create(
            new SaveCampaignRequest(CampaignKind.Birthday, "", Aniversario()), null, CancellationToken.None));
        CampaignsController objBruno = CreateController(objDbContext, objCompany.Id, "bruno");

        CampaignDetailDto objEditada = Ok(await objBruno.Update(objCriada.Id,
            new SaveCampaignRequest(null, "Aniversário", Aniversario("Feliz aniversário!") with { SendTime = "10:00" }),
            null, CancellationToken.None));
        await objBruno.Update(objCriada.Id,
            new SaveCampaignRequest(null, "Aniversário", Aniversario("Feliz aniversário!") with { SendTime = "10:00" }),
            null, CancellationToken.None);

        Assert.Equal(2, objEditada.CurrentVersion);
        List<CampaignVersionDto> objVersoes = Ok(await objBruno.Versions(objCriada.Id, null, CancellationToken.None));
        Assert.Equal(2, objVersoes.Count); // a segunda edição, igual, não gerou versão
        Assert.Equal("bruno", objVersoes[0].Username);
        Assert.Contains("Horário: 09:00 → 10:00", objVersoes[0].Changes);
        Assert.Contains("Mensagem alterada", objVersoes[0].Changes);
        Assert.Equal("ana", objVersoes[1].Username);
    }

    [Fact]
    public async Task PausarERetomar_TiraEPoeNaAgenda_ERegistraQuemFez()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Company objCompany = CreateCompany(objDbContext, arrKinds: CampaignKind.Birthday);
        CampaignsController objController = CreateController(objDbContext, objCompany.Id);
        CampaignDetailDto objCriada = Ok(await objController.Create(
            new SaveCampaignRequest(CampaignKind.Birthday, "", Aniversario()), null, CancellationToken.None));

        CampaignDetailDto objPausada = Ok(await objController.Pause(objCriada.Id, null, CancellationToken.None));
        Assert.Equal(CampaignStatus.Paused, objPausada.Status);
        Assert.Null(objPausada.NextRunAt);

        CampaignDetailDto objRetomada = Ok(await objController.Resume(objCriada.Id, null, CancellationToken.None));
        Assert.Equal(CampaignStatus.Active, objRetomada.Status);
        Assert.NotNull(objRetomada.NextRunAt);

        List<CampaignEventDto> objEventos = Ok(await objController.Events(objCriada.Id, null, CancellationToken.None));
        Assert.Equal(
            new[] { CampaignEventAction.Resumed, CampaignEventAction.Paused, CampaignEventAction.Created },
            objEventos.Select(evt => evt.Action).ToArray());
    }

    [Fact]
    public async Task Preview_ContaQuemRecebeHojeEMontaUmaMensagemDeExemplo()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Company objCompany = CreateCompany(objDbContext, arrKinds: CampaignKind.Filtered);
        foreach (string sNome in new[] { "Ana Souza", "Bruno Lima" })
        {
            objDbContext.Customers.Add(new Customer
            {
                IDCompany = objCompany.Id, Phone = Guid.NewGuid().ToString("N")[..11], Name = sNome, VisitCount = 3,
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
    public async Task OutraEmpresa_NaoVeNemMexeNaCampanha()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Company objRegional = CreateCompany(objDbContext, "regional", CampaignKind.Birthday);
        Company objDoce = CreateCompany(objDbContext, "doce", CampaignKind.Birthday);
        CampaignDetailDto objDaRegional = Ok(await CreateController(objDbContext, objRegional.Id).Create(
            new SaveCampaignRequest(CampaignKind.Birthday, "", Aniversario()), null, CancellationToken.None));
        CampaignsController objAdminDoce = CreateController(objDbContext, objDoce.Id);

        Assert.IsType<NotFoundObjectResult>((await objAdminDoce.Get(objDaRegional.Id, null, CancellationToken.None)).Result);
        Assert.IsType<NotFoundObjectResult>((await objAdminDoce.Pause(objDaRegional.Id, null, CancellationToken.None)).Result);
        // Mesmo mandando ?company=regional: o admin de empresa fica preso à própria.
        Assert.IsType<NotFoundObjectResult>((await objAdminDoce.Get(objDaRegional.Id, "regional", CancellationToken.None)).Result);
    }

    [Fact]
    public async Task SuperAdmin_PrecisaDizerAEmpresa()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Company objCompany = CreateCompany(objDbContext, arrKinds: CampaignKind.Birthday);
        CampaignsController objSuper = CreateController(objDbContext, null, "root");

        Assert.IsType<BadRequestObjectResult>((await objSuper.GetAll(null, CancellationToken.None)).Result);
        List<CampaignCatalogItemDto> objCatalogo = Ok(await objSuper.Catalog(objCompany.Slug, CancellationToken.None));
        Assert.True(objCatalogo.Single(item => item.Kind == CampaignKind.Birthday).Enabled);
        Assert.False(objCatalogo.Single(item => item.Kind == CampaignKind.Filtered).Enabled);
    }

    [Fact]
    public async Task Execucao_PausarRetomarCancelar_SoNasSituacoesCertas()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Company objCompany = CreateCompany(objDbContext, arrKinds: CampaignKind.Birthday);
        CampaignsController objController = CreateController(objDbContext, objCompany.Id);
        CampaignDetailDto objCampanha = Ok(await objController.Create(
            new SaveCampaignRequest(CampaignKind.Birthday, "", Aniversario()), null, CancellationToken.None));
        CampaignRun objRun = new CampaignRun
        {
            IDCampaign = objCampanha.Id, IDCompany = objCompany.Id, Status = CampaignRunStatus.Running,
            IDCampaignVersion = objDbContext.CampaignVersions.Single().Id, VersionNumber = 1,
        };
        objDbContext.CampaignRuns.Add(objRun);
        objDbContext.SaveChanges();

        Assert.Contains("retomar", Erro(await objController.ResumeRun(objRun.Id, null, CancellationToken.None)));
        Assert.Equal(CampaignRunStatus.Paused, Ok(await objController.PauseRun(objRun.Id, null, CancellationToken.None)).Status);
        Assert.Equal(CampaignRunStatus.Running, Ok(await objController.ResumeRun(objRun.Id, null, CancellationToken.None)).Status);
        Assert.Equal(CampaignRunStatus.Cancelled, Ok(await objController.CancelRun(objRun.Id, null, CancellationToken.None)).Status);
        Assert.Contains("já terminou", Erro(await objController.CancelRun(objRun.Id, null, CancellationToken.None)));

        Assert.Equal(3, objDbContext.CampaignEvents.Count(evt => evt.IDRun == objRun.Id));
    }
}

public class CompanyCampaignSettingsTests
{
    [Fact]
    public async Task Update_LigaDesligaTiposEFuso_EDesligarTiraCampanhaDaAgenda()
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
            new UpdateCompanyRequest("Regional", true, null, null, "America/Manaus", [CampaignKind.Filtered, CampaignKind.Welcome]),
            CancellationToken.None);

        CompanyDto objDto = Assert.IsType<CompanyDto>(Assert.IsType<OkObjectResult>(objResult.Result).Value);
        Assert.Equal("America/Manaus", objDto.TimeZone);
        Assert.Equal(new[] { CampaignKind.Welcome, CampaignKind.Filtered }, objDto.CampaignKinds);
        Assert.Equal("root", objDbContext.CompanyCampaignKinds.Single(kind => kind.Kind == CampaignKind.Welcome).EnabledBy);
        // Aniversário foi desligado: a campanha dele sai da agenda.
        Assert.Null(objDbContext.Campaigns.Single().NextRunAt);
    }

    [Fact]
    public async Task Update_FusoOuTipoInvalido_Recusa()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Company objCompany = new Company { Name = "Regional", Slug = "regional" };
        objDbContext.Companies.Add(objCompany);
        objDbContext.SaveChanges();
        CompaniesController objController = new CompaniesController(objDbContext);
        TestHelpers.SetUser(objController, null, "root");

        ActionResult<CompanyDto> objFuso = await objController.Update(objCompany.Id,
            new UpdateCompanyRequest("Regional", true, null, null, "Lua/Crateras"), CancellationToken.None);
        ActionResult<CompanyDto> objTipo = await objController.Update(objCompany.Id,
            new UpdateCompanyRequest("Regional", true, null, null, null, ["Inventado"]), CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(objFuso.Result);
        Assert.IsType<BadRequestObjectResult>(objTipo.Result);
    }
}
