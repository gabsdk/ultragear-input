import Darwin
import Foundation
import IOKit

struct AVService: @unchecked Sendable {
    typealias Create = @convention(c) (CFAllocator?, io_service_t) -> Unmanaged<CFTypeRef>?
    typealias WriteI2C = @convention(c) (CFTypeRef, UInt32, UInt32, UnsafeMutableRawPointer, UInt32) -> IOReturn

    static let defaultChipAddress: UInt32 = 0x37
    static let mcdp29xxChipAddress: UInt32 = 0xB7
    static let writeAttempts = 2
    static let writeDelay: useconds_t = 10_000

    private let create: Create
    private let writeI2C: WriteI2C

    static let shared: AVService? = {
        guard
            let handle = dlopen("/System/Library/Frameworks/IOKit.framework/IOKit", RTLD_NOW),
            let create = dlsym(handle, "IOAVServiceCreateWithService"),
            let write = dlsym(handle, "IOAVServiceWriteI2C")
        else {
            return nil
        }
        return AVService(
            create: unsafeBitCast(create, to: Create.self),
            writeI2C: unsafeBitCast(write, to: WriteI2C.self)
        )
    }()

    func service(for proxy: io_service_t) -> CFTypeRef? {
        create(kCFAllocatorDefault, proxy)?.takeRetainedValue()
    }

    func write(_ frame: [UInt8], to service: CFTypeRef, chipAddress: UInt32) throws(DDCError) {
        guard let dataAddress = frame.first else { return }
        var payload = Array(frame.dropFirst())
        for _ in 0..<Self.writeAttempts {
            usleep(Self.writeDelay)
            let result = payload.withUnsafeMutableBytes { buffer in
                writeI2C(service, chipAddress, UInt32(dataAddress), buffer.baseAddress!, UInt32(buffer.count))
            }
            guard result == kIOReturnSuccess else { throw .writeFailed(result) }
        }
    }
}

enum DDCError: Error, Equatable {
    case unsupportedSystem
    case displayNotFound
    case writeFailed(IOReturn)
}
