import Testing

@testable import UltraGearInputCore

struct DisplayTests {
    @Test func decodesManufacturerVectors() {
        for manufacturer in ProtocolSpec.shared.manufacturers {
            #expect(ManufacturerCode.decode(edid: manufacturer.raw) == manufacturer.code)
        }
    }

    @Test func rejectsOutOfRangeLetters() {
        #expect(ManufacturerCode.decode(edid: 0x7FFF) == nil)
    }

    @Test(
        arguments: [
            ("GSM", "Monitor", true),
            (nil, "LG ULTRAGEAR", true),
            (nil, "lg hdr 4k", true),
            ("DEL", "DELL U2723QE", false),
            (nil, "External Display", false),
        ] as [(String?, String, Bool)])
    func detectsLGDisplays(code: String?, name: String, expected: Bool) {
        let display = DisplayDescriptor(key: "key", name: name, manufacturerCode: code)
        #expect(display.isLG == expected)
        #expect(display.defaultProtocol == (expected ? .lg : .standard))
    }
}
