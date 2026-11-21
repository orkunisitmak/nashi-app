import XCTest
import Foundation
@testable import NashiCore

/// Word saves `https://host/page#part` as a relationship target without the
/// fragment plus `w:anchor="part"` on the hyperlink. Both halves belong to the link.
final class PagesDOCXHyperlinkAnchorTests: XCTestCase {
    private var root: URL!

    override func setUpWithError() throws { root = try OutputFiles.temporaryDirectory() }
    override func tearDownWithError() throws { if let root { try? FileManager.default.removeItem(at: root) } }

    // F1803
    func testExternalLinkKeepsItsAnchorFragment() throws {
        let links = try convert(name: "Anchor", body: "<w:p><w:hyperlink r:id=\"rIdPage\" w:anchor=\"install\"><w:r><w:t>Install guide</w:t></w:r></w:hyperlink></w:p>")
        XCTAssertEqual(links, ["Install guide": "https://example.com/guide#install"])
    }

    // F1803 edge cases: an anchor with a space is percent-encoded; a target
    // that already carries a fragment is not given a second one.
    func testAnchorIsEncodedAndNeverDoubled() throws {
        let body = "<w:p><w:hyperlink r:id=\"rIdPage\" w:anchor=\"step two\"><w:r><w:t>Step</w:t></w:r></w:hyperlink></w:p><w:p><w:hyperlink r:id=\"rIdFragment\" w:anchor=\"other\"><w:r><w:t>Faq</w:t></w:r></w:hyperlink></w:p>"
        XCTAssertEqual(try convert(name: "Edges", body: body), ["Step": "https://example.com/guide#step%20two", "Faq": "https://example.com/faq#top"])
    }

    // Control: a plain external link and an internal-only bookmark link are unchanged.
    func testLinksWithoutBothHalvesAreUnchanged() throws {
        let body = "<w:p><w:hyperlink w:anchor=\"_Toc1\"><w:r><w:t>Intro</w:t></w:r></w:hyperlink></w:p><w:p><w:hyperlink r:id=\"rIdPage\"><w:r><w:t>Guide</w:t></w:r></w:hyperlink></w:p>"
        XCTAssertEqual(try convert(name: "Plain", body: body), ["Guide": "https://example.com/guide"])
    }

    private func convert(name: String, body: String) throws -> [String: String] {
        let package = root.appendingPathComponent(name + " package", isDirectory: true)
        let word = package.appendingPathComponent("word", isDirectory: true)
        try FileManager.default.createDirectory(at: word.appendingPathComponent("_rels"), withIntermediateDirectories: true)
        try "<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><w:body>\(body)</w:body></w:document>".write(to: word.appendingPathComponent("document.xml"), atomically: true, encoding: .utf8)
        try "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rIdPage\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/hyperlink\" Target=\"https://example.com/guide\" TargetMode=\"External\"/><Relationship Id=\"rIdFragment\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/hyperlink\" Target=\"https://example.com/faq#top\" TargetMode=\"External\"/></Relationships>".write(to: word.appendingPathComponent("_rels/document.xml.rels"), atomically: true, encoding: .utf8)
        let input = root.appendingPathComponent(name).appendingPathExtension("docx")
        try ArchiveCodec.create(from: package, at: input, format: "zip", token: .init())
        let output = root.appendingPathComponent(name).appendingPathExtension("pages")
        try PagesCodec.create(fromDOCX: input, at: output, token: .init())
        let blocks = try XCTUnwrap(PagesCodec.model(output, token: .init())["blocks"] as? [[String: Any]])
        var links: [String: String] = [:]
        for run in blocks.flatMap({ $0["runs"] as? [[String: Any]] ?? [] }) { if let text = run["text"] as? String, let link = run["link"] as? String { links[text] = link } }
        return links
    }
}
