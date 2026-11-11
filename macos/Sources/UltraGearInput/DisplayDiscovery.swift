import Foundation
import IOKit
import UltraGearInputCore

struct ExternalDisplayPort {
    let descriptor: DisplayDescriptor
    let proxy: io_service_t
    let chipAddress: UInt32
}

enum DisplayDiscovery {
    static func descriptors() -> [DisplayDescriptor] {
        var descriptors: [DisplayDescriptor] = []
        scan { port in
            descriptors.append(port.descriptor)
            return false
        }
        return descriptors
    }

    static func withPort<Result>(
        forKey key: String,
        _ body: (ExternalDisplayPort) throws(DDCError) -> Result
    ) throws(DDCError) -> Result {
        var outcome: Swift.Result<Result, DDCError> = .failure(.displayNotFound)
        scan { port in
            guard port.descriptor.key == key else { return false }
            do throws(DDCError) {
                outcome = .success(try body(port))
            } catch {
                outcome = .failure(error)
            }
            return true
        }
        return try outcome.get()
    }

    private static func scan(_ visit: (ExternalDisplayPort) -> Bool) {
        var iterator: io_iterator_t = 0
        let root = IORegistryGetRootEntry(kIOMainPortDefault)
        defer { IOObjectRelease(root) }
        let options = IOOptionBits(kIORegistryIterateRecursively)
        guard IORegistryEntryCreateIterator(root, kIOServicePlane, options, &iterator) == KERN_SUCCESS else {
            return
        }
        defer { IOObjectRelease(iterator) }

        var pendingDescriptor: DisplayDescriptor?
        while case let entry = IOIteratorNext(iterator), entry != 0 {
            defer { IOObjectRelease(entry) }
            if IOObjectConformsTo(entry, "IOMobileFramebuffer") != 0 {
                pendingDescriptor = descriptor(forFramebuffer: entry)
                continue
            }
            guard
                IOObjectConformsTo(entry, "DCPAVServiceProxy") != 0,
                let descriptor = pendingDescriptor,
                isExternal(entry)
            else {
                continue
            }
            pendingDescriptor = nil
            let port = ExternalDisplayPort(
                descriptor: descriptor,
                proxy: entry,
                chipAddress: isMCDP29XX(entry) ? AVService.mcdp29xxChipAddress : AVService.defaultChipAddress
            )
            if visit(port) { return }
        }
    }

    private static func descriptor(forFramebuffer framebuffer: io_registry_entry_t) -> DisplayDescriptor? {
        guard
            let attributes = searchProperty(framebuffer, "DisplayAttributes") as? [String: Any],
            let product = attributes["ProductAttributes"] as? [String: Any]
        else {
            return nil
        }
        let name = (product["ProductName"] as? String).flatMap { $0.isEmpty ? nil : $0 } ?? Copy.unknownDisplay
        let manufacturer = (product["LegacyManufacturerID"] as? NSNumber).flatMap { ManufacturerCode.decode(edid: $0.uint16Value) }
        let key = (searchProperty(framebuffer, "EDID UUID") as? String) ?? "registry-\(registryID(framebuffer))"
        return DisplayDescriptor(key: key, name: name, manufacturerCode: manufacturer)
    }

    private static func isExternal(_ proxy: io_service_t) -> Bool {
        (searchProperty(proxy, "Location") as? String) == "External"
    }

    private static func isMCDP29XX(_ proxy: io_service_t) -> Bool {
        var parent: io_registry_entry_t = 0
        guard IORegistryEntryGetParentEntry(proxy, kIOServicePlane, &parent) == KERN_SUCCESS else { return false }
        defer { IOObjectRelease(parent) }
        let providerClass =
            IORegistryEntryCreateCFProperty(parent, "EPICProviderClass" as CFString, kCFAllocatorDefault, 0)?
            .takeRetainedValue() as? String
        return providerClass == "AppleDCPMCDP29XX"
    }

    private static func searchProperty(_ entry: io_registry_entry_t, _ key: String) -> Any? {
        IORegistryEntrySearchCFProperty(
            entry,
            kIOServicePlane,
            key as CFString,
            kCFAllocatorDefault,
            IOOptionBits(kIORegistryIterateRecursively)
        )
    }

    private static func registryID(_ entry: io_registry_entry_t) -> UInt64 {
        var identifier: UInt64 = 0
        IORegistryEntryGetRegistryEntryID(entry, &identifier)
        return identifier
    }
}
