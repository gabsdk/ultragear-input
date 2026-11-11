import Foundation
import Testing

@testable import UltraGearInputCore

struct UpdateTests {
    @Test(arguments: [("1.2.10", "1.2.9", true), ("v2.0.0", "1.99.99", true), ("1.2.3", "1.2.3", false), ("1.0.0", "2.0.0", false)])
    func comparesVersions(latest: String, current: String, newer: Bool) throws {
        #expect(try (ReleaseVersion(latest) > ReleaseVersion(current)) == newer)
    }

    @Test(arguments: ["1.2", "1.2.3-beta", "../1.2.3", "01.2.3", "1.2.3.4", "1.2.999999999999999999999"])
    func rejectsInvalidVersions(_ version: String) {
        #expect(throws: UpdateFailure.self) { try ReleaseVersion(version) }
    }

    @Test
    func selectsTheExactMacPackage() throws {
        let release = try UpdateRelease(data: fixture())
        #expect(release.package.name == "UltraGearInput-macos-2.3.4.zip")
        #expect(release.checksum.name == release.package.name + ".sha256")
        #expect(release.version.description == "2.3.4")
    }

    @Test(arguments: [
        ("\"draft\":false", "\"draft\":true"), ("\"prerelease\":false", "\"prerelease\":true"),
        ("https://github.com/", "http://github.com/"), ("gabsdk/ultragear-input", "other/project"),
        (".zip.sha256", ".zip.wrong"), ("\"size\":64", "\"size\":0"),
    ])
    func rejectsUntrustedReleases(original: String, replacement: String) throws {
        let data = try fixture()
        let modified = String(decoding: data, as: UTF8.self).replacingOccurrences(of: original, with: replacement)
        #expect(throws: UpdateFailure.self) { try UpdateRelease(data: Data(modified.utf8)) }
    }

    @Test
    func validatesHashesAndRedirectHosts() throws {
        let digest = UpdateClient.digest(Data("payload".utf8))
        #expect(try UpdateClient.parseChecksum(digest + "  package.zip\n", name: "package.zip") == digest)
        #expect(throws: UpdateFailure.self) { try UpdateClient.parseChecksum(digest + "  other.zip", name: "package.zip") }
        #expect(throws: UpdateFailure.self) { try UpdateClient.parseChecksum("bad", name: "package.zip") }
        #expect(UpdateClient.trusted(URL(string: "https://release-assets.githubusercontent.com/payload")!))
        for text in [
            "http://github.com/payload", "https://github.com.evil.example/payload", "https://user@github.com/payload",
            "https://github.com:444/payload",
        ] {
            #expect(!UpdateClient.trusted(URL(string: text)!))
        }
    }

    @Test
    func validatesArchivePathsAndRejectsLinks() throws {
        try UpdateArchive.validate(archive(["UltraGear Input.app/Contents/MacOS/UltraGearInput"]))
        for name in [
            "../escape", "/tmp/escape", "UltraGear Input.app/../../escape", "UltraGear Input.app/./escape",
            "UltraGear Input.app/Contents/\\escape",
        ] {
            #expect(throws: UpdateFailure.self) { try UpdateArchive.validate(archive([name])) }
        }
        #expect(throws: UpdateFailure.self) {
            try UpdateArchive.validate(archive(["UltraGear Input.app/Contents/MacOS/UltraGearInput"], mode: 0xA000))
        }
        #expect(throws: UpdateFailure.self) { try UpdateArchive.validate(Data([1, 2, 3])) }
    }

    @Test
    func validatesAnArchiveProducedByDitto() throws {
        let root = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        defer { try? FileManager.default.removeItem(at: root) }
        let executable = root.appendingPathComponent("UltraGear Input.app/Contents/MacOS/UltraGearInput")
        try FileManager.default.createDirectory(at: executable.deletingLastPathComponent(), withIntermediateDirectories: true)
        try Data("executable".utf8).write(to: executable)
        let archiveURL = root.appendingPathComponent("app.zip")
        let process = Process()
        process.executableURL = URL(fileURLWithPath: "/usr/bin/ditto")
        process.arguments = [
            "-c", "-k", "--norsrc", "--keepParent", root.appendingPathComponent("UltraGear Input.app").path, archiveURL.path,
        ]
        try process.run()
        process.waitUntilExit()
        #expect(process.terminationStatus == 0)
        try UpdateArchive.validate(Data(contentsOf: archiveURL))
    }

    @Test
    func restoresThePreviousBundleWhenRestartFails() throws {
        let root = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        defer { try? FileManager.default.removeItem(at: root) }
        let target = root.appendingPathComponent("Installed App.app")
        let payload = root.appendingPathComponent("Payload.app")
        try FileManager.default.createDirectory(at: target, withIntermediateDirectories: true)
        try FileManager.default.createDirectory(at: payload, withIntermediateDirectories: true)
        try Data("previous".utf8).write(to: target.appendingPathComponent("value"))
        try Data("next".utf8).write(to: payload.appendingPathComponent("value"))
        #expect(throws: UpdateFailure.self) {
            try UpdateInstaller.replace(target: target, payload: payload) { throw UpdateFailure.installFailed }
        }
        #expect(try String(contentsOf: target.appendingPathComponent("value"), encoding: .utf8) == "previous")
        try UpdateInstaller.replace(target: target, payload: payload)
        #expect(try String(contentsOf: target.appendingPathComponent("value"), encoding: .utf8) == "next")
        #expect(try FileManager.default.contentsOfDirectory(atPath: root.path).sorted() == ["Installed App.app", "Payload.app"])
    }

    @Test
    func localizesUpdateMessagesAndSharesCatalogKeys() throws {
        #expect(UpdateText.language("pt_BR") == "pt")
        #expect(UpdateText.language("es-MX") == "es")
        #expect(UpdateText.language("fr") == "en")
        #expect(UpdateText.get("install", language: "pt", value: "2.3.4") == "Instalar 2.3.4…")
        #expect(UpdateText.get("install", language: "es", value: "2.3.4") == "Instalar 2.3.4…")
        #expect(UpdateText.get("install", language: "en", value: "2.3.4") == "Install 2.3.4…")
        let root = URL(fileURLWithPath: #filePath).deletingLastPathComponent().appendingPathComponent("../../../src/locales")
        let catalogs = try ["en", "pt", "es"].map { language in
            try JSONDecoder().decode([String: String].self, from: Data(contentsOf: root.appendingPathComponent(language + ".json")))
        }
        for catalog in catalogs {
            #expect(Set(catalog.keys) == Set(catalogs[0].keys))
            for (key, text) in catalog {
                #expect(!text.isEmpty)
                #expect(text.contains("{0}") == catalogs[0][key]!.contains("{0}"))
            }
        }
    }

    private func fixture() throws -> Data {
        let name = "UltraGearInput-macos-2.3.4.zip"
        let assets = [name, name + ".sha256"].map { file in
            [
                "name": file, "size": 64,
                "browser_download_url": "https://github.com/gabsdk/ultragear-input/releases/download/v2.3.4/" + file,
            ] as [String: Any]
        }
        return try JSONSerialization.data(
            withJSONObject: ["tag_name": "v2.3.4", "draft": false, "prerelease": false, "assets": assets],
            options: [.sortedKeys, .withoutEscapingSlashes])
    }

    private func archive(_ names: [String], mode: UInt32 = 0x8000) -> Data {
        var data = Data()
        func append(_ value: UInt32, bytes: Int) {
            for offset in 0..<bytes { data.append(UInt8(truncatingIfNeeded: value >> (offset * 8))) }
        }
        var offsets: [UInt32] = []
        for name in names {
            offsets.append(UInt32(data.count))
            append(0x0403_4B50, bytes: 4)
            append(20, bytes: 2)
            for _ in 0..<4 { append(0, bytes: 2) }
            for _ in 0..<3 { append(0, bytes: 4) }
            append(UInt32(name.utf8.count), bytes: 2)
            append(0, bytes: 2)
            data.append(contentsOf: name.utf8)
        }
        let centralOffset = UInt32(data.count)
        for (index, name) in names.enumerated() {
            append(0x0201_4B50, bytes: 4)
            append(0x0314, bytes: 2)
            append(20, bytes: 2)
            for _ in 0..<4 { append(0, bytes: 2) }
            for _ in 0..<3 { append(0, bytes: 4) }
            append(UInt32(name.utf8.count), bytes: 2)
            for _ in 0..<4 { append(0, bytes: 2) }
            append(mode << 16, bytes: 4)
            append(offsets[index], bytes: 4)
            data.append(contentsOf: name.utf8)
        }
        let size = UInt32(data.count) - centralOffset
        append(0x0605_4B50, bytes: 4)
        append(0, bytes: 4)
        append(UInt32(names.count), bytes: 2)
        append(UInt32(names.count), bytes: 2)
        append(size, bytes: 4)
        append(centralOffset, bytes: 4)
        append(0, bytes: 2)
        return data
    }
}
