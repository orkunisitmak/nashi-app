import XCTest
import Foundation
@testable import NashiCore

final class PagesDOCXHighlightTests: XCTestCase {
    private var root: URL!
    override func setUpWithError() throws { root = try OutputFiles.temporaryDirectory() }
    override func tearDownWithError() throws { if let root { try? FileManager.default.removeItem(at: root) } }

    /// w:highlight only takes named colours (ST_HighlightColor), never hex.
    func testNamedWordHighlightsBecomePagesBackgroundColors() throws {
        let runs = ["yellow", "darkBlue", "none"].map { "<w:r><w:rPr><w:highlight w:val=\"\($0)\"/></w:rPr><w:t>\($0)</w:t></w:r>" }.joined()
        XCTAssertEqual(try colors(pages("<w:p>\(runs)</w:p>")), ["yellow": [255, 255, 0], "darkBlue": [0, 0, 128]])
    }

    /// "none" turns off a highlight inherited from a run or paragraph style.
    func testNoneClearsAnInheritedHighlight() throws {
        let styles = """
        <w:style w:styleId="Marked"><w:rPr><w:highlight w:val="yellow"/></w:rPr></w:style>
        <w:style w:styleId="Plain"><w:basedOn w:val="Marked"/><w:rPr><w:highlight w:val="none"/></w:rPr></w:style>
        """
        let body = """
        <w:p><w:r><w:rPr><w:rStyle w:val="Marked"/></w:rPr><w:t>inherited</w:t></w:r>\
        <w:r><w:rPr><w:rStyle w:val="Marked"/><w:highlight w:val="none"/></w:rPr><w:t>cleared</w:t></w:r></w:p>\
        <w:p><w:pPr><w:pStyle w:val="Plain"/></w:pPr><w:r><w:t>paragraph</w:t></w:r></w:p>
        """
        XCTAssertEqual(try colors(pages(body, styles: styles)), ["inherited": [255, 255, 0]])
    }

    private func colors(_ model: [String: Any]) throws -> [String: [Double]] {
        let blocks = try XCTUnwrap(model["blocks"] as? [[String: Any]])
        var colors: [String: [Double]] = [:]
        for run in blocks.flatMap({ $0["runs"] as? [[String: Any]] ?? [] }) {
            guard let text = run["text"] as? String, let color = (run["character"] as? [String: Any])?["backgroundColor"] as? [String: Any] else { continue }
            colors[text] = ["r", "g", "b"].map { ((color[$0] as? NSNumber)?.doubleValue ?? -1) * 255 }.map { $0.rounded() }
        }
        return colors
    }

    private func pages(_ body: String, styles: String? = nil) throws -> [String: Any] {
        let package = root.appendingPathComponent("Package", isDirectory: true)
        let word = package.appendingPathComponent("word", isDirectory: true)
        try FileManager.default.createDirectory(at: word, withIntermediateDirectories: true)
        try "<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:body>\(body)</w:body></w:document>".write(to: word.appendingPathComponent("document.xml"), atomically: true, encoding: .utf8)
        if let styles { try "<w:styles xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\">\(styles)</w:styles>".write(to: word.appendingPathComponent("styles.xml"), atomically: true, encoding: .utf8) }
        let input = root.appendingPathComponent("Highlight.docx")
        try ArchiveCodec.create(from: package, at: input, format: "zip", token: .init())
        let output = root.appendingPathComponent("Highlight.pages")
        try PagesCodec.create(fromDOCX: input, at: output, token: .init())
        return try PagesCodec.model(output, token: .init())
    }
}
