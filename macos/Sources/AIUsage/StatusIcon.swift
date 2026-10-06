import AppKit
import UsageCore

/// Menu-bar icon: two stacked mini bars, Claude on top, Codex below, filled to the 5-hour usage.
enum StatusIcon {
    static let barWidth: CGFloat = 18
    static let barHeight: CGFloat = 4
    static let gap: CGFloat = 3

    static func image(claude: Double?, codex: Double?) -> NSImage {
        let size = NSSize(width: barWidth, height: barHeight * 2 + gap)
        // The drawing handler runs at draw time, so the track picks up the menu bar's current appearance.
        let image = NSImage(size: size, flipped: true) { _ in
            draw(percent: claude, y: 0)
            draw(percent: codex, y: barHeight + gap)
            return true
        }
        image.isTemplate = false
        image.accessibilityDescription = "AI usage"
        return image
    }

    private static func draw(percent: Double?, y: CGFloat) {
        let track = NSRect(x: 0, y: y, width: barWidth, height: barHeight)
        NSColor.labelColor.withAlphaComponent(0.25).setFill()
        NSBezierPath(roundedRect: track, xRadius: barHeight / 2, yRadius: barHeight / 2).fill()

        guard let percent, percent > 0 else { return }
        let width = max(barHeight, barWidth * min(percent, 100) / 100)
        let color: NSColor = switch UsageLevel(percent: percent) {
        case .green: .systemGreen
        case .yellow: .systemYellow
        case .red: .systemRed
        }
        color.setFill()
        NSBezierPath(roundedRect: NSRect(x: 0, y: y, width: width, height: barHeight),
                     xRadius: barHeight / 2, yRadius: barHeight / 2).fill()
    }
}
