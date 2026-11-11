// swift-tools-version: 6.0
import PackageDescription

let package = Package(
    name: "UltraGearInput",
    platforms: [.macOS(.v14)],
    products: [
        .executable(name: "UltraGearInput", targets: ["UltraGearInput"])
    ],
    targets: [
        .target(name: "UltraGearInputCore", resources: [.process("Resources")]),
        .executableTarget(
            name: "UltraGearInput",
            dependencies: ["UltraGearInputCore"],
            linkerSettings: [
                .linkedFramework("AppKit"),
                .linkedFramework("IOKit"),
                .linkedFramework("ServiceManagement"),
            ]
        ),
        .testTarget(
            name: "UltraGearInputCoreTests",
            dependencies: ["UltraGearInputCore"]
        ),
    ]
)
