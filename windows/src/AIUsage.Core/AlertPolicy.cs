namespace AIUsage.Core;

/// <summary>Decides when to send the 75% warning: once per provider, per window, per reset cycle.</summary>
public sealed class AlertPolicy(Dictionary<string, DateTimeOffset>? notified = null)
{
    /// <summary>Reset time of the cycle we last warned about, keyed by "claude.fiveHour" etc.</summary>
    public Dictionary<string, DateTimeOffset> Notified { get; } = notified ?? [];

    /// <summary>Reset times can drift by a few seconds between fetches; anything closer than this is the same cycle.</summary>
    private static readonly TimeSpan SameCycleTolerance = TimeSpan.FromMinutes(30);

    public static string Key(Provider provider, WindowKind kind) => $"{provider.Key()}.{kind.Key()}";

    /// <summary>True (and recorded) when this window has crossed the threshold and we haven't warned for this cycle yet.</summary>
    public bool ShouldNotify(Provider provider, WindowKind kind, UsageWindow window)
    {
        if (window.Percent < Levels.WarningThreshold) return false;
        var key = Key(provider, kind);
        var cycle = window.ResetsAt ?? DateTimeOffset.MaxValue;
        if (Notified.TryGetValue(key, out var last) && Distance(last, cycle) < SameCycleTolerance) return false;
        Notified[key] = cycle;
        return true;
    }

    private static TimeSpan Distance(DateTimeOffset a, DateTimeOffset b) =>
        a == b ? TimeSpan.Zero
        : a == DateTimeOffset.MaxValue || b == DateTimeOffset.MaxValue ? TimeSpan.MaxValue
        : (a - b).Duration();
}
