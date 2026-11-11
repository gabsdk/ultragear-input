import IOKit
import UltraGearInputCore

enum Copy {
    static let appName = "UltraGear Input"
    static let unknownDisplay = "External Display"
    static let noDisplays = "No external display found"
    static let protocolMenu = "Protocol"
    static let openSettings = "Open Settings File…"
    static let launchAtLogin = "Launch at Login"
    static let quit = "Quit UltraGear Input"
    static let switchFailed = "Could not switch the input"
    static let settingsFailed = "Could not save the settings"
    static let loginItemFailed = "Could not change Launch at Login"
    static let ok = "OK"

    static func protocolName(_ ddcProtocol: DDCProtocol) -> String {
        switch ddcProtocol {
        case .lg: "LG"
        case .standard: "Standard DDC/CI"
        }
    }

    static func invalidSettings(_ error: SettingsError) -> String {
        switch error {
        case .malformed: "Settings file is not valid JSON. Using defaults."
        case .emptyInputs: "Settings file has an empty input list. Using defaults."
        case .tooManyInputs: "Settings file has more than \(Settings.maximumInputs) inputs. Using defaults."
        case .blankInputId: "Settings file has an input without an id. Using defaults."
        case .duplicateInputId: "Settings file repeats an input id. Using defaults."
        case .blankInputName: "Settings file has an input without a name. Using defaults."
        }
    }

    static func describe(_ error: DDCError) -> String {
        switch error {
        case .unsupportedSystem:
            "This Mac cannot send DDC commands. UltraGear Input needs a Mac with Apple silicon."
        case .displayNotFound:
            "The display is no longer connected."
        case .writeFailed(let code):
            "The display did not accept the command (IOReturn \(String(format: "0x%08X", UInt32(bitPattern: code)))). Try the other protocol, another cable or another port."
        }
    }
}
