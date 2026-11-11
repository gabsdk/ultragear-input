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

    func applicationWillTerminate(_ notification: Notification) {
        controller?.cancelUpdate()
    }

    func applicationDidFinishLaunching(_ notification: Notification) {
        controller = StatusMenuController()
        if CommandLine.arguments.count == 3 {
            let flag = CommandLine.arguments[1]
            if flag == UpdateInstaller.updatedFlag || flag == UpdateInstaller.failedFlag {
                let alert = NSAlert()
                alert.messageText = Copy.appName
                alert.informativeText = UpdateText.get(
                    flag == UpdateInstaller.updatedFlag ? "updated" : "installFailed", value: CommandLine.arguments[2])
                alert.addButton(withTitle: UpdateText.get("ok"))
                NSApp.activate()
                alert.runModal()
            }
        }
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

if CommandLine.arguments == [CommandLine.arguments[0], "--version"] {
    print(Bundle.main.object(forInfoDictionaryKey: "CFBundleShortVersionString") as? String ?? "0.0.0")
    exit(EXIT_SUCCESS)
}

if CommandLine.arguments.count == 3, CommandLine.arguments[1] == UpdateInstaller.applyFlag {
    Task {
        let result = await UpdateInstaller.apply(URL(fileURLWithPath: CommandLine.arguments[2]))
        exit(result)
    }
    dispatchMain()
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
