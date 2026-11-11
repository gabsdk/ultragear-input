import Foundation

public enum SettingsError: String, Error, Equatable, Sendable {
    case malformed
    case emptyInputs
    case tooManyInputs
    case blankInputId
    case duplicateInputId
    case blankInputName
}

public struct Settings: Codable, Equatable, Sendable {
    public static let maximumInputs = 32

    public var inputs: [InputSource]?
    public var protocols: [String: DDCProtocol]

    public init(inputs: [InputSource]? = nil, protocols: [String: DDCProtocol] = [:]) {
        self.inputs = inputs
        self.protocols = protocols
    }

    public init(from decoder: Decoder) throws {
        let container = try decoder.container(keyedBy: CodingKeys.self)
        inputs = try container.decodeIfPresent([InputSource].self, forKey: .inputs)
        protocols = try container.decodeIfPresent([String: DDCProtocol].self, forKey: .protocols) ?? [:]
    }

    public var resolvedInputs: [InputSource] {
        inputs ?? InputSource.defaults
    }

    public func ddcProtocol(for display: DisplayDescriptor) -> DDCProtocol {
        protocols[display.key] ?? display.defaultProtocol
    }

    public func setting(_ ddcProtocol: DDCProtocol, for display: DisplayDescriptor) -> Settings {
        var copy = self
        copy.protocols[display.key] = ddcProtocol == display.defaultProtocol ? nil : ddcProtocol
        return copy
    }

    public static func decode(_ data: Data) throws(SettingsError) -> Settings {
        let settings: Settings
        do {
            settings = try JSONDecoder().decode(Settings.self, from: data)
        } catch {
            throw .malformed
        }
        try settings.validate()
        return settings
    }

    public func encoded() throws -> Data {
        let encoder = JSONEncoder()
        encoder.outputFormatting = [.prettyPrinted, .sortedKeys, .withoutEscapingSlashes]
        return try encoder.encode(self)
    }

    public func validate() throws(SettingsError) {
        guard let inputs else { return }
        guard !inputs.isEmpty else { throw .emptyInputs }
        guard inputs.count <= Self.maximumInputs else { throw .tooManyInputs }
        guard inputs.allSatisfy({ !$0.id.isBlank }) else { throw .blankInputId }
        guard Set(inputs.map(\.id)).count == inputs.count else { throw .duplicateInputId }
        guard inputs.allSatisfy({ !$0.name.isBlank }) else { throw .blankInputName }
    }
}

extension String {
    var isBlank: Bool {
        trimmingCharacters(in: .whitespacesAndNewlines).isEmpty
    }
}
