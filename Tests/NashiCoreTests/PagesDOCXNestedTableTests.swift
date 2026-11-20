import XCTest
import Foundation
@testable import NashiCore

/// A Word table cell may contain another table. Pages cells hold plain text,
/// so the nested table's text must survive in the outer cell, not vanish.
final class PagesDOCXNestedTableTests: XCTestCase {
    private var root: URL!

    override func setUpWithError() throws { root = try OutputFiles.temporaryDirectory() }
    override func tearDownWithError() throws { if let root { try? FileManager.default.removeItem(at: root) } }

    // F1802
    func testNestedTableTextIsKeptInTheOuterCell() throws {
        let inner = table([["A1", "B1"], ["A2", "B2"]])
        let outer = "<w:tbl><w:tr><w:tc>\(paragraph("Before"))\(inner)\(paragraph("After"))</w:tc><w:tc>\(paragraph("Side"))</w:tc></w:tr></w:tbl>"
        let cells = try convert(name: "Nested", body: outer + paragraph("End"))
        XCTAssertEqual(cells, ["Before\nA1\tB1\nA2\tB2\nAfter", "Side"])
    }

    // F1802
    func testDoublyNestedTableTextIsKept() throws {
        let deepest = table([["Deep"]])
        let middle = "<w:tbl><w:tr><w:tc>\(paragraph("Middle"))\(deepest)</w:tc></w:tr></w:tbl>"
        let outer = "<w:tbl><w:tr><w:tc>\(middle)</w:tc></w:tr></w:tbl>"
        XCTAssertEqual(try convert(name: "Deep", body: outer + paragraph("End")), ["Middle\nDeep"])
    }

    // Control: ordinary multi-paragraph cells are unchanged.
    func testPlainCellParagraphsAreUnchanged() throws {
        let outer = "<w:tbl><w:tr><w:tc>\(paragraph("One"))\(paragraph("Two"))</w:tc><w:tc>\(paragraph("Three"))</w:tc></w:tr></w:tbl>"
        XCTAssertEqual(try convert(name: "Plain", body: outer + paragraph("End")), ["One\nTwo", "Three"])
    }

    private func paragraph(_ text: String) -> String { "<w:p><w:r><w:t>\(text)</w:t></w:r></w:p>" }
    private func table(_ rows: [[String]]) -> String {
        "<w:tbl>" + rows.map { "<w:tr>" + $0.map { "<w:tc>\(paragraph($0))</w:tc>" }.joined() + "</w:tr>" }.joined() + "</w:tbl>"
    }

    /// Texts of the first table's cells, row-major.
    private func convert(name: String, body: String) throws -> [String] {
        let package = root.appendingPathComponent(name + " package", isDirectory: true)
        let word = package.appendingPathComponent("word", isDirectory: true)
        try FileManager.default.createDirectory(at: word, withIntermediateDirectories: true)
        try "<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:body>\(body)</w:body></w:document>".write(to: word.appendingPathComponent("document.xml"), atomically: true, encoding: .utf8)
        let input = root.appendingPathComponent(name).appendingPathExtension("docx")
        try ArchiveCodec.create(from: package, at: input, format: "zip", token: .init())
        let output = root.appendingPathComponent(name).appendingPathExtension("pages")
        try PagesCodec.create(fromDOCX: input, at: output, token: .init())
        let blocks = try XCTUnwrap(PagesCodec.model(output, token: .init())["blocks"] as? [[String: Any]])
        let table = try XCTUnwrap(blocks.first { $0["type"] as? String == "table" })
        return (table["rows"] as? [[[String: Any]]] ?? []).flatMap { $0.map { $0["text"] as? String ?? "" } }
    }
}
