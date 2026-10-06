import AppKit

let app = NSApplication.shared

if let flag = CommandLine.arguments.firstIndex(of: "--snapshot"), CommandLine.arguments.indices.contains(flag + 1) {
    let directory = URL(fileURLWithPath: CommandLine.arguments[flag + 1])
    Task {
        await Snapshot.run(to: directory, demo: CommandLine.arguments.contains("--demo"))
        exit(0)
    }
    RunLoop.main.run()
}

if CommandLine.arguments.contains("--check") {
    Task {
        await Diagnostics.run()
        exit(0)
    }
    RunLoop.main.run()
}

let delegate = AppDelegate()
app.delegate = delegate
app.setActivationPolicy(.accessory)
app.run()
