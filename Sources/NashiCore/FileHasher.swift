import CryptoKit
import Foundation

/// Computes content checksums for files without loading them into memory.
///
/// The app ships no checksum tool today, so users fall back to `shasum` or
/// `md5` in a terminal. `nashi-cli hash` calls this type instead and stays on
/// the same Foundation/CryptoKit stack as the rest of the package.
public enum FileHasher {
    /// Digest algorithms the command line accepts. Values match the usual
    /// `shasum`/`md5` names so a checksum file written elsewhere keeps working.
    public enum Algorithm: String, CaseIterable {
        case sha256
        case sha512
        case md5
    }

    /// Returns the lowercase hexadecimal digest of `url` for `algorithm`.
    ///
    /// The file is read in 1 MiB chunks, so hashing a multi-gigabyte movie
    /// costs the same memory as hashing a text file.
    ///
    /// - Parameters:
    ///   - url: A local file URL. Directories and missing paths throw
    ///     ``NashiError/invalidInput(_:)``.
    ///   - algorithm: Digest to compute; defaults to SHA-256.
    /// - Returns: Lowercase hexadecimal digest without separators.
    public static func digest(of url: URL, algorithm: Algorithm = .sha256) throws -> String {
        switch algorithm {
        case .sha256: return try hexadecimal(stream(url, into: SHA256()))
        case .sha512: return try hexadecimal(stream(url, into: SHA512()))
        case .md5: return try hexadecimal(stream(url, into: Insecure.MD5()))
        }
    }

    /// One line of a `shasum`-style checksum file.
    public enum ChecksumLine: Equatable {
        /// Blank lines and `#` comments carry no checksum.
        case ignored
        /// A checksum entry: lowercase digest and the path exactly as written.
        case entry(digest: String, path: String)
        /// Text that is neither an entry nor a comment. A verify run counts it
        /// as a failure instead of skipping it.
        case damaged(String)
    }

    /// Number of hexadecimal characters each algorithm produces.
    public static func digestLength(_ algorithm: Algorithm) -> Int {
        switch algorithm {
        case .md5: return 32
        case .sha256: return 64
        case .sha512: return 128
        }
    }

    /// The algorithm a digest of `hexCharacters` belongs to, if any.
    public static func algorithm(forDigestLength hexCharacters: Int) -> Algorithm? {
        Algorithm.allCases.first { digestLength($0) == hexCharacters }
    }

    /// Parses one line of a `shasum`-style checksum file.
    ///
    /// Accepted forms are `<hex>   <path>` (text mode) and `<hex> *<path>`
    /// (binary mode). The path is kept exactly as written, so leading or
    /// trailing spaces and a name that really starts with `*` survive. Only a
    /// `*` that follows the single-space binary separator is treated as a mode
    /// marker.
    public static func parseChecksumLine(_ line: String) -> ChecksumLine {
        if line.allSatisfy({ $0 == " " || $0 == "\t" }) { return .ignored }
        // Only a comment at the start of the line is a comment; a name may contain '#'.
        if line.hasPrefix("#") { return .ignored }
        guard let separator = line.firstIndex(of: " "),
              let name = name(after: line.index(after: separator), in: line) else {
            return .damaged(line)
        }
        let digest = String(line[line.startIndex..<separator])
        guard digest.count >= 32, digest.allSatisfy(\.isHexDigit), !name.isEmpty else { return .damaged(line) }
        return .entry(digest: digest.lowercased(), path: name)
    }

    /// Applies the one-character mode marker that follows the first space.
    private static func name(after index: String.Index, in line: String) -> String? {
        guard index < line.endIndex else { return nil }
        if line[index] == "*" { return String(line[line.index(after: index)...]) }
        guard line[index] == " " else { return nil }
        let start = line.index(after: index)
        return start < line.endIndex ? String(line[start...]) : nil
    }

    private static func stream<H: HashFunction>(_ url: URL, into hasher: H) throws -> H.Digest {
        var isDirectory: ObjCBool = false
        guard FileManager.default.fileExists(atPath: url.path, isDirectory: &isDirectory), !isDirectory.boolValue else {
            throw NashiError.invalidInput("File not found: \(url.lastPathComponent)")
        }
        let handle = try FileHandle(forReadingFrom: url)
        defer { try? handle.close() }
        var hasher = hasher
        while let chunk = try handle.read(upToCount: 1 << 20), !chunk.isEmpty {
            hasher.update(data: chunk)
        }
        return hasher.finalize()
    }

    private static func hexadecimal<D: Sequence>(_ digest: D) -> String where D.Element == UInt8 {
        digest.map { String(format: "%02x", $0) }.joined()
    }
}
