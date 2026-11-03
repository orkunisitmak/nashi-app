import XCTest
import Foundation
@testable import NashiCore

/// Word also stores hyperlinks as complex fields:
/// `fldChar begin` + `instrText HYPERLINK "url"` + `fldChar separate` +
/// visible result runs + `fldChar end`. The visible text must keep its link.
final class PagesDOCXFieldHyperlinkTests: XCTestCase {
    private var root: URL!

    override func setUpWithError() throws { root = try OutputFiles.temporaryDirectory() }
    override func tearDownWithError() throws { if let root { try? FileManager.default.removeItem(at: root) } }

    // F1801
    func testHyperlinkFieldKeepsItsLink() throws {
        let field = fieldRuns(instruction: " HYPERLINK \"https://example.com/docs\" \\o \"Tip\" ", result: "the docs")
        let runs = try convert(name: "Field link", body: "<w:p>\(run("See "))\(field)\(run(" now"))</w:p>")
        XCTAssertEqual(runs.map(\.text).joined(), "See the docs now")
        XCTAssertEqual(runs.first { $0.text == "the docs" }?.link, "https://example.com/docs")
        XCTAssertEqual(runs.filter { $0.link != nil }.map(\.text), ["the docs"])
    }

    // F1801
    func testSplitInstructionAndNestedFieldsKeepTheOuterLink() throws {
        let begin = "<w:r><w:fldChar w:fldCharType=\"begin\"/></w:r>"
        let instruction = "<w:r><w:instrText xml:space=\"preserve\"> HYPERLINK </w:instrText></w:r><w:r><w:instrText>\"https://example.com/a\"</w:instrText></w:r>"
        let nested = fieldRuns(instruction: " PAGE ", result: "7")
        let field = begin + instruction + "<w:r><w:fldChar w:fldCharType=\"separate\"/></w:r>" + run("Page ") + nested + "<w:r><w:fldChar w:fldCharType=\"end\"/></w:r>"
        let runs = try convert(name: "Split field", body: "<w:p>\(field)\(run(" tail"))</w:p>")
        XCTAssertEqual(runs.map(\.text).joined(), "Page 7 tail")
        XCTAssertEqual(runs.filter { $0.link != nil }.map(\.text).joined(), "Page 7")
        XCTAssertTrue(runs.filter { $0.link != nil }.allSatisfy { $0.link == "https://example.com/a" })
    }

    // Control: relationship hyperlinks and non-link fields are unchanged.
    func testRelationshipHyperlinkAndOrdinaryFieldsAreUnchanged() throws {
        let body = "<w:p><w:hyperlink r:id=\"rIdLink\">\(run("Site"))</w:hyperlink>\(fieldRuns(instruction: " HYPERLINK \\l \"_Toc1\" ", result: " Intro"))\(fieldRuns(instruction: " DATE ", result: " today"))</w:p>"
        let runs = try convert(name: "Control", body: body)
        XCTAssertEqual(runs.map(\.text).joined(), "Site Intro today")
        XCTAssertEqual(runs.filter { $0.link != nil }.map(\.text), ["Site"])
        XCTAssertEqual(runs.first?.link, "https://example.com/site")
    }

    private func run(_ text: String) -> String { "<w:r><w:t xml:space=\"preserve\">\(text)</w:t></w:r>" }

    private func fieldRuns(instruction: String, result: String) -> String {
        "<w:r><w:fldChar w:fldCharType=\"begin\"/></w:r><w:r><w:instrText xml:space=\"preserve\">\(instruction)</w:instrText></w:r><w:r><w:fldChar w:fldCharType=\"separate\"/></w:r>\(run(result))<w:r><w:fldChar w:fldCharType=\"end\"/></w:r>"
    }

    private func convert(name: String, body: String) throws -> [(text: String, link: String?)] {
        let package = root.appendingPathComponent(name + " package", isDirectory: true)
        let word = package.appendingPathComponent("word", isDirectory: true)
        try FileManager.default.createDirectory(at: word.appendingPathComponent("_rels"), withIntermediateDirectories: true)
        try "<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><w:body>\(body)</w:body></w:document>".write(to: word.appendingPathComponent("document.xml"), atomically: true, encoding: .utf8)
        try "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rIdLink\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/hyperlink\" Target=\"https://example.com/site\" TargetMode=\"External\"/></Relationships>".write(to: word.appendingPathComponent("_rels/document.xml.rels"), atomically: true, encoding: .utf8)
        let input = root.appendingPathComponent(name).appendingPathExtension("docx")
        try ArchiveCodec.create(from: package, at: input, format: "zip", token: .init())
        let output = root.appendingPathComponent(name).appendingPathExtension("pages")
        try PagesCodec.create(fromDOCX: input, at: output, token: .init())
        let blocks = try XCTUnwrap(PagesCodec.model(output, token: .init())["blocks"] as? [[String: Any]])
        return blocks.flatMap { $0["runs"] as? [[String: Any]] ?? [] }.compactMap { run in
            guard let text = run["text"] as? String, !text.isEmpty else { return nil }
            return (text, run["link"] as? String)
        }
    }
}
