import Foundation

/// A Services request arrives over the pasteboard from another application, so
/// the payload is untrusted. Keep only existing local files, drop duplicates,
/// normalise the path and cap the batch before the app starts any work.
public enum ServiceInput {
    /// Most files one Services request may deliver. A larger pasteboard
    /// payload is truncated so a single menu action cannot start unbounded work.
    public static let maximumItems = 200
    /// Longest accepted path in UTF-8 bytes. Longer values are rejected before
    /// they reach `URL`/`FileManager`, which keeps malformed payloads cheap.
    public static let maximumPathBytes = 4096

    /// Validates the paths a Services request carries.
    ///
    /// The pasteboard payload is cross-application data, so every candidate is
    /// kept only if it is a non-empty local path, fits ``maximumPathBytes``,
    /// contains no NUL byte and still exists on disk. Duplicates are removed by
    /// their standardised path and the result is capped at ``maximumItems``.
    ///
    /// - Parameters:
    ///   - paths: Raw path strings from the pasteboard.
    ///   - fileExists: Existence probe, injectable for tests. Defaults to
    ///     `FileManager.default.fileExists(atPath:)`.
    /// - Returns: Standardised file URLs in input order, each appearing once.
    public static func files(from paths: [String],
                             fileExists: (String) -> Bool = { FileManager.default.fileExists(atPath: $0) }) -> [URL] {
        var seen = Set<String>()
        var result: [URL] = []
        for path in paths {
            guard result.count < maximumItems else { break }
            guard !path.isEmpty, path.utf8.count <= maximumPathBytes, !path.contains("\0") else { continue }
            let url = URL(fileURLWithPath: path).standardizedFileURL
            guard url.isFileURL, !url.path.isEmpty else { continue }
            guard fileExists(url.path) else { continue }
            guard seen.insert(url.path).inserted else { continue }
            result.append(url)
        }
        return result
    }
}
