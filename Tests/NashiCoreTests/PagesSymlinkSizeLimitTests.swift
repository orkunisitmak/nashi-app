import XCTest
@testable import NashiCore
/// The 200 MB Pages limit used to be checked against the size of a symlink itself (a few bytes),
/// so a link to an oversized Pages file was read into memory in full instead of being refused.
final class PagesSymlinkSizeLimitTests: XCTestCase {
    func testSizeLimitAppliesToTheFileASymlinkPointsTo() throws {
        let folder = try OutputFiles.temporaryDirectory(); defer { try? FileManager.default.removeItem(at: folder) }
        let documents = folder.appendingPathComponent("documents"), links = folder.appendingPathComponent("links"), word = folder.appendingPathComponent("package/word")
        for directory in [documents, links, word] { try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true) }
        let big = documents.appendingPathComponent("Big.pages"); FileManager.default.createFile(atPath: big.path, contents: nil)
        let handle = try FileHandle(forWritingTo: big); try handle.truncate(atOffset: 210_000_000); try handle.close()
        try "<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:body><w:p><w:r><w:t>Hello</w:t></w:r></w:p></w:body></w:document>".write(to: word.appendingPathComponent("document.xml"), atomically: true, encoding: .utf8)
        let docx = folder.appendingPathComponent("Small.docx"), small = documents.appendingPathComponent("Small.pages")
        try ArchiveCodec.create(from: folder.appendingPathComponent("package"), at: docx, format: "zip", token: .init())
        try PagesCodec.create(fromDOCX: docx, at: small, token: .init())
        let bigLink = links.appendingPathComponent("Big.pages"), smallLink = links.appendingPathComponent("Small.pages")
        try FileManager.default.createSymbolicLink(atPath: bigLink.path, withDestinationPath: "../documents/Big.pages")
        try FileManager.default.createSymbolicLink(atPath: smallLink.path, withDestinationPath: "../documents/Small.pages")
        XCTAssertThrowsError(try PagesCodec.model(bigLink, token: .init())) {
            XCTAssertEqual($0.localizedDescription, "This Pages file is too large or is not a regular document file.")
        }
        XCTAssertNotNil(try PagesCodec.model(smallLink, token: .init())["blocks"] as? [[String: Any]])
        XCTAssertEqual(try FileManager.default.destinationOfSymbolicLink(atPath: bigLink.path), "../documents/Big.pages")
    }
}
