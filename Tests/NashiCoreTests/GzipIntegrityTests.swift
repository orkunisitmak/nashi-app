import XCTest
@testable import NashiCore

/// A gzip member's CRC32 and length trailer must be honoured, as `gzip -t` does.
final class GzipIntegrityTests: XCTestCase {
    private let fm = FileManager.default

    private func gzip(_ data: Data, in dir: URL) throws -> Data {
        let plain = dir.appendingPathComponent(UUID().uuidString)
        try data.write(to: plain)
        defer { try? fm.removeItem(at: plain) }
        return try ProcessRunner.run(URL(fileURLWithPath: "/usr/bin/gzip"), ["-c", "-n", plain.path]).output
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

    func testDamagedGzipMembersAreRejected() throws {
        let dir = fm.temporaryDirectory.appendingPathComponent(UUID().uuidString, isDirectory: true)
        try fm.createDirectory(at: dir, withIntermediateDirectories: true)
        defer { try? fm.removeItem(at: dir) }
        let original = Data((0..<40_000).map { (i: Int) -> UInt8 in UInt8(97 + (i * 7 + i / 13) % 26) })
        let raw = try gzip(original, in: dir)
        let src = dir.appendingPathComponent("src", isDirectory: true)
        try fm.createDirectory(at: src, withIntermediateDirectories: true)
        try original.write(to: src.appendingPathComponent("inner.bin"))
        let made = dir.appendingPathComponent("made.tar.gz")
        try ArchiveCodec.create(from: src, at: made, format: "gz", token: .init())
        let tar = try Data(contentsOf: made)

        for (base, name, file) in [(raw, "payload.bin.gz", "payload.bin"), (tar, "bundle.tar.gz", "inner.bin"), (tar, "bundle.tgz", "inner.bin")] {
            let root = try XCTUnwrap(try extract(base, named: name, in: dir), name)
            XCTAssertEqual(try Data(contentsOf: root.appendingPathComponent(file)), original, name)
            var crc = base; crc[crc.count - 8] ^= 0x01
            var size = base; size[size.count - 4] ^= 0x01
            var body = base; body[body.count / 2] ^= 0x10
            for damaged in [crc, size, body, base.prefix(base.count - 4)] {
                XCTAssertNil(try extract(damaged, named: name, in: dir), "\(name): damaged stream was accepted")
            }
        }

        let second = Data("second member\n".utf8)
        let rawSecond = try gzip(second, in: dir)
        let multi = try XCTUnwrap(try extract(raw + rawSecond, named: "multi.bin.gz", in: dir))
        XCTAssertEqual(try Data(contentsOf: multi.appendingPathComponent("multi.bin")), original + second)
        var firstBad = raw; firstBad[firstBad.count - 8] ^= 0x01
        XCTAssertNil(try extract(firstBad + rawSecond, named: "multi.bin.gz", in: dir), "a damaged earlier member was accepted")
    }
}
