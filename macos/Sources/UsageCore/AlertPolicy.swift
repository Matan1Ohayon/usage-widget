import Foundation

/// Decides when to send the 75% warning: once per provider, per window, per reset cycle.
public struct AlertPolicy: Sendable {
    /// Reset time of the cycle we last warned about, keyed by `"claude.fiveHour"` etc.
    public private(set) var notified: [String: Date]

    /// Reset times can drift by a few seconds between fetches; anything closer than this is the same cycle.
    static let sameCycleTolerance: TimeInterval = 30 * 60

    public init(notified: [String: Date] = [:]) {
        self.notified = notified
    }

    public static func key(_ provider: Provider, _ kind: WindowKind) -> String {
        "\(provider.rawValue).\(kind.rawValue)"
    }

    /// Returns true (and records it) when this window has crossed the threshold and we haven't warned for this cycle yet.
    public mutating func shouldNotify(_ provider: Provider, _ kind: WindowKind, _ window: UsageWindow) -> Bool {
        guard window.percent >= UsageLevel.warningThreshold else { return false }
        let key = Self.key(provider, kind)
        let cycle = window.resetsAt ?? .distantFuture
        if let last = notified[key], abs(last.timeIntervalSince(cycle)) < Self.sameCycleTolerance {
            return false
        }
        notified[key] = cycle
        return true
    }
}
