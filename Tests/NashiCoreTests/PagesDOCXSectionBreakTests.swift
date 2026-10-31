import XCTest
import Foundation
@testable import NashiCore

/// A Word section break is a `<w:sectPr>` inside the last paragraph's
/// properties. Next/odd/even page breaks start the next section on a new
/// page; a continuous break does not.
final class PagesDOCXSectionBreakTests: XCTestCase {
    private var root: URL!

    override func setUpWithError() throws { root = try OutputFiles.temporaryDirectory() }
    override func tearDownWithError() throws { if let root { try? FileManager.default.removeItem(at: root) } }

    // F1800
    func testPageSectionBreaksStartTheNextSectionOnANewPage() throws {
        let body = paragraph("Part one", section: "") + paragraph("Part two", section: "<w:type w:val=\"continuous\"/>")
            + paragraph("Part three", section: "<w:type w:val=\"oddPage\"/>") + paragraph("Part four", section: "<w:type w:val=\"evenPage\"/>")
            + paragraph("Part five", section: "<w:type w:val=\"nextPage\"/>") + paragraph("Part six") + finalSection
        let blocks = try convert(name: "Sections", body: body).filter { !$0.text.isEmpty }
        XCTAssertEqual(blocks.map(\.text), ["Part one", "Part two", "Part three", "Part four", "Part five", "Part six"])
        XCTAssertEqual(blocks.map(\.pageBreakBefore), [false, true, false, true, true, true])
    }

    // F1800 edge cases: a column break is not a page break, an empty
    // section-ending paragraph still moves the next section, and a break
    // that ends the document adds nothing.
    func testColumnBreakEmptyEndingParagraphAndFinalBreak() throws {
        let empty = "<w:p><w:pPr><w:sectPr><w:pgSz w:w=\"11906\" w:h=\"16838\"/></w:sectPr></w:pPr></w:p>"
        let body = paragraph("Columns", section: "<w:type w:val=\"nextColumn\"/>") + paragraph("Same page") + empty
            + paragraph("New page", section: "") + finalSection
        let blocks = try convert(name: "Edges", body: body).filter { !$0.text.isEmpty }
        XCTAssertEqual(blocks.map(\.text), ["Columns", "Same page", "New page"])
        XCTAssertEqual(blocks.map(\.pageBreakBefore), [false, false, true])
    }

    // Control: a continuous section break and an ordinary final section keep the flow.
    func testContinuousSectionBreakKeepsTheFlow() throws {
        let body = paragraph("First", section: "<w:type w:val=\"continuous\"/>") + paragraph("Second") + finalSection
        let blocks = try convert(name: "Continuous", body: body).filter { !$0.text.isEmpty }
        XCTAssertEqual(blocks.map(\.text), ["First", "Second"])
        XCTAssertEqual(blocks.map(\.pageBreakBefore), [false, false])
    }

    private let finalSection = "<w:sectPr><w:pgSz w:w=\"11906\" w:h=\"16838\"/></w:sectPr>"

    private func paragraph(_ text: String, section: String? = nil) -> String {
        let properties = section.map { "<w:pPr><w:sectPr>\($0)<w:pgSz w:w=\"11906\" w:h=\"16838\"/></w:sectPr></w:pPr>" } ?? ""
        return "<w:p>\(properties)<w:r><w:t>\(text)</w:t></w:r></w:p>"
    }

    private func convert(name: String, body: String) throws -> [(text: String, pageBreakBefore: Bool, isTable: Bool)] {
        let package = root.appendingPathComponent(name + " package", isDirectory: true)
        let word = package.appendingPathComponent("word", isDirectory: true)
        try FileManager.default.createDirectory(at: word, withIntermediateDirectories: true)
        try "<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:body>\(body)</w:body></w:document>".write(to: word.appendingPathComponent("document.xml"), atomically: true, encoding: .utf8)
        let input = root.appendingPathComponent(name).appendingPathExtension("docx")
        try ArchiveCodec.create(from: package, at: input, format: "zip", token: .init())
        let output = root.appendingPathComponent(name).appendingPathExtension("pages")
        try PagesCodec.create(fromDOCX: input, at: output, token: .init())
        let blocks = try XCTUnwrap(PagesCodec.model(output, token: .init())["blocks"] as? [[String: Any]])
        return blocks.map { block in
            let text = (block["runs"] as? [[String: Any]] ?? []).compactMap { $0["text"] as? String }.joined()
            return (text, (block["paragraph"] as? [String: Any])?["pageBreakBefore"] as? Bool ?? false, block["type"] as? String == "table")
        }
    }
}
