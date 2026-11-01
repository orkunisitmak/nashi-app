import XCTest
import AppKit
import CoreText
@testable import NashiCore

final class PDFMarkdownExportTests: XCTestCase {
    private var root: URL!
    override func setUpWithError() throws { root = try OutputFiles.temporaryDirectory() }
    override func tearDownWithError() throws { if let root { try? FileManager.default.removeItem(at: root) } }

    func testPDFToMarkdownKeepsLiteralTextFromBeingReadAsFormatting() throws {
        let lines = ["Total = 2*3*4", "# Not a heading", "1. not a list", "- not an item", "__init__ and C:\\a\\*b", "Use <b>bold</b> and [x](http://e.com)", "A & B &amp; C"]
        let markdown = try String(contentsOf: export(pages: [lines], to: "md"), encoding: .utf8)
        let rendered = try AttributedString(markdown: markdown, options: .init(interpretedSyntax: .full))
        XCTAssertEqual(String(rendered.characters), lines.joined(separator: " "))
        XCTAssertEqual(try String(contentsOf: export(pages: [lines], to: "txt"), encoding: .utf8), lines.joined(separator: "\n"))
        let plain = try String(contentsOf: export(pages: [["Nashi fixture"], ["Page two"]], to: "md"), encoding: .utf8)
        XCTAssertEqual(plain, "Nashi fixture\n\n---\n\nPage two")
    }

    private func export(pages: [[String]], to format: String) throws -> URL {
        let input = root.appendingPathComponent("Text \(format).pdf")
        var bounds = CGRect(x: 0, y: 0, width: 612, height: 792)
        let context = try XCTUnwrap(CGContext(input as CFURL, mediaBox: &bounds, nil))
        for lines in pages {
            context.beginPDFPage(nil)
            for (index, value) in lines.enumerated() {
                let font = CTFontCreateWithName("Helvetica" as CFString, 14, nil)
                let text = NSAttributedString(string: value, attributes: [NSAttributedString.Key(kCTFontAttributeName as String): font])
                context.textPosition = CGPoint(x: 50, y: 730 - CGFloat(index) * 30)
                CTLineDraw(CTLineCreateWithAttributedString(text), context)
            }
            context.endPDFPage()
        }
        context.closePDF()
        return try XCTUnwrap(DocumentEngine.convert(input, to: format, options: .init(), token: .init()).first)
    }
}
