import Testing

@testable import UltraGearInputCore

struct InputSourceTests {
    @Test func defaultsMatchSpec() {
        #expect(InputSource.defaults == ProtocolSpec.shared.defaultInputs)
    }

    @Test func valueDependsOnProtocol() {
        let input = InputSource(id: "hdmi1", name: "HDMI 1", standardValue: 17, lgValue: 144)
        #expect(input.value(for: .standard) == 17)
        #expect(input.value(for: .lg) == 144)
    }
}
