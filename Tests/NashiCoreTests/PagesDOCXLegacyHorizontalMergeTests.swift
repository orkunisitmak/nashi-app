import XCTest
import Foundation
@testable import NashiCore

/// Legacy w:hMerge cells join the restart cell to their left, the way older
/// Word documents expressed horizontal merges before w:gridSpan.
final class PagesDOCXLegacyHorizontalMergeTests: XCTestCase {
    private let restart = "<w:hMerge w:val=\"restart\"/>", proceed = "<w:hMerge/>"
    private var root: URL!

    override func setUpWithError() throws { root = try OutputFiles.temporaryDirectory() }
    override func tearDownWithError() throws { if let root { try? FileManager.default.removeItem(at: root) } }

    func testHorizontalMergeRunBecomesOneMergedCell() throws {
        let table = "<w:tbl><w:tr>\(cell("A", restart))\(cell("", proceed))\(cell("C"))</w:tr><w:tr>\(cell("D"))\(cell("E"))\(cell("F"))</w:tr></w:tbl>"
        let result = try convert(table)
        XCTAssertEqual(result.rows, [["A", "", "C"], ["D", "E", "F"]])
        XCTAssertEqual(result.merges, ["0,0 1x2"])
    }

    func testHorizontalMergeExtendsSpannedRestartAndSpannedContinuation() throws {
        let two = "<w:gridSpan w:val=\"2\"/>"
        let table = "<w:tbl><w:tr>\(cell("A", two + restart))\(cell("", two + proceed))\(cell("C"))</w:tr><w:tr>\(cell("D"))\(cell("E"))\(cell("F"))\(cell("G"))\(cell("H"))</w:tr></w:tbl>"
        let result = try convert(table)
        XCTAssertEqual(result.rows.first, ["A", "", "", "", "C"])
        XCTAssertEqual(result.merges, ["0,0 1x4"])
    }

    func testContinuationWithoutRestartStaysSeparate() throws {
        let table = "<w:tbl><w:tr>\(cell("A"))\(cell("B", proceed))\(cell("C"))</w:tr></w:tbl>"
        let result = try convert(table)
        XCTAssertEqual(result.rows, [["A", "B", "C"]])
        XCTAssertEqual(result.merges, [])
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
