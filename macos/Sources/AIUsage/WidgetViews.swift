import SwiftUI
import UsageCore

enum WidgetMetrics {
    static let small = CGSize(width: 170, height: 170)
    static let medium = CGSize(width: 360, height: 170)
    static let cornerRadius: CGFloat = 22
}

/// Small desktop widget: the 5-hour figure big, weekly underneath.
struct SmallWidgetView: View {
    var store: UsageStore
    var provider: Provider

    var body: some View {
        let five = store.window(provider, .fiveHour)
        let weekly = store.window(provider, .weekly)
        VStack(alignment: .leading, spacing: 0) {
            WidgetHeader(provider: provider, issue: store.state(provider).issue)
            Spacer(minLength: 0)
            Text(five.map { UsageFormat.percent($0.percent) } ?? "—")
                .font(.system(size: 30, weight: .bold).monospacedDigit())
                .tracking(-0.6)
                .foregroundStyle(five.map { UsageLevel(percent: $0.percent).text } ?? .secondary)
            Text(five.map { caption(.fiveHour, $0) } ?? "5h")
                .font(.system(size: 10.5).monospacedDigit())
                .foregroundStyle(.tertiary)
                .padding(.top, 2)
            UsageBar(percent: five?.percent).padding(.top, 6)
            Spacer(minLength: 0)
            HStack {
                Text(weekly?.resetsAt.map { "Weekly · " + UsageFormat.weekdayAndTime($0) } ?? "Weekly")
                    .lineLimit(1)
                    .minimumScaleFactor(0.8)
                    .foregroundStyle(.secondary)
                Spacer(minLength: 4)
                Text(weekly.map { UsageFormat.percent($0.percent) } ?? "—")
                    .fontWeight(.semibold)
                    .foregroundStyle(weekly.map { UsageLevel(percent: $0.percent).text } ?? .secondary)
            }
            .font(.system(size: 10.5).monospacedDigit())
            UsageBar(percent: weekly?.percent).padding(.top, 3)
        }
        .padding(.horizontal, 16)
        .padding(.vertical, 14)
        .frame(width: WidgetMetrics.small.width, height: WidgetMetrics.small.height)
    }

    private func caption(_ kind: WindowKind, _ window: UsageWindow) -> String {
        guard let resetsAt = window.resetsAt else { return "5h · not started" }
        return "5h · resets \(UsageFormat.time(resetsAt))"
    }
}

/// Medium desktop widget: Claude and Codex side by side.
struct MediumWidgetView: View {
    var store: UsageStore

    var body: some View {
        HStack(alignment: .top, spacing: 18) {
            ForEach(Provider.allCases, id: \.self) { provider in
                VStack(alignment: .leading, spacing: 0) {
                    WidgetHeader(provider: provider, issue: store.state(provider).issue)
                    Spacer(minLength: 0)
                    line(provider, .fiveHour)
                    Spacer(minLength: 0)
                    line(provider, .weekly)
                }
                .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .topLeading)
            }
        }
        .padding(.horizontal, 16)
        .padding(.vertical, 14)
        .frame(width: WidgetMetrics.medium.width, height: WidgetMetrics.medium.height)
    }

    private func line(_ provider: Provider, _ kind: WindowKind) -> some View {
        let window = store.window(provider, kind)
        let (value, color) = store.percentLabel(window)
        return VStack(alignment: .leading, spacing: 3) {
            HStack {
                Text(kind.rowLabel).foregroundStyle(.secondary)
                Spacer()
                Text(value).fontWeight(.semibold).foregroundStyle(color)
            }
            .font(.system(size: 11).monospacedDigit())
            UsageBar(percent: window?.percent)
            Text(resetText(kind, window))
                .font(.system(size: 10.5).monospacedDigit())
                .foregroundStyle(.tertiary)
                .lineLimit(1)
        }
    }

    private func resetText(_ kind: WindowKind, _ window: UsageWindow?) -> String {
        guard let window else { return " " }
        guard let resetsAt = window.resetsAt else { return "Not started" }
        return kind == .fiveHour ? "Resets \(UsageFormat.time(resetsAt))" : "Resets \(UsageFormat.dayAndTime(resetsAt))"
    }
}

private struct WidgetHeader: View {
    var provider: Provider
    var issue: String?

    var body: some View {
        HStack(spacing: 6) {
            ProviderBadge(provider: provider)
            Text(provider.displayName).font(.system(size: 12, weight: .semibold))
            if let issue {
                Image(systemName: "exclamationmark.triangle.fill")
                    .font(.system(size: 10))
                    .foregroundStyle(.secondary)
                    .help(issue)
            }
        }
    }
}
