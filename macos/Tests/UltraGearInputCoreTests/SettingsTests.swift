import Foundation
import Testing

@testable import UltraGearInputCore

struct SettingsTests {
    let lgDisplay = DisplayDescriptor(key: "display-a", name: "LG ULTRAGEAR", manufacturerCode: "GSM")
    let otherDisplay = DisplayDescriptor(key: "display-b", name: "DELL U2723QE", manufacturerCode: "DEL")

    @Test func maximumInputsMatchesSpec() {
        #expect(Settings.maximumInputs == ProtocolSpec.shared.maximumInputs)
    }

    @Test func decodesSpecCases() {
        for settingsCase in ProtocolSpec.shared.settingsCases {
            do throws(SettingsError) {
                _ = try Settings.decode(Data(settingsCase.json.utf8))
                #expect(settingsCase.error == nil, "\(settingsCase.json)")
            } catch {
                #expect(error == settingsCase.error, "\(settingsCase.json)")
            }
        }
    }

    @Test func emptySettingsUseDefaults() throws {
        let settings = try Settings.decode(Data("{}".utf8))
        #expect(settings.resolvedInputs == InputSource.defaults)
        #expect(settings.ddcProtocol(for: lgDisplay) == .lg)
        #expect(settings.ddcProtocol(for: otherDisplay) == .standard)
    }

    @Test func overrideWinsOverDefault() {
        let settings = Settings(protocols: ["display-a": .standard])
        #expect(settings.ddcProtocol(for: lgDisplay) == .standard)
    }

    @Test func settingDefaultProtocolRemovesOverride() {
        let overridden = Settings().setting(.standard, for: lgDisplay)
        #expect(overridden.protocols == ["display-a": .standard])
        #expect(overridden.setting(.lg, for: lgDisplay).protocols.isEmpty)
    }

    @Test func roundTripsThroughEncoding() throws {
        let settings = Settings(
            inputs: [InputSource(id: "dp", name: "DisplayPort", standardValue: 15, lgValue: 208)],
            protocols: ["display-b": .lg]
        )
        #expect(try Settings.decode(settings.encoded()) == settings)
    }
}
