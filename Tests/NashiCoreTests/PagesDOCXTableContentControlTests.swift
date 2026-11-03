import XCTest
import Foundation
@testable import NashiCore

/// Word puts repeating-section and custom XML wrappers around table rows and
/// cells (w:sdt and w:customXml around w:tr or w:tc). Their text belongs in the table.
final class PagesDOCXTableContentControlTests: XCTestCase {
    private var root: URL!

    override func setUpWithError() throws { root = try OutputFiles.temporaryDirectory() }
    override func tearDownWithError() throws { if let root { try? FileManager.default.removeItem(at: root) } }

    func testWrappedRowsAndCellsKeepTheirText() throws {
        let first = "<w:tr>\(cell("A1"))\(sdt(cell("B1")))</w:tr>"
        let second = sdt("<w:tr>\(cell("A2"))\(customXml(cell("B2")))</w:tr>")
        let third = customXml("<w:tr>\(cell("A3"))\(cell("B3"))</w:tr>")
        let table = try convert(name: "Wrapped", body: "<w:tbl>\(grid)\(first)\(second)\(third)</w:tbl>")
        XCTAssertEqual(texts(table), [["A1", "B1"], ["A2", "B2"], ["A3", "B3"]])
    }

    func testWrappedRowsInANestedTableKeepTheirText() throws {
        let inner = "<w:tbl>" + sdt("<w:tr>\(cell("x"))\(sdt(cell("y")))</w:tr>") + "</w:tbl>"
        let outer = "<w:tbl>\(grid)<w:tr><w:tc>\(paragraph("Before"))\(inner)</w:tc>\(cell("Side"))</w:tr></w:tbl>"
        XCTAssertEqual(texts(try convert(name: "Nested", body: outer)), [["Before\nx\ty", "Side"]])
    }

    func testAWrappedHeaderRowStillRepeats() throws {
        let header = sdt("<w:tr><w:trPr><w:tblHeader/></w:trPr>\(cell("Head"))\(cell("Er"))</w:tr>")
        let table = try convert(name: "Header", body: "<w:tbl>\(grid)\(header)<w:tr>\(cell("a"))\(cell("b"))</w:tr></w:tbl>")
        XCTAssertEqual(table["headerRows"] as? Int, 1)
        XCTAssertEqual(texts(table), [["Head", "Er"], ["a", "b"]])
    }

    /// A vertical merge continues through a wrapped row like any other row.
    func testAVerticalMergeContinuesIntoAWrappedRow() throws {
        let restart = "<w:tc><w:tcPr><w:vMerge w:val=\"restart\"/></w:tcPr>\(paragraph("M"))</w:tc>"
        let continuation = "<w:tc><w:tcPr><w:vMerge/></w:tcPr><w:p/></w:tc>"
        let body = "<w:tbl>\(grid)<w:tr>\(restart)\(cell("r1"))</w:tr>" + sdt("<w:tr>\(continuation)\(cell("r2"))</w:tr>") + "</w:tbl>"
        let table = try convert(name: "Merge", body: body)
        let merges = table["merges"] as? [[String: Int]] ?? []
        XCTAssertEqual(merges, [["row": 0, "column": 0, "rowCount": 2, "columnCount": 1]])
    }

    private let grid = "<w:tblGrid><w:gridCol w:w=\"2400\"/><w:gridCol w:w=\"2400\"/></w:tblGrid>"
    private func paragraph(_ text: String) -> String { "<w:p><w:r><w:t>\(text)</w:t></w:r></w:p>" }
    private func cell(_ text: String) -> String { "<w:tc>\(paragraph(text))</w:tc>" }
    private func sdt(_ content: String) -> String { "<w:sdt><w:sdtPr><w:tag w:val=\"t\"/></w:sdtPr><w:sdtContent>\(content)</w:sdtContent></w:sdt>" }
    private func customXml(_ content: String) -> String { "<w:customXml w:element=\"item\">\(content)</w:customXml>" }

    private func texts(_ table: [String: Any]) -> [[String]] {
        let rows = table["rows"] as? [[[String: Any]]] ?? []
        return rows.map { row in row.map { $0["text"] as? String ?? "" } }
    }

    private func convert(name: String, body: String) throws -> [String: Any] {
        let package = root.appendingPathComponent(name + " package", isDirectory: true)
        let word = package.appendingPathComponent("word", isDirectory: true)
        try FileManager.default.createDirectory(at: word, withIntermediateDirectories: true)
        let document = "<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:body>\(body)\(paragraph("End"))</w:body></w:document>"
        try document.write(to: word.appendingPathComponent("document.xml"), atomically: true, encoding: .utf8)
        let input = root.appendingPathComponent(name).appendingPathExtension("docx")
        try ArchiveCodec.create(from: package, at: input, format: "zip", token: .init())
        let output = root.appendingPathComponent(name).appendingPathExtension("pages")
        try PagesCodec.create(fromDOCX: input, at: output, token: .init())
        let blocks = try XCTUnwrap(PagesCodec.model(output, token: .init())["blocks"] as? [[String: Any]])
        return try XCTUnwrap(blocks.first { $0["type"] as? String == "table" })
    }
}
