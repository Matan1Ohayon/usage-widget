namespace AIUsage.Core;

public enum Provider { Claude, Codex }

public enum WindowKind { FiveHour, Weekly }

/// <summary>Bar colour bands: green 0–60, yellow 60–75, red 75–100.</summary>
public enum UsageLevel { Green, Yellow, Red }

public static class Names
{
    public static readonly Provider[] Providers = [Provider.Claude, Provider.Codex];
    public static readonly WindowKind[] Windows = [WindowKind.FiveHour, WindowKind.Weekly];

    public static string DisplayName(this Provider provider) => provider == Provider.Claude ? "Claude" : "Codex";

    /// <summary>Stable lowercase key, matching the macOS app ("claude", "codex").</summary>
    public static string Key(this Provider provider) => provider == Provider.Claude ? "claude" : "codex";

    public static string DisplayName(this WindowKind kind) => kind == WindowKind.FiveHour ? "5-hour" : "weekly";

    public static string RowLabel(this WindowKind kind) => kind == WindowKind.FiveHour ? "5 hours" : "Weekly";

    /// <summary>Stable key, matching the macOS app ("fiveHour", "weekly").</summary>
    public static string Key(this WindowKind kind) => kind == WindowKind.FiveHour ? "fiveHour" : "weekly";

    public static Provider? ParseProvider(string key) => key switch
    {
        "claude" => Provider.Claude,
        "codex" => Provider.Codex,
        _ => null,
    };

    public static WindowKind? ParseWindow(string key) => key switch
    {
        "fiveHour" => WindowKind.FiveHour,
        "weekly" => WindowKind.Weekly,
        _ => null,
    };
}

/// <summary>Usage in one rate-limit window, as reported by the provider.</summary>
public sealed record UsageWindow(double Percent, DateTimeOffset? ResetsAt)
{
    /// <summary>Once the reset time has passed the window is empty until the next fetch says otherwise.</summary>
    public UsageWindow Current(DateTimeOffset now) =>
        ResetsAt is { } reset && reset <= now
            ? new UsageWindow(0, null)
            : new UsageWindow(Math.Clamp(Percent, 0, 100), ResetsAt);
}

public sealed record ProviderUsage(UsageWindow? FiveHour, UsageWindow? Weekly, string? Plan, DateTimeOffset FetchedAt)
{
    public UsageWindow? Window(WindowKind kind) => kind == WindowKind.FiveHour ? FiveHour : Weekly;
}

public sealed class UsageException(string message) : Exception(message);

public static class Levels
{
    public const double WarningThreshold = 75;

    public static UsageLevel From(double percent) => percent switch
    {
        < 60 => UsageLevel.Green,
        < WarningThreshold => UsageLevel.Yellow,
        _ => UsageLevel.Red,
    };
}
