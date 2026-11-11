public enum DDCProtocol: String, Codable, CaseIterable, Sendable {
    case standard
    case lg

    public var sourceAddress: UInt8 {
        switch self {
        case .standard: 0x51
        case .lg: 0x50
        }
    }

    public var inputCode: UInt8 {
        switch self {
        case .standard: 0x60
        case .lg: 0xF4
        }
    }
}

public enum DDCFrame {
    public static let destinationAddress: UInt8 = 0x6E

    public static func setInput(_ value: UInt16, using ddcProtocol: DDCProtocol) -> [UInt8] {
        let body: [UInt8] = [
            ddcProtocol.sourceAddress,
            0x84,
            0x03,
            ddcProtocol.inputCode,
            UInt8(value >> 8),
            UInt8(value & 0xFF),
        ]
        return body + [checksum(of: body)]
    }

    public static func checksum(of bytes: [UInt8]) -> UInt8 {
        bytes.reduce(destinationAddress, ^)
    }
}
