import SwiftUI
import UsageCore

/// Menu-bar dashboard (design B): one row per bar, reset time underneath.
struct DashboardView: View {
    var store: UsageStore
    var settings: AppSettings
    var actions: AppActions
    /// Offscreen renders can't draw AppKit-backed buttons and menus, so `--snapshot` shows their icons instead.
    var staticControls = false

    /// Stamped into Info.plist by the release workflow; "dev" for local builds run outside the bundle.
    static let version = Bundle.main.object(forInfoDictionaryKey: "CFBundleShortVersionString") as? String ?? "dev"

    private let labelWidth: CGFloat = 56
    private let gap: CGFloat = 8

    var body: some View {
        VStack(alignment: .leading, spacing: 0) {
            ForEach(Provider.allCases, id: \.self) { provider in
                section(provider)
                Divider().padding(.vertical, 6)
            }
            footer
        }
        .padding(10)
        .frame(width: 300)
    }

    private func section(_ provider: Provider) -> some View {
        let state = store.state(provider)
        return VStack(alignment: .leading, spacing: 0) {
            HStack(spacing: 6) {
                ProviderBadge(provider: provider)
                Text(provider.displayName).font(.system(size: 12, weight: .semibold))
            }
            .padding(.horizontal, 4)
            .padding(.top, 4)
            .padding(.bottom, 2)

            ForEach(WindowKind.allCases, id: \.self) { kind in
                row(provider, kind, hasData: state.usage != nil)
            }

            if let issue = state.issue {
                Label(issue, systemImage: "exclamationmark.triangle.fill")
                    .font(.system(size: 11))
                    .foregroundStyle(.secondary)
                    .padding(.leading, 4 + labelWidth + gap)
                    .padding(.bottom, 4)
            }
        }
    }

    private func row(_ provider: Provider, _ kind: WindowKind, hasData: Bool) -> some View {
        let window = store.window(provider, kind)
        let (value, color) = store.percentLabel(window)
        return VStack(alignment: .leading, spacing: 0) {
            HStack(spacing: gap) {
                Text(kind.rowLabel)
                    .font(.system(size: 12))
                    .foregroundStyle(.secondary)
                    .frame(width: labelWidth, alignment: .leading)
                UsageBar(percent: window?.percent)
                Text(value)
                    .font(.system(size: 13, weight: .semibold).monospacedDigit())
                    .foregroundStyle(color)
                    .frame(width: 40, alignment: .trailing)
            }
            .padding(.horizontal, 4)
            .padding(.vertical, 5)

            Text(subtitle(kind, window, hasData: hasData))
                .font(.system(size: 11).monospacedDigit())
                .foregroundStyle(.tertiary)
                .padding(.leading, 4 + labelWidth + gap)
                .padding(.bottom, 6)
        }
        .accessibilityElement(children: .combine)
    }

    private func subtitle(_ kind: WindowKind, _ window: UsageWindow?, hasData: Bool) -> String {
        guard let window else { return hasData ? "No limit reported" : "Loading…" }
        return UsageFormat.resetLine(kind, window, now: store.now)
    }

    private var footer: some View {
        HStack(spacing: 10) {
            Text(store.lastUpdated.map { "Updated \(UsageFormat.time($0))" } ?? "Not updated yet")
                .font(.system(size: 11).monospacedDigit())
                .foregroundStyle(.tertiary)
            Spacer()
            if staticControls {
                Image(systemName: "arrow.clockwise")
                Image(systemName: "ellipsis.circle")
            } else {
                controls
            }
        }
        .buttonStyle(.borderless)
        .foregroundStyle(.secondary)
        .font(.system(size: 12, weight: .medium))
        .padding(.horizontal, 4)
        .padding(.top, 2)
    }

    @ViewBuilder private var controls: some View {
        Button {
            Task { await store.refresh() }
        } label: {
            Image(systemName: "arrow.clockwise")
                .rotationEffect(.degrees(store.isRefreshing ? 360 : 0))
                .animation(store.isRefreshing ? .linear(duration: 0.9).repeatForever(autoreverses: false) : .default,
                           value: store.isRefreshing)
        }
        .help("Refresh now")
        .keyboardShortcut("r")

        Menu {
            Text("AI Usage \(Self.version)")
            Divider()
            Picker("Desktop Widget", selection: Binding(
                get: { settings.widgetStyle },
                set: { settings.widgetStyle = $0 }
            )) {
                ForEach(WidgetStyle.allCases, id: \.self) { Text($0.title).tag($0) }
            }
            Toggle("Launch at Login", isOn: Binding(
                get: { settings.launchAtLogin },
                set: { settings.launchAtLogin = $0 }
            ))
            Button("Send Test Notification", action: actions.testNotification)
            Divider()
            Button("Quit AI Usage", action: actions.quit)
        } label: {
            Image(systemName: "ellipsis.circle")
        }
        .menuStyle(.borderlessButton)
        .menuIndicator(.hidden)
        .fixedSize()
    }
}
