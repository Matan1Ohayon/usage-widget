// Developer probe: fetches live usage with the same client the Windows app uses and prints it.
// Runs on any OS (on macOS the Claude login is read from the keychain). Never prints tokens.
using AIUsage.Core;

var now = DateTimeOffset.Now;
foreach (var (provider, fetch) in new[] { (Provider.Claude, (Func<Task<FetchResult>>)(() => UsageClient.FetchClaudeAsync())),
                                          (Provider.Codex, () => UsageClient.FetchCodexAsync()) })
{
    var result = await fetch();
    if (result.Usage is not { } usage)
    {
        Console.WriteLine($"{provider.DisplayName()}: {result.Error}");
        continue;
    }
    var windows = Names.Windows.Select(kind => usage.Window(kind)?.Current(now) is { } w
        ? $"{kind.Key()}={UsageFormat.Percent(w.Percent)} {UsageFormat.ResetLine(kind, w, now)}"
        : $"{kind.Key()}=none");
    Console.WriteLine($"{provider.DisplayName()} ({usage.Plan ?? "unknown plan"}): {string.Join(", ", windows)}");
}
