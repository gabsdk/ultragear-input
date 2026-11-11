import AppKit
import Foundation
import UltraGearInputCore

struct DisplayReport: Encodable {
    let key: String
    let name: String
    let manufacturerCode: String?
    let `protocol`: DDCProtocol
}

@MainActor
final class AppDelegate: NSObject, NSApplicationDelegate {
    private var controller: StatusMenuController?

    func applicationDidFinishLaunching(_ notification: Notification) {
        controller = StatusMenuController()
    }
}

@MainActor
func printDisplayReport() throws {
    let settings = (try? SettingsStore.standard.load().get()) ?? Settings()
    let reports = DisplayDiscovery.descriptors().map {
        DisplayReport(key: $0.key, name: $0.name, manufacturerCode: $0.manufacturerCode, protocol: settings.ddcProtocol(for: $0))
    }
    let encoder = JSONEncoder()
    encoder.outputFormatting = [.prettyPrinted, .sortedKeys]
    print(String(decoding: try encoder.encode(reports), as: UTF8.self))
}

if CommandLine.arguments.contains("--list-displays") {
    do {
        try printDisplayReport()
        exit(EXIT_SUCCESS)
    } catch {
        FileHandle.standardError.write(Data("\(error)\n".utf8))
        exit(EXIT_FAILURE)
    }
}

let application = NSApplication.shared
let delegate = AppDelegate()
application.delegate = delegate
application.setActivationPolicy(.accessory)
application.run()
