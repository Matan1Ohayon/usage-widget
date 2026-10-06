import Foundation
import Testing
@testable import UsageCore

private let jerusalem = TimeZone(identifier: "Asia/Jerusalem")!

@Suite struct LevelTests {
    @Test(arguments: [(0.0, UsageLevel.green), (59.9, .green), (60, .yellow), (74.9, .yellow), (75, .red), (100, .red)])
    func bands(percent: Double, expected: UsageLevel) {
        #expect(UsageLevel(percent: percent) == expected)
    }
}

@Suite struct ParsingTests {
    @Test func claudeUsage() throws {
        let json = """
        {"five_hour":{"utilization":23.0,"resets_at":"2026-10-06T11:29:59.846441+00:00"},
         "seven_day":{"utilization":27.0,"resets_at":"2026-10-09T14:59:59.846461+00:00"},
         "seven_day_opus":null,"extra_usage":{"is_enabled":false}}
        """
        let usage = try ClaudeParser.parse(Data(json.utf8), plan: "Max", fetchedAt: Date())
        #expect(usage.fiveHour?.percent == 23)
        #expect(usage.weekly?.percent == 27)
        #expect(UsageFormat.time(try #require(usage.fiveHour?.resetsAt), timeZone: jerusalem) == "14:30")
        #expect(UsageFormat.dayAndTime(try #require(usage.weekly?.resetsAt), timeZone: jerusalem) == "Fri 9 Oct, 18:00")
        #expect(UsageFormat.weekdayAndTime(try #require(usage.weekly?.resetsAt), timeZone: jerusalem) == "Fri 18:00")
    }

    @Test func claudeWindowNotStarted() throws {
        let json = #"{"five_hour":{"utilization":0,"resets_at":null},"seven_day":null}"#
        let usage = try ClaudeParser.parse(Data(json.utf8), plan: nil, fetchedAt: Date())
        #expect(usage.fiveHour == UsageWindow(percent: 0, resetsAt: nil))
        #expect(usage.weekly == nil)
    }

    @Test func claudeCredentials() throws {
        let json = #"{"claudeAiOauth":{"accessToken":"t","refreshToken":"r","expiresAt":1791300000000,"subscriptionType":"max"}}"#
        let creds = try ClaudeParser.parseCredentials(Data(json.utf8))
        #expect(creds.accessToken == "t")
        #expect(creds.plan == "Max")
        #expect(creds.expiresAt == Date(timeIntervalSince1970: 1_791_300_000))
    }

    @Test func codexUsage() throws {
        let json = """
        {"plan_type":"plus","rate_limit":{"allowed":true,
          "primary_window":{"used_percent":25,"limit_window_seconds":18000,"reset_after_seconds":16622,"reset_at":1791291810},
          "secondary_window":{"used_percent":31,"limit_window_seconds":604800,"reset_after_seconds":344246,"reset_at":1791619433}}}
        """
        let usage = try CodexParser.parse(Data(json.utf8), fetchedAt: Date())
        #expect(usage.plan == "Plus")
        #expect(usage.fiveHour == UsageWindow(percent: 25, resetsAt: Date(timeIntervalSince1970: 1_791_291_810)))
        #expect(usage.weekly == UsageWindow(percent: 31, resetsAt: Date(timeIntervalSince1970: 1_791_619_433)))
    }

    @Test func codexWindowsMatchedByLength() throws {
        // If only the weekly window is reported in the primary slot it must not be shown as the 5-hour one.
        let json = #"{"rate_limit":{"primary_window":{"used_percent":40,"limit_window_seconds":604800,"reset_at":1791619433}}}"#
        let usage = try CodexParser.parse(Data(json.utf8), fetchedAt: Date())
        #expect(usage.fiveHour == nil)
        #expect(usage.weekly?.percent == 40)
    }

    @Test func codexSessionLine() throws {
        let line = """
        {"timestamp":"2026-10-04T09:00:06.578Z","type":"event_msg","payload":{"type":"token_count","info":null,"rate_limits":{"limit_id":"codex","primary":{"used_percent":73.0,"window_minutes":300,"resets_at":1791114077},"secondary":{"used_percent":11.0,"window_minutes":10080,"resets_at":1791619433},"plan_type":"plus"}}}
        """
        let usage = try #require(CodexParser.parseSessionLine(Substring(line)))
        #expect(usage.fiveHour?.percent == 73)
        #expect(usage.weekly?.percent == 11)
        #expect(usage.plan == "Plus")
        #expect(CodexParser.parseSessionLine(#"{"type":"response_item"}"#) == nil)
    }
}

@Suite struct WindowTests {
    @Test func elapsedWindowReadsAsEmpty() {
        let now = Date()
        let window = UsageWindow(percent: 90, resetsAt: now.addingTimeInterval(-1))
        #expect(window.current(at: now) == UsageWindow(percent: 0, resetsAt: nil))
        let live = UsageWindow(percent: 120, resetsAt: now.addingTimeInterval(60))
        #expect(live.current(at: now).percent == 100)
    }
}

@Suite struct FormattingTests {
    @Test func countdowns() {
        let now = Date(timeIntervalSince1970: 1_791_280_020) // on a minute boundary
        #expect(UsageFormat.countdown(from: now, to: now.addingTimeInterval(3 * 3600 + 6 * 60)) == "3h 06m")
        #expect(UsageFormat.countdown(from: now, to: now.addingTimeInterval(46 * 3600)) == "1d 22h")
        #expect(UsageFormat.countdown(from: now, to: now.addingTimeInterval(12 * 60)) == "12m")
    }

    @Test func countdownMatchesTheClock() {
        // The countdown only ticks when the clock's minute changes, never mid-minute.
        let reset = Date(timeIntervalSince1970: 1_791_280_020 + 3 * 3600 + 6 * 60)
        #expect(UsageFormat.countdown(from: Date(timeIntervalSince1970: 1_791_280_079), to: reset) == "3h 06m")
        #expect(UsageFormat.countdown(from: Date(timeIntervalSince1970: 1_791_280_080), to: reset) == "3h 05m")
    }

    @Test func resetLines() {
        let reset = Date(timeIntervalSince1970: 1_791_291_810) // 2026-10-06 16:03:30 in Jerusalem, shown as 16:04
        let now = reset.addingTimeInterval(-(101 * 60))
        let window = UsageWindow(percent: 78, resetsAt: reset)
        #expect(UsageFormat.resetLine(.fiveHour, window, now: now, timeZone: jerusalem) == "Resets 16:04 (1h 42m)")
        #expect(UsageFormat.resetLine(.weekly, window, now: now, timeZone: jerusalem) == "Resets Tue 6 Oct, 16:04")
        #expect(UsageFormat.resetLine(.fiveHour, UsageWindow(percent: 0, resetsAt: nil), now: now) == "Starts with your next message")
    }

    @Test func notificationText() {
        let reset = Date(timeIntervalSince1970: 1_791_291_810)
        let now = reset.addingTimeInterval(-(101 * 60))
        let window = UsageWindow(percent: 76, resetsAt: reset)
        #expect(UsageFormat.notificationBody(.fiveHour, window, now: now, timeZone: jerusalem)
            == "You've used 76% of your 5-hour limit. Next reset at 16:04 (in 1h 42m).")
        #expect(UsageFormat.notificationBody(.weekly, window, now: now, timeZone: jerusalem)
            == "You've used 76% of your weekly limit. Next reset Tue 6 Oct at 16:04 (in 1h 42m).")
    }
}

/// `#expect` can't wrap a mutating call, so each decision is captured first.
@Suite struct AlertPolicyTests {
    let reset = Date(timeIntervalSince1970: 1_791_291_810)

    @Test func belowThresholdNeverNotifies() {
        var policy = AlertPolicy()
        let notified = policy.shouldNotify(.claude, .fiveHour, UsageWindow(percent: 74.9, resetsAt: reset))
        #expect(!notified)
    }

    @Test func notifiesOncePerCycle() {
        var policy = AlertPolicy()
        let first = policy.shouldNotify(.claude, .fiveHour, UsageWindow(percent: 75, resetsAt: reset))
        let again = policy.shouldNotify(.claude, .fiveHour, UsageWindow(percent: 80, resetsAt: reset))
        // Reset time jitters by a second between fetches: still the same cycle.
        let jittered = policy.shouldNotify(.claude, .fiveHour, UsageWindow(percent: 90, resetsAt: reset.addingTimeInterval(1)))
        // Other windows and providers are tracked separately.
        let weekly = policy.shouldNotify(.claude, .weekly, UsageWindow(percent: 75, resetsAt: reset))
        let codex = policy.shouldNotify(.codex, .fiveHour, UsageWindow(percent: 75, resetsAt: reset))
        #expect(first)
        #expect(!again)
        #expect(!jittered)
        #expect(weekly)
        #expect(codex)
    }

    @Test func notifiesAgainAfterReset() {
        var policy = AlertPolicy()
        let first = policy.shouldNotify(.codex, .fiveHour, UsageWindow(percent: 76, resetsAt: reset))
        let nextCycle = policy.shouldNotify(.codex, .fiveHour, UsageWindow(percent: 76, resetsAt: reset.addingTimeInterval(5 * 3600)))
        #expect(first)
        #expect(nextCycle)
    }

    @Test func survivesRestart() {
        var first = AlertPolicy()
        _ = first.shouldNotify(.codex, .weekly, UsageWindow(percent: 80, resetsAt: reset))
        var restored = AlertPolicy(notified: first.notified)
        let notified = restored.shouldNotify(.codex, .weekly, UsageWindow(percent: 82, resetsAt: reset))
        #expect(!notified)
    }
}
