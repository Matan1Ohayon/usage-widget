using System.Globalization;
using System.Text.Json;

namespace AIUsage.Core;

/// <summary>GET https://api.anthropic.com/api/oauth/usage</summary>
public static class ClaudeParser
{
    public static ProviderUsage Parse(string json, string? plan, DateTimeOffset fetchedAt)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        return new ProviderUsage(Window(root, "five_hour"), Window(root, "seven_day"), plan, fetchedAt);
    }

    private static UsageWindow? Window(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var window) || window.ValueKind != JsonValueKind.Object) return null;
        var percent = window.Number("utilization") ?? 0;
        var reset = window.String("resets_at") is { } text ? Json.ParseIsoDate(text) : null;
        return new UsageWindow(percent, reset);
    }

    /// <summary>The credentials Claude Code stores: <c>{"claudeAiOauth": {"accessToken", "expiresAt" (ms), "subscriptionType"}}</c>.</summary>
    public sealed record Credentials(string AccessToken, DateTimeOffset? ExpiresAt, string? Plan);

    public static Credentials ParseCredentials(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("claudeAiOauth", out var oauth) || oauth.String("accessToken") is not { } token)
            throw new UsageException("Not signed in to Claude Code");
        var expires = oauth.Number("expiresAt") is { } ms ? DateTimeOffset.FromUnixTimeMilliseconds((long)ms) : (DateTimeOffset?)null;
        return new Credentials(token, expires, Json.Capitalized(oauth.String("subscriptionType")));
    }
}

/// <summary>GET https://chatgpt.com/backend-api/wham/usage, plus the Codex CLI's session logs as a fallback.</summary>
public static class CodexParser
{
    private const double FiveHourSeconds = 5 * 3600;
    private const double WeeklySeconds = 7 * 24 * 3600;

    public static ProviderUsage Parse(string json, DateTimeOffset fetchedAt)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var windows = new List<(double? Seconds, UsageWindow Window)>();
        if (root.TryGetProperty("rate_limit", out var limits) && limits.ValueKind == JsonValueKind.Object)
        {
            foreach (var name in new[] { "primary_window", "secondary_window" })
            {
                if (!limits.TryGetProperty(name, out var w) || w.ValueKind != JsonValueKind.Object || w.Number("used_percent") is not { } used)
                    continue;
                var reset = w.Number("reset_at") is { } at ? Json.FromUnix(at)
                    : w.Number("reset_after_seconds") is { } after ? fetchedAt.AddSeconds(after) : (DateTimeOffset?)null;
                windows.Add((w.Number("limit_window_seconds"), new UsageWindow(used, reset)));
            }
        }
        var (fiveHour, weekly) = Assign(windows);
        return new ProviderUsage(fiveHour, weekly, Json.Capitalized(root.String("plan_type")), fetchedAt);
    }

    /// <summary>
    /// The newest <c>rate_limits</c> snapshot Codex wrote into its session logs. Lines look like
    /// <c>{"timestamp": "...", "type": "event_msg", "payload": {"type": "token_count", "rate_limits": {...}}}</c>.
    /// </summary>
    public static ProviderUsage? ParseSessionLine(string line)
    {
        if (!line.Contains("\"rate_limits\"", StringComparison.Ordinal)) return null;
        try
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            if (!root.TryGetProperty("payload", out var payload) || payload.ValueKind != JsonValueKind.Object ||
                !payload.TryGetProperty("rate_limits", out var limits) || limits.ValueKind != JsonValueKind.Object)
                return null;

            var stamp = root.String("timestamp") is { } text ? Json.ParseIsoDate(text) ?? DateTimeOffset.Now : DateTimeOffset.Now;
            var windows = new List<(double? Seconds, UsageWindow Window)>();
            foreach (var name in new[] { "primary", "secondary" })
            {
                if (!limits.TryGetProperty(name, out var w) || w.ValueKind != JsonValueKind.Object || w.Number("used_percent") is not { } used)
                    continue;
                var reset = w.Number("resets_at") is { } at ? Json.FromUnix(at)
                    : w.Number("resets_in_seconds") is { } inSeconds ? stamp.AddSeconds(inSeconds) : (DateTimeOffset?)null;
                windows.Add((w.Number("window_minutes") * 60, new UsageWindow(used, reset)));
            }
            if (windows.Count == 0) return null;
            var (fiveHour, weekly) = Assign(windows);
            return new ProviderUsage(fiveHour, weekly, Json.Capitalized(limits.String("plan_type")), stamp);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Match windows by their length when it's known, otherwise by position (primary = 5h, secondary = weekly).</summary>
    private static (UsageWindow?, UsageWindow?) Assign(List<(double? Seconds, UsageWindow Window)> windows)
    {
        var fiveHour = windows.FirstOrDefault(w => w.Seconds == FiveHourSeconds).Window;
        var weekly = windows.FirstOrDefault(w => w.Seconds == WeeklySeconds).Window;
        if (fiveHour is null && weekly is null)
        {
            fiveHour = windows.ElementAtOrDefault(0).Window;
            weekly = windows.ElementAtOrDefault(1).Window;
        }
        return (fiveHour, weekly);
    }
}

internal static class Json
{
    public static double? Number(this JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetDouble() : null;

    public static string? String(this JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    public static DateTimeOffset FromUnix(double seconds) => DateTimeOffset.FromUnixTimeMilliseconds((long)Math.Round(seconds * 1000));

    public static DateTimeOffset? ParseIsoDate(string text) =>
        DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date) ? date : null;

    /// <summary>"plus" → "Plus", like Swift's <c>capitalized</c>.</summary>
    public static string? Capitalized(string? text) =>
        string.IsNullOrEmpty(text) ? null : CultureInfo.InvariantCulture.TextInfo.ToTitleCase(text.ToLowerInvariant());
}
