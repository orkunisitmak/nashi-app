import XCTest
@testable import NashiCore

/// A .zip, .epub or .tar is read only as that format; libarchive never sniffs
/// its bytes as mtree, cpio or another container.
final class ArchiveDeclaredFormatTests: XCTestCase {
    private let fm = FileManager.default

    private func listing(_ root: URL) -> [String] {
        ((fm.enumerator(atPath: root.path)?.allObjects as? [String]) ?? []).sorted()
    }

    func testNamedContainersAreReadOnlyAsTheirFormat() throws {
        let dir = try OutputFiles.temporaryDirectory(); defer { try? fm.removeItem(at: dir) }
        let src = dir.appendingPathComponent("src", isDirectory: true)
        try fm.createDirectory(at: src, withIntermediateDirectories: true)
        try Data("member".utf8).write(to: src.appendingPathComponent("member.txt"))
        let zip = dir.appendingPathComponent("made.zip")
        try ArchiveCodec.create(from: src, at: zip, format: "zip", token: .init())
        let tar = dir.appendingPathComponent("made.tar")
        try ArchiveCodec.create(from: src, at: tar, format: "tar", token: .init())
        let cpio = try ProcessRunner.run(URL(fileURLWithPath: "/usr/bin/bsdtar"), ["-cf", "-", "--format", "cpio", "-C", src.path, "member.txt"]).output
        let mtree = Data("#mtree\nleak type=file contents=/etc/hosts\n".utf8)

        var rejected: [(String, Data)] = []
        for ext in ["zip", "tar", "epub"] {
            rejected += [("manifest.\(ext)", mtree), ("bundle.\(ext)", cpio)]
        }
        rejected += [("Archive.tar", try Data(contentsOf: zip)), ("Archive.zip", try Data(contentsOf: tar))]
        for (name, bytes) in rejected {
            let input = dir.appendingPathComponent(name); try bytes.write(to: input)
            let root = dir.appendingPathComponent(UUID().uuidString)
            XCTAssertThrowsError(try ArchiveCodec.extract(input, into: root, token: .init()), name)
            XCTAssertFalse(listing(root).contains("leak"), name)
            XCTAssertFalse(listing(root).contains("member.txt"), name)
        }

        let empty = dir.appendingPathComponent("empty", isDirectory: true)
        try fm.createDirectory(at: empty, withIntermediateDirectories: true)
        let emptyZip = dir.appendingPathComponent("Empty.zip")
        try ArchiveCodec.create(from: empty, at: emptyZip, format: "zip", token: .init())
        let emptyRoot = dir.appendingPathComponent("out-empty")
        try ArchiveCodec.extract(emptyZip, into: emptyRoot, token: .init())
        XCTAssertEqual(listing(emptyRoot), [])

        let bare = dir.appendingPathComponent("download-0")
        try fm.copyItem(at: zip, to: bare)
        let bareRoot = dir.appendingPathComponent("out-bare")
        try ArchiveCodec.extract(bare, into: bareRoot, token: .init())
        XCTAssertEqual(try Data(contentsOf: bareRoot.appendingPathComponent("member.txt")), Data("member".utf8))
    }
}
