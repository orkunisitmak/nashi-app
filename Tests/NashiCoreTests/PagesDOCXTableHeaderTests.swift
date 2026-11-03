import XCTest
import Foundation
@testable import NashiCore

/// `w:tblHeader` is an on/off property: `w:val="0"`, `"false"` or `"off"`
/// explicitly marks a row that does NOT repeat as a header.
final class PagesDOCXTableHeaderTests: XCTestCase {
    private var root: URL!

    override func setUpWithError() throws { root = try OutputFiles.temporaryDirectory() }
    override func tearDownWithError() throws { if let root { try? FileManager.default.removeItem(at: root) } }

    // F1804
    func testExplicitlyOffHeaderRowsAreNotHeaders() throws {
        for value in ["0", "false", "off"] {
            XCTAssertEqual(try headerRows(name: "Off \(value)", rows: ["<w:tblHeader w:val=\"\(value)\"/>", ""]), 0, value)
        }
        XCTAssertEqual(try headerRows(name: "Mixed", rows: ["<w:tblHeader/>", "<w:tblHeader w:val=\"0\"/>", ""]), 1)
    }

    // Control: on-valued header rows still repeat.
    func testOnHeaderRowsAreHeaders() throws {
        XCTAssertEqual(try headerRows(name: "On", rows: ["<w:tblHeader/>", "<w:tblHeader w:val=\"1\"/>", ""]), 2)
        XCTAssertEqual(try headerRows(name: "None", rows: ["", ""]), 0)
    }

    private func headerRows(name: String, rows: [String]) throws -> Int? {
        let table = "<w:tbl><w:tblGrid><w:gridCol w:w=\"2000\"/></w:tblGrid>" + rows.enumerated().map { index, header in
            "<w:tr>\(header.isEmpty ? "" : "<w:trPr>\(header)</w:trPr>")<w:tc><w:p><w:r><w:t>Row \(index)</w:t></w:r></w:p></w:tc></w:tr>"
        }.joined() + "</w:tbl><w:p/>"
        let package = root.appendingPathComponent(name + " package", isDirectory: true)
        let word = package.appendingPathComponent("word", isDirectory: true)
        try FileManager.default.createDirectory(at: word, withIntermediateDirectories: true)
        try "<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:body>\(table)</w:body></w:document>".write(to: word.appendingPathComponent("document.xml"), atomically: true, encoding: .utf8)
        let input = root.appendingPathComponent(name).appendingPathExtension("docx")
        try ArchiveCodec.create(from: package, at: input, format: "zip", token: .init())
        let output = root.appendingPathComponent(name).appendingPathExtension("pages")
        try PagesCodec.create(fromDOCX: input, at: output, token: .init())
        let blocks = try XCTUnwrap(PagesCodec.model(output, token: .init())["blocks"] as? [[String: Any]])
        return blocks.first { $0["type"] as? String == "table" }?["headerRows"] as? Int
    }
}
