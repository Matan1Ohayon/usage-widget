// Renders the 1024×1024 app icon: two usage bars (Claude on top, Codex below) on a dark squircle.
// Usage: swift scripts/make-icon.swift <output.png>
import AppKit

let side: CGFloat = 1024
let image = NSImage(size: NSSize(width: side, height: side), flipped: true) { _ in
    let inset: CGFloat = 100
    let body = NSRect(x: inset, y: inset, width: side - inset * 2, height: side - inset * 2)
    let squircle = NSBezierPath(roundedRect: body, xRadius: 185, yRadius: 185)
    NSGradient(starting: NSColor(calibratedRed: 0.20, green: 0.21, blue: 0.27, alpha: 1),
               ending: NSColor(calibratedRed: 0.09, green: 0.09, blue: 0.12, alpha: 1))!
        .draw(in: squircle, angle: 90)
    NSColor(white: 1, alpha: 0.12).setStroke()
    squircle.lineWidth = 4
    squircle.stroke()

    let barWidth: CGFloat = 560, barHeight: CGFloat = 104, gap: CGFloat = 78
    let x = (side - barWidth) / 2
    let top = (side - barHeight * 2 - gap) / 2
    for (index, (fraction, color)) in [(0.45, NSColor.systemGreen), (0.82, NSColor.systemRed)].enumerated() {
        let y = top + CGFloat(index) * (barHeight + gap)
        NSColor(white: 1, alpha: 0.14).setFill()
        NSBezierPath(roundedRect: NSRect(x: x, y: y, width: barWidth, height: barHeight), xRadius: barHeight / 2, yRadius: barHeight / 2).fill()
        color.setFill()
        NSBezierPath(roundedRect: NSRect(x: x, y: y, width: barWidth * fraction, height: barHeight), xRadius: barHeight / 2, yRadius: barHeight / 2).fill()
    }
    return true
}

let rep = NSBitmapImageRep(bitmapDataPlanes: nil, pixelsWide: Int(side), pixelsHigh: Int(side), bitsPerSample: 8,
                           samplesPerPixel: 4, hasAlpha: true, isPlanar: false, colorSpaceName: .deviceRGB, bytesPerRow: 0, bitsPerPixel: 0)!
NSGraphicsContext.saveGraphicsState()
NSGraphicsContext.current = NSGraphicsContext(bitmapImageRep: rep)
image.draw(in: NSRect(x: 0, y: 0, width: side, height: side))
NSGraphicsContext.restoreGraphicsState()
try! rep.representation(using: .png, properties: [:])!.write(to: URL(fileURLWithPath: CommandLine.arguments[1]))
