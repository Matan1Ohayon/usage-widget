import Foundation
import UsageCore

private let userAgent = "ai-usage-widget/1.0"

/// Reads the OAuth token Claude Code keeps in the login keychain. Read-only: we never refresh or rewrite it,
/// so Claude Code's own session is never disturbed.
enum ClaudeClient {
    static func fetch() async -> Result<ProviderUsage, UsageError> {
        let credentials: ClaudeParser.Credentials
        do {
            credentials = try ClaudeParser.parseCredentials(try readCredentialData())
        } catch let error as UsageError {
            return .failure(error)
        } catch {
            return .failure(UsageError("Not signed in. Run `claude` to sign in"))
        }
        if let expiresAt = credentials.expiresAt, expiresAt < Date() {
            return .failure(UsageError("Login expired. Open Claude Code to refresh"))
        }

        var request = URLRequest(url: URL(string: "https://api.anthropic.com/api/oauth/usage")!, timeoutInterval: 20)
        request.setValue("Bearer \(credentials.accessToken)", forHTTPHeaderField: "Authorization")
        request.setValue("oauth-2025-04-20", forHTTPHeaderField: "anthropic-beta")
        request.setValue(userAgent, forHTTPHeaderField: "User-Agent")

        return await send(request, provider: "Claude") { data in
            try ClaudeParser.parse(data, plan: credentials.plan, fetchedAt: Date())
        }
    }

    private static func readCredentialData() throws -> Data {
        // Claude Code writes this item with /usr/bin/security, so reading it the same way doesn't trigger a keychain prompt.
        let process = Process()
        process.executableURL = URL(fileURLWithPath: "/usr/bin/security")
        process.arguments = ["find-generic-password", "-s", "Claude Code-credentials", "-w"]
        let output = Pipe()
        process.standardOutput = output
        process.standardError = FileHandle.nullDevice
        try process.run()
        let data = output.fileHandleForReading.readDataToEndOfFile()
        process.waitUntilExit()
        if process.terminationStatus == 0, !data.isEmpty { return data }

        // Linux-style installs keep the same JSON on disk.
        let file = FileManager.default.homeDirectoryForCurrentUser.appending(path: ".claude/.credentials.json")
        return try Data(contentsOf: file)
    }
}

/// Uses the ChatGPT login stored by the Codex CLI. Falls back to the last snapshot in Codex's session logs.
enum CodexClient {
    static var codexHome: URL {
        if let custom = ProcessInfo.processInfo.environment["CODEX_HOME"] { return URL(fileURLWithPath: custom) }
        return FileManager.default.homeDirectoryForCurrentUser.appending(path: ".codex")
    }

    static func fetch() async -> Result<ProviderUsage, UsageError> {
        let live = await fetchLive()
        if case .failure = live, let logged = latestSessionSnapshot() {
            return .success(logged)
        }
        return live
    }

    private static func fetchLive() async -> Result<ProviderUsage, UsageError> {
        struct Auth: Decodable {
            struct Tokens: Decodable {
                let access_token: String
                let account_id: String?
            }
            let tokens: Tokens?
        }
        guard let data = try? Data(contentsOf: codexHome.appending(path: "auth.json")),
              let tokens = (try? JSONDecoder().decode(Auth.self, from: data))?.tokens
        else { return .failure(UsageError("Not signed in. Run `codex login`")) }

        var request = URLRequest(url: URL(string: "https://chatgpt.com/backend-api/wham/usage")!, timeoutInterval: 20)
        request.setValue("Bearer \(tokens.access_token)", forHTTPHeaderField: "Authorization")
        if let account = tokens.account_id { request.setValue(account, forHTTPHeaderField: "ChatGPT-Account-Id") }
        request.setValue(userAgent, forHTTPHeaderField: "User-Agent")

        return await send(request, provider: "Codex") { data in
            try CodexParser.parse(data, fetchedAt: Date())
        }
    }

    /// Scans the newest few session logs from the end for a `rate_limits` event.
    static func latestSessionSnapshot() -> ProviderUsage? {
        let root = codexHome.appending(path: "sessions")
        guard let enumerator = FileManager.default.enumerator(
            at: root, includingPropertiesForKeys: [.contentModificationDateKey], options: [.skipsHiddenFiles]
        ) else { return nil }

        let cutoff = Date().addingTimeInterval(-8 * 24 * 3600)
        let recent = enumerator.compactMap { $0 as? URL }
            .filter { $0.pathExtension == "jsonl" }
            .compactMap { url -> (URL, Date)? in
                guard let date = try? url.resourceValues(forKeys: [.contentModificationDateKey]).contentModificationDate,
                      date > cutoff else { return nil }
                return (url, date)
            }
            .sorted { $0.1 > $1.1 }
            .prefix(5)

        for (url, _) in recent {
            guard let text = try? String(contentsOf: url, encoding: .utf8) else { continue }
            for line in text.split(separator: "\n").reversed() {
                if let usage = CodexParser.parseSessionLine(line) { return usage }
            }
        }
        return nil
    }
}

private func send(
    _ request: URLRequest,
    provider: String,
    parse: (Data) throws -> ProviderUsage
) async -> Result<ProviderUsage, UsageError> {
    do {
        let (data, response) = try await URLSession.shared.data(for: request)
        let status = (response as? HTTPURLResponse)?.statusCode ?? 0
        switch status {
        case 200: break
        case 401, 403: return .failure(UsageError("Login expired. Open \(provider) to refresh"))
        case 429: return .failure(UsageError("Rate limited. Will retry"))
        default: return .failure(UsageError("\(provider) returned HTTP \(status)"))
        }
        return .success(try parse(data))
    } catch is DecodingError {
        return .failure(UsageError("Unexpected response from \(provider)"))
    } catch {
        return .failure(UsageError("Offline. Showing last known usage"))
    }
}
