import XCTest
import Foundation
import PDFKit
import CoreText
@testable import NashiCore

/// PDF -> spreadsheet must not type a digit run as a number when the
/// spreadsheet's binary64 value would silently change it.
final class PDFSpreadsheetNumberPrecisionRegressionTests: XCTestCase {
    private var root: URL!
    override func setUpWithError() throws { root = try OutputFiles.temporaryDirectory() }
    override func tearDownWithError() throws { if let root { try? FileManager.default.removeItem(at: root) } }

    func testLongDigitRunsStayExactTextWhileShortNumbersStayNumeric() throws {
        let expected: [(String, String)] = [
            ("12345678901234567890", "string"), ("1234567890123456", "string"),
            ("-98765432109876543", "string"), ("1234567890.1234567", "string"),
            ("123456789012345", "float"), ("12.5", "float"), ("-3", "float"), ("007", "string"),
        ]
        let url = root.appendingPathComponent("Numbers.pdf")
        var box = CGRect(x: 0, y: 0, width: 612, height: 792)
        let context = try XCTUnwrap(CGContext(url as CFURL, mediaBox: &box, nil))
        context.beginPDFPage(nil)
        for (offset, item) in expected.enumerated() {
            let font = CTFontCreateWithName("Helvetica" as CFString, 14, nil)
            let text = NSAttributedString(string: item.0, attributes: [NSAttributedString.Key(kCTFontAttributeName as String): font])
            context.textPosition = CGPoint(x: 50, y: 740 - CGFloat(offset) * 40)
            CTLineDraw(CTLineCreateWithAttributedString(text), context)
        }
        context.endPDFPage(); context.closePDF()
        let work = root.appendingPathComponent("Work", isDirectory: true)
        try FileManager.default.createDirectory(at: work, withIntermediateDirectories: true)
        let fods = try PDFOfficeCodec.prepare(url, to: "xlsx", at: work, options: .init(), token: .init())
        let xml = try XMLDocument(contentsOf: fods, options: [.nodeLoadExternalEntitiesNever])
        var cells: [String: XMLElement] = [:]
        for case let cell as XMLElement in try xml.nodes(forXPath: "//*[local-name()='table-cell']") {
            if let text = cell.stringValue, !text.isEmpty { cells[text] = cell }
        }
        for (text, type) in expected {
            let cell = try XCTUnwrap(cells[text], text)
            XCTAssertEqual(cell.attribute(forName: "office:value-type")?.stringValue, type, text)
            if type == "float" { XCTAssertEqual(cell.attribute(forName: "office:value")?.stringValue, text) }
        }
    }
}
