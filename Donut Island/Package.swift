// swift-tools-version:5.9
import PackageDescription

let package = Package(
    name: "DonutIsland",
    platforms: [.macOS(.v27)],
    targets: [
        .executableTarget(
            name: "DonutIsland",
            path: "Sources/DonutIsland"
        )
    ]
)
