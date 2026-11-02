import XCTest
@testable import NashiCore

/// libarchive's gzip filter ignores the CRC32 and length trailer, and only an
/// outermost gzip layer is checked before extraction. A gzip stream nested
/// inside another compression layer must therefore be refused, not extracted.
final class NestedGzipLayerTests: XCTestCase {
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
        return try ProcessRunner.run(URL(fileURLWithPath: "/usr/bin/\(tool)"), ["-c", "-f", file.path]).output
    }

    private func extract(_ data: Data, named name: String, in dir: URL) throws -> URL? {
        let caseDir = dir.appendingPathComponent(UUID().uuidString, isDirectory: true)
        try fm.createDirectory(at: caseDir, withIntermediateDirectories: true)
        let input = caseDir.appendingPathComponent(name)
        try data.write(to: input)
        let root = caseDir.appendingPathComponent("out")
        do { try ArchiveCodec.extract(input, into: root, token: .init()) } catch {
            XCTAssertFalse(fm.fileExists(atPath: root.path), "\(name): a rejected stream must not leave output")
            return nil
        }
        return root
    }

    func testGzipInsideAnotherCompressionLayerIsRefused() throws {
        let dir = fm.temporaryDirectory.appendingPathComponent(UUID().uuidString, isDirectory: true)
        try fm.createDirectory(at: dir, withIntermediateDirectories: true)
        defer { try? fm.removeItem(at: dir) }
        let text = Data(String(repeating: "visible\n", count: 64).utf8)
        let tar = header("a.txt", size: text.count) + text + Data(count: 512 - text.count % 512 + 1024)
        let gz = try compress("gzip", tar, in: dir)
        var crc = gz; crc[gz.count - 8] ^= 0x01
        var size = gz; size[gz.count - 4] ^= 0x01
        let wrappers: [(String, (Data) throws -> Data)] = [
            ("bzip2", { try self.compress("bzip2", $0, in: dir) }),
            ("compress", { try self.compress("compress", $0, in: dir) }),
            ("gzip", { try self.compress("gzip", $0, in: dir) }),
            ("gzip+bzip2", { try self.compress("gzip", try self.compress("bzip2", $0, in: dir), in: dir) }),
        ]
        for (label, wrap) in wrappers {
            for name in ["bundle.tgz", "bundle.tar", "download-0"] + (label.hasPrefix("gzip") ? ["bundle.gz"] : []) {
                for inner in [crc, size, gz] {
                    XCTAssertNil(try extract(try wrap(inner), named: name, in: dir), "\(label) around gzip as \(name) was extracted")
                }
            }
        }
        // gzip as the outermost layer, alone or around bzip2, still extracts.
        for data in [gz, try compress("gzip", try compress("bzip2", tar, in: dir), in: dir)] {
            for name in ["bundle.tgz", "bundle.tar", "download-0"] {
                let root = try XCTUnwrap(try extract(data, named: name, in: dir), name)
                XCTAssertEqual(try Data(contentsOf: root.appendingPathComponent("a.txt")), text, name)
            }
        }
        XCTAssertNil(try extract(crc, named: "bundle.tgz", in: dir), "damaged outer gzip was extracted")
    }
}
