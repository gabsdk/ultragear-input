public struct DisplayDescriptor: Hashable, Sendable {
    public let key: String
    public let name: String
    public let manufacturerCode: String?

    public init(key: String, name: String, manufacturerCode: String?) {
        self.key = key
        self.name = name
        self.manufacturerCode = manufacturerCode
    }

    public var isLG: Bool {
        manufacturerCode == ManufacturerCode.lg || name.uppercased().hasPrefix("LG")
    }

    public var defaultProtocol: DDCProtocol {
        isLG ? .lg : .standard
    }
}

public enum ManufacturerCode {
    public static let lg = "GSM"

    public static func decode(edid raw: UInt16) -> String? {
        guard raw & 0x8000 == 0 else { return nil }
        let letters = [(raw >> 10) & 0x1F, (raw >> 5) & 0x1F, raw & 0x1F]
        guard letters.allSatisfy({ (1...26).contains($0) }) else { return nil }
        return String(letters.map { Character(UnicodeScalar(UInt8($0) + 0x40)) })
    }
}
