import XCTest
import Foundation
@testable import NashiCore

/// A Word manual page break (Ctrl+Enter, `<w:br w:type="page"/>`) must start
/// a new page in the Pages document, not become a soft line break.
final class PagesDOCXPageBreakTests: XCTestCase {
    private var root: URL!

    override func setUpWithError() throws { root = try OutputFiles.temporaryDirectory() }
    override func tearDownWithError() throws { if let root { try? FileManager.default.removeItem(at: root) } }

    // F1781
    func testManualPageBreakStartsTheFollowingTextOnANewPage() throws {
        let paragraphs = try convert(name: "Inline break", body: "<w:p><w:r><w:t>Chapter one</w:t><w:br w:type=\"page\"/><w:t>Chapter two</w:t></w:r></w:p>")
        XCTAssertEqual(paragraphs.map(\.text), ["Chapter one", "Chapter two"])
        XCTAssertEqual(paragraphs.map(\.pageBreakBefore), [false, true])
    }

    func testPageBreakInItsOwnParagraphMovesTheNextParagraph() throws {
        let body = "<w:p><w:r><w:t>Cover</w:t></w:r></w:p><w:p><w:r><w:br w:type=\"page\"/></w:r></w:p><w:p><w:r><w:t>Contents</w:t></w:r></w:p>"
        let paragraphs = try convert(name: "Own paragraph", body: body)
        // Blank paragraph count is the writer's business (see open PR #253);
        // what matters is that a page starts between the cover and contents.
        XCTAssertEqual(paragraphs.map(\.text).filter { !$0.isEmpty }, ["Cover", "Contents"])
        let cover = try XCTUnwrap(paragraphs.firstIndex { $0.text == "Cover" })
        let contents = try XCTUnwrap(paragraphs.firstIndex { $0.text == "Contents" })
        XCTAssertEqual(paragraphs.indices.filter { paragraphs[$0].pageBreakBefore }.count, 1)
        let newPage = try XCTUnwrap(paragraphs.firstIndex { $0.pageBreakBefore })
        XCTAssertTrue(newPage > cover && newPage <= contents, "page break at \(newPage), cover \(cover), contents \(contents)")
    }

    func testLineBreaksAndRenderedPageBreaksStayInTheParagraph() throws {
        let body = "<w:p><w:r><w:t>One</w:t><w:br/><w:t>Two</w:t><w:br w:type=\"textWrapping\"/><w:t>Three</w:t><w:lastRenderedPageBreak/><w:t>Four</w:t></w:r></w:p>"
        let paragraphs = try convert(name: "Line breaks", body: body)
        XCTAssertEqual(paragraphs.map(\.text), ["One\u{2028}Two\u{2028}ThreeFour"])
        XCTAssertEqual(paragraphs.map(\.pageBreakBefore), [false])
    }

    func testPageBreakBeforePropertyIsStillKept() throws {
        let paragraphs = try convert(name: "Property", body: "<w:p><w:r><w:t>First</w:t></w:r></w:p><w:p><w:pPr><w:pageBreakBefore/></w:pPr><w:r><w:t>Second</w:t></w:r></w:p>")
        XCTAssertEqual(paragraphs.map(\.text), ["First", "Second"])
        XCTAssertEqual(paragraphs.map(\.pageBreakBefore), [false, true])
    }

    private func convert(name: String, body: String) throws -> [(text: String, pageBreakBefore: Bool)] {
        let package = root.appendingPathComponent(name + " package", isDirectory: true)
        let word = package.appendingPathComponent("word", isDirectory: true)
        try FileManager.default.createDirectory(at: word, withIntermediateDirectories: true)
        try "<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:body>\(body)</w:body></w:document>".write(to: word.appendingPathComponent("document.xml"), atomically: true, encoding: .utf8)
        let input = root.appendingPathComponent(name).appendingPathExtension("docx")
        try ArchiveCodec.create(from: package, at: input, format: "zip", token: .init())
        let output = root.appendingPathComponent(name).appendingPathExtension("pages")
        try PagesCodec.create(fromDOCX: input, at: output, token: .init())
        let blocks = try XCTUnwrap(PagesCodec.model(output, token: .init())["blocks"] as? [[String: Any]])
        return blocks.filter { $0["type"] as? String == "paragraph" }.map { block in
            let text = (block["runs"] as? [[String: Any]] ?? []).compactMap { $0["text"] as? String }.joined()
            return (text, (block["paragraph"] as? [String: Any])?["pageBreakBefore"] as? Bool ?? false)
        }
    }
}
