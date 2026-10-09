using AccessWifi.Ops;

namespace AccessWifi.Ops.Tests;

public class UnifiMonitorTests
{
    // Linhas como a API escreve no journal (com o recuo do console logger).
    private const string Failure = "      Falha ao autorizar guest na UniFi da unidade itaituba.";
    private const string Reason = "      AccessWifi.Api.Infrastructure.Unifi.UnifiException: "
        + "A chave de API não alcança este console UniFi (confira o escopo da chave).";
    private const string Success = "      Autorização UniFi (nuvem) pelo caminho clássico em 1284 ms.";

    private static async Task FailAsync(UnifiMonitor objMonitor, string sTime, bool bWithReason = true)
    {
        await objMonitor.LineAsync(Failure, Belem.At(sTime));
        if (bWithReason)
        {
            await objMonitor.LineAsync(Reason, Belem.At(sTime));
        }
    }

    private static async Task RunClockAsync(UnifiMonitor objMonitor, string sFrom, string sTo)
    {
        for (DateTimeOffset dtNow = Belem.At(sFrom); dtNow <= Belem.At(sTo); dtNow = dtNow.AddSeconds(1))
        {
            await objMonitor.TickAsync(dtNow);
        }
    }

    [Fact]
    public async Task RealSequence_05_10_WarnsOnFirstRejection_SummarizesIn10Min_AndWarnsOnRecovery()
    {
        ListNotifier objNotifier = new ListNotifier();
        UnifiMonitor objMonitor = new UnifiMonitor(objNotifier);

        // 15:14:34 — a primeira recusa já avisa, com unidade e motivo.
        await FailAsync(objMonitor, "15:14:34");
        (string sTitle, string sText) = Assert.Single(objNotifier.Sent);
        Assert.Contains("recusou a liberação", sTitle);
        Assert.Contains("Unidade: itaituba", sText);
        Assert.Contains("não alcança este console", sText);

        // A mesma pessoa tocou mais 6 vezes em 21 s: conta, mas não manda mensagem por toque.
        foreach (string sTime in new[] { "15:14:36", "15:14:39", "15:14:46", "15:14:48", "15:14:51", "15:14:55" })
        {
            await FailAsync(objMonitor, sTime);
        }
        Assert.Single(objNotifier.Sent);
        Assert.Equal(7, objMonitor.CurrentIncident!.Attempts);

        // Resumo só 10 min depois do primeiro aviso.
        await RunClockAsync(objMonitor, "15:14:56", "15:24:33");
        Assert.Single(objNotifier.Sent);
        await RunClockAsync(objMonitor, "15:24:34", "15:24:40");
        Assert.Equal(2, objNotifier.Sent.Count);
        Assert.Contains("Mais 6 tentativa(s)", objNotifier.Sent[1].Text);
        Assert.Contains("7 desde 15:14:34", objNotifier.Sent[1].Text);

        // 15:57:56 — a próxima liberação boa fecha o incidente.
        await objMonitor.LineAsync(Success, Belem.At("15:57:56"));
        Assert.Equal(3, objNotifier.Sent.Count);
        Assert.Contains("voltou a liberar", objNotifier.Sent[2].Title);
        Assert.Contains("7 tentativa(s) recusada(s) entre 15:14:34 e 15:14:55", objNotifier.Sent[2].Text);
        Assert.Contains("15:57:56", objNotifier.Sent[2].Text);
        Assert.Null(objMonitor.CurrentIncident);
    }

    [Fact]
    public async Task PanelTestButton_IsNotCustomer_DoesNotWarn()
    {
        ListNotifier objNotifier = new ListNotifier();
        UnifiMonitor objMonitor = new UnifiMonitor(objNotifier);

        await objMonitor.LineAsync("      Teste de conexão UniFi falhou na unidade itaituba.", Belem.At("15:20:22"));
        await objMonitor.LineAsync(Reason, Belem.At("15:20:22"));
        await RunClockAsync(objMonitor, "15:20:23", "15:31:00");

        Assert.Empty(objNotifier.Sent);
    }

    [Fact]
    public async Task RejectionWithoutReasonLine_WarnsAfter3Seconds()
    {
        ListNotifier objNotifier = new ListNotifier();
        UnifiMonitor objMonitor = new UnifiMonitor(objNotifier);

        await FailAsync(objMonitor, "16:05:00", bWithReason: false);
        await RunClockAsync(objMonitor, "16:05:01", "16:05:02");
        Assert.Empty(objNotifier.Sent);
        Assert.True(objMonitor.WaitingReason);

        await objMonitor.TickAsync(Belem.At("16:05:03"));
        (string _, string sText) = Assert.Single(objNotifier.Sent);
        Assert.Contains("(sem detalhe no log)", sText);
    }

    [Fact]
    public async Task NewProblemAfterRecovery_WarnsAgainAtOnce()
    {
        ListNotifier objNotifier = new ListNotifier();
        UnifiMonitor objMonitor = new UnifiMonitor(objNotifier);

        await FailAsync(objMonitor, "10:00:00");
        await objMonitor.LineAsync(Success, Belem.At("10:05:00"));
        await FailAsync(objMonitor, "11:00:00");

        Assert.Equal(3, objNotifier.Sent.Count);
        Assert.Contains("recusou a liberação", objNotifier.Sent[2].Title);
    }

    [Fact]
    public async Task SuccessfulAuthorizationWithoutIncident_DoesNotWarn()
    {
        ListNotifier objNotifier = new ListNotifier();
        UnifiMonitor objMonitor = new UnifiMonitor(objNotifier);

        await objMonitor.LineAsync(Success, Belem.At("09:08:35"));

        Assert.Empty(objNotifier.Sent);
    }

    // ---------------------------------------------------------------- pontos de acesso

    private const string UnknownAp = "      Portal aberto por um ponto de acesso desconhecido d0:21:f9:aa:bb:01 no endereço "
        + "vps11702.panel.icontainer.online: a loja não foi identificada.";
    private const string RepeatedAp = "      Ponto de acesso 8c:30:66:4e:9b:58 em mais de uma unidade: o portal não escolhe a loja por ele.";
    private const string DevicesNotRead = "      Aparelhos da unidade itaituba não lidos na nuvem da UniFi: "
        + "Chave de API da nuvem UniFi inválida ou revogada.";

    [Fact]
    public async Task UnknownAp_WarnsOnceEvery6h()
    {
        ListNotifier objNotifier = new ListNotifier();
        UnifiMonitor objMonitor = new UnifiMonitor(objNotifier);

        await objMonitor.LineAsync(UnknownAp, Belem.At("10:00:00"));
        await objMonitor.LineAsync(UnknownAp, Belem.At("10:00:01"));
        await objMonitor.LineAsync(UnknownAp, Belem.At("15:59:59"));

        (string sTitle, string sText) = Assert.Single(objNotifier.Sent);
        Assert.Contains("nenhuma loja tem", sTitle);
        Assert.Contains("AP: d0:21:f9:aa:bb:01", sText);
        Assert.Contains("Endereço: vps11702.panel.icontainer.online", sText);
        Assert.Null(objMonitor.CurrentIncident);

        await objMonitor.LineAsync(UnknownAp, Belem.At("16:00:00"));
        Assert.Equal(2, objNotifier.Sent.Count);
    }

    [Fact]
    public async Task RepeatedApAndFailedRead_EachWarnsOnce()
    {
        ListNotifier objNotifier = new ListNotifier();
        UnifiMonitor objMonitor = new UnifiMonitor(objNotifier);

        // O serviço tenta de novo a cada 5 min: o aviso não pode repetir a cada rodada.
        foreach (string sTime in new[] { "10:00:00", "10:05:00", "10:10:00" })
        {
            await objMonitor.LineAsync(RepeatedAp, Belem.At(sTime));
            await objMonitor.LineAsync(DevicesNotRead, Belem.At(sTime));
        }

        Assert.Equal(2, objNotifier.Sent.Count);
        Assert.Contains("mais de uma unidade", objNotifier.Sent[0].Title);
        Assert.Contains("AP: 8c:30:66:4e:9b:58", objNotifier.Sent[0].Text);
        Assert.Contains("não foram lidos", objNotifier.Sent[1].Title);
        Assert.Contains("Unidade: itaituba", objNotifier.Sent[1].Text);
        Assert.Contains("inválida ou revogada", objNotifier.Sent[1].Text);
    }
}
