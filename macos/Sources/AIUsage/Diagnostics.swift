import Foundation
import ServiceManagement
import UserNotifications

/// `AIUsage --check` (run from inside the installed bundle): reports notification permission and
/// login-item state, then sends one test notification through the same path the 75% warning uses.
@MainActor
enum Diagnostics {
    static func run() async {
        let settings = await UNUserNotificationCenter.current().notificationSettings()
        let permission = switch settings.authorizationStatus {
        case .authorized: "allowed"
        case .denied: "denied (will fall back to AppleScript notifications)"
        case .notDetermined: "not asked yet"
        case .provisional, .ephemeral: "provisional"
        @unknown default: "unknown"
        }
        let login = switch SMAppService.mainApp.status {
        case .enabled: "enabled"
        case .requiresApproval: "needs approval in System Settings → General → Login Items"
        case .notRegistered: "off"
        case .notFound: "not found"
        @unknown default: "unknown"
        }
        print("Notifications: \(permission)")
        print("Launch at login: \(login)")

        guard settings.authorizationStatus != .notDetermined else {
            print("Skipping test notification until the permission prompt is answered.")
            return
        }
        let notifier = Notifier()
        notifier.requestAuthorization()
        notifier.post(id: "check.\(Date().timeIntervalSince1970)",
                      title: "AI Usage · test notification",
                      body: "This is how the 75% warning will look.")
        try? await Task.sleep(for: .seconds(2))
        print("Test notification sent.")
    }
}
