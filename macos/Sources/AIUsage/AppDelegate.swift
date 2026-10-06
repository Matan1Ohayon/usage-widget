import AppKit
import Observation
import ServiceManagement
import UsageCore

enum WidgetStyle: String, CaseIterable {
    case medium, small, off

    var title: String {
        switch self {
        case .medium: "Medium (Claude + Codex)"
        case .small: "Small (one per AI)"
        case .off: "Off"
        }
    }
}

@MainActor
@Observable
final class AppSettings {
    @ObservationIgnored var onWidgetStyleChange: ((WidgetStyle) -> Void)?

    var widgetStyle: WidgetStyle {
        didSet {
            UserDefaults.standard.set(widgetStyle.rawValue, forKey: "widgetStyle")
            onWidgetStyleChange?(widgetStyle)
        }
    }

    var launchAtLogin: Bool {
        didSet {
            guard launchAtLogin != (SMAppService.mainApp.status == .enabled) else { return }
            do {
                if launchAtLogin { try SMAppService.mainApp.register() } else { try SMAppService.mainApp.unregister() }
            } catch {
                NSLog("AIUsage: launch at login change failed: \(error)")
            }
            UserDefaults.standard.set(true, forKey: "launchAtLoginConfigured")
        }
    }

    init() {
        widgetStyle = WidgetStyle(rawValue: UserDefaults.standard.string(forKey: "widgetStyle") ?? "") ?? .medium
        launchAtLogin = SMAppService.mainApp.status == .enabled
    }

    /// First run: start at login by default.
    func enableLaunchAtLoginOnFirstRun() {
        guard !UserDefaults.standard.bool(forKey: "launchAtLoginConfigured") else { return }
        launchAtLogin = true
    }
}

@MainActor
struct AppActions {
    var refresh: () -> Void
    var hideWidget: () -> Void
    var testNotification: () -> Void
    var quit: () -> Void
}

@MainActor
final class AppDelegate: NSObject, NSApplicationDelegate {
    private let notifier = Notifier()
    private lazy var store = UsageStore(notifier: notifier)
    private let settings = AppSettings()
    private var statusItem: NSStatusItem!
    private var dashboard: DashboardController!
    private var widgets: DesktopWidgetController!

    private static let statusItemName = "AIUsage"
    private static let defaultStatusItemPosition: Double = 240

    func applicationDidFinishLaunching(_ notification: Notification) {
        // One copy only: a second launch (Finder, login item, install script) just exits.
        if let id = Bundle.main.bundleIdentifier,
           NSRunningApplication.runningApplications(withBundleIdentifier: id).contains(where: { $0 != .current }) {
            NSApp.terminate(nil)
            return
        }

        let actions = AppActions(
            refresh: { [weak self] in Task { await self?.store.refresh() } },
            hideWidget: { [weak self] in self?.settings.widgetStyle = .off },
            testNotification: { [weak self] in self?.sendTestNotification() },
            quit: { NSApp.terminate(nil) }
        )

        // New status items are added at the far left, which on notched MacBooks is often hidden behind the notch.
        // Start next to the system icons instead (points from the right edge); ⌘-dragging the icon overrides this.
        let positionKey = "NSStatusItem Preferred Position \(Self.statusItemName)"
        if UserDefaults.standard.object(forKey: positionKey) == nil {
            UserDefaults.standard.set(Self.defaultStatusItemPosition, forKey: positionKey)
        }
        statusItem = NSStatusBar.system.statusItem(withLength: NSStatusItem.variableLength)
        statusItem.autosaveName = Self.statusItemName
        statusItem.isVisible = true
        statusItem.button?.target = self
        statusItem.button?.action = #selector(toggleDashboard)
        statusItem.button?.sendAction(on: [.leftMouseDown, .rightMouseDown]) // respond on press, like system menus

        dashboard = DashboardController(content: DashboardView(store: store, settings: settings, actions: actions))
        widgets = DesktopWidgetController(store: store, actions: actions)
        widgets.apply(settings.widgetStyle)
        settings.onWidgetStyleChange = { [weak self] style in self?.widgets.apply(style) }
        settings.enableLaunchAtLoginOnFirstRun()

        observeStatusIcon()
        notifier.requestAuthorization()
        store.start()

        // `--show-dashboard`: open the panel on launch, for checking it without a click.
        if CommandLine.arguments.contains("--show-dashboard") {
            Task {
                try? await Task.sleep(for: .seconds(3))
                toggleDashboard()
            }
        }
    }

    @objc private func toggleDashboard() {
        guard let button = statusItem.button else { return }
        if !dashboard.isShown { store.refreshIfStale() }
        dashboard.toggle(from: button)
    }

    /// Redraw the menu-bar bars and tooltip whenever the 5-hour figures change.
    private func observeStatusIcon() {
        withObservationTracking {
            let claude = store.window(.claude, .fiveHour)
            let codex = store.window(.codex, .fiveHour)
            statusItem.button?.image = StatusIcon.image(claude: claude?.percent, codex: codex?.percent)
            statusItem.button?.toolTip = [
                "Claude 5h \(claude.map { UsageFormat.percent($0.percent) } ?? "—")",
                "Codex 5h \(codex.map { UsageFormat.percent($0.percent) } ?? "—")",
            ].joined(separator: " · ")
            if dashboard.isShown { dashboard.layout() }
        } onChange: {
            Task { @MainActor [weak self] in self?.observeStatusIcon() }
        }
    }

    private func sendTestNotification() {
        let sample = UsageWindow(percent: 75, resetsAt: Date().addingTimeInterval(101 * 60))
        notifier.post(
            id: "test.\(Date().timeIntervalSince1970)",
            title: UsageFormat.notificationTitle(.codex, .fiveHour),
            body: UsageFormat.notificationBody(.fiveHour, sample, now: Date())
        )
    }
}
