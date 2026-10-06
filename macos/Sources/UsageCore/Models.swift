import Foundation

public enum Provider: String, Codable, CaseIterable, Sendable {
    case claude, codex

    public var displayName: String {
        switch self {
        case .claude: "Claude"
        case .codex: "Codex"
        }
    }
}

public enum WindowKind: String, Codable, CaseIterable, Sendable {
    case fiveHour, weekly

    public var displayName: String {
        switch self {
        case .fiveHour: "5-hour"
        case .weekly: "weekly"
        }
    }

    public var rowLabel: String {
        switch self {
        case .fiveHour: "5 hours"
        case .weekly: "Weekly"
        }
    }
}

/// Usage in one rate-limit window, as reported by the provider.
public struct UsageWindow: Codable, Equatable, Sendable {
    public var percent: Double
    public var resetsAt: Date?

    public init(percent: Double, resetsAt: Date?) {
        self.percent = percent
        self.resetsAt = resetsAt
    }

    /// Once the reset time has passed the window is empty until the next fetch says otherwise.
    public func current(at now: Date) -> UsageWindow {
        if let resetsAt, resetsAt <= now { return UsageWindow(percent: 0, resetsAt: nil) }
        return UsageWindow(percent: min(max(percent, 0), 100), resetsAt: resetsAt)
    }
}

public struct ProviderUsage: Codable, Equatable, Sendable {
    public var fiveHour: UsageWindow?
    public var weekly: UsageWindow?
    public var plan: String?
    public var fetchedAt: Date

    public init(fiveHour: UsageWindow?, weekly: UsageWindow?, plan: String?, fetchedAt: Date) {
        self.fiveHour = fiveHour
        self.weekly = weekly
        self.plan = plan
        self.fetchedAt = fetchedAt
    }

    public func window(_ kind: WindowKind) -> UsageWindow? {
        switch kind {
        case .fiveHour: fiveHour
        case .weekly: weekly
        }
    }
}

public struct UsageError: Error, Equatable, Sendable {
    public var message: String
    public init(_ message: String) { self.message = message }
}

/// Bar colour bands: green 0–60, yellow 60–75, red 75–100.
public enum UsageLevel: Sendable {
    case green, yellow, red

    public static let warningThreshold: Double = 75

    public init(percent: Double) {
        switch percent {
        case ..<60: self = .green
        case ..<UsageLevel.warningThreshold: self = .yellow
        default: self = .red
        }
    }
}
