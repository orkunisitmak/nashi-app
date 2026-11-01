import XCTest
import Foundation
@testable import NashiCore

/// DOCX -> Pages must carry every visible piece of Word text into the model.
final class PagesTextExtractionTests: XCTestCase {
    private var root: URL!
    private let namespaces = "xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\""

    override func setUpWithError() throws { root = try OutputFiles.temporaryDirectory() }
    override func tearDownWithError() throws { if let root { try? FileManager.default.removeItem(at: root) } }

    // F1540
    func testBlockLevelContentControlParagraphsAreKept() throws {
        let sdt = "<w:sdt><w:sdtPr><w:alias w:val=\"Cover\"/></w:sdtPr><w:sdtContent>\(paragraph("Cover title"))</w:sdtContent></w:sdt>"
        let cell = "<w:tbl><w:tr><w:tc><w:sdt><w:sdtContent>\(paragraph("Cell choice"))</w:sdtContent></w:sdt></w:tc></w:tr></w:tbl>"
        let model = try convert(body: sdt + paragraph("Body") + cell)
        XCTAssertEqual(paragraphTexts(model), ["Cover title", "Body"])
        XCTAssertEqual(cellTexts(model), ["Cell choice"])
    }

    // F1541
    func testRunsInsideInlineWrappersAreKept() throws {
        for wrapper in ["fldSimple", "customXml", "moveTo", "dir", "bdo"] {
            let body = "<w:p>\(run("Page "))<w:\(wrapper) w:val=\"rtl\">\(run("7"))</w:\(wrapper)></w:p>"
            XCTAssertEqual(paragraphTexts(try convert(body: body)), ["Page 7"], wrapper)
        }
    }

    // F1542
    func testCellAndHeaderTextKeepTabsBreaksAndParagraphs() throws {
        let cell = "<w:tbl><w:tr><w:tc><w:p><w:pPr><w:tabs><w:tab w:val=\"left\" w:pos=\"720\"/></w:tabs></w:pPr><w:r><w:t>Name</w:t><w:tab/><w:t>Value</w:t><w:br/><w:t>Second line</w:t></w:r></w:p></w:tc></w:tr></w:tbl>"
        let model = try convert(body: cell, sections: "<w:headerReference w:type=\"default\" r:id=\"hD\"/>", parts: ["hD": ("hdr", paragraph("Company") + paragraph("Confidential"))])
        XCTAssertEqual(cellTexts(model), ["Name\tValue\nSecond line"])
        XCTAssertEqual(model["header"] as? String, "Company\nConfidential")
    }

    // F1543
    func testDefaultHeaderAndFooterVariantsAreUsed() throws {
        let references = "<w:headerReference w:type=\"even\" r:id=\"hE\"/><w:headerReference w:type=\"default\" r:id=\"hD\"/><w:footerReference w:type=\"even\" r:id=\"fE\"/><w:footerReference w:type=\"default\" r:id=\"fD\"/><w:headerReference w:type=\"first\" r:id=\"hF\"/>"
        let parts = ["hE": ("hdr", paragraph("Even header")), "hD": ("hdr", paragraph("Default header")), "hF": ("hdr", paragraph("First header")), "fE": ("ftr", paragraph("Even footer")), "fD": ("ftr", paragraph("Default footer"))]
        // A table selects the Pages template that has a section master for headers.
        let model = try convert(body: paragraph("Body") + "<w:tbl><w:tr><w:tc>\(paragraph("c"))</w:tc></w:tr></w:tbl>", sections: references, parts: parts)
        XCTAssertEqual(model["header"] as? String, "Default header")
        XCTAssertEqual(model["footer"] as? String, "Default footer")
    }

    // F1544
    func testNonBreakingHyphenIsKeptInParagraphsAndCells() throws {
        let number = "<w:r><w:t>555</w:t><w:noBreakHyphen/><w:t>0100</w:t></w:r>"
        let model = try convert(body: "<w:p>\(number)</w:p><w:tbl><w:tr><w:tc><w:p>\(number)</w:p></w:tc></w:tr></w:tbl>")
        XCTAssertEqual(paragraphTexts(model), ["555\u{2011}0100"])
        XCTAssertEqual(cellTexts(model), ["555\u{2011}0100"])
    }

    private func run(_ text: String) -> String { "<w:r><w:t xml:space=\"preserve\">\(text)</w:t></w:r>" }
    private func paragraph(_ text: String) -> String { "<w:p>\(run(text))</w:p>" }

    private func paragraphTexts(_ model: [String: Any]) -> [String] {
        (model["blocks"] as? [[String: Any]] ?? []).filter { $0["type"] as? String != "table" }.map { ($0["runs"] as? [[String: Any]] ?? []).compactMap { $0["text"] as? String }.joined() }
    }

    private func cellTexts(_ model: [String: Any]) -> [String] {
        (model["blocks"] as? [[String: Any]] ?? []).filter { $0["type"] as? String == "table" }.flatMap { ($0["rows"] as? [[[String: Any]]] ?? []).flatMap { $0.map { $0["text"] as? String ?? "" } } }
    }

    private func convert(body: String, sections: String = "", parts: [String: (String, String)] = [:]) throws -> [String: Any] {
        let package = root.appendingPathComponent(UUID().uuidString, isDirectory: true)
        let word = package.appendingPathComponent("word", isDirectory: true)
        try FileManager.default.createDirectory(at: word.appendingPathComponent("_rels", isDirectory: true), withIntermediateDirectories: true)
        try "<w:document \(namespaces)><w:body>\(body)<w:sectPr>\(sections)</w:sectPr></w:body></w:document>".write(to: word.appendingPathComponent("document.xml"), atomically: true, encoding: .utf8)
        var relations = ""
        for (id, part) in parts {
            try "<w:\(part.0) \(namespaces)>\(part.1)</w:\(part.0)>".write(to: word.appendingPathComponent("\(id).xml"), atomically: true, encoding: .utf8)
            relations += "<Relationship Id=\"\(id)\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/\(part.0 == "hdr" ? "header" : "footer")\" Target=\"\(id).xml\"/>"
        }
        try "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">\(relations)</Relationships>".write(to: word.appendingPathComponent("_rels/document.xml.rels"), atomically: true, encoding: .utf8)
        let input = package.appendingPathExtension("docx")
        try ArchiveCodec.create(from: package, at: input, format: "zip", token: .init())
        let output = package.appendingPathExtension("pages")
        try PagesCodec.create(fromDOCX: input, at: output, token: .init())
        return try PagesCodec.model(output, token: .init())
    }
}
