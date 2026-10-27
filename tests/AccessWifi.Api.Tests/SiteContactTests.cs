using AccessWifi.Api.Controllers;
using AccessWifi.Api.Features;
using AccessWifi.Api.Features.SiteContacts;
using AccessWifiService;
using AccessWifiService.SiteContacts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Models.DataBase;
using Models.Persistence;

namespace AccessWifi.Api.Tests;

/// <summary>Formulário do site institucional (PROPOSTA_SITE.md §4): gravação, validação, tela, aviso e retenção.</summary>
public class SiteContactTests
{
    private static readonly DateTime s_dtNow = new DateTime(2026, 10, 27, 15, 0, 0, DateTimeKind.Utc);

    private static SiteContactRequest ValidRequest(
        string? sPhone = "(91) 98888-7777", bool bConsent = true, string? sWebsite = null, string? sState = "PA",
        string? sStores = "2-5", string? sUsesUnifi = "Yes", string? sEmail = "Joana@Loja.com.br", string? sMessage = "Quero ver o painel.") =>
        new SiteContactRequest(
            Name: "  Joana   Silva ", Company: "Loja da Joana", Phone: sPhone, Email: sEmail, City: "Belém",
            State: sState, Stores: sStores, UsesUnifi: sUsesUnifi, Message: sMessage, Consent: bConsent,
            Website: sWebsite, Page: "/site/",
            UtmSource: "google", UtmMedium: "cpc", UtmCampaign: "wifi-varejo", UtmTerm: "wifi para loja", UtmContent: "anuncio-1",
            Gclid: "abc123", Fbclid: "", LandingPage: "/site/?utm_source=google&gclid=abc123", Referrer: "https://www.google.com/");

    private static SiteContactController PublicController(AppDbContext objDbContext) =>
        new SiteContactController(objDbContext, NullLogger<SiteContactController>.Instance);

    private static SiteContactsController AdminController(AppDbContext objDbContext)
    {
        SiteContactsController objController = new SiteContactsController(objDbContext);
        TestHelpers.SetUser(objController, null, "root");
        return objController;
    }

    [Fact]
    public async Task Create_ValidForm_SavesCleanDataAndTheOrigin()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();

        IActionResult objResult = await PublicController(objDbContext).Create(ValidRequest(), CancellationToken.None);

        Assert.IsType<OkObjectResult>(objResult);
        SiteContact objContact = Assert.Single(objDbContext.SiteContacts);
        Assert.Equal("Joana Silva", objContact.Name);
        Assert.Equal("91988887777", objContact.Phone);
        Assert.Equal("joana@loja.com.br", objContact.Email);
        Assert.Equal("PA", objContact.State);
        Assert.Equal("google", objContact.UtmSource);
        Assert.Equal("abc123", objContact.Gclid);
        Assert.Equal("wifi para loja", objContact.UtmTerm);
        Assert.Equal(SiteContactStatus.New, objContact.Status);
        Assert.Null(objContact.NotifiedAt);
    }

    [Fact]
    public async Task Create_BaitFieldFilled_AnswersOkButSavesNothing()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();

        IActionResult objResult = await PublicController(objDbContext).Create(
            ValidRequest(sWebsite: "http://spam.example"), CancellationToken.None);

        Assert.IsType<OkObjectResult>(objResult);
        Assert.Empty(objDbContext.SiteContacts);
    }

    public static TheoryData<SiteContactRequest, string> InvalidRequests => new()
    {
        { ValidRequest(bConsent: false), "Marque a autorização de contato." },
        { ValidRequest(sPhone: "9888-7777"), "Informe o WhatsApp com DDD." },
        { ValidRequest(sPhone: null), "Informe o WhatsApp com DDD." },
        { ValidRequest(sState: "XX"), "Escolha a UF." },
        { ValidRequest(sStores: "muitas"), "Escolha quantas lojas." },
        { ValidRequest(sUsesUnifi: "talvez"), "Responda se já usa UniFi." },
        { ValidRequest(sEmail: "joana@"), "Confira o e-mail." },
        { ValidRequest(sMessage: new string('a', 1001)), "A mensagem deve ter no máximo 1000 caracteres." },
    };

    [Theory]
    [MemberData(nameof(InvalidRequests))]
    public async Task Create_InvalidForm_Returns400WithTheReason(SiteContactRequest objRequest, string sExpected)
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();

        IActionResult objResult = await PublicController(objDbContext).Create(objRequest, CancellationToken.None);

        BadRequestObjectResult objBad = Assert.IsType<BadRequestObjectResult>(objResult);
        Assert.Equal(sExpected, Assert.IsType<ErrorResponse>(objBad.Value).Error);
        Assert.Empty(objDbContext.SiteContacts);
    }

    [Theory]
    [InlineData("(91) 98888-7777", "91988887777")]
    [InlineData("+55 91 98888-7777", "91988887777")]
    [InlineData("091 3222-1234", "9132221234")]
    [InlineData("98888-7777", "")]
    [InlineData("", "")]
    public void NormalizePhone_KeepsDddAndNumber(string sInput, string sExpected)
    {
        Assert.Equal(sExpected, SiteContactRules.NormalizePhone(sInput));
    }

    [Fact]
    public async Task Create_EmptyEmailAndMessage_AreOptional()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();

        IActionResult objResult = await PublicController(objDbContext).Create(
            ValidRequest(sEmail: "", sMessage: null), CancellationToken.None);

        Assert.IsType<OkObjectResult>(objResult);
        SiteContact objContact = Assert.Single(objDbContext.SiteContacts);
        Assert.Equal("", objContact.Email);
        Assert.Equal("", objContact.Message);
    }

    private static void Seed(AppDbContext objDbContext)
    {
        objDbContext.SiteContacts.AddRange(
            new SiteContact { Name = "Ana", Company = "Farmácia Ana", City = "Belém", Phone = "91988880001", CreatedAt = s_dtNow.AddDays(-3) },
            new SiteContact { Name = "Bruno", Company = "Mercado Bruno", City = "Castanhal", Phone = "91988880002", CreatedAt = s_dtNow.AddDays(-2), Status = SiteContactStatus.Talking },
            new SiteContact { Name = "Carla", Company = "Café Carla", City = "Belém", Phone = "91988880003", CreatedAt = s_dtNow.AddDays(-1) });
        objDbContext.SaveChanges();
    }

    [Fact]
    public async Task List_NewestFirst_FiltersByStatusAndSearch_AndCountsEachStatus()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Seed(objDbContext);
        SiteContactsController objController = AdminController(objDbContext);

        SiteContactPageDto objAll = GetPage(await objController.List(null, null, 1, 20, CancellationToken.None));
        Assert.Equal(["Carla", "Bruno", "Ana"], objAll.Items.Select(item => item.Name));
        Assert.Equal(new SiteContactCountsDto(2, 1, 0, 0), objAll.Counts);

        SiteContactPageDto objNew = GetPage(await objController.List("new", null, 1, 20, CancellationToken.None));
        Assert.Equal(2, objNew.Total);

        SiteContactPageDto objSearch = GetPage(await objController.List(null, "belém", 1, 20, CancellationToken.None));
        Assert.Equal(["Carla", "Ana"], objSearch.Items.Select(item => item.Name));

        SiteContactPageDto objPhone = GetPage(await objController.List(null, "0002", 1, 20, CancellationToken.None));
        Assert.Equal("Bruno", Assert.Single(objPhone.Items).Name);

        Assert.IsType<BadRequestObjectResult>((await objController.List("9", null, 1, 20, CancellationToken.None)).Result);
    }

    [Fact]
    public async Task Update_ChangesStatusAndNotes_RecordsWhoAndWhen()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Seed(objDbContext);
        SiteContact objAna = objDbContext.SiteContacts.Single(contact => contact.Name == "Ana");

        ActionResult<SiteContactDto> objResult = await AdminController(objDbContext).Update(
            objAna.Id, new SiteContactUpdateRequest("Client", "Fechou 2 lojas."), CancellationToken.None);

        SiteContactDto objDto = Assert.IsType<SiteContactDto>(Assert.IsType<OkObjectResult>(objResult.Result).Value);
        Assert.Equal("Client", objDto.Status);
        Assert.Equal("Fechou 2 lojas.", objDto.Notes);
        Assert.Equal("root", objDto.StatusChangedBy);
        Assert.NotNull(objDto.StatusChangedAt);

        ActionResult<SiteContactDto> objBad = await AdminController(objDbContext).Update(
            objAna.Id, new SiteContactUpdateRequest("Sumido", null), CancellationToken.None);
        Assert.IsType<BadRequestObjectResult>(objBad.Result);
    }

    [Fact]
    public async Task Delete_RemovesTheContact()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Seed(objDbContext);
        SiteContact objAna = objDbContext.SiteContacts.Single(contact => contact.Name == "Ana");

        Assert.IsType<NoContentResult>(await AdminController(objDbContext).Delete(objAna.Id, CancellationToken.None));
        Assert.IsType<NotFoundObjectResult>(await AdminController(objDbContext).Delete(objAna.Id, CancellationToken.None));
        Assert.Equal(2, objDbContext.SiteContacts.Count());
    }

    private static SiteContactPageDto GetPage(ActionResult<SiteContactPageDto> objResult) =>
        Assert.IsType<SiteContactPageDto>(Assert.IsType<OkObjectResult>(objResult.Result).Value);

    private static SiteContactNotifier Notifier(AppDbContext objDbContext, FakeEmailSender objSender, string sTo = "dono@exemplo.com.br") =>
        new SiteContactNotifier(
            objDbContext, objSender,
            Options.Create(new SiteContactOptions { NotifyEmail = sTo, MaxAttempts = 3 }),
            NullLogger<SiteContactNotifier>.Instance);

    [Fact]
    public async Task Notifier_SendsOneEmailPerContact_WithTheOriginAndTheWhatsAppLink()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        await PublicController(objDbContext).Create(ValidRequest(), CancellationToken.None);
        FakeEmailSender objSender = new FakeEmailSender();

        await Notifier(objDbContext, objSender).TickAsync(s_dtNow);
        await Notifier(objDbContext, objSender).TickAsync(s_dtNow.AddMinutes(1));

        FakeEmailSender.Email objEmail = Assert.Single(objSender.Sent);
        Assert.Equal("dono@exemplo.com.br", objEmail.To);
        Assert.Equal("Novo contato do site: Loja da Joana (Belém/PA)", objEmail.Subject);
        Assert.Contains("WhatsApp: (91) 98888-7777 (https://wa.me/5591988887777)", objEmail.Body);
        Assert.Contains("Lojas: 2 a 5 lojas", objEmail.Body);
        Assert.Contains("Já usa UniFi: Sim", objEmail.Body);
        Assert.Contains("Origem: anúncio do Google, google / cpc", objEmail.Body);
        Assert.Contains("Palavra-chave: wifi para loja", objEmail.Body);
        Assert.Equal(s_dtNow, objDbContext.SiteContacts.Single().NotifiedAt);
    }

    [Fact]
    public async Task Notifier_SmtpDown_TriesAgainNextRound_AndGivesUpAfterTheLimit()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        await PublicController(objDbContext).Create(ValidRequest(), CancellationToken.None);
        FakeEmailSender objSender = new FakeEmailSender { FailTimes = 5 };

        for (int iRound = 0; iRound < 5; iRound++)
        {
            await Notifier(objDbContext, objSender).TickAsync(s_dtNow.AddMinutes(iRound));
        }

        SiteContact objContact = objDbContext.SiteContacts.Single();
        Assert.Empty(objSender.Sent);
        Assert.Null(objContact.NotifiedAt);
        Assert.Equal(3, objContact.NotifyAttempts);
        Assert.Equal("SMTP fora do ar", objContact.NotifyError);
    }

    [Fact]
    public async Task Notifier_WithoutAddress_DoesNothing()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        await PublicController(objDbContext).Create(ValidRequest(), CancellationToken.None);
        FakeEmailSender objSender = new FakeEmailSender();

        await Notifier(objDbContext, objSender, sTo: "").TickAsync(s_dtNow);

        Assert.Empty(objSender.Sent);
        Assert.Equal(0, objDbContext.SiteContacts.Single().NotifyAttempts);
    }

    [Fact]
    public async Task Retention_DeletesContactsAfter12Months_ButKeepsTheOnesThatBecameClients()
    {
        // PROPOSTA_SITE.md, D11.
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        objDbContext.SiteContacts.AddRange(
            new SiteContact { Name = "Antigo", CreatedAt = s_dtNow.AddMonths(-13) },
            new SiteContact { Name = "Cliente antigo", CreatedAt = s_dtNow.AddMonths(-13), Status = SiteContactStatus.Client },
            new SiteContact { Name = "Recente", CreatedAt = s_dtNow.AddMonths(-11) });
        objDbContext.SaveChanges();
        IConfiguration objConfig = new ConfigurationBuilder().AddInMemoryCollection([]).Build();

        await new LeadRetentionService(objDbContext, objConfig, NullLogger<LeadRetentionService>.Instance)
            .PurgeExpiredLeadsAsync(s_dtNow, CancellationToken.None);

        Assert.Equal(["Cliente antigo", "Recente"], objDbContext.SiteContacts.Select(contact => contact.Name).OrderBy(sName => sName));
    }
}
