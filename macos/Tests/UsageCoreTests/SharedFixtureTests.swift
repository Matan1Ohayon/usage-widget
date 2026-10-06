import Foundation
import Testing
@testable import UsageCore

/// Runs the cross-platform cases in `shared/fixtures/expectations.json`.
/// The Windows test suite runs the same file, so both apps stay in lockstep.
@Suite struct SharedFixtureTests {
    static let directory = URL(fileURLWithPath: #filePath)
        .deletingLastPathComponent().deletingLastPathComponent().deletingLastPathComponent().deletingLastPathComponent()
        .appending(path: "shared/fixtures")

    struct FixtureError: Error { let key: String }
    typealias Object = [String: Any]

    static func data(_ name: String) throws -> Data { try Data(contentsOf: directory.appending(path: name)) }

    let expected: Object
    let timeZone: TimeZone

    init() throws {
        expected = try Self.cast(try JSONSerialization.jsonObject(with: Self.data("expectations.json")), "expectations.json")
        guard let zone = TimeZone(identifier: try Self.cast(expected["timeZone"], "timeZone")) else { throw FixtureError(key: "timeZone") }
        timeZone = zone
    }

    static func cast<T>(_ value: Any?, _ key: String) throws -> T {
        guard let value = value as? T else { throw FixtureError(key: key) }
        return value
    }

    private func object(_ key: String, in parent: Object? = nil) throws -> Object { try Self.cast((parent ?? expected)[key], key) }
    private func list(_ key: String) throws -> [Object] { try Self.cast(expected[key], key) }
    private func string(_ key: String, in object: Object) throws -> String { try Self.cast(object[key], key) }
    private func number(_ key: String, in object: Object) throws -> Double { try Self.cast(object[key], key) }
    private func date(_ key: String, in object: Object) -> Date? { (object[key] as? Double).map { Date(timeIntervalSince1970: $0) } }

    private func check(_ window: UsageWindow?, against spec: Any?) throws {
        guard let spec = spec as? Object else {
            #expect(window == nil)
            return
        }
        let window = try #require(window)
        #expect(window.percent == (try number("percent", in: spec)))
        if let epoch = date("resetsAtEpoch", in: spec) {
            #expect(window.resetsAt == epoch)
        }
    }

    @Test func claudeUsage() throws {
        let usage = try ClaudeParser.parse(Self.data("claude-usage.json"), plan: nil, fetchedAt: Date())
        let spec = try object("claudeUsage")
        let five = try object("fiveHour", in: spec), weekly = try object("weekly", in: spec)
        let fiveReset = try #require(usage.fiveHour?.resetsAt), weeklyReset = try #require(usage.weekly?.resetsAt)
        #expect(usage.fiveHour?.percent == (try number("percent", in: five)))
        #expect(UsageFormat.time(fiveReset, timeZone: timeZone) == (try string("time", in: five)))
        #expect(usage.weekly?.percent == (try number("percent", in: weekly)))
        #expect(UsageFormat.dayAndTime(weeklyReset, timeZone: timeZone) == (try string("dayAndTime", in: weekly)))
        #expect(UsageFormat.weekdayAndTime(weeklyReset, timeZone: timeZone) == (try string("weekdayAndTime", in: weekly)))
    }

    @Test func claudeCredentials() throws {
        let creds = try ClaudeParser.parseCredentials(Self.data("claude-credentials.json"))
        let spec = try object("claudeCredentials")
        #expect(creds.accessToken == (try string("accessToken", in: spec)))
        #expect(creds.plan == (try string("plan", in: spec)))
        #expect(creds.expiresAt == date("expiresAtEpoch", in: spec))
    }

    @Test func codexUsage() throws {
        let usage = try CodexParser.parse(Self.data("codex-usage.json"), fetchedAt: Date())
        let spec = try object("codexUsage")
        #expect(usage.plan == (try string("plan", in: spec)))
        try check(usage.fiveHour, against: spec["fiveHour"])
        try check(usage.weekly, against: spec["weekly"])

        let weeklyOnly = try CodexParser.parse(Self.data("codex-usage-weekly-only.json"), fetchedAt: Date())
        let weeklySpec = try object("codexUsageWeeklyOnly")
        try check(weeklyOnly.fiveHour, against: weeklySpec["fiveHour"])
        try check(weeklyOnly.weekly, against: weeklySpec["weekly"])
    }

    @Test func codexSessionLog() throws {
        let text = String(decoding: try Self.data("codex-session.jsonl"), as: UTF8.self)
        let usage = try #require(text.split(separator: "\n").reversed().lazy.compactMap(CodexParser.parseSessionLine).first)
        let spec = try object("codexSession")
        #expect(usage.plan == (try string("plan", in: spec)))
        try check(usage.fiveHour, against: spec["fiveHour"])
        try check(usage.weekly, against: spec["weekly"])
    }

    @Test func levels() throws {
        for item in try list("levels") {
            let level: String = switch UsageLevel(percent: try number("percent", in: item)) {
            case .green: "green"
            case .yellow: "yellow"
            case .red: "red"
            }
            #expect(level == (try string("level", in: item)))
        }
    }

    @Test func dates() throws {
        for item in try list("dates") {
            let moment = try #require(date("epoch", in: item))
            #expect(UsageFormat.time(moment, timeZone: timeZone) == (try string("time", in: item)))
            #expect(UsageFormat.dayAndTime(moment, timeZone: timeZone) == (try string("dayAndTime", in: item)))
            #expect(UsageFormat.weekdayAndTime(moment, timeZone: timeZone) == (try string("weekdayAndTime", in: item)))
        }
    }

    @Test func percents() throws {
        for item in try list("percents") {
            #expect(UsageFormat.percent(try number("value", in: item)) == (try string("text", in: item)))
        }
    }

    @Test func countdowns() throws {
        for item in try list("countdowns") {
            let now = try #require(date("now", in: item)), reset = try #require(date("reset", in: item))
            #expect(UsageFormat.countdown(from: now, to: reset) == (try string("text", in: item)))
        }
    }

    @Test func resetLines() throws {
        for item in try list("resetLines") {
            let kind = try #require(WindowKind(rawValue: try string("kind", in: item)))
            let now = try #require(date("now", in: item))
            let window = UsageWindow(percent: 50, resetsAt: date("reset", in: item))
            #expect(UsageFormat.resetLine(kind, window, now: now, timeZone: timeZone) == (try string("text", in: item)))
        }
    }

    @Test func notifications() throws {
        for item in try list("notifications") {
            let provider = try #require(Provider(rawValue: try string("provider", in: item)))
            let kind = try #require(WindowKind(rawValue: try string("kind", in: item)))
            let now = try #require(date("now", in: item))
            let window = UsageWindow(percent: try number("percent", in: item), resetsAt: date("reset", in: item))
            #expect(UsageFormat.notificationTitle(provider, kind) == (try string("title", in: item)))
            #expect(UsageFormat.notificationBody(kind, window, now: now, timeZone: timeZone) == (try string("body", in: item)))
        }
    }
}
