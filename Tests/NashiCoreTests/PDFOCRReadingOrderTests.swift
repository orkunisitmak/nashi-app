import XCTest
import AppKit
import PDFKit
import CoreText
@testable import NashiCore

/// Plain-text OCR of a scanned page puts words that share a printed line on one
/// output line, left to right, and starts a new line only for the next printed line.
/// Recognized cells are matched to the drawn items by position, so the check does
/// not depend on how OCR spells them.
final class PDFOCRReadingOrderTests: XCTestCase {
    private var documents: [PDFDocument] = []

    func testOCRTextKeepsEachPrintedLineOnOneLineLeftToRight() throws {
        typealias Item = (String, CGPoint, CGFloat)
        let paragraph: [Item] = (0..<4).map { ("PARAGRAPH LINE NUMBER \($0 + 1)", CGPoint(x: 60, y: 600 - CGFloat($0) * 17), 14) }
        // Items, then the item indices expected on each output line.
        let pages: [([Item], [[Int]])] = [
            ([("Invoice", CGPoint(x: 60, y: 600), 20), ("#12345", CGPoint(x: 400, y: 600), 20), ("Due", CGPoint(x: 60, y: 500), 20), ("July 14", CGPoint(x: 400, y: 500), 20)], [[0, 1], [2, 3]]),
            ([("Total", CGPoint(x: 60, y: 400), 20), ("Qty 7", CGPoint(x: 300, y: 400), 20), ("$42.00", CGPoint(x: 450, y: 400), 20)], [[0, 1, 2]]),
            ([("Total", CGPoint(x: 60, y: 400), 14), ("42.00", CGPoint(x: 400, y: 400), 40)], [[0, 1]]),
            // The tall figure overlaps both small lines; they still stay two lines.
            ([("Subtotal", CGPoint(x: 60, y: 421), 14), ("Total", CGPoint(x: 200, y: 400), 14), ("42.00", CGPoint(x: 400, y: 400), 40)], [[0, 2], [1]]),
            (paragraph, [[0], [1], [2], [3]]),
        ]
        for (items, rows) in pages {
            let (page, boxes) = try scannedPage(items)
            let cells = try PDFOfficeCodec.recognizedCells(page, token: .init())
            let item = { (cell: PDFOfficeCodec.TextCell) in boxes.indices.min { hypot(boxes[$0].midX - cell.rect.midX, boxes[$0].midY - cell.rect.midY) < hypot(boxes[$1].midX - cell.rect.midX, boxes[$1].midY - cell.rect.midY) } ?? 0 }
            XCTAssertEqual(Set(cells.map(item)).count, items.count, "\(cells.map(\.text))")
            let text = Dictionary(cells.map { (item($0), $0.text) }, uniquingKeysWith: { first, _ in first })
            XCTAssertEqual(try PDFOfficeCodec.text(page, token: .init()), rows.map { $0.compactMap { text[$0] }.joined(separator: " ") }.joined(separator: "\n"), "\(cells.map { ($0.text, $0.rect) })")
        }
        XCTAssertEqual(try PDFOfficeCodec.text(try scannedPage([]).0, token: .init()), "")
    }

    /// Outline-only text (no text layer) on a letter page; returns each item's box.
    private func scannedPage(_ items: [(String, CGPoint, CGFloat)]) throws -> (PDFPage, [CGRect]) {
        let data = NSMutableData()
        var media = CGRect(x: 0, y: 0, width: 612, height: 792)
        let ctx = try XCTUnwrap(CGContext(consumer: XCTUnwrap(CGDataConsumer(data: data as CFMutableData)), mediaBox: &media, nil))
        ctx.beginPDFPage(nil)
        var boxes: [CGRect] = []
        for (text, point, size) in items {
            let font = CTFontCreateWithName("Helvetica-Bold" as CFString, size, nil)
            let line = CTLineCreateWithAttributedString(NSAttributedString(string: text, attributes: [NSAttributedString.Key(kCTFontAttributeName as String): font]))
            boxes.append(CTLineGetImageBounds(line, nil).offsetBy(dx: point.x, dy: point.y))
            for run in CTLineGetGlyphRuns(line) as! [CTRun] {
                let n = CTRunGetGlyphCount(run); var glyphs = [CGGlyph](repeating: 0, count: n), positions = [CGPoint](repeating: .zero, count: n)
                CTRunGetGlyphs(run, CFRange(location: 0, length: n), &glyphs); CTRunGetPositions(run, CFRange(location: 0, length: n), &positions)
                for i in 0..<n {
                    guard let path = CTFontCreatePathForGlyph(font, glyphs[i], nil) else { continue }
                    ctx.saveGState(); ctx.translateBy(x: point.x + positions[i].x, y: point.y + positions[i].y); ctx.addPath(path); ctx.fillPath(); ctx.restoreGState()
                }
            }
        }
        ctx.endPDFPage(); ctx.closePDF()
        let document = try XCTUnwrap(PDFDocument(data: data as Data)); documents.append(document)
        return (try XCTUnwrap(document.page(at: 0)), boxes)
    }
}
