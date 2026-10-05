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
    public async Task SequenciaReal_05_10_AvisaNaPrimeiraRecusa_ResumeEm10Min_EAvisaQuandoVolta()
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
    public async Task BotaoTestarDoPainel_NaoEhCliente_NaoAvisa()
    {
        ListNotifier objNotifier = new ListNotifier();
        UnifiMonitor objMonitor = new UnifiMonitor(objNotifier);

        await objMonitor.LineAsync("      Teste de conexão UniFi falhou na unidade itaituba.", Belem.At("15:20:22"));
        await objMonitor.LineAsync(Reason, Belem.At("15:20:22"));
        await RunClockAsync(objMonitor, "15:20:23", "15:31:00");

        Assert.Empty(objNotifier.Sent);
    }

    [Fact]
    public async Task RecusaSemLinhaDeMotivo_AvisaDepoisDe3Segundos()
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
    public async Task NovoProblemaDepoisDeVoltar_AvisaDeNovoNaHora()
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
    public async Task LiberacaoBoaSemIncidente_NaoAvisa()
    {
        ListNotifier objNotifier = new ListNotifier();
        UnifiMonitor objMonitor = new UnifiMonitor(objNotifier);

        await objMonitor.LineAsync(Success, Belem.At("09:08:35"));

        Assert.Empty(objNotifier.Sent);
    }
}
