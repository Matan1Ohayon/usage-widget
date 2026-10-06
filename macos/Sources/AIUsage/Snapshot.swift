import AppKit
import SwiftUI
import UsageCore

/// `AIUsage --snapshot DIR` fetches live usage once and renders every surface to PNG, light and dark,
/// so the UI can be checked without clicking through the menu bar. Sends no notifications, saves nothing.
@MainActor
enum Snapshot {
    static func run(to directory: URL, demo: Bool) async {
        try? FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
        let store = UsageStore(notifier: Notifier(), sideEffects: false)
        if demo {
            // The mockup's figures: one bar in each colour band, plus a fetch error.
            let now = Date()
            store.setDemo(.claude, ProviderState(usage: ProviderUsage(
                fiveHour: UsageWindow(percent: 42, resetsAt: now.addingTimeInterval(186 * 60)),
                weekly: UsageWindow(percent: 68, resetsAt: now.addingTimeInterval(46 * 3600)),
                plan: "Max", fetchedAt: now)))
            store.setDemo(.codex, ProviderState(usage: ProviderUsage(
                fiveHour: UsageWindow(percent: 78, resetsAt: now.addingTimeInterval(101 * 60)),
                weekly: UsageWindow(percent: 11, resetsAt: now.addingTimeInterval(141 * 3600)),
                plan: "Plus", fetchedAt: now), issue: "Offline. Showing last known usage"))
        } else {
            await store.refresh()
        }
        for provider in Provider.allCases {
            let state = store.state(provider)
            let windows = WindowKind.allCases.map { kind in
                "\(kind.rawValue)=\(store.window(provider, kind).map { "\(UsageFormat.percent($0.percent)) \(UsageFormat.resetLine(kind, $0, now: store.now))" } ?? "none")"
            }
            print("\(provider.displayName): \(windows.joined(separator: ", "))\(state.issue.map { " issue=\($0)" } ?? "")")
        }

        let actions = AppActions(refresh: {}, hideWidget: {}, testNotification: {}, quit: {})
        for dark in [true, false] {
            let suffix = dark ? "dark" : "light"
            write(DashboardView(store: store, settings: AppSettings(), actions: actions, staticControls: true), cornerRadius: 12, dark: dark,
                  to: directory.appending(path: "dashboard-\(suffix).png"))
            write(MediumWidgetView(store: store), cornerRadius: WidgetMetrics.cornerRadius, dark: dark,
                  to: directory.appending(path: "widget-medium-\(suffix).png"))
            write(HStack(spacing: 16) {
                ForEach(Provider.allCases, id: \.self) { provider in
                    SmallWidgetView(store: store, provider: provider)
                        .background(dark ? Color(white: 0.17) : Color(white: 0.97))
                        .clipShape(RoundedRectangle(cornerRadius: WidgetMetrics.cornerRadius, style: .continuous))
                }
            }, cornerRadius: 0, dark: dark, surface: false, to: directory.appending(path: "widget-small-\(suffix).png"))
            writeStatusIcon(store: store, dark: dark, to: directory.appending(path: "menubar-\(suffix).png"))
        }
        print("Snapshots written to \(directory.path)")
    }

    /// Material blur can't be captured offscreen, so panels get a flat stand-in surface.
    private static func write<V: View>(_ view: V, cornerRadius: CGFloat, dark: Bool, surface: Bool = true, to url: URL) {
        let content = view
            .background(surface ? (dark ? Color(white: 0.17) : Color(white: 0.97)) : .clear)
            .clipShape(RoundedRectangle(cornerRadius: cornerRadius, style: .continuous))
            .padding(24)
            .background(dark ? Color(red: 0.09, green: 0.10, blue: 0.14) : Color(red: 0.80, green: 0.84, blue: 0.90))
            .environment(\.colorScheme, dark ? .dark : .light)
        let renderer = ImageRenderer(content: content)
        renderer.scale = 2
        guard let cgImage = renderer.cgImage else { return }
        savePNG(NSBitmapImageRep(cgImage: cgImage), to: url)
    }

    /// The menu-bar icon at 8× on a menu-bar-coloured strip.
    private static func writeStatusIcon(store: UsageStore, dark: Bool, to url: URL) {
        let icon = StatusIcon.image(claude: store.window(.claude, .fiveHour)?.percent,
                                    codex: store.window(.codex, .fiveHour)?.percent)
        let scale: CGFloat = 8
        let size = NSSize(width: (icon.size.width + 16) * scale, height: 24 * scale)
        let rep = NSBitmapImageRep(bitmapDataPlanes: nil, pixelsWide: Int(size.width), pixelsHigh: Int(size.height),
                                   bitsPerSample: 8, samplesPerPixel: 4, hasAlpha: true, isPlanar: false,
                                   colorSpaceName: .deviceRGB, bytesPerRow: 0, bitsPerPixel: 0)!
        NSGraphicsContext.saveGraphicsState()
        NSGraphicsContext.current = NSGraphicsContext(bitmapImageRep: rep)
        NSAppearance(named: dark ? .darkAqua : .aqua)!.performAsCurrentDrawingAppearance {
            (dark ? NSColor(white: 0.15, alpha: 1) : NSColor(white: 0.93, alpha: 1)).setFill()
            NSRect(origin: .zero, size: size).fill()
            let iconRect = NSRect(x: 8 * scale, y: (24 - icon.size.height) / 2 * scale,
                                  width: icon.size.width * scale, height: icon.size.height * scale)
            icon.draw(in: iconRect)
        }
        NSGraphicsContext.restoreGraphicsState()
        savePNG(rep, to: url)
    }

    private static func savePNG(_ rep: NSBitmapImageRep, to url: URL) {
        try? rep.representation(using: .png, properties: [:])?.write(to: url)
    }
}
