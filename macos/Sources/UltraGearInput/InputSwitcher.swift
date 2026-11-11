import UltraGearInputCore

actor InputSwitcher {
    static let shared = InputSwitcher()

    func switchInput(displayKey: String, value: UInt16, ddcProtocol: DDCProtocol) throws(DDCError) {
        guard let avService = AVService.shared else { throw .unsupportedSystem }
        let frame = DDCFrame.setInput(value, using: ddcProtocol)
        try DisplayDiscovery.withPort(forKey: displayKey) { port throws(DDCError) in
            guard let service = avService.service(for: port.proxy) else { throw .unsupportedSystem }
            try avService.write(frame, to: service, chipAddress: port.chipAddress)
        }
    }
}
