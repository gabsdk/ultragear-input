public struct InputSource: Codable, Hashable, Identifiable, Sendable {
    public let id: String
    public let name: String
    public let standardValue: UInt16
    public let lgValue: UInt16

    public init(id: String, name: String, standardValue: UInt16, lgValue: UInt16) {
        self.id = id
        self.name = name
        self.standardValue = standardValue
        self.lgValue = lgValue
    }

    public func value(for ddcProtocol: DDCProtocol) -> UInt16 {
        switch ddcProtocol {
        case .standard: standardValue
        case .lg: lgValue
        }
    }
}

extension InputSource {
    public static let defaults: [InputSource] = [
        InputSource(id: "displayPort1", name: "DisplayPort 1", standardValue: 0x0F, lgValue: 0xD0),
        InputSource(id: "displayPort2", name: "DisplayPort 2 / USB-C", standardValue: 0x10, lgValue: 0xD1),
        InputSource(id: "usbC", name: "USB-C / DisplayPort 3", standardValue: 0x1B, lgValue: 0xD2),
        InputSource(id: "hdmi1", name: "HDMI 1", standardValue: 0x11, lgValue: 0x90),
        InputSource(id: "hdmi2", name: "HDMI 2", standardValue: 0x12, lgValue: 0x91),
    ]
}
