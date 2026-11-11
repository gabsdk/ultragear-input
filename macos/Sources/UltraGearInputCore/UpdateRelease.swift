import Foundation

public enum UpdateFailure: String, Error, Sendable {
    case network
    case release
    case checksum
    case package
    case permission
    case installFailed
    case unsupported
}

public struct ReleaseVersion: Equatable, Comparable, Sendable, CustomStringConvertible {
    public let major: Int
    public let minor: Int
    public let patch: Int

    public init(_ text: String) throws {
        let value = text.hasPrefix("v") ? String(text.dropFirst()) : text
        let parts = value.split(separator: ".", omittingEmptySubsequences: false)
        guard parts.count == 3,
            let major = Int(parts[0]), let minor = Int(parts[1]), let patch = Int(parts[2]),
            major >= 0, minor >= 0, patch >= 0,
            String(major) == parts[0], String(minor) == parts[1], String(patch) == parts[2]
        else { throw UpdateFailure.release }
        self.major = major
        self.minor = minor
        self.patch = patch
    }

    public var description: String { "\(major).\(minor).\(patch)" }

    public static func < (left: Self, right: Self) -> Bool {
        if left.major != right.major { return left.major < right.major }
        if left.minor != right.minor { return left.minor < right.minor }
        return left.patch < right.patch
    }
}

public struct UpdateAsset: Sendable {
    public let name: String
    public let url: URL
    public let size: Int
}

public struct UpdateRelease: Sendable {
    public static let repository = "gabsdk/ultragear-input"
    public static let latestURL = URL(string: "https://api.github.com/repos/\(repository)/releases/latest")!
    public static let maximumSize = 256 * 1024 * 1024

    public let version: ReleaseVersion
    public let package: UpdateAsset
    public let checksum: UpdateAsset

    private struct Response: Decodable {
        struct Asset: Decodable {
            let name: String
            let browserDownloadUrl: String
            let size: Int
        }
        let tagName: String
        let draft: Bool
        let prerelease: Bool
        let assets: [Asset]
    }

    public init(data: Data) throws {
        do {
            let decoder = JSONDecoder()
            decoder.keyDecodingStrategy = .convertFromSnakeCase
            let response = try decoder.decode(Response.self, from: data)
            let version = try ReleaseVersion(response.tagName)
            guard !response.draft, !response.prerelease, response.tagName == "v\(version)" else { throw UpdateFailure.release }
            let name = "UltraGearInput-macos-\(version).zip"
            func asset(_ expectedName: String) throws -> UpdateAsset {
                let matches = response.assets.filter { $0.name == expectedName }
                let expectedURL = "https://github.com/\(Self.repository)/releases/download/\(response.tagName)/\(expectedName)"
                guard matches.count == 1, let item = matches.first, item.size > 0, item.size <= Self.maximumSize,
                    item.browserDownloadUrl == expectedURL, let url = URL(string: expectedURL)
                else { throw UpdateFailure.release }
                return UpdateAsset(name: expectedName, url: url, size: item.size)
            }
            self.version = version
            package = try asset(name)
            checksum = try asset(name + ".sha256")
        } catch {
            throw UpdateFailure.release
        }
    }
}

public enum UpdateText {
    public static func language(_ locale: String) -> String {
        let base = locale.replacingOccurrences(of: "_", with: "-").split(separator: "-").first?.lowercased()
        return base == "pt" || base == "es" ? base! : "en"
    }

    public static var currentLanguage: String {
        language(Locale.preferredLanguages.first ?? "en")
    }

    public static func get(_ key: String, language: String = currentLanguage, value: String? = nil) -> String {
        let text = catalogs[Self.language(language)]?[key] ?? catalogs["en"]?[key] ?? key
        return value.map { text.replacingOccurrences(of: "{0}", with: $0) } ?? text
    }

    public static var resourceBundleURL: URL { Bundle.module.bundleURL }

    private static let catalogs: [String: [String: String]] = {
        var result: [String: [String: String]] = [:]
        for language in ["en", "pt", "es"] {
            if let url = Bundle.module.url(forResource: language, withExtension: "json"),
                let data = try? Data(contentsOf: url),
                let catalog = try? JSONDecoder().decode([String: String].self, from: data)
            {
                result[language] = catalog
            }
        }
        return result
    }()
}
