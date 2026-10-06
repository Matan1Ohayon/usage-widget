import AppKit
import SwiftUI
import UsageCore

extension UsageLevel {
    /// Fill colour for bars (system colours adapt to light/dark).
    var fill: Color {
        switch self {
        case .green: Color(nsColor: .systemGreen)
        case .yellow: Color(nsColor: .systemYellow)
        case .red: Color(nsColor: .systemRed)
        }
    }

    /// Colour for the percentage label. Green stays neutral so only warnings draw the eye;
    /// yellow is darkened in light mode, where systemYellow text is unreadable.
    var text: Color {
        switch self {
        case .green: .primary
        case .yellow: Color(nsColor: NSColor(name: nil) { appearance in
            appearance.bestMatch(from: [.darkAqua, .aqua]) == .darkAqua
                ? .systemYellow
                : NSColor(red: 0.70, green: 0.50, blue: 0, alpha: 1)
        })
        case .red: Color(nsColor: .systemRed)
        }
    }
}

/// Horizontal usage bar with hairline ticks at the 60% and 75% thresholds.
struct UsageBar: View {
    var percent: Double?
    var height: CGFloat = 6

    @Environment(\.accessibilityReduceMotion) private var reduceMotion

    var body: some View {
        GeometryReader { geo in
            let fraction = min(max((percent ?? 0) / 100, 0), 1)
            ZStack(alignment: .leading) {
                Capsule().fill(Color.primary.opacity(0.10))
                if let percent {
                    Capsule()
                        .fill(UsageLevel(percent: percent).fill)
                        .frame(width: fraction > 0 ? max(height, geo.size.width * fraction) : 0)
                }
                ForEach([0.60, 0.75], id: \.self) { tick in
                    Rectangle()
                        .fill(Color.black.opacity(0.28))
                        .frame(width: 1)
                        .offset(x: geo.size.width * tick)
                }
            }
            .clipShape(Capsule())
            .animation(reduceMotion ? nil : .spring(duration: 0.4, bounce: 0), value: percent)
        }
        .frame(height: height)
        .accessibilityElement()
        .accessibilityValue(percent.map { UsageFormat.percent($0) } ?? "Unknown")
    }
}

struct ProviderBadge: View {
    var provider: Provider
    var size: CGFloat = 16

    var body: some View {
        RoundedRectangle(cornerRadius: size * 0.27, style: .continuous)
            .fill(provider == .claude ? Color(red: 0.85, green: 0.47, blue: 0.34) : .white)
            .overlay {
                RoundedRectangle(cornerRadius: size * 0.27, style: .continuous)
                    .strokeBorder(Color.black.opacity(provider == .codex ? 0.15 : 0), lineWidth: 0.5)
            }
            .overlay {
                Text(provider == .claude ? "✳" : "❯_")
                    .font(.system(size: size * (provider == .claude ? 0.6 : 0.5), weight: .heavy, design: .rounded))
                    .foregroundStyle(provider == .claude ? .white : .black)
            }
            .frame(width: size, height: size)
            .accessibilityHidden(true)
    }
}

extension UsageStore {
    func percentLabel(_ window: UsageWindow?) -> (String, Color) {
        guard let window else { return ("—", .secondary) }
        return (UsageFormat.percent(window.percent), UsageLevel(percent: window.percent).text)
    }
}
