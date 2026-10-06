using System.Text.Json;
using System.Text.Json.Serialization;

namespace AIUsage.Core;

public sealed record ProviderState(ProviderUsage? Usage = null, string? Issue = null);

public interface INotifier
{
    void Post(string id, string title, string body);
}

/// <summary>
/// Current usage for both providers, the 75% warnings, and the saved last-known state.
/// Not thread-safe: call it from the UI thread (awaits resume there).
/// </summary>
public sealed class UsageStore
{
    public event Action? Changed;

    private readonly Dictionary<Provider, ProviderState> _states = [];
    private readonly INotifier _notifier;
    private readonly AlertPolicy _alerts;
    private readonly string? _statePath;
    private readonly Func<CancellationToken, Task<FetchResult>> _fetchClaude;
    private readonly Func<CancellationToken, Task<FetchResult>> _fetchCodex;

    /// <summary>Refresh every 5–7 minutes; jitter keeps us from hitting the endpoints on a fixed beat.</summary>
    public static TimeSpan NextPollDelay() => TimeSpan.FromSeconds(Random.Shared.Next(300, 421));

    public DateTimeOffset? LastUpdated { get; private set; }
    public bool IsRefreshing { get; private set; }

    /// <summary>Advanced by <see cref="Tick"/> so countdowns and expired windows update between fetches.</summary>
    public DateTimeOffset Now { get; private set; } = DateTimeOffset.Now;

    /// <param name="statePath">Where last-known usage and sent warnings are saved; null for snapshots/tests (no side effects).</param>
    public UsageStore(INotifier notifier, string? statePath,
                      Func<CancellationToken, Task<FetchResult>>? fetchClaude = null,
                      Func<CancellationToken, Task<FetchResult>>? fetchCodex = null)
    {
        _notifier = notifier;
        _statePath = statePath;
        _fetchClaude = fetchClaude ?? UsageClient.FetchClaudeAsync;
        _fetchCodex = fetchCodex ?? UsageClient.FetchCodexAsync;

        var saved = Load();
        _alerts = new AlertPolicy(saved?.AlertsNotified);
        if (saved?.LastUsage is { } usage)
        {
            foreach (var (key, value) in usage)
                if (Names.ParseProvider(key) is { } provider) _states[provider] = new ProviderState(value);
            LastUpdated = usage.Values.Select(u => (DateTimeOffset?)u.FetchedAt).Max();
        }
    }

    public ProviderState State(Provider provider) => _states.GetValueOrDefault(provider) ?? new ProviderState();

    /// <summary>The window as it stands right now (an elapsed window reads as 0%).</summary>
    public UsageWindow? Window(Provider provider, WindowKind kind) => State(provider).Usage?.Window(kind)?.Current(Now);

    public void Tick()
    {
        Now = DateTimeOffset.Now;
        Changed?.Invoke();
    }

    public bool IsStale => LastUpdated is not { } last || DateTimeOffset.Now - last >= TimeSpan.FromSeconds(60);

    public async Task RefreshAsync()
    {
        if (IsRefreshing) return;
        IsRefreshing = true;
        Changed?.Invoke();
        try
        {
            var claude = _fetchClaude(CancellationToken.None);
            var codex = _fetchCodex(CancellationToken.None);
            var results = new[] { (Provider.Claude, await claude), (Provider.Codex, await codex) };

            foreach (var (provider, result) in results)
                _states[provider] = result.Usage is { } usage
                    ? new ProviderState(usage)
                    : State(provider) with { Issue = result.Error };

            Now = DateTimeOffset.Now;
            LastUpdated = Now;
            if (_statePath is not null)
            {
                CheckAlerts();
                Save();
            }
        }
        finally
        {
            IsRefreshing = false;
            Changed?.Invoke();
        }
    }

    /// <summary>For <c>--snapshot --demo</c> only.</summary>
    public void SetDemo(Provider provider, ProviderState state)
    {
        _states[provider] = state;
        LastUpdated = Now;
        Changed?.Invoke();
    }

    private void CheckAlerts()
    {
        foreach (var provider in Names.Providers)
        {
            // Only warn about data we just fetched, not about a stale cached value.
            if (State(provider).Issue is not null) continue;
            foreach (var kind in Names.Windows)
            {
                if (Window(provider, kind) is not { } window || !_alerts.ShouldNotify(provider, kind, window)) continue;
                _notifier.Post(
                    $"{AlertPolicy.Key(provider, kind)}.{window.ResetsAt?.ToUnixTimeSeconds() ?? 0}",
                    UsageFormat.NotificationTitle(provider, kind),
                    UsageFormat.NotificationBody(kind, window, Now));
            }
        }
    }

    // Persistence

    private sealed record SavedState(Dictionary<string, ProviderUsage>? LastUsage, Dictionary<string, DateTimeOffset>? AlertsNotified);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true,
    };

    private SavedState? Load()
    {
        if (_statePath is null || !File.Exists(_statePath)) return null;
        try { return JsonSerializer.Deserialize<SavedState>(File.ReadAllText(_statePath), JsonOptions); }
        catch (Exception error) when (error is JsonException or IOException) { return null; }
    }

    private void Save()
    {
        if (_statePath is null) return;
        var usage = _states.Where(s => s.Value.Usage is not null).ToDictionary(s => s.Key.Key(), s => s.Value.Usage!);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_statePath)!);
            File.WriteAllText(_statePath, JsonSerializer.Serialize(new SavedState(usage, _alerts.Notified), JsonOptions));
        }
        catch (IOException)
        {
        }
    }
}
