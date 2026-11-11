import CryptoKit
import Foundation

private final class UpdateRedirectPolicy: NSObject, URLSessionTaskDelegate, Sendable {
    func urlSession(
        _ session: URLSession, task: URLSessionTask, willPerformHTTPRedirection response: HTTPURLResponse,
        newRequest request: URLRequest, completionHandler: @escaping @Sendable (URLRequest?) -> Void
    ) {
        completionHandler(request.url.map(UpdateClient.trusted) == true ? request : nil)
    }
}

public struct UpdateClient: Sendable {
    public init() {}

    public func latest() async throws -> UpdateRelease {
        try UpdateRelease(data: await fetch(UpdateRelease.latestURL, maximum: 1024 * 1024))
    }

    public func check(currentVersion: String) async throws -> UpdateRelease? {
        let data = try await fetch(UpdateRelease.latestURL, maximum: 1024 * 1024)
        do {
            guard let head = try JSONSerialization.jsonObject(with: data) as? [String: Any],
                let tag = head["tag_name"] as? String, head["draft"] as? Bool == false, head["prerelease"] as? Bool == false
            else { throw UpdateFailure.release }
            let version = try ReleaseVersion(tag)
            guard tag == "v\(version)" else { throw UpdateFailure.release }
            return version > (try ReleaseVersion(currentVersion)) ? try UpdateRelease(data: data) : nil
        } catch {
            throw UpdateFailure.release
        }
    }

    public func download(_ release: UpdateRelease, to file: URL) async throws {
        let checksum = try await fetch(release.checksum.url, maximum: 1024)
        let expected = try Self.parseChecksum(String(decoding: checksum, as: UTF8.self), name: release.package.name)
        let data = try await fetch(release.package.url, maximum: release.package.size)
        guard data.count == release.package.size, Self.digest(data) == expected else { throw UpdateFailure.checksum }
        try data.write(to: file, options: .withoutOverwriting)
    }

    public static func parseChecksum(_ text: String, name: String) throws -> String {
        let parts = text.split(whereSeparator: { $0.isWhitespace })
        guard parts.count == 2, parts[0].count == 64,
            parts[0].utf8.allSatisfy({ (48...57).contains($0) || (65...70).contains($0) || (97...102).contains($0) }),
            parts[1] == name || parts[1] == "*" + name
        else { throw UpdateFailure.checksum }
        return parts[0].lowercased()
    }

    public static func digest(_ data: Data) -> String {
        SHA256.hash(data: data).map { String(format: "%02x", $0) }.joined()
    }

    public static func trusted(_ url: URL) -> Bool {
        url.scheme == "https" && (url.port == nil || url.port == 443) && url.user == nil && url.password == nil
            && ["api.github.com", "github.com", "release-assets.githubusercontent.com", "objects.githubusercontent.com"].contains(
                url.host ?? "")
    }

    private func fetch(_ url: URL, maximum: Int) async throws -> Data {
        guard Self.trusted(url) else { throw UpdateFailure.release }
        var request = URLRequest(url: url, cachePolicy: .reloadIgnoringLocalCacheData, timeoutInterval: 120)
        request.setValue("UltraGearInput-Updater/1.0", forHTTPHeaderField: "User-Agent")
        request.setValue(
            url.host == "api.github.com" ? "application/vnd.github+json" : "application/octet-stream", forHTTPHeaderField: "Accept")
        let (bytes, response) = try await URLSession.shared.bytes(for: request, delegate: UpdateRedirectPolicy())
        guard let response = response as? HTTPURLResponse, response.statusCode == 200 else { throw UpdateFailure.network }
        guard response.expectedContentLength <= maximum else { throw UpdateFailure.package }
        var data = Data()
        for try await byte in bytes {
            guard data.count < maximum else { throw UpdateFailure.package }
            data.append(byte)
        }
        try Task.checkCancellation()
        return data
    }
}
