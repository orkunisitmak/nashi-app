import XCTest
@testable import NashiCore

/// `gzip empty.txt` is a valid stream and restores an empty empty.txt.
final class GzipEmptyMemberTests: XCTestCase {
    func testGzipOfEmptyFileRestoresEmptyFile() throws {
        let fm = FileManager.default
        let dir = fm.temporaryDirectory.appendingPathComponent(UUID().uuidString, isDirectory: true)
        try fm.createDirectory(at: dir, withIntermediateDirectories: true)
        defer { try? fm.removeItem(at: dir) }
        let plain = dir.appendingPathComponent("plain")
        try Data().write(to: plain)
        let result = try ProcessRunner.run(URL(fileURLWithPath: "/usr/bin/gzip"), ["-c", "-n", plain.path])
        let input = dir.appendingPathComponent("empty.txt.gz")
        try result.output.write(to: input)
        let root = dir.appendingPathComponent("out")
        try ArchiveCodec.extract(input, into: root, token: .init())
        XCTAssertEqual(try fm.contentsOfDirectory(atPath: root.path), ["empty.txt"])
        XCTAssertEqual(try Data(contentsOf: root.appendingPathComponent("empty.txt")).count, 0)

        let source = dir.appendingPathComponent("source", isDirectory: true)
        try fm.createDirectory(at: source, withIntermediateDirectories: true)
        let tgz = dir.appendingPathComponent("nothing.tar.gz")
        try ArchiveCodec.create(from: source, at: tgz, format: "gz", token: .init())
        let tgzRoot = dir.appendingPathComponent("tgz")
        try ArchiveCodec.extract(tgz, into: tgzRoot, token: .init())
        XCTAssertEqual(try fm.contentsOfDirectory(atPath: tgzRoot.path), [])
    }
}
