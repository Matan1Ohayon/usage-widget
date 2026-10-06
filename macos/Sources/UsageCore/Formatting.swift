import Foundation

/// English-only, 24-hour formatting ("14:30", "Thu 8 Oct, 10:00") regardless of system language.
/// Day and month names are fixed rather than taken from locale data, so macOS and Windows always agree.
public enum UsageFormat {
    static let weekdays = ["Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat"]
    static let months = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"]

    /// Providers report resets a fraction of a second before the minute (e.g. 11:29:59.8), so round to the nearest minute.
    static func rounded(_ date: Date) -> Date {
        Date(timeIntervalSince1970: (date.timeIntervalSince1970 / 60).rounded() * 60)
    }

    private static func parts(_ date: Date, _ timeZone: TimeZone) -> (weekday: String, day: Int, month: String, time: String) {
        var calendar = Calendar(identifier: .gregorian)
        calendar.timeZone = timeZone
        let c = calendar.dateComponents([.weekday, .day, .month, .hour, .minute], from: rounded(date))
        return (weekdays[c.weekday! - 1], c.day!, months[c.month! - 1], String(format: "%02d:%02d", c.hour!, c.minute!))
    }

    public static func time(_ date: Date, timeZone: TimeZone = .current) -> String {
        parts(date, timeZone).time
    }

    /// "Thu 8 Oct, 10:00"
    public static func dayAndTime(_ date: Date, timeZone: TimeZone = .current) -> String {
        let p = parts(date, timeZone)
        return "\(p.weekday) \(p.day) \(p.month), \(p.time)"
    }

    /// "Sat 11:04" — unambiguous inside a 7-day window, and fits the small widget.
    public static func weekdayAndTime(_ date: Date, timeZone: TimeZone = .current) -> String {
        let p = parts(date, timeZone)
        return "\(p.weekday) \(p.time)"
    }

    /// "3h 06m", "1d 22h", "12m" — the gap between the clock's minute and the displayed reset minute,
    /// so "Resets 14:30" at 11:24 always reads "3h 06m".
    public static func countdown(from now: Date, to date: Date) -> String {
        let nowMinute = Int((now.timeIntervalSince1970 / 60).rounded(.down))
        let resetMinute = Int((date.timeIntervalSince1970 / 60).rounded())
        let minutes = max(0, resetMinute - nowMinute)
        let days = minutes / 1440, hours = (minutes % 1440) / 60, mins = minutes % 60
        if days > 0 { return "\(days)d \(hours)h" }
        if hours > 0 { return "\(hours)h " + String(format: "%02dm", mins) }
        return "\(mins)m"
    }

    public static func percent(_ value: Double) -> String {
        "\(Int(value.rounded()))%"
    }

    /// Row subtitle: 5-hour shows the exact time and countdown, weekly shows the day.
    public static func resetLine(_ kind: WindowKind, _ window: UsageWindow, now: Date, timeZone: TimeZone = .current) -> String {
        guard let resetsAt = window.resetsAt else { return "Starts with your next message" }
        switch kind {
        case .fiveHour: return "Resets \(time(resetsAt, timeZone: timeZone)) (\(countdown(from: now, to: resetsAt)))"
        case .weekly: return "Resets \(dayAndTime(resetsAt, timeZone: timeZone))"
        }
    }

    /// "Codex · 5-hour limit at 75%", "Claude · Weekly limit at 75%".
    public static func notificationTitle(_ provider: Provider, _ kind: WindowKind) -> String {
        let window = kind.displayName.prefix(1).uppercased() + kind.displayName.dropFirst()
        return "\(provider.displayName) · \(window) limit at \(Int(UsageLevel.warningThreshold))%"
    }

    public static func notificationBody(_ kind: WindowKind, _ window: UsageWindow, now: Date, timeZone: TimeZone = .current) -> String {
        let used = "You've used \(percent(window.percent)) of your \(kind.displayName) limit."
        guard let resetsAt = window.resetsAt else { return used }
        let left = countdown(from: now, to: resetsAt)
        switch kind {
        case .fiveHour:
            return "\(used) Next reset at \(time(resetsAt, timeZone: timeZone)) (in \(left))."
        case .weekly:
            let p = parts(resetsAt, timeZone)
            return "\(used) Next reset \(p.weekday) \(p.day) \(p.month) at \(p.time) (in \(left))."
        }
    }
}
