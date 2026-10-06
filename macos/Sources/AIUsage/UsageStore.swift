import AppKit
import Observation
import UsageCore

struct ProviderState: Equatable {
    var usage: ProviderUsage?
    /// Shown under the provider when the latest fetch failed; the last good `usage` is kept.
    var issue: String?
}

@MainActor
@Observable
final class UsageStore {
    private(set) var states: [Provider: ProviderState] = [:]
    private(set) var lastUpdated: Date?
    private(set) var isRefreshing = false
    /// Ticks every 30s so countdowns and expired windows update between fetches.
    private(set) var now = Date()

    @ObservationIgnored private let notifier: Notifier
    @ObservationIgnored private var alerts: AlertPolicy
    @ObservationIgnored private var pollTask: Task<Void, Never>?
    @ObservationIgnored private var clockTask: Task<Void, Never>?
    @ObservationIgnored private let defaults = UserDefaults.standard
    /// Off for `--snapshot` renders, which must not send warnings or overwrite the saved state.
    @ObservationIgnored private let sideEffects: Bool

    /// Refresh every 5–7 minutes; jitter keeps us from hitting the endpoints on a fixed beat.
    static let pollInterval: ClosedRange<Double> = 300...420

    init(notifier: Notifier, sideEffects: Bool = true) {
        self.notifier = notifier
        self.sideEffects = sideEffects
        alerts = AlertPolicy(notified: (try? JSONDecoder().decode(
            [String: Date].self, from: defaults.data(forKey: "alertsNotified") ?? Data()
        )) ?? [:])
        if let saved = defaults.data(forKey: "lastUsage"),
           let usage = try? JSONDecoder().decode([Provider: ProviderUsage].self, from: saved) {
            for (provider, value) in usage { states[provider] = ProviderState(usage: value) }
            lastUpdated = usage.values.map(\.fetchedAt).max()
        }
    }

    func state(_ provider: Provider) -> ProviderState { states[provider] ?? ProviderState() }

    /// For `--snapshot --demo` only.
    func setDemo(_ provider: Provider, _ state: ProviderState) {
        states[provider] = state
        lastUpdated = now
    }

    /// The window as it stands right now (an elapsed window reads as 0%).
    func window(_ provider: Provider, _ kind: WindowKind) -> UsageWindow? {
        state(provider).usage?.window(kind)?.current(at: now)
    }

    func start() {
        clockTask = Task { [weak self] in
            while !Task.isCancelled {
                try? await Task.sleep(for: .seconds(30))
                self?.now = Date()
            }
        }
        pollTask = Task { [weak self] in
            while !Task.isCancelled {
                await self?.refresh()
                try? await Task.sleep(for: .seconds(Double.random(in: Self.pollInterval)))
            }
        }
        NSWorkspace.shared.notificationCenter.addObserver(
            forName: NSWorkspace.didWakeNotification, object: nil, queue: .main
        ) { [weak self] _ in
            Task { @MainActor in
                try? await Task.sleep(for: .seconds(5)) // let the network come back first
                await self?.refresh()
            }
        }
    }

    /// Opening the dashboard refreshes too, unless we just did.
    func refreshIfStale() {
        guard let lastUpdated, Date().timeIntervalSince(lastUpdated) < 60 else {
            Task { await refresh() }
            return
        }
    }

    func refresh() async {
        guard !isRefreshing else { return }
        isRefreshing = true
        defer { isRefreshing = false }

        async let claude = ClaudeClient.fetch()
        async let codex = CodexClient.fetch()
        let results: [(Provider, Result<ProviderUsage, UsageError>)] = [(.claude, await claude), (.codex, await codex)]

        for (provider, result) in results {
            var state = self.state(provider)
            switch result {
            case .success(let usage):
                state = ProviderState(usage: usage)
            case .failure(let error):
                state.issue = error.message
            }
            states[provider] = state
        }
        now = Date()
        lastUpdated = now
        guard sideEffects else { return }
        persist()
        checkAlerts()
    }

    private func checkAlerts() {
        for provider in Provider.allCases {
            // Only warn about data we just fetched, not about a stale cached value.
            guard state(provider).issue == nil else { continue }
            for kind in WindowKind.allCases {
                guard let window = window(provider, kind), alerts.shouldNotify(provider, kind, window) else { continue }
                notifier.post(
                    id: "\(AlertPolicy.key(provider, kind)).\(Int(window.resetsAt?.timeIntervalSince1970 ?? 0))",
                    title: UsageFormat.notificationTitle(provider, kind),
                    body: UsageFormat.notificationBody(kind, window, now: now)
                )
            }
        }
        defaults.set(try? JSONEncoder().encode(alerts.notified), forKey: "alertsNotified")
    }

    private func persist() {
        let usage = states.compactMapValues(\.usage)
        defaults.set(try? JSONEncoder().encode(usage), forKey: "lastUsage")
    }
}
