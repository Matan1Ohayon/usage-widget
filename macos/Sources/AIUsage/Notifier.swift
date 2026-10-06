import Foundation
import UserNotifications

/// Posts macOS notifications. Falls back to AppleScript's `display notification` if the
/// app hasn't been allowed to use the Notification Center.
@MainActor
final class Notifier: NSObject, UNUserNotificationCenterDelegate {
    /// Resolves once the user answers the permission prompt; posts wait on it instead of blocking data refresh.
    private var authorization: Task<Bool, Never>?

    func requestAuthorization() {
        let center = UNUserNotificationCenter.current()
        center.delegate = self
        authorization = Task {
            (try? await center.requestAuthorization(options: [.alert, .sound])) ?? false
        }
    }

    func post(id: String, title: String, body: String) {
        Task {
            let authorized = await authorization?.value ?? false
            deliver(id: id, title: title, body: body, authorized: authorized)
        }
    }

    private func deliver(id: String, title: String, body: String, authorized: Bool) {
        guard authorized else { return postViaAppleScript(title: title, body: body) }
        let content = UNMutableNotificationContent()
        content.title = title
        content.body = body
        content.sound = .default
        content.interruptionLevel = .timeSensitive
        UNUserNotificationCenter.current().add(UNNotificationRequest(identifier: id, content: content, trigger: nil)) { error in
            guard error != nil else { return }
            Task { @MainActor in self.postViaAppleScript(title: title, body: body) }
        }
    }

    private func postViaAppleScript(title: String, body: String) {
        func quoted(_ s: String) -> String {
            "\"" + s.replacingOccurrences(of: "\\", with: "\\\\").replacingOccurrences(of: "\"", with: "\\\"") + "\""
        }
        let process = Process()
        process.executableURL = URL(fileURLWithPath: "/usr/bin/osascript")
        process.arguments = ["-e", "display notification \(quoted(body)) with title \(quoted(title)) sound name \"default\""]
        try? process.run()
    }

    // Show banners even while the dashboard is open.
    nonisolated func userNotificationCenter(
        _ center: UNUserNotificationCenter,
        willPresent notification: UNNotification,
        withCompletionHandler completionHandler: @escaping (UNNotificationPresentationOptions) -> Void
    ) {
        completionHandler([.banner, .sound, .list])
    }
}
