import Foundation

// MARK: - Claude (GET https://api.anthropic.com/api/oauth/usage)

public enum ClaudeParser {
    struct Response: Decodable {
        struct Window: Decodable {
            let utilization: Double?
            let resets_at: String?
        }
        let five_hour: Window?
        let seven_day: Window?
    }

    public static func parse(_ data: Data, plan: String?, fetchedAt: Date) throws -> ProviderUsage {
        let response = try JSONDecoder().decode(Response.self, from: data)
        func window(_ w: Response.Window?) -> UsageWindow? {
            guard let w else { return nil }
            return UsageWindow(percent: w.utilization ?? 0, resetsAt: w.resets_at.flatMap(parseISODate))
        }
        return ProviderUsage(
            fiveHour: window(response.five_hour),
            weekly: window(response.seven_day),
            plan: plan,
            fetchedAt: fetchedAt
        )
    }

    /// The keychain item Claude Code stores: `{"claudeAiOauth": {"accessToken", "expiresAt" (ms), "subscriptionType"}}`.
    public struct Credentials: Sendable {
        public let accessToken: String
        public let expiresAt: Date?
        public let plan: String?
    }

    public static func parseCredentials(_ data: Data) throws -> Credentials {
        struct File: Decodable {
            struct OAuth: Decodable {
                let accessToken: String
                let expiresAt: Double?
                let subscriptionType: String?
            }
            let claudeAiOauth: OAuth?
        }
        guard let oauth = try JSONDecoder().decode(File.self, from: data).claudeAiOauth else {
            throw UsageError("Not signed in to Claude Code")
        }
        return Credentials(
            accessToken: oauth.accessToken,
            expiresAt: oauth.expiresAt.map { Date(timeIntervalSince1970: $0 / 1000) },
            plan: oauth.subscriptionType?.capitalized
        )
    }
}

// MARK: - Codex (GET https://chatgpt.com/backend-api/wham/usage)

public enum CodexParser {
    static let fiveHourSeconds: Double = 5 * 3600
    static let weeklySeconds: Double = 7 * 24 * 3600

    struct Response: Decodable {
        struct RateLimit: Decodable {
            let primary_window: Window?
            let secondary_window: Window?
        }
        struct Window: Decodable {
            let used_percent: Double
            let limit_window_seconds: Double?
            let reset_at: Double?
            let reset_after_seconds: Double?
        }
        let plan_type: String?
        let rate_limit: RateLimit?
    }

    public static func parse(_ data: Data, fetchedAt: Date) throws -> ProviderUsage {
        let response = try JSONDecoder().decode(Response.self, from: data)
        let windows = [response.rate_limit?.primary_window, response.rate_limit?.secondary_window].compactMap { $0 }
        let parsed = windows.map { w -> (seconds: Double?, window: UsageWindow) in
            let reset = w.reset_at.map { Date(timeIntervalSince1970: $0) }
                ?? w.reset_after_seconds.map { fetchedAt.addingTimeInterval($0) }
            return (w.limit_window_seconds, UsageWindow(percent: w.used_percent, resetsAt: reset))
        }
        let (fiveHour, weekly) = assign(parsed)
        return ProviderUsage(fiveHour: fiveHour, weekly: weekly, plan: response.plan_type?.capitalized, fetchedAt: fetchedAt)
    }

    /// Fallback: the newest `rate_limits` snapshot Codex wrote into its session logs.
    /// Lines look like `{"timestamp": "...", "type": "event_msg", "payload": {"type": "token_count", "rate_limits": {...}}}`.
    public static func parseSessionLine(_ line: Substring) -> ProviderUsage? {
        guard line.contains("\"rate_limits\""),
              let object = try? JSONSerialization.jsonObject(with: Data(line.utf8)) as? [String: Any],
              let payload = object["payload"] as? [String: Any],
              let limits = payload["rate_limits"] as? [String: Any]
        else { return nil }

        let stamp = (object["timestamp"] as? String).flatMap(parseISODate) ?? Date()
        let parsed = ["primary", "secondary"].compactMap { key -> (seconds: Double?, window: UsageWindow)? in
            guard let w = limits[key] as? [String: Any], let used = w["used_percent"] as? Double else { return nil }
            let reset = (w["resets_at"] as? Double).map { Date(timeIntervalSince1970: $0) }
                ?? (w["resets_in_seconds"] as? Double).map { stamp.addingTimeInterval($0) }
            let seconds = (w["window_minutes"] as? Double).map { $0 * 60 }
            return (seconds, UsageWindow(percent: used, resetsAt: reset))
        }
        guard !parsed.isEmpty else { return nil }
        let (fiveHour, weekly) = assign(parsed)
        return ProviderUsage(
            fiveHour: fiveHour,
            weekly: weekly,
            plan: (limits["plan_type"] as? String)?.capitalized,
            fetchedAt: stamp
        )
    }

    /// Match windows by their length when it's known, otherwise by position (primary = 5h, secondary = weekly).
    static func assign(_ windows: [(seconds: Double?, window: UsageWindow)]) -> (UsageWindow?, UsageWindow?) {
        var fiveHour = windows.first { $0.seconds == fiveHourSeconds }?.window
        var weekly = windows.first { $0.seconds == weeklySeconds }?.window
        if fiveHour == nil, weekly == nil {
            fiveHour = windows.first?.window
            weekly = windows.dropFirst().first?.window
        }
        return (fiveHour, weekly)
    }
}

// MARK: - Dates

func parseISODate(_ string: String) -> Date? {
    let withFraction = ISO8601DateFormatter()
    withFraction.formatOptions = [.withInternetDateTime, .withFractionalSeconds]
    if let date = withFraction.date(from: string) { return date }
    return ISO8601DateFormatter().date(from: string)
}
