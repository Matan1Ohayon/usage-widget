using AIUsage.Core;

namespace AIUsage.Core.Tests;

public class AlertPolicyTests
{
    private static readonly DateTimeOffset Reset = DateTimeOffset.FromUnixTimeSeconds(1_791_291_810);

    [Fact]
    public void BelowThresholdNeverNotifies() =>
        Assert.False(new AlertPolicy().ShouldNotify(Provider.Claude, WindowKind.FiveHour, new UsageWindow(74.9, Reset)));

    [Fact]
    public void NotifiesOncePerCycle()
    {
        var policy = new AlertPolicy();
        Assert.True(policy.ShouldNotify(Provider.Claude, WindowKind.FiveHour, new UsageWindow(75, Reset)));
        Assert.False(policy.ShouldNotify(Provider.Claude, WindowKind.FiveHour, new UsageWindow(80, Reset)));
        // Reset time jitters by a second between fetches: still the same cycle.
        Assert.False(policy.ShouldNotify(Provider.Claude, WindowKind.FiveHour, new UsageWindow(90, Reset.AddSeconds(1))));
        // Other windows and providers are tracked separately.
        Assert.True(policy.ShouldNotify(Provider.Claude, WindowKind.Weekly, new UsageWindow(75, Reset)));
        Assert.True(policy.ShouldNotify(Provider.Codex, WindowKind.FiveHour, new UsageWindow(75, Reset)));
    }

    [Fact]
    public void NotifiesAgainAfterReset()
    {
        var policy = new AlertPolicy();
        Assert.True(policy.ShouldNotify(Provider.Codex, WindowKind.FiveHour, new UsageWindow(76, Reset)));
        Assert.True(policy.ShouldNotify(Provider.Codex, WindowKind.FiveHour, new UsageWindow(76, Reset.AddHours(5))));
    }

    [Fact]
    public void UnknownResetTimeNotifiesOnce()
    {
        var policy = new AlertPolicy();
        Assert.True(policy.ShouldNotify(Provider.Codex, WindowKind.Weekly, new UsageWindow(90, null)));
        Assert.False(policy.ShouldNotify(Provider.Codex, WindowKind.Weekly, new UsageWindow(95, null)));
    }

    [Fact]
    public void ElapsedWindowReadsAsEmpty()
    {
        var now = DateTimeOffset.Now;
        Assert.Equal(new UsageWindow(0, null), new UsageWindow(90, now.AddSeconds(-1)).Current(now));
        Assert.Equal(100, new UsageWindow(120, now.AddMinutes(1)).Current(now).Percent);
    }
}

public class UsageStoreTests : IDisposable
{
    private sealed class RecordingNotifier : INotifier
    {
        public List<(string Title, string Body)> Posted { get; } = [];
        public void Post(string id, string title, string body) => Posted.Add((title, body));
    }

    private readonly string _statePath = Path.Combine(Path.GetTempPath(), $"aiusage-test-{Guid.NewGuid()}", "state.json");

    public void Dispose()
    {
        var dir = Path.GetDirectoryName(_statePath)!;
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
    }

    private static Func<CancellationToken, Task<FetchResult>> Returns(Func<FetchResult> result) => _ => Task.FromResult(result());

    private static FetchResult Usage(double fiveHour, double weekly) => FetchResult.Ok(new ProviderUsage(
        new UsageWindow(fiveHour, DateTimeOffset.Now.AddHours(2)),
        new UsageWindow(weekly, DateTimeOffset.Now.AddDays(3)), "Max", DateTimeOffset.Now));

    [Fact]
    public async Task WarnsOnceAndKeepsLastUsageOnError()
    {
        var notifier = new RecordingNotifier();
        var claude = Usage(80, 10);
        var store = new UsageStore(notifier, _statePath, Returns(() => claude), Returns(() => FetchResult.Fail("Not signed in. Run `codex login`")));

        await store.RefreshAsync();
        Assert.Single(notifier.Posted);
        Assert.Equal("Claude · 5-hour limit at 75%", notifier.Posted[0].Title);
        Assert.Equal("Not signed in. Run `codex login`", store.State(Provider.Codex).Issue);

        await store.RefreshAsync();
        Assert.Single(notifier.Posted); // same cycle, no repeat

        claude = FetchResult.Fail("Offline. Showing last known usage");
        await store.RefreshAsync();
        Assert.Equal(80, store.Window(Provider.Claude, WindowKind.FiveHour)!.Percent);
        Assert.Equal("Offline. Showing last known usage", store.State(Provider.Claude).Issue);
    }

    [Fact]
    public async Task RestoresLastUsageAndSentWarningsAfterRestart()
    {
        var notifier = new RecordingNotifier();
        await new UsageStore(notifier, _statePath, Returns(() => Usage(30, 90)), Returns(() => Usage(20, 20))).RefreshAsync();
        Assert.Single(notifier.Posted);

        var restarted = new UsageStore(notifier, _statePath, Returns(() => Usage(30, 91)), Returns(() => Usage(20, 20)));
        Assert.Equal(30, restarted.Window(Provider.Claude, WindowKind.FiveHour)!.Percent);
        Assert.NotNull(restarted.LastUpdated);
        await restarted.RefreshAsync();
        Assert.Single(notifier.Posted); // the weekly warning was already sent for this cycle
    }

    [Fact]
    public async Task SnapshotStoreHasNoSideEffects()
    {
        var notifier = new RecordingNotifier();
        await new UsageStore(notifier, statePath: null, Returns(() => Usage(99, 99)), Returns(() => Usage(99, 99))).RefreshAsync();
        Assert.Empty(notifier.Posted);
        Assert.False(File.Exists(_statePath));
    }
}
