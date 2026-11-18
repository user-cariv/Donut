// swift-tools-version: 5.9
import PackageDescription
let package = Package(name: "DonutLock", platforms: [.macOS(.v13)], products: [.executable(name: "DonutLock", targets: ["DonutLock"])], targets: [.executableTarget(name: "DonutLock", path: ".", exclude: ["README.md", "Package.swift", ".build"], resources: [.process("Assets")])])
