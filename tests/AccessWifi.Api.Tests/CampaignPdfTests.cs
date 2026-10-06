using Models.Campaigns;
using Models.DataBase;

namespace AccessWifi.Api.Tests;

/// <summary>O PDF de campanha que vai para a unidade (D20) e os links dele.</summary>
public class CampaignPdfTests
{
    [Theory]
    [InlineData("93991234567", "5593991234567")]
    [InlineData("(93) 99123-4567", "5593991234567")]
    [InlineData("5593991234567", "5593991234567")] // já com o código do país
    [InlineData("9335181234", "559335181234")]    // fixo, 10 dígitos
    public void WhatsAppNumber_PoeO55NaFrente(string sPhone, string sEsperado)
    {
        Assert.Equal(sEsperado, CampaignContact.WhatsAppNumber(sPhone));
    }

    [Fact]
    public void WhatsAppUrl_LevaAMensagemCodificada_ComEmoji()
    {
        string sUrl = CampaignContact.WhatsAppUrl("93991234567", "Feliz aniversário, Ana! 🎉");

        Assert.Equal("https://wa.me/5593991234567?text=Feliz%20anivers%C3%A1rio%2C%20Ana%21%20%F0%9F%8E%89", sUrl);
        Assert.Equal("https://wa.me/5593991234567", CampaignContact.WhatsAppUrl("93991234567", ""));
    }

    [Theory]
    [InlineData("93991234567", "(93) 99123-4567")]
    [InlineData("9335181234", "(93) 3518-1234")]
    [InlineData("5593991234567", "(93) 99123-4567")]
    [InlineData("123", "123")]
    public void FormatPhone(string sPhone, string sEsperado)
    {
        Assert.Equal(sEsperado, CampaignContact.FormatPhone(sPhone));
    }

    [Fact]
    public void WithoutEmoji_TiraOsEmojisEAvisa_SemMexerNoResto()
    {
        string sLimpo = CampaignContact.WithoutEmoji(
            "Feliz aniversário, {primeiro_nome}! 🎉 A {empresa} deseja um dia incrível ❤️ para você.", out bool bTinha);

        Assert.True(bTinha);
        Assert.Equal("Feliz aniversário, {primeiro_nome}! A {empresa} deseja um dia incrível para você.", sLimpo);
        Assert.Equal("Olá — tudo bem? Até já…", CampaignContact.WithoutEmoji("Olá — tudo bem? Até já…", out bool bNenhum));
        Assert.False(bNenhum);
    }

    [Fact]
    public void Build_GeraOPdf_ComLogoInvalidaETemaQuebrado()
    {
        CampaignPdfData objDados = new CampaignPdfData(
            "Lojas Regional", "Itaituba", "Aniversário", CampaignKind.Birthday, new DateOnly(2026, 10, 12),
            "Feliz aniversário, {primeiro_nome}! 🎉", "data:image/png;base64,isto-nao-e-base64!!",
            new ThemeColors { Brand = "vermelho", Ink = "#123" },
            [
                new CampaignPdfRow("Ana Souza", "93991234567", "ana.souza", "seg, 12/10 · 28 anos", true, "Feliz aniversário, Ana!"),
                new CampaignPdfRow("Bruno Lima", "93991230000", "", "sex, 16/10 · 36 anos", false, "Feliz aniversário, Bruno!"),
            ]);

        byte[] arrPdf = CampaignPdf.Build(objDados);

        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(arrPdf, 0, 4));
        Assert.True(arrPdf.Length > 1000);
    }

    [Fact]
    public void Build_ComLogoPngDeVerdade_EUmaCampanhaSemColunaDeInformacao()
    {
        // PNG 1×1 transparente.
        const string sLogo = "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNkYAAAAAYAAjCB0C8AAAAASUVORK5CYII=";
        CampaignPdfData objDados = new CampaignPdfData(
            "Lojas Regional", "Itaituba", "Promoção de outubro", CampaignKind.Filtered, new DateOnly(2026, 10, 12),
            "Oi {primeiro_nome}!", sLogo, new ThemeColors(),
            [new CampaignPdfRow("Ana", "93991234567", "", "", false, "Oi Ana!")]);

        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(CampaignPdf.Build(objDados), 0, 4));
    }

    [Fact]
    public void FileName_PorTipoUnidadeEDia()
    {
        Assert.Equal("campanha-aniversario-itaituba-2026-10-12.pdf",
            CampaignDeliveryDocument.FileName(CampaignKind.Birthday, "itaituba", new DateOnly(2026, 10, 12)));
        Assert.Equal("campanha-sentimos-sua-falta-sem-unidade-2026-10-12.pdf",
            CampaignDeliveryDocument.FileName(CampaignKind.WeMissYou, "", new DateOnly(2026, 10, 12)));
    }
}
