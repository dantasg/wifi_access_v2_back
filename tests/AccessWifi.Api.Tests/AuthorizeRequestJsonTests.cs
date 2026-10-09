using System.Text.Json;
using AccessWifi.Api.Features.Authorize;

namespace AccessWifi.Api.Tests;

/// <summary>O pedido do portal chega pelo JSON do navegador, com as mesmas opções da API (camelCase).</summary>
public class AuthorizeRequestJsonTests
{
    private static readonly JsonSerializerOptions s_objWebOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);

    [Fact]
    public void Read_EnglishNames_FillsAllFields()
    {
        AuthorizeRequest objRequest = JsonSerializer.Deserialize<AuthorizeRequest>(
            """
            {"name":"Ana","instagram":"@ana","phone":"(93) 90000-0001","birthDate":"12/10/1994","consent":true,
             "unit":"itaituba","mac":"aa:bb:cc:dd:ee:ff","ap":"11:22:33:44:55:66","ssid":"PIX","url":"https://x","host":"h"}
            """, s_objWebOptions)!;

        Assert.Equal(new AuthorizeRequest("Ana", "@ana", "(93) 90000-0001", "12/10/1994", true,
            "itaituba", "aa:bb:cc:dd:ee:ff", "11:22:33:44:55:66", "PIX", "https://x", "h"), objRequest);
    }

    [Fact]
    public void Read_OldPortugueseNames_StillAccepted()
    {
        AuthorizeRequest objRequest = JsonSerializer.Deserialize<AuthorizeRequest>(
            """
            {"nome":"Ana","instagram":"@ana","telefone":"(93) 90000-0001","nascimento":"12/10/1994","consentimento":true,
             "unit":"itaituba","mac":"aa:bb:cc:dd:ee:ff"}
            """, s_objWebOptions)!;

        Assert.Equal("Ana", objRequest.Name);
        Assert.Equal("(93) 90000-0001", objRequest.Phone);
        Assert.Equal("12/10/1994", objRequest.BirthDate);
        Assert.True(objRequest.Consent);
        Assert.Null(objRequest.Host);
    }

    [Fact]
    public void Read_MissingFields_StayNullAndConsentFalse()
    {
        AuthorizeRequest objRequest = JsonSerializer.Deserialize<AuthorizeRequest>("""{"mac":"aa"}""", s_objWebOptions)!;

        Assert.Null(objRequest.Name);
        Assert.Null(objRequest.Phone);
        Assert.False(objRequest.Consent);
    }

    [Fact]
    public void Read_WrongType_Throws()
    {
        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<AuthorizeRequest>("""{"consent":"sim"}""", s_objWebOptions));
    }
}
