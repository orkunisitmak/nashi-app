import Foundation

/// A Services request arrives over the pasteboard from another application, so
/// the payload is untrusted. Keep only existing local files, drop duplicates,
/// normalise the path and cap the batch before the app starts any work.
public enum ServiceInput {
    public static let maximumItems = 200
    public static let maximumPathBytes = 4096

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
