import Foundation

@testable import UltraGearInputCore

struct ProtocolSpec: Decodable {
    struct ProtocolEntry: Decodable {
        let sourceAddress: UInt8
        let inputCode: UInt8
    }

    struct Frame: Decodable {
        let `protocol`: DDCProtocol
        let inputId: String
        let bytes: [UInt8]
    }

    struct Manufacturer: Decodable {
        let raw: UInt16
        let code: String?
    }

    struct SettingsCase: Decodable {
        let json: String
        let error: SettingsError?
    }

    let destinationAddress: UInt8
    let protocols: [String: ProtocolEntry]
    let defaultInputs: [InputSource]
    let frames: [Frame]
    let manufacturers: [Manufacturer]
    let settingsCases: [SettingsCase]
    let maximumInputs: Int

    static let shared: ProtocolSpec = {
        let url = URL(fileURLWithPath: #filePath)
            .deletingLastPathComponent()
            .appendingPathComponent("../../../spec/protocol.json")
            .standardizedFileURL
        do {
            return try JSONDecoder().decode(ProtocolSpec.self, from: Data(contentsOf: url))
        } catch {
            fatalError("Unable to load \(url.path): \(error)")
        }
    }()
}

extension SettingsError: Decodable {}
