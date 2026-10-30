import XCTest
@testable import NashiCore

/// A single-file .gz restores that one file; its payload is never reparsed as
/// a ZIP, mtree manifest or any other non-tar archive format.
final class GzipPayloadFormatTests: XCTestCase {
    private func gzip(_ data: Data, name: String, in dir: URL) throws -> URL {
        let plain = dir.appendingPathComponent("plain-\(UUID().uuidString)")
        try data.write(to: plain)
        let result = try ProcessRunner.run(URL(fileURLWithPath: "/usr/bin/gzip"), ["-c", "-n", plain.path])
        try FileManager.default.removeItem(at: plain)
        let out = dir.appendingPathComponent(name)
        try result.output.write(to: out)
        return out
    }
    private func listing(_ root: URL) -> [String] {
        ((FileManager.default.enumerator(atPath: root.path)?.allObjects as? [String]) ?? []).sorted()
    }

    func testSingleFileGzipPayloadIsNotSniffedAsAnotherArchive() throws {
        let fm = FileManager.default
        let dir = fm.temporaryDirectory.appendingPathComponent(UUID().uuidString, isDirectory: true)
        try fm.createDirectory(at: dir, withIntermediateDirectories: true)
        defer { try? fm.removeItem(at: dir) }
        let src = dir.appendingPathComponent("src", isDirectory: true)
        try fm.createDirectory(at: src, withIntermediateDirectories: true)
        try Data("member".utf8).write(to: src.appendingPathComponent("member.txt"))
        let zip = dir.appendingPathComponent("doc.zip")
        try ArchiveCodec.create(from: src, at: zip, format: "zip", token: .init())
        let zipBytes = try Data(contentsOf: zip)
        let cases: [(String, Data, String)] = [
            ("Report.docx.gz", zipBytes, "Report.docx"),
            ("notes.txt.gz", Data("#mtree\nleak type=file contents=/etc/hosts\n".utf8), "notes.txt"),
            ("A.TXT.GZ", Data("hello\n".utf8), "A.TXT")
        ]
        for (name, payload, expected) in cases {
            let root = dir.appendingPathComponent(UUID().uuidString)
            try ArchiveCodec.extract(try gzip(payload, name: name, in: dir), into: root, token: .init())
            XCTAssertEqual(listing(root), [expected], name)
            XCTAssertEqual(try Data(contentsOf: root.appendingPathComponent(expected)), payload, name)
        }
        let tar = dir.appendingPathComponent("bundle.tar")
        try ArchiveCodec.create(from: src, at: tar, format: "tar", token: .init())
        for name in ["bundle.tar.gz", "bundle.gz"] {
            let root = dir.appendingPathComponent(UUID().uuidString)
            try ArchiveCodec.extract(try gzip(try Data(contentsOf: tar), name: name, in: dir), into: root, token: .init())
            XCTAssertEqual(listing(root), ["member.txt"], name)
        }
    }
}
