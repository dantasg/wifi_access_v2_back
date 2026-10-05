using AccessWifi.Ops;

namespace AccessWifi.Ops.Tests;

/// <summary>Guarda os avisos em vez de mandar para o Telegram/e-mail.</summary>
public class ListNotifier : INotifier
{
    public List<(string Title, string Text)> Sent { get; } = new List<(string Title, string Text)>();

    public Task NotifyAsync(string sTitle, string sText)
    {
        Sent.Add((sTitle, sText));
        return Task.CompletedTask;
    }
}

/// <summary>Relógio parado no horário que o teste quiser.</summary>
public class FixedTime : TimeProvider
{
    public DateTimeOffset Now { get; set; }

    public FixedTime(DateTimeOffset dtNow)
    {
        Now = dtNow;
    }

    public override DateTimeOffset GetUtcNow()
    {
        return Now.ToUniversalTime();
    }
}

public static class Belem
{
    /// <summary>Horário de Belém em 05/10/2026, como nos logs da produção.</summary>
    public static DateTimeOffset At(string sTime)
    {
        TimeSpan tsTime = TimeSpan.Parse(sTime);
        return new DateTimeOffset(2026, 10, 5, tsTime.Hours, tsTime.Minutes, tsTime.Seconds, Clock.s_tsBelem);
    }
}
