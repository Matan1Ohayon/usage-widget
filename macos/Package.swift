// swift-tools-version: 6.0
import PackageDescription

let package = Package(
    name: "AIUsage",
    platforms: [.macOS(.v14)],
    targets: [
        .target(name: "UsageCore"),
        .executableTarget(name: "AIUsage", dependencies: ["UsageCore"]),
        .testTarget(name: "UsageCoreTests", dependencies: ["UsageCore"]),
    ]
)
