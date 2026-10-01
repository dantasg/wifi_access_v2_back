using Models.DataBase;

namespace AccessWifi.Api.Tests;

public class InstagramHandleTests
{
    [Theory]
    [InlineData("@itsthejordan_", "itsthejordan_")]
    [InlineData("daniel_dantogues", "daniel_dantogues")]
    [InlineData("  @Ana.Souza  ", "ana.souza")]
    [InlineData("@@ana", "ana")]
    [InlineData("https://www.instagram.com/ana.souza/?igsh=abc123", "ana.souza")]
    [InlineData("instagram.com/ana_souza", "ana_souza")]
    [InlineData("http://instagr.am/ana", "ana")]
    [InlineData("a", "a")]
    [InlineData("abcdefghijklmnopqrstuvwxyz1234", "abcdefghijklmnopqrstuvwxyz1234")]
    public void Normalize_FormatoValido_DevolveOUsuario(string sDigitado, string sEsperado)
    {
        Assert.Equal(sEsperado, InstagramHandle.Normalize(sDigitado));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("@")]
    [InlineData("biell6555@gmail.com")]
    [InlineData("laison@")]
    [InlineData("daniel dantogues")]
    [InlineData("joão")]
    [InlineData("ana-souza")]
    [InlineData(".ana")]
    [InlineData("ana.")]
    [InlineData("ana..souza")]
    [InlineData("abcdefghijklmnopqrstuvwxyz12345")]
    [InlineData("instagram.com/")]
    public void Normalize_ForaDoFormato_ViraVazio(string? sDigitado)
    {
        Assert.Equal("", InstagramHandle.Normalize(sDigitado));
    }

    [Theory]
    [InlineData("@itsthejordan_", "https://www.instagram.com/itsthejordan_")]
    [InlineData("daniel_dantogues", "https://www.instagram.com/daniel_dantogues")]
    [InlineData("https://www.instagram.com/Ana.Souza/?igsh=abc123", "https://www.instagram.com/ana.souza")]
    [InlineData("biell6555@gmail.com", "")]
    [InlineData("", "")]
    public void ProfileUrl_DevolveOLinkDoPerfilOuVazio(string sDigitado, string sEsperado)
    {
        Assert.Equal(sEsperado, InstagramHandle.ProfileUrl(sDigitado));
    }
}
