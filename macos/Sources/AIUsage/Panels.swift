import AppKit
import SwiftUI
import UsageCore

/// Borderless panel with a native blurred material and rounded corners.
class MaterialPanel: NSPanel {
    let effectView = NSVisualEffectView()

    init(material: NSVisualEffectView.Material, cornerRadius: CGFloat) {
        super.init(contentRect: .zero, styleMask: [.borderless, .nonactivatingPanel], backing: .buffered, defer: true)
        isOpaque = false
        backgroundColor = .clear
        hasShadow = true
        isReleasedWhenClosed = false
        animationBehavior = .none

        effectView.material = material
        effectView.blendingMode = .behindWindow
        effectView.state = .active
        effectView.maskImage = Self.roundedMask(radius: cornerRadius)
        contentView = effectView
    }

    func embed<V: View>(_ view: V) -> NSHostingView<V> {
        let hosting = NSHostingView(rootView: view)
        hosting.translatesAutoresizingMaskIntoConstraints = false
        effectView.addSubview(hosting)
        NSLayoutConstraint.activate([
            hosting.leadingAnchor.constraint(equalTo: effectView.leadingAnchor),
            hosting.trailingAnchor.constraint(equalTo: effectView.trailingAnchor),
            hosting.topAnchor.constraint(equalTo: effectView.topAnchor),
            hosting.bottomAnchor.constraint(equalTo: effectView.bottomAnchor),
        ])
        return hosting
    }

    private static func roundedMask(radius: CGFloat) -> NSImage {
        let edge = radius * 2 + 1
        let image = NSImage(size: NSSize(width: edge, height: edge), flipped: false) { rect in
            NSColor.black.setFill()
            NSBezierPath(roundedRect: rect, xRadius: radius, yRadius: radius).fill()
            return true
        }
        image.capInsets = NSEdgeInsets(top: radius, left: radius, bottom: radius, right: radius)
        image.resizingMode = .stretch
        return image
    }
}

// MARK: - Menu-bar dashboard

final class DashboardPanel: MaterialPanel {
    override var canBecomeKey: Bool { true }
    var onClose: (() -> Void)?

    override func cancelOperation(_ sender: Any?) { onClose?() }
}

@MainActor
final class DashboardController {
    private let panel = DashboardPanel(material: .menu, cornerRadius: 12)
    private var hosting: NSHostingView<DashboardView>?
    private var outsideClickMonitor: Any?
    private weak var anchor: NSStatusBarButton?

    var isShown: Bool { panel.isVisible }

    init(content: DashboardView) {
        panel.level = .statusBar
        panel.collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary, .transient]
        hosting = panel.embed(content)
        panel.onClose = { [weak self] in self?.close() }
    }

    func toggle(from button: NSStatusBarButton) {
        isShown ? close() : show(from: button)
    }

    func show(from button: NSStatusBarButton) {
        anchor = button
        layout()
        panel.alphaValue = 0
        panel.makeKeyAndOrderFront(nil)
        NSAnimationContext.runAnimationGroup { context in
            context.duration = 0.12
            panel.animator().alphaValue = 1
        }
        button.highlight(true)

        outsideClickMonitor = NSEvent.addGlobalMonitorForEvents(matching: [.leftMouseDown, .rightMouseDown]) { [weak self] _ in
            Task { @MainActor in self?.close() }
        }
    }

    func close() {
        guard panel.isVisible else { return }
        if let outsideClickMonitor { NSEvent.removeMonitor(outsideClickMonitor) }
        outsideClickMonitor = nil
        anchor?.highlight(false)
        NSAnimationContext.runAnimationGroup({ context in
            context.duration = 0.1
            panel.animator().alphaValue = 0
        }, completionHandler: { [panel] in
            Task { @MainActor in panel.orderOut(nil) }
        })
    }

    /// Hang the panel from the status item, left-aligned like a system menu, kept on screen.
    func layout() {
        guard let hosting, let button = anchor, let buttonWindow = button.window else { return }
        let size = hosting.fittingSize
        let buttonFrame = buttonWindow.convertToScreen(button.convert(button.bounds, to: nil))
        let screen = buttonWindow.screen?.visibleFrame ?? NSScreen.main?.visibleFrame ?? .zero
        var x = buttonFrame.minX
        x = min(x, screen.maxX - size.width - 8)
        x = max(x, screen.minX + 8)
        let y = buttonFrame.minY - 4 - size.height
        panel.setFrame(NSRect(x: x, y: y, width: size.width, height: size.height), display: true)
        panel.invalidateShadow()
    }
}

// MARK: - Desktop widgets

final class DraggableHostingView<Content: View>: NSHostingView<Content> {
    override var mouseDownCanMoveWindow: Bool { true }
}

final class DesktopWidgetPanel: NSPanel {
    init<V: View>(content: V, size: CGSize, autosaveName: String, defaultOrigin: NSPoint) {
        super.init(contentRect: NSRect(origin: defaultOrigin, size: size),
                   styleMask: [.borderless, .nonactivatingPanel], backing: .buffered, defer: false)
        isOpaque = false
        backgroundColor = .clear
        hasShadow = true
        isReleasedWhenClosed = false
        // Sit just above the desktop icons, below every normal window — like a real desktop widget.
        level = NSWindow.Level(rawValue: Int(CGWindowLevelForKey(.desktopIconWindow)) + 1)
        collectionBehavior = [.canJoinAllSpaces, .stationary, .ignoresCycle, .fullScreenNone]
        isMovableByWindowBackground = true
        // Widgets sit on photos and wallpapers: light text on darkened glass stays legible on any of them.
        appearance = NSAppearance(named: .darkAqua)

        // Size every view to the final frame up front; autoresizing from a zero-sized parent doubled the content.
        let bounds = NSRect(origin: .zero, size: size)
        let hosting = DraggableHostingView(rootView: content)
        hosting.frame = bounds
        hosting.autoresizingMask = [.width, .height]

        if #available(macOS 26, *) {
            let glass = NSGlassEffectView(frame: bounds)
            glass.cornerRadius = WidgetMetrics.cornerRadius
            glass.style = .regular
            glass.tintColor = NSColor.black.withAlphaComponent(0.32)
            glass.contentView = hosting
            contentView = glass
        } else {
            let material = NSVisualEffectView(frame: bounds)
            material.material = .hudWindow
            material.blendingMode = .behindWindow
            material.state = .active
            material.wantsLayer = true
            material.layer?.cornerRadius = WidgetMetrics.cornerRadius
            material.layer?.masksToBounds = true
            material.addSubview(hosting)
            contentView = material
        }

        setFrameAutosaveName(autosaveName) // restores the last dragged position, if any
        if !NSScreen.screens.contains(where: { $0.visibleFrame.contains(frame) }) {
            setFrameOrigin(defaultOrigin)
        }
    }
}

@MainActor
final class DesktopWidgetController {
    private let store: UsageStore
    private let actions: AppActions
    private var panels: [DesktopWidgetPanel] = []

    init(store: UsageStore, actions: AppActions) {
        self.store = store
        self.actions = actions
    }

    func apply(_ style: WidgetStyle) {
        panels.forEach { $0.orderOut(nil) }
        panels = []
        let screen = NSScreen.main?.visibleFrame ?? .zero
        let margin: CGFloat = 24

        switch style {
        case .off:
            return
        case .medium:
            let size = WidgetMetrics.medium
            panels = [DesktopWidgetPanel(
                content: MediumWidgetView(store: store).widgetMenu(actions),
                size: size,
                autosaveName: "widget.medium",
                defaultOrigin: NSPoint(x: screen.maxX - size.width - margin, y: screen.maxY - size.height - margin)
            )]
        case .small:
            let size = WidgetMetrics.small
            panels = Provider.allCases.enumerated().map { index, provider in
                DesktopWidgetPanel(
                    content: SmallWidgetView(store: store, provider: provider).widgetMenu(actions),
                    size: size,
                    autosaveName: "widget.small.\(provider.rawValue)",
                    defaultOrigin: NSPoint(
                        x: screen.maxX - CGFloat(Provider.allCases.count - index) * (size.width + 16) - margin + 16,
                        y: screen.maxY - size.height - margin
                    )
                )
            }
        }
        panels.forEach { $0.orderFront(nil) }
    }
}

private extension View {
    func widgetMenu(_ actions: AppActions) -> some View {
        contextMenu {
            Button("Refresh Now", action: actions.refresh)
            Button("Hide Desktop Widget", action: actions.hideWidget)
        }
    }
}
