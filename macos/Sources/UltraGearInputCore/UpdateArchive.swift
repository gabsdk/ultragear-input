import Foundation

public enum UpdateArchive {
    public static func validate(_ data: Data) throws {
        guard data.count >= 22, data.count <= UpdateRelease.maximumSize else { throw UpdateFailure.package }
        let bytes = [UInt8](data)
        func number(_ offset: Int, _ count: Int) throws -> Int {
            guard offset >= 0, offset + count <= bytes.count else { throw UpdateFailure.package }
            return (0..<count).reduce(0) { $0 | (Int(bytes[offset + $1]) << ($1 * 8)) }
        }
        let lower = max(0, bytes.count - 65_557)
        guard
            let end = stride(from: bytes.count - 22, through: lower, by: -1).first(where: {
                bytes[$0..<($0 + 4)].elementsEqual([0x50, 0x4B, 0x05, 0x06])
            })
        else { throw UpdateFailure.package }
        guard try number(end + 4, 2) == 0, try number(end + 6, 2) == 0,
            try number(end + 8, 2) == number(end + 10, 2), try end + 22 + number(end + 20, 2) == bytes.count
        else { throw UpdateFailure.package }
        let count = try number(end + 10, 2)
        let centralSize = try number(end + 12, 4)
        var offset = try number(end + 16, 4)
        guard count > 0, count < 10_000, offset + centralSize == end else { throw UpdateFailure.package }
        let centralOffset = offset
        var names = Set<String>()
        var size = 0
        for _ in 0..<count {
            guard try number(offset, 4) == 0x0201_4B50, try number(offset + 8, 2) & 1 == 0,
                try [0, 8].contains(number(offset + 10, 2))
            else { throw UpdateFailure.package }
            let local = try number(offset + 42, 4)
            guard try number(local, 4) == 0x0403_4B50,
                try number(local + 6, 2) == number(offset + 8, 2),
                try number(local + 8, 2) == number(offset + 10, 2)
            else { throw UpdateFailure.package }
            let localNameLength = try number(local + 26, 2)
            let localExtraLength = try number(local + 28, 2)
            let localNameStart = local + 30
            let localBodyStart = localNameStart + localNameLength + localExtraLength
            let compressed = try number(offset + 20, 4)
            guard localBodyStart + compressed <= centralOffset else { throw UpdateFailure.package }
            let nameLength = try number(offset + 28, 2)
            let extraLength = try number(offset + 30, 2)
            let commentLength = try number(offset + 32, 2)
            let mode = try number(offset + 38, 4) >> 16
            let start = offset + 46
            guard localNameLength == nameLength, localNameStart + localNameLength <= centralOffset,
                start + nameLength <= end,
                bytes[localNameStart..<(localNameStart + localNameLength)].elementsEqual(bytes[start..<(start + nameLength)]),
                let name = String(bytes: bytes[start..<(start + nameLength)], encoding: .utf8),
                name == "UltraGear Input.app/" || name.hasPrefix("UltraGear Input.app/"),
                !name.contains("\\"), !name.contains("\0"), !name.split(separator: "/").contains(".."),
                !name.split(separator: "/").contains("."), names.insert(name.lowercased()).inserted,
                mode & 0xF000 == 0 || [0x4000, 0x8000].contains(mode & 0xF000)
            else { throw UpdateFailure.package }
            size += try number(offset + 24, 4)
            guard size <= 512 * 1024 * 1024 else { throw UpdateFailure.package }
            offset = start + nameLength + extraLength + commentLength
        }
        guard offset == end, names.contains("ultragear input.app/contents/macos/ultragearinput") else { throw UpdateFailure.package }
    }
}
