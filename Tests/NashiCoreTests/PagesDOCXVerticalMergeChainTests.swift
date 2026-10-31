import XCTest
import Foundation
@testable import NashiCore

/// A Word vertical merge only continues through rows that carry a continuation
/// cell in that same grid column. A row that covers the column with another
/// cell, or leaves it empty, ends the merge.
final class PagesDOCXVerticalMergeChainTests: XCTestCase {
    private let restart = "<w:vMerge w:val=\"restart\"/>", proceed = "<w:vMerge/>", two = "<w:gridSpan w:val=\"2\"/>"
    private var root: URL!

    override func setUpWithError() throws { root = try OutputFiles.temporaryDirectory() }
    override func tearDownWithError() throws { if let root { try? FileManager.default.removeItem(at: root) } }

    func testColumnCoveredByWideCellEndsVerticalMerge() throws {
        let table = "<w:tbl><w:tr>\(cell("A"))\(cell("B", restart))</w:tr><w:tr>\(cell("C", two))</w:tr><w:tr>\(cell("D"))\(cell("E", proceed))</w:tr></w:tbl>"
        let result = try convert(table)
        XCTAssertEqual(result.rows, [["A", "B"], ["C", ""], ["D", "E"]])
        XCTAssertEqual(result.merges, ["1,0 1x2"])
    }

    func testMissingCellEndsVerticalMerge() throws {
        let table = "<w:tbl><w:tr>\(cell("A"))\(cell("B", restart))</w:tr><w:tr>\(cell("C"))</w:tr><w:tr>\(cell("D"))\(cell("E", proceed))</w:tr></w:tbl>"
        let result = try convert(table)
        XCTAssertEqual(result.rows, [["A", "B"], ["C", ""], ["D", "E"]])
        XCTAssertEqual(result.merges, [])
    }

    func testContiguousVerticalMergeStillSpansEveryContinuedRow() throws {
        let table = "<w:tbl><w:tr>\(cell("A", restart))\(cell("B"))</w:tr><w:tr>\(cell("", proceed))\(cell("C"))</w:tr><w:tr>\(cell("", proceed))\(cell("D"))</w:tr><w:tr>\(cell("E"))\(cell("F"))</w:tr></w:tbl>"
        let result = try convert(table)
        XCTAssertEqual(result.rows, [["A", "B"], ["", "C"], ["", "D"], ["E", "F"]])
        XCTAssertEqual(result.merges, ["0,0 3x1"])
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
