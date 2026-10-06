using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace AIUsage.Core;

public sealed record FetchResult(ProviderUsage? Usage, string? Error)
{
    public static FetchResult Ok(ProviderUsage usage) => new(usage, null);
    public static FetchResult Fail(string error) => new(null, error);
}

/// <summary>
/// Reads each user's own Claude Code / Codex CLI logins and asks the providers for current usage.
/// Read-only: tokens are never refreshed or rewritten, so the CLIs' own sessions are never disturbed.
/// </summary>
public static class UsageClient
{
    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("ai-usage-widget/1.0");
        return client;
    }

    private static string Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    // Claude

    public static string ClaudeConfigDirectory =>
        Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR") is { Length: > 0 } custom ? custom : Path.Combine(Home, ".claude");

    public static async Task<FetchResult> FetchClaudeAsync(CancellationToken cancellation = default)
    {
        ClaudeParser.Credentials credentials;
        try
        {
            credentials = ClaudeParser.ParseCredentials(await ReadClaudeCredentialsAsync(cancellation).ConfigureAwait(false));
        }
        catch (UsageException error)
        {
            return FetchResult.Fail(error.Message);
        }
        catch (Exception)
        {
            return FetchResult.Fail("Not signed in. Run `claude` to sign in");
        }
        if (credentials.ExpiresAt is { } expires && expires < DateTimeOffset.Now)
            return FetchResult.Fail("Login expired. Open Claude Code to refresh");

        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.anthropic.com/api/oauth/usage");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credentials.AccessToken);
        request.Headers.Add("anthropic-beta", "oauth-2025-04-20");
        return await SendAsync(request, "Claude", body => ClaudeParser.Parse(body, credentials.Plan, DateTimeOffset.Now), cancellation)
            .ConfigureAwait(false);
    }

    private static async Task<string> ReadClaudeCredentialsAsync(CancellationToken cancellation)
    {
        var file = Path.Combine(ClaudeConfigDirectory, ".credentials.json");
        if (File.Exists(file)) return await File.ReadAllTextAsync(file, cancellation).ConfigureAwait(false);

        // macOS keeps the same JSON in the login keychain (only used by the developer probe; the Mac app has its own client).
        if (OperatingSystem.IsMacOS())
        {
            using var process = Process.Start(new ProcessStartInfo("/usr/bin/security", "find-generic-password -s \"Claude Code-credentials\" -w")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            }) ?? throw new UsageException("Not signed in. Run `claude` to sign in");
            var output = await process.StandardOutput.ReadToEndAsync(cancellation).ConfigureAwait(false);
            await process.WaitForExitAsync(cancellation).ConfigureAwait(false);
            if (process.ExitCode == 0 && output.Length > 0) return output;
        }
        throw new UsageException("Not signed in. Run `claude` to sign in");
    }

    // Codex

    public static string CodexHome =>
        Environment.GetEnvironmentVariable("CODEX_HOME") is { Length: > 0 } custom ? custom : Path.Combine(Home, ".codex");

    public static async Task<FetchResult> FetchCodexAsync(CancellationToken cancellation = default)
    {
        var live = await FetchCodexLiveAsync(cancellation).ConfigureAwait(false);
        if (live.Usage is null && LatestCodexSessionSnapshot() is { } logged) return FetchResult.Ok(logged);
        return live;
    }

    private static async Task<FetchResult> FetchCodexLiveAsync(CancellationToken cancellation)
    {
        string accessToken;
        string? accountId;
        try
        {
            using var auth = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(CodexHome, "auth.json"), cancellation).ConfigureAwait(false));
            var tokens = auth.RootElement.GetProperty("tokens");
            accessToken = tokens.GetProperty("access_token").GetString() ?? throw new UsageException("no token");
            accountId = tokens.TryGetProperty("account_id", out var account) ? account.GetString() : null;
        }
        catch (Exception)
        {
            return FetchResult.Fail("Not signed in. Run `codex login`");
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, "https://chatgpt.com/backend-api/wham/usage");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        if (accountId is not null) request.Headers.Add("ChatGPT-Account-Id", accountId);
        return await SendAsync(request, "Codex", body => CodexParser.Parse(body, DateTimeOffset.Now), cancellation).ConfigureAwait(false);
    }

    /// <summary>Scans the newest few session logs from the end for a <c>rate_limits</c> event.</summary>
    public static ProviderUsage? LatestCodexSessionSnapshot()
    {
        var root = Path.Combine(CodexHome, "sessions");
        if (!Directory.Exists(root)) return null;
        var cutoff = DateTime.UtcNow.AddDays(-8);
        try
        {
            var recent = new DirectoryInfo(root).EnumerateFiles("*.jsonl", SearchOption.AllDirectories)
                .Where(f => f.LastWriteTimeUtc > cutoff)
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .Take(5);
            foreach (var file in recent)
            {
                string[] lines;
                try { lines = File.ReadAllLines(file.FullName); }
                catch (IOException) { continue; }
                for (var i = lines.Length - 1; i >= 0; i--)
                    if (CodexParser.ParseSessionLine(lines[i]) is { } usage) return usage;
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
        }
        return null;
    }

    // Shared

    private static async Task<FetchResult> SendAsync(HttpRequestMessage request, string provider, Func<string, ProviderUsage> parse,
                                                     CancellationToken cancellation)
    {
        try
        {
            using var response = await Http.SendAsync(request, cancellation).ConfigureAwait(false);
            switch (response.StatusCode)
            {
                case HttpStatusCode.OK: break;
                case HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden: return FetchResult.Fail($"Login expired. Open {provider} to refresh");
                case HttpStatusCode.TooManyRequests: return FetchResult.Fail("Rate limited. Will retry");
                default: return FetchResult.Fail($"{provider} returned HTTP {(int)response.StatusCode}");
            }
            return FetchResult.Ok(parse(await response.Content.ReadAsStringAsync(cancellation).ConfigureAwait(false)));
        }
        catch (JsonException)
        {
            return FetchResult.Fail($"Unexpected response from {provider}");
        }
        catch (Exception error) when (error is HttpRequestException or TaskCanceledException)
        {
            return FetchResult.Fail("Offline. Showing last known usage");
        }
    }
}
