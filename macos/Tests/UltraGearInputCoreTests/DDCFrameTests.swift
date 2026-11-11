import Testing

@testable import UltraGearInputCore

struct DDCFrameTests {
    let spec = ProtocolSpec.shared

    @Test func destinationAddressMatchesSpec() {
        #expect(DDCFrame.destinationAddress == spec.destinationAddress)
    }

    @Test(arguments: DDCProtocol.allCases)
    func protocolAddressesMatchSpec(ddcProtocol: DDCProtocol) throws {
        let entry = try #require(spec.protocols[ddcProtocol.rawValue])
        #expect(ddcProtocol.sourceAddress == entry.sourceAddress)
        #expect(ddcProtocol.inputCode == entry.inputCode)
    }

    @Test func framesMatchSpecVectors() throws {
        #expect(spec.frames.count == DDCProtocol.allCases.count * InputSource.defaults.count)
        for frame in spec.frames {
            let input = try #require(InputSource.defaults.first { $0.id == frame.inputId })
            #expect(DDCFrame.setInput(input.value(for: frame.protocol), using: frame.protocol) == frame.bytes)
        }
    }

    @Test func encodesHighByteOfValue() {
        let frame = DDCFrame.setInput(0x1234, using: .standard)
        #expect(frame[4] == 0x12)
        #expect(frame[5] == 0x34)
        #expect(DDCFrame.checksum(of: Array(frame.dropLast())) == frame.last)
    }

    @Test func checksumOfEmptyBodyIsDestinationAddress() {
        #expect(DDCFrame.checksum(of: []) == DDCFrame.destinationAddress)
    }
}
