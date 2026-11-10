import Foundation

/// A Services request arrives over the pasteboard from another application, so
/// the payload is untrusted. Keep only existing local files, drop duplicates,
/// normalise the path and bound the batch before the app starts any work.
public enum ServiceInput {
    /// Most files one Services request may open. A larger selection is refused
    /// with an explanation instead of being truncated, so nothing disappears
    /// quietly.
    public static let maximumItems = 200

    /// Most pasted paths examined for one request. Invalid and duplicate paths
    /// do not count toward ``maximumItems``, so this keeps a hostile or damaged
    /// pasteboard payload from turning into an unbounded series of stat calls.
    public static let maximumCandidates = 1_000

    /// Longest accepted path in UTF-8 bytes. Longer values are rejected before
    /// they reach `URL`/`FileManager`, which keeps malformed payloads cheap.
    public static let maximumPathBytes = 4096

    /// Shown when a request carries more than ``maximumItems`` files or more
    /// than ``maximumCandidates`` paths.
    public static let tooManyFilesMessage = "Choose at most 200 files at a time."

    /// Validates the paths a Services request carries.
    ///
    /// The pasteboard payload is cross-application data, so every candidate is
    /// kept only if it is a non-empty local path, fits ``maximumPathBytes``,
    /// contains no NUL byte and still exists as a file on disk. Directories are
    /// dropped because they open an empty wheel. Duplicates are removed by
    /// their standardised path.
    ///
    /// - Parameters:
    ///   - paths: Raw path strings from the pasteboard.
    ///   - fileExists: Existence probe, injectable for tests. Defaults to
    ///     `FileManager.default`, which also rejects directories.
    /// - Returns: Standardised file URLs in input order, each appearing once.
    /// - Throws: ``NashiError/invalidInput(_:)`` when the request carries more
    ///   than ``maximumItems`` files or more than ``maximumCandidates`` paths.
    public static func files(from paths: [String],
                             fileExists: (String) -> Bool = { path in
                                 var isDirectory: ObjCBool = false
                                 guard FileManager.default.fileExists(atPath: path, isDirectory: &isDirectory) else { return false }
                                 return !isDirectory.boolValue
                             }) throws -> [URL] {
        guard paths.count <= maximumCandidates else { throw NashiError.invalidInput(tooManyFilesMessage) }
        var seen = Set<String>()
        var result: [URL] = []
        for path in paths {
            guard !path.isEmpty, path.utf8.count <= maximumPathBytes, !path.contains("\0") else { continue }
            let url = URL(fileURLWithPath: path).standardizedFileURL
            guard url.isFileURL, !url.path.isEmpty else { continue }
            guard fileExists(url.path) else { continue }
            guard seen.insert(url.path).inserted else { continue }
            result.append(url)
            guard result.count <= maximumItems else { throw NashiError.invalidInput(tooManyFilesMessage) }
        }
        return result
    }
}
