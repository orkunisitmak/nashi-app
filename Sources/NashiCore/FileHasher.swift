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

    /// Parses one line of a `shasum`-style checksum file.
    ///
    /// Accepted forms are `<hex>  <path>` and `<hex> *<path>`, with the path
    /// kept exactly as written so spaces in file names survive. Comment and
    /// blank lines return `nil`.
    public static func parseChecksumLine(_ line: String) -> (digest: String, path: String)? {
        let trimmed = line.trimmingCharacters(in: .whitespaces)
        guard !trimmed.isEmpty, !trimmed.hasPrefix("#") else { return nil }
        let fields = trimmed.split(separator: " ", maxSplits: 1, omittingEmptySubsequences: true)
        guard fields.count == 2 else { return nil }
        let digest = String(fields[0])
        guard digest.count >= 32, digest.allSatisfy(\.isHexDigit) else { return nil }
        var path = String(fields[1]).trimmingCharacters(in: .whitespacesAndNewlines)
        if path.hasPrefix("*") { path.removeFirst() }
        return path.isEmpty ? nil : (digest.lowercased(), path)
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
