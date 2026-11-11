import Darwin
import Foundation

public struct UpdatePlan: Codable, Sendable {
    public let target: String
    public let payload: String
    public let sha256: String
    public let parentId: Int32
    public let version: String
    public let restart: Bool

    public init(target: String, payload: String, sha256: String, parentId: Int32, version: String, restart: Bool = true) {
        self.target = target
        self.payload = payload
        self.sha256 = sha256
        self.parentId = parentId
        self.version = version
        self.restart = restart
    }
}

public enum UpdateInstaller {
    public static let applyFlag = "--apply-update"
    public static let updatedFlag = "--updated"
    public static let failedFlag = "--update-failed"
    public static let bundleIdentifier = "dev.gabsdk.ultragear-input"

    public static func prepare(_ release: UpdateRelease, target: URL) async throws {
        let files = FileManager.default
        guard target.pathExtension == "app", Bundle(url: target)?.bundleIdentifier == bundleIdentifier else {
            throw UpdateFailure.unsupported
        }
        guard files.isWritableFile(atPath: target.deletingLastPathComponent().path) else { throw UpdateFailure.permission }
        let stage = files.temporaryDirectory.appendingPathComponent("ultragear-update-\(UUID().uuidString)", isDirectory: true)
        try files.createDirectory(at: stage, withIntermediateDirectories: false, attributes: [.posixPermissions: 0o700])
        var handedOff = false
        defer { if !handedOff { try? files.removeItem(at: stage) } }
        let archive = stage.appendingPathComponent(release.package.name)
        try await UpdateClient().download(release, to: archive)
        try UpdateArchive.validate(Data(contentsOf: archive))
        let extracted = stage.appendingPathComponent("extracted", isDirectory: true)
        try files.createDirectory(at: extracted, withIntermediateDirectories: false)
        try run("/usr/bin/ditto", ["-x", "-k", archive.path, extracted.path])
        let payload = extracted.appendingPathComponent("UltraGear Input.app", isDirectory: true)
        try validateApp(payload, version: release.version.description)
        let helper = stage.appendingPathComponent("helper")
        try files.copyItem(at: target.appendingPathComponent("Contents/MacOS/UltraGearInput"), to: helper)
        try files.copyItem(
            at: UpdateText.resourceBundleURL, to: stage.appendingPathComponent(UpdateText.resourceBundleURL.lastPathComponent))
        let plan = UpdatePlan(
            target: target.path, payload: payload.path, sha256: try digest(payload),
            parentId: getpid(), version: release.version.description)
        let planFile = stage.appendingPathComponent("plan.json")
        try JSONEncoder().encode(plan).write(to: planFile, options: .withoutOverwriting)
        try Task.checkCancellation()
        let process = Process()
        process.executableURL = helper
        process.arguments = [applyFlag, planFile.path]
        process.currentDirectoryURL = stage
        try process.run()
        handedOff = true
    }

    public static func apply(_ planFile: URL) async -> Int32 {
        var plan: UpdatePlan?
        var validated = false
        do {
            let stage = planFile.deletingLastPathComponent().standardizedFileURL
            let helper = URL(fileURLWithPath: CommandLine.arguments[0]).standardizedFileURL
            guard planFile.lastPathComponent == "plan.json", helper.deletingLastPathComponent() == stage else {
                throw UpdateFailure.package
            }
            let decoded = try JSONDecoder().decode(UpdatePlan.self, from: Data(contentsOf: planFile))
            plan = decoded
            let target = URL(fileURLWithPath: decoded.target)
            let payload = stage.appendingPathComponent("extracted/UltraGear Input.app", isDirectory: true)
            guard decoded.payload == payload.path, target.pathExtension == "app", decoded.parentId > 0,
                decoded.target.hasPrefix("/"), try digest(payload) == decoded.sha256
            else { throw UpdateFailure.package }
            _ = try ReleaseVersion(decoded.version)
            try validateApp(payload, version: decoded.version)
            validated = true
            let deadline = ContinuousClock.now.advanced(by: .seconds(30))
            while kill(decoded.parentId, 0) == 0 {
                guard ContinuousClock.now < deadline else { throw UpdateFailure.installFailed }
                try await Task.sleep(for: .milliseconds(100))
            }
            try replace(target: target, payload: payload) {
                try verifyVersion(target, version: decoded.version)
                if decoded.restart { try launch(target, flag: updatedFlag, value: decoded.version) }
            }
            try? FileManager.default.removeItem(at: stage)
            return 0
        } catch {
            if validated, let plan, plan.restart {
                try? launch(URL(fileURLWithPath: plan.target), flag: failedFlag, value: UpdateFailure.installFailed.rawValue)
            }
            FileHandle.standardError.write(Data((UpdateText.get("installFailed") + "\n").utf8))
            return 1
        }
    }

    public static func replace(target: URL, payload: URL, launch: () throws -> Void = {}) throws {
        let files = FileManager.default
        let replacement = target.deletingLastPathComponent().appendingPathComponent(".ultragear-new-\(UUID().uuidString).app")
        let backup = target.deletingLastPathComponent().appendingPathComponent(".ultragear-previous-\(UUID().uuidString).app")
        defer { try? files.removeItem(at: replacement) }
        try files.copyItem(at: payload, to: replacement)
        try files.moveItem(at: target, to: backup)
        do {
            try files.moveItem(at: replacement, to: target)
            try launch()
        } catch {
            try? files.removeItem(at: target)
            try files.moveItem(at: backup, to: target)
            throw error
        }
        try? files.removeItem(at: backup)
    }

    public static func validateApp(_ app: URL, version: String) throws {
        let files = FileManager.default
        guard let bundle = Bundle(url: app), bundle.bundleIdentifier == bundleIdentifier,
            bundle.object(forInfoDictionaryKey: "CFBundleShortVersionString") as? String == version,
            files.isExecutableFile(atPath: app.appendingPathComponent("Contents/MacOS/UltraGearInput").path)
        else { throw UpdateFailure.package }
        let keys: [URLResourceKey] = [.isSymbolicLinkKey]
        guard let entries = files.enumerator(at: app, includingPropertiesForKeys: keys) else { throw UpdateFailure.package }
        for case let entry as URL in entries {
            guard try entry.resourceValues(forKeys: Set(keys)).isSymbolicLink != true else { throw UpdateFailure.package }
        }
        try run("/usr/bin/codesign", ["--verify", "--strict", app.path])
    }

    public static func digest(_ app: URL) throws -> String {
        try UpdateClient.digest(Data(contentsOf: app.appendingPathComponent("Contents/MacOS/UltraGearInput")))
    }

    private static func verifyVersion(_ app: URL, version: String) throws {
        let process = Process()
        process.executableURL = app.appendingPathComponent("Contents/MacOS/UltraGearInput")
        process.arguments = ["--version"]
        let output = Pipe()
        process.standardOutput = output
        process.standardError = FileHandle.nullDevice
        try process.run()
        let deadline = Date().addingTimeInterval(15)
        while process.isRunning && Date() < deadline { Thread.sleep(forTimeInterval: 0.05) }
        if process.isRunning { kill(process.processIdentifier, SIGKILL) }
        process.waitUntilExit()
        let text = String(decoding: output.fileHandleForReading.readDataToEndOfFile(), as: UTF8.self)
        guard process.terminationStatus == 0, text.trimmingCharacters(in: .whitespacesAndNewlines) == version else {
            throw UpdateFailure.package
        }
    }

    private static func launch(_ app: URL, flag: String, value: String) throws {
        let process = Process()
        process.executableURL = app.appendingPathComponent("Contents/MacOS/UltraGearInput")
        process.arguments = [flag, value]
        try process.run()
    }

    private static func run(_ executable: String, _ arguments: [String]) throws {
        let process = Process()
        process.executableURL = URL(fileURLWithPath: executable)
        process.arguments = arguments
        process.standardOutput = FileHandle.nullDevice
        process.standardError = FileHandle.nullDevice
        try process.run()
        process.waitUntilExit()
        guard process.terminationStatus == 0 else { throw UpdateFailure.package }
    }
}
