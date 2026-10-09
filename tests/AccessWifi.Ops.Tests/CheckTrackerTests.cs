using AccessWifi.Ops;

namespace AccessWifi.Ops.Tests;

public class CheckTrackerTests
{
    [Fact]
    public async Task Breaks_Reminds_AndRecovers()
    {
        OpsState objState = new OpsState();
        ListNotifier objNotifier = new ListNotifier();
        FixedTime objTime = new FixedTime(Belem.At("10:00:00"));
        CheckTracker objTracker = new CheckTracker(objState, objNotifier, objTime);

        // Tudo bem: nada a avisar.
        await objTracker.CheckAsync("api", "API (portal e painel)", true, "HTTP 400", 1);
        Assert.Empty(objNotifier.Sent);

        // Quebrou: avisa na hora.
        objTime.Now = Belem.At("10:05:00");
        await objTracker.CheckAsync("api", "API (portal e painel)", false, "a API não responde", 1);
        Assert.Equal("🔴 API (portal e painel): problema", Assert.Single(objNotifier.Sent).Title);
        Assert.True(objState.Checks["api"].Failing);

        // Continua quebrada, mas antes de 1 h: não repete.
        objTime.Now = Belem.At("10:50:00");
        await objTracker.CheckAsync("api", "API (portal e painel)", false, "a API não responde", 1);
        Assert.Single(objNotifier.Sent);

        // 1 h depois do último aviso: lembra.
        objTime.Now = Belem.At("11:05:00");
        await objTracker.CheckAsync("api", "API (portal e painel)", false, "a API não responde", 1);
        Assert.Equal(2, objNotifier.Sent.Count);
        Assert.Contains("continua com problema", objNotifier.Sent[1].Title);
        Assert.Contains("Desde 05/10/2026 10:05", objNotifier.Sent[1].Text);

        // Voltou: avisa e limpa.
        objTime.Now = Belem.At("11:10:00");
        await objTracker.CheckAsync("api", "API (portal e painel)", true, "HTTP 400", 1);
        Assert.Equal(3, objNotifier.Sent.Count);
        Assert.Equal("✅ API (portal e painel): voltou ao normal", objNotifier.Sent[2].Title);
        Assert.Contains("de 05/10/2026 10:05 até 05/10/2026 11:10", objNotifier.Sent[2].Text);
        Assert.False(objState.Checks["api"].Failing);
    }
}
