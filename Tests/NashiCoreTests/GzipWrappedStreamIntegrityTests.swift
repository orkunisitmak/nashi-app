import XCTest
@testable import NashiCore

/// A gzip member can wrap a bzip2 stream. The inner stream's CRCs and the
/// expansion limit must be honoured as they are for a bare .tar.bz2.
final class GzipWrappedStreamIntegrityTests: XCTestCase {
    private let fm = FileManager.default

    private func header(_ name: String, size: Int) -> Data {
        var h = [UInt8](repeating: 0, count: 512)
        func put(_ s: String, _ at: Int) { for (i, b) in s.utf8.enumerated() { h[at + i] = b } }
        put(name, 0); put("0000644\0", 100); put(String(format: "%011o\0", size), 124); put("14524770400\0", 136)
        put("        ", 148); put("0", 156); put("ustar\0", 257); put("00", 263)
        put(String(format: "%06o\0 ", h.reduce(0) { $0 + Int($1) }), 148)
        return Data(h)
    }

    private func compress(_ tool: String, _ data: Data, in dir: URL) throws -> Data {
        let file = dir.appendingPathComponent(UUID().uuidString)
        try data.write(to: file)
        return try ProcessRunner.run(URL(fileURLWithPath: "/usr/bin/\(tool)"), ["-c", file.path]).output
    }

    private func extract(_ data: Data, named name: String, in dir: URL, limit: Int64 = 20 * 1024 * 1024 * 1024) throws -> URL? {
        let caseDir = dir.appendingPathComponent(UUID().uuidString, isDirectory: true)
        try fm.createDirectory(at: caseDir, withIntermediateDirectories: true)
        let input = caseDir.appendingPathComponent(name)
        try data.write(to: input)
        let root = caseDir.appendingPathComponent("out")
        do { try ArchiveCodec.extract(input, into: root, token: .init(), limits: .init(maximumEntries: 100_000, maximumBytes: limit)) } catch {
            XCTAssertFalse(fm.fileExists(atPath: root.path), "\(name): a rejected stream must not leave output")
            return nil
        }
        return root
    }

    func testGzipWrappedBzip2IsCheckedAndCounted() throws {
        let dir = fm.temporaryDirectory.appendingPathComponent(UUID().uuidString, isDirectory: true)
        try fm.createDirectory(at: dir, withIntermediateDirectories: true)
        defer { try? fm.removeItem(at: dir) }
        let text = Data("visible\n".utf8)
        // The damage and the skipped "." entry both sit where the tar reader never looks.
        let skipped = 4 * 1024 * 1024
        let tar = header(".", size: skipped) + Data(count: skipped) + header("a.txt", size: text.count) + text + Data(count: 504 + 1024)
        let bz = try compress("bzip2", tar, in: dir)
        var damaged = bz; damaged[bz.count - 2] ^= 0x01
        for name in ["bundle.tgz", "download-0"] {
            let root = try XCTUnwrap(try extract(try compress("gzip", bz, in: dir), named: name, in: dir), name)
            XCTAssertEqual(try Data(contentsOf: root.appendingPathComponent("a.txt")), text, name)
            XCTAssertNil(try extract(try compress("gzip", damaged, in: dir), named: name, in: dir), "\(name): damaged inner stream was accepted")
            XCTAssertNil(try extract(try compress("gzip", bz.prefix(bz.count - 4), in: dir), named: name, in: dir), "\(name): truncated inner stream was accepted")
            XCTAssertNil(try extract(try compress("gzip", bz, in: dir), named: name, in: dir, limit: 1024 * 1024), "\(name): skipped data escaped the expansion limit")
        }
    }
}
