import Foundation
import UltraGearInputCore

struct SettingsStore {
    let fileURL: URL

    static let standard = SettingsStore(
        fileURL: URL.applicationSupportDirectory
            .appending(path: "UltraGear Input", directoryHint: .isDirectory)
            .appending(path: "settings.json", directoryHint: .notDirectory)
    )

    func load() -> Result<Settings, SettingsError> {
        guard let data = try? Data(contentsOf: fileURL) else { return .success(Settings()) }
        do throws(SettingsError) {
            return .success(try Settings.decode(data))
        } catch {
            return .failure(error)
        }
    }

    func save(_ settings: Settings) throws {
        try FileManager.default.createDirectory(
            at: fileURL.deletingLastPathComponent(),
            withIntermediateDirectories: true
        )
        try settings.encoded().write(to: fileURL, options: .atomic)
    }

    func ensureFileExists() throws {
        guard !FileManager.default.fileExists(atPath: fileURL.path) else { return }
        try save(Settings(inputs: InputSource.defaults))
    }
}
