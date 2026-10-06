using System.Text.Json;
using AIUsage.Core;

namespace AIUsage.Core.Tests;

/// <summary>
/// Runs the cross-platform cases in shared/fixtures/expectations.json.
/// The macOS test suite runs the same file, so both apps stay in lockstep.
/// </summary>
public class SharedFixtureTests
{
    private static readonly string Directory = FindFixtures();
    private static readonly JsonElement Expected = JsonDocument.Parse(Read("expectations.json")).RootElement;
    private static readonly TimeZoneInfo Zone = TimeZoneInfo.FindSystemTimeZoneById(Expected.GetProperty("timeZone").GetString()!);

    private static string FindFixtures()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "shared", "fixtures");
            if (System.IO.Directory.Exists(candidate)) return candidate;
        }
        throw new DirectoryNotFoundException("shared/fixtures not found above the test output directory");
    }

    private static string Read(string name) => File.ReadAllText(Path.Combine(Directory, name));

    private static DateTimeOffset? Date(JsonElement item, string key) =>
        item.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.Number
            ? DateTimeOffset.FromUnixTimeSeconds(value.GetInt64())
            : null;

    private static void Check(UsageWindow? window, JsonElement spec)
    {
        if (spec.ValueKind == JsonValueKind.Null)
        {
            Assert.Null(window);
            return;
        }
        Assert.NotNull(window);
        Assert.Equal(spec.GetProperty("percent").GetDouble(), window.Percent);
        if (Date(spec, "resetsAtEpoch") is { } reset) Assert.Equal(reset, window.ResetsAt);
    }

    [Fact]
    public void ClaudeUsage()
    {
        var usage = ClaudeParser.Parse(Read("claude-usage.json"), null, DateTimeOffset.Now);
        var spec = Expected.GetProperty("claudeUsage");
        var five = spec.GetProperty("fiveHour");
        var weekly = spec.GetProperty("weekly");
        Assert.Equal(five.GetProperty("percent").GetDouble(), usage.FiveHour!.Percent);
        Assert.Equal(five.GetProperty("time").GetString(), UsageFormat.Time(usage.FiveHour.ResetsAt!.Value, Zone));
        Assert.Equal(weekly.GetProperty("percent").GetDouble(), usage.Weekly!.Percent);
        Assert.Equal(weekly.GetProperty("dayAndTime").GetString(), UsageFormat.DayAndTime(usage.Weekly.ResetsAt!.Value, Zone));
        Assert.Equal(weekly.GetProperty("weekdayAndTime").GetString(), UsageFormat.WeekdayAndTime(usage.Weekly.ResetsAt!.Value, Zone));
    }

    [Fact]
    public void ClaudeCredentials()
    {
        var credentials = ClaudeParser.ParseCredentials(Read("claude-credentials.json"));
        var spec = Expected.GetProperty("claudeCredentials");
        Assert.Equal(spec.GetProperty("accessToken").GetString(), credentials.AccessToken);
        Assert.Equal(spec.GetProperty("plan").GetString(), credentials.Plan);
        Assert.Equal(Date(spec, "expiresAtEpoch"), credentials.ExpiresAt);
    }

    [Fact]
    public void CodexUsage()
    {
        var usage = CodexParser.Parse(Read("codex-usage.json"), DateTimeOffset.Now);
        var spec = Expected.GetProperty("codexUsage");
        Assert.Equal(spec.GetProperty("plan").GetString(), usage.Plan);
        Check(usage.FiveHour, spec.GetProperty("fiveHour"));
        Check(usage.Weekly, spec.GetProperty("weekly"));

        var weeklyOnly = CodexParser.Parse(Read("codex-usage-weekly-only.json"), DateTimeOffset.Now);
        var weeklySpec = Expected.GetProperty("codexUsageWeeklyOnly");
        Check(weeklyOnly.FiveHour, weeklySpec.GetProperty("fiveHour"));
        Check(weeklyOnly.Weekly, weeklySpec.GetProperty("weekly"));
    }

    [Fact]
    public void CodexSessionLog()
    {
        var usage = Read("codex-session.jsonl").Split('\n').Reverse().Select(CodexParser.ParseSessionLine).First(u => u is not null);
        var spec = Expected.GetProperty("codexSession");
        Assert.Equal(spec.GetProperty("plan").GetString(), usage!.Plan);
        Check(usage.FiveHour, spec.GetProperty("fiveHour"));
        Check(usage.Weekly, spec.GetProperty("weekly"));
        Assert.Null(CodexParser.ParseSessionLine("""{"type":"response_item"}"""));
    }

    [Fact]
    public void LevelBands()
    {
        foreach (var item in Expected.GetProperty("levels").EnumerateArray())
            Assert.Equal(item.GetProperty("level").GetString(), Levels.From(item.GetProperty("percent").GetDouble()).ToString().ToLowerInvariant());
    }

    [Fact]
    public void Dates()
    {
        foreach (var item in Expected.GetProperty("dates").EnumerateArray())
        {
            var date = Date(item, "epoch")!.Value;
            Assert.Equal(item.GetProperty("time").GetString(), UsageFormat.Time(date, Zone));
            Assert.Equal(item.GetProperty("dayAndTime").GetString(), UsageFormat.DayAndTime(date, Zone));
            Assert.Equal(item.GetProperty("weekdayAndTime").GetString(), UsageFormat.WeekdayAndTime(date, Zone));
        }
    }

    [Fact]
    public void Percents()
    {
        foreach (var item in Expected.GetProperty("percents").EnumerateArray())
            Assert.Equal(item.GetProperty("text").GetString(), UsageFormat.Percent(item.GetProperty("value").GetDouble()));
    }

    [Fact]
    public void Countdowns()
    {
        foreach (var item in Expected.GetProperty("countdowns").EnumerateArray())
            Assert.Equal(item.GetProperty("text").GetString(), UsageFormat.Countdown(Date(item, "now")!.Value, Date(item, "reset")!.Value));
    }

    [Fact]
    public void ResetLines()
    {
        foreach (var item in Expected.GetProperty("resetLines").EnumerateArray())
        {
            var kind = Names.ParseWindow(item.GetProperty("kind").GetString()!)!.Value;
            var window = new UsageWindow(50, Date(item, "reset"));
            Assert.Equal(item.GetProperty("text").GetString(), UsageFormat.ResetLine(kind, window, Date(item, "now")!.Value, Zone));
        }
    }

    [Fact]
    public void Notifications()
    {
        foreach (var item in Expected.GetProperty("notifications").EnumerateArray())
        {
            var provider = Names.ParseProvider(item.GetProperty("provider").GetString()!)!.Value;
            var kind = Names.ParseWindow(item.GetProperty("kind").GetString()!)!.Value;
            var window = new UsageWindow(item.GetProperty("percent").GetDouble(), Date(item, "reset"));
            Assert.Equal(item.GetProperty("title").GetString(), UsageFormat.NotificationTitle(provider, kind));
            Assert.Equal(item.GetProperty("body").GetString(), UsageFormat.NotificationBody(kind, window, Date(item, "now")!.Value, Zone));
        }
    }
}
