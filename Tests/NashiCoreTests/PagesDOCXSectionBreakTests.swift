import XCTest
import Foundation
@testable import NashiCore

/// A Word section break is a `<w:sectPr>` inside the paragraph that ends the
/// section. Its `w:type` says how that section itself started, so the text
/// after a break follows the next `sectPr`: next/odd/even page sections start
/// on a new page; continuous and next-column sections do not.
final class PagesDOCXSectionBreakTests: XCTestCase {
    private var root: URL!

    override func setUpWithError() throws { root = try OutputFiles.temporaryDirectory() }
    override func tearDownWithError() throws { if let root { try? FileManager.default.removeItem(at: root) } }

    // F1800
    func testEachSectionStartsAsItsOwnSectionPropertiesSay() throws {
        let body = paragraph("Part one", section: "") + paragraph("Part two", section: "<w:type w:val=\"continuous\"/>")
            + paragraph("Part three", section: "<w:type w:val=\"oddPage\"/>") + paragraph("Part four", section: "<w:type w:val=\"evenPage\"/>")
            + paragraph("Part five", section: "<w:type w:val=\"nextPage\"/>") + paragraph("Part six") + finalSection()
        let blocks = try convert(name: "Sections", body: body)
        XCTAssertEqual(blocks.map(\.text), ["Part one", "Part two", "Part three", "Part four", "Part five", "Part six"])
        // Part two is the continuous section; Part six is the last section,
        // whose body-level properties have no type and so mean next page.
        XCTAssertEqual(blocks.map(\.pageBreakBefore), [false, false, true, true, true, true])
    }

    func testContinuousSectionAfterADefaultSectionKeepsTheFlow() throws {
        let body = paragraph("First", section: "") + paragraph("Second", section: "<w:type w:val=\"continuous\"/>")
            + paragraph("Third") + finalSection("<w:type w:val=\"continuous\"/>")
        let blocks = try convert(name: "Continuous after default", body: body)
        XCTAssertEqual(blocks.map(\.text), ["First", "Second", "Third"])
        XCTAssertEqual(blocks.map(\.pageBreakBefore), [false, false, false])
    }

    func testNextPageSectionAfterAContinuousSectionStartsANewPage() throws {
        let body = paragraph("First", section: "<w:type w:val=\"continuous\"/>") + paragraph("Second", section: "<w:type w:val=\"nextPage\"/>")
            + paragraph("Third") + finalSection()
        let blocks = try convert(name: "Next page after continuous", body: body)
        XCTAssertEqual(blocks.map(\.text), ["First", "Second", "Third"])
        XCTAssertEqual(blocks.map(\.pageBreakBefore), [false, true, true])
    }

    // F1800 edge cases: a column break is not a page break, an empty
    // section-ending paragraph still moves the next section, and a break
    // that ends the document adds nothing.
    func testColumnBreakEmptyEndingParagraphAndFinalBreak() throws {
        let empty = "<w:p><w:pPr><w:sectPr><w:type w:val=\"nextColumn\"/><w:pgSz w:w=\"11906\" w:h=\"16838\"/></w:sectPr></w:pPr></w:p>"
        let body = paragraph("Columns", section: "") + paragraph("Same page") + empty
            + paragraph("New page", section: "") + finalSection()
        let blocks = try convert(name: "Edges", body: body)
        XCTAssertEqual(blocks.map(\.text), ["Columns", "Same page", "", "New page"])
        XCTAssertEqual(blocks.map(\.pageBreakBefore), [false, false, false, true])
    }

    // A table cannot carry the break, so an empty paragraph before it does.
    func testSectionStartingWithATableStillStartsANewPage() throws {
        let table = "<w:tbl><w:tblGrid><w:gridCol w:w=\"2400\"/></w:tblGrid><w:tr><w:tc><w:p><w:r><w:t>Cell</w:t></w:r></w:p></w:tc></w:tr></w:tbl>"
        let body = paragraph("Before", section: "") + table + paragraph("After", section: "") + table + paragraph("End") + finalSection("<w:type w:val=\"continuous\"/>")
        let blocks = try convert(name: "Table", body: body)
        XCTAssertEqual(blocks.map(\.isTable), [false, false, true, false, true, false])
        XCTAssertEqual(blocks.map(\.text), ["Before", "", "", "After", "", "End"])
        XCTAssertEqual(blocks.map(\.pageBreakBefore), [false, true, false, false, false, false])
    }

    private func finalSection(_ type: String = "") -> String { "<w:sectPr>\(type)<w:pgSz w:w=\"11906\" w:h=\"16838\"/></w:sectPr>" }

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
