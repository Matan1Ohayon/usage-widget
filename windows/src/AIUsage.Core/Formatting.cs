using System.Globalization;

namespace AIUsage.Core;

/// <summary>
/// English-only, 24-hour formatting ("14:30", "Thu 8 Oct, 10:00") regardless of system language.
/// Mirrors <c>macos/Sources/UsageCore/Formatting.swift</c>; both are pinned by <c>shared/fixtures/expectations.json</c>.
/// </summary>
public static class UsageFormat
{
    private static readonly string[] Weekdays = ["Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat"];
    private static readonly string[] Months = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];

    // Swift's rounded() rounds halves away from zero; .NET's default is banker's rounding.
    private static double Round(double value) => Math.Round(value, MidpointRounding.AwayFromZero);

    private static double Seconds(DateTimeOffset date) => date.ToUnixTimeMilliseconds() / 1000.0;

    /// <summary>Providers report resets a fraction of a second before the minute (e.g. 11:29:59.8), so round to the nearest minute.</summary>
    public static DateTimeOffset Rounded(DateTimeOffset date) =>
        DateTimeOffset.FromUnixTimeSeconds((long)Round(Seconds(date) / 60) * 60);

    private static (string Weekday, int Day, string Month, string Time) Parts(DateTimeOffset date, TimeZoneInfo? timeZone)
    {
        var local = TimeZoneInfo.ConvertTime(Rounded(date), timeZone ?? TimeZoneInfo.Local);
        return (Weekdays[(int)local.DayOfWeek], local.Day, Months[local.Month - 1],
                local.ToString("HH:mm", CultureInfo.InvariantCulture));
    }

    public static string Time(DateTimeOffset date, TimeZoneInfo? timeZone = null) => Parts(date, timeZone).Time;

    /// <summary>"Thu 8 Oct, 10:00"</summary>
    public static string DayAndTime(DateTimeOffset date, TimeZoneInfo? timeZone = null)
    {
        var p = Parts(date, timeZone);
        return $"{p.Weekday} {p.Day} {p.Month}, {p.Time}";
    }

    /// <summary>"Sat 11:04" — unambiguous inside a 7-day window, and fits the small widget.</summary>
    public static string WeekdayAndTime(DateTimeOffset date, TimeZoneInfo? timeZone = null)
    {
        var p = Parts(date, timeZone);
        return $"{p.Weekday} {p.Time}";
    }

    /// <summary>
    /// "3h 06m", "1d 22h", "12m" — the gap between the clock's minute and the displayed reset minute,
    /// so "Resets 14:30" at 11:24 always reads "3h 06m".
    /// </summary>
    public static string Countdown(DateTimeOffset now, DateTimeOffset date)
    {
        var nowMinute = (long)Math.Floor(Seconds(now) / 60);
        var resetMinute = (long)Round(Seconds(date) / 60);
        var minutes = Math.Max(0, resetMinute - nowMinute);
        long days = minutes / 1440, hours = minutes % 1440 / 60, mins = minutes % 60;
        if (days > 0) return $"{days}d {hours}h";
        if (hours > 0) return $"{hours}h {mins:00}m";
        return $"{mins}m";
    }

    public static string Percent(double value) => $"{(long)Round(value)}%";

    /// <summary>Row subtitle: 5-hour shows the exact time and countdown, weekly shows the day.</summary>
    public static string ResetLine(WindowKind kind, UsageWindow window, DateTimeOffset now, TimeZoneInfo? timeZone = null)
    {
        if (window.ResetsAt is not { } reset) return "Starts with your next message";
        return kind == WindowKind.FiveHour
            ? $"Resets {Time(reset, timeZone)} ({Countdown(now, reset)})"
            : $"Resets {DayAndTime(reset, timeZone)}";
    }

    /// <summary>"Codex · 5-hour limit at 75%", "Claude · Weekly limit at 75%".</summary>
    public static string NotificationTitle(Provider provider, WindowKind kind)
    {
        var name = kind.DisplayName();
        return $"{provider.DisplayName()} · {char.ToUpperInvariant(name[0])}{name[1..]} limit at {(int)Levels.WarningThreshold}%";
    }

    public static string NotificationBody(WindowKind kind, UsageWindow window, DateTimeOffset now, TimeZoneInfo? timeZone = null)
    {
        var used = $"You've used {Percent(window.Percent)} of your {kind.DisplayName()} limit.";
        if (window.ResetsAt is not { } reset) return used;
        var left = Countdown(now, reset);
        if (kind == WindowKind.FiveHour) return $"{used} Next reset at {Time(reset, timeZone)} (in {left}).";
        var p = Parts(reset, timeZone);
        return $"{used} Next reset {p.Weekday} {p.Day} {p.Month} at {p.Time} (in {left}).";
    }
}
