using System.Net;
using System.Text;
using System.Text.Json;
using AccessWifi.Api.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Models.DataBase;
using Models.Persistence;

namespace AccessWifi.Api.Tests;

/// <summary>Captura temporária dos avisos da UniFi (22/10/2026): guarda tudo como chegou, só com o código certo.</summary>
public class UnifiWebhookControllerTests
{
    private const string Token = "codigo-secreto-de-teste-1234567890";

    private static UnifiWebhookController Create(
        AppDbContext objDbContext, string? sConfiguredToken, string sMethod, string sBody, string sQuery = "",
        string sContentType = "application/json")
    {
        IConfiguration objConfiguration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["UnifiWebhook:CaptureToken"] = sConfiguredToken })
            .Build();
        DefaultHttpContext objHttpContext = new DefaultHttpContext();
        objHttpContext.Request.Method = sMethod;
        objHttpContext.Request.QueryString = new QueryString(sQuery);
        objHttpContext.Request.ContentType = sContentType;
        objHttpContext.Request.Headers["User-Agent"] = "UniFi-Alarm-Manager/1.0";
        objHttpContext.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(sBody));
        objHttpContext.Connection.RemoteIpAddress = IPAddress.Parse("177.10.20.30");
        return new UnifiWebhookController(objDbContext, objConfiguration, NullLogger<UnifiWebhookController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = objHttpContext },
        };
    }

    [Fact]
    public async Task WrongOrMissingToken_Is404_AndSavesNothing()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        Assert.IsType<NotFoundResult>(await Create(objDbContext, null, "POST", "{}").Capture(Token, null, CancellationToken.None));
        Assert.IsType<NotFoundResult>(await Create(objDbContext, "curto", "POST", "{}").Capture("curto", null, CancellationToken.None));
        Assert.IsType<NotFoundResult>(await Create(objDbContext, Token, "POST", "{}").Capture("outro-codigo-qualquer-123456", null, CancellationToken.None));
        Assert.Empty(objDbContext.UnifiWebhookCaptures);
    }

    [Fact]
    public async Task SavesEverythingAsItArrived()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        const string sJson = """{"alarm":"Client Connected","client":{"mac":"36:9d:94:1e:aa:10","name":"iPhone"}}""";

        Assert.IsType<OkObjectResult>(await Create(objDbContext, Token, "POST", sJson, "?unit=lojao-coronel-leal")
            .Capture(Token, "lojao-coronel-leal", CancellationToken.None));
        Assert.IsType<OkObjectResult>(await Create(objDbContext, Token, "GET", "", "?unit=Loja Com Espaço&mac=aa:bb")
            .Capture(Token, "Loja Com Espaço", CancellationToken.None));
        Assert.IsType<OkObjectResult>(await Create(objDbContext, Token, "POST", "não é json", "", "text/plain")
            .Capture(Token, null, CancellationToken.None));

        List<UnifiWebhookCapture> objCaptures = objDbContext.UnifiWebhookCaptures.OrderBy(capture => capture.Id).ToList();
        Assert.Equal(3, objCaptures.Count);

        UnifiWebhookCapture objPost = objCaptures[0];
        Assert.Equal("POST", objPost.Method);
        Assert.Equal("lojao-coronel-leal", objPost.Label);
        Assert.Equal(sJson, objPost.Body);
        Assert.Equal("36:9d:94:1e:aa:10", JsonDocument.Parse(objPost.BodyJson!).RootElement.GetProperty("client").GetProperty("mac").GetString());
        Assert.Equal("UniFi-Alarm-Manager/1.0", JsonDocument.Parse(objPost.HeadersJson).RootElement.GetProperty("User-Agent").GetString());
        Assert.Equal("177.10.20.30", objPost.RemoteIp);
        Assert.Equal("application/json", objPost.ContentType);

        // GET: a query fica inteira; rótulo fora do formato não é guardado como rótulo.
        Assert.Equal("GET", objCaptures[1].Method);
        Assert.Contains("mac=aa:bb", objCaptures[1].QueryString);
        Assert.Equal("", objCaptures[1].Label);

        // Corpo que não é JSON: guardado como texto, sem a cópia em JSON.
        Assert.Equal("não é json", objCaptures[2].Body);
        Assert.Null(objCaptures[2].BodyJson);

        // O código secreto não fica guardado em lugar nenhum.
        Assert.All(objCaptures, capture => Assert.DoesNotContain(Token, capture.QueryString + capture.HeadersJson + capture.Body));
    }

    [Fact]
    public async Task StopsSavingWhenFull()
    {
        using AppDbContext objDbContext = TestHelpers.CreateDbContext();
        objDbContext.UnifiWebhookCaptures.AddRange(Enumerable.Range(0, UnifiWebhookController.MaxCaptures)
            .Select(_ => new UnifiWebhookCapture { Method = "POST" }));
        objDbContext.SaveChanges();

        Assert.IsType<OkObjectResult>(await Create(objDbContext, Token, "POST", "{}").Capture(Token, null, CancellationToken.None));
        Assert.Equal(UnifiWebhookController.MaxCaptures, objDbContext.UnifiWebhookCaptures.Count());
    }
}
