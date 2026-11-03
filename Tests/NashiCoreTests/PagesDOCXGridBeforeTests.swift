import XCTest
import Foundation
@testable import NashiCore

/// w:gridBefore skips leading grid columns, so the row's cells start further
/// right; w:gridAfter only leaves trailing columns empty.
final class PagesDOCXGridBeforeTests: XCTestCase {
    private let restart = "<w:vMerge w:val=\"restart\"/>", proceed = "<w:vMerge/>"
    private var root: URL!

    override func setUpWithError() throws { root = try OutputFiles.temporaryDirectory() }
    override func tearDownWithError() throws { if let root { try? FileManager.default.removeItem(at: root) } }

    func testGridBeforeShiftsCellsIntoTheirColumns() throws {
        let table = "<w:tbl><w:tr>\(cell("A"))\(cell("B"))\(cell("C"))</w:tr>\(row(cell("E") + cell("F"), before: "1"))</w:tbl>"
        let result = try convert(table)
        XCTAssertEqual(result.rows, [["A", "B", "C"], ["", "E", "F"]])
    }

    func testGridBeforeKeepsVerticalMergeColumn() throws {
        let table = "<w:tbl><w:tr>\(cell("A"))\(cell("B", restart))\(cell("C"))</w:tr>\(row(cell("", proceed) + cell("F"), before: "1"))</w:tbl>"
        let result = try convert(table)
        XCTAssertEqual(result.rows, [["A", "B", "C"], ["", "", "F"]])
        XCTAssertEqual(result.merges, ["0,1 2x1"])
    }

    func testGridBeforeCountsTowardTheColumnLimit() throws {
        let table = "<w:tbl>\(row(cell("Wide", "<w:gridSpan w:val=\"1000\"/>"), before: "100"))</w:tbl>"
        XCTAssertThrowsError(try convert(table)) { error in
            XCTAssertTrue(error.localizedDescription.contains("1024 columns"), error.localizedDescription)
        }
    }

    func testGridAfterRowKeepsLeadingCells() throws {
        let table = "<w:tbl><w:tr>\(cell("A"))\(cell("B"))\(cell("C"))</w:tr><w:tr><w:trPr><w:gridAfter w:val=\"1\"/></w:trPr>\(cell("E"))\(cell("F"))</w:tr></w:tbl>"
        let result = try convert(table)
        XCTAssertEqual(result.rows, [["A", "B", "C"], ["E", "F", ""]])
    }

    private func row(_ cells: String, before: String) -> String {
        "<w:tr><w:trPr><w:gridBefore w:val=\"\(before)\"/></w:trPr>\(cells)</w:tr>"
    }

    private func cell(_ text: String, _ properties: String = "") -> String {
        "<w:tc>" + (properties.isEmpty ? "" : "<w:tcPr>\(properties)</w:tcPr>") + "<w:p><w:r><w:t>\(text)</w:t></w:r></w:p></w:tc>"
    }

    /// Converts a one-table DOCX body to Pages and reads back the cell text and merges.
    private func convert(_ table: String, name: String = #function) throws -> (rows: [[String]], merges: [String]) {
        let package = root.appendingPathComponent(name + " package", isDirectory: true)
        let word = package.appendingPathComponent("word", isDirectory: true)
        try FileManager.default.createDirectory(at: word, withIntermediateDirectories: true)
        let document = "<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:body>\(table)</w:body></w:document>"
        try document.write(to: word.appendingPathComponent("document.xml"), atomically: true, encoding: .utf8)
        let input = root.appendingPathComponent(name).appendingPathExtension("docx")
        let output = root.appendingPathComponent(name).appendingPathExtension("pages")
        try ArchiveCodec.create(from: package, at: input, format: "zip", token: .init())
        try PagesCodec.create(fromDOCX: input, at: output, token: .init())
        let model = try PagesCodec.model(output, token: .init())
        let block = try XCTUnwrap((model["blocks"] as? [[String: Any]])?.first { $0["type"] as? String == "table" })
        let rows = try XCTUnwrap(block["rows"] as? [[[String: Any]]]).map { $0.map { $0["text"] as? String ?? "" } }
        let merges = (block["merges"] as? [[String: Any]] ?? []).map { "\($0["row"] as? Int ?? -1),\($0["column"] as? Int ?? -1) \($0["rowCount"] as? Int ?? -1)x\($0["columnCount"] as? Int ?? -1)" }.sorted()
        return (rows, merges)
    }
}
