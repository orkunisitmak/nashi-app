import XCTest
import AppKit
import PDFKit
import CoreText
@testable import NashiCore

/// A scanned page with /Rotate 90 or 270 is shown upright. OCR boxes must land where
/// the text is displayed (as they already do for /Rotate 0 and 180), long rotated
/// pages must not be shrunk before recognition, and Word output uses the shown size.
final class PDFOCRRotatedPageTests: XCTestCase {
    private var documents: [PDFDocument] = []

    func testOCRBoxesAndWordPageFollowTheDisplayedRotatedPage() throws {
        let letter = CGRect(x: 0, y: 0, width: 612, height: 792)
        let lines: [(String, CGPoint, CGFloat)] = [("FIRST LINE", CGPoint(x: 60, y: 450), 30), ("SECOND", CGPoint(x: 300, y: 150), 30)]
        for (rotate, media, crop, lines) in [(0, letter, nil, lines), (90, letter, nil, lines), (180, letter, nil, lines), (270, letter, nil, lines),
                                             (90, CGRect(x: 0, y: 0, width: 300, height: 3000), nil, [("LONG STRIP RECEIPT", CGPoint(x: 1000, y: 150), 24)]),
                                             (270, CGRect(x: 50, y: 50, width: 612, height: 792), CGRect(x: 100, y: 100, width: 400, height: 500), [("ORIGIN TEXT", CGPoint(x: 60, y: 200), 30)])] as [(Int, CGRect, CGRect?, [(String, CGPoint, CGFloat)])] {
            let (page, expected) = try scannedPage(media: media, crop: crop, rotate: rotate, lines: lines)
            let cells = try PDFOfficeCodec.recognizedCells(page, token: .init())
            for box in expected {
                XCTAssertTrue(cells.contains { abs($0.rect.midX - box.midX) < 12 && abs($0.rect.midY - box.midY) < 12 && abs($0.rect.height - box.height) < 10 }, "/Rotate \(rotate) \(media.size): \(box) not in \(cells.map(\.rect))")
            }
        }
        let (scan, _) = try scannedPage(media: letter, crop: nil, rotate: 90, lines: [("LEFT HEADING", CGPoint(x: 60, y: 500), 24), ("RIGHT SIDE NOTE", CGPoint(x: 560, y: 300), 18)])
        let root = try OutputFiles.temporaryDirectory(); defer { try? FileManager.default.removeItem(at: root) }
        let xml = try String(contentsOf: PDFDocumentReconstruction.document(XCTUnwrap(scan.document), at: root.appendingPathComponent("scan.fodt"), options: .init(), token: .init()), encoding: .utf8)
        XCTAssertTrue(xml.contains("fo:page-width=\"792.0pt\" fo:page-height=\"612.0pt\""), "a rotated scan becomes a landscape Word page")
    }

    /// Outline-only text (no text layer) drawn so it reads upright once /Rotate is applied.
    /// Returns each line's box in the displayed frame, offset by the CropBox origin.
    private func scannedPage(media: CGRect, crop: CGRect?, rotate: Int, lines: [(String, CGPoint, CGFloat)]) throws -> (PDFPage, [CGRect]) {
        let data = NSMutableData(), box = crop ?? media
        var media = media
        let ctx = try XCTUnwrap(CGContext(consumer: XCTUnwrap(CGDataConsumer(data: data as CFMutableData)), mediaBox: &media, nil))
        var info: [CFString: Any] = [kCGPDFContextMediaBox: Data(bytes: &media, count: MemoryLayout<CGRect>.size)]
        if var crop { info[kCGPDFContextCropBox] = Data(bytes: &crop, count: MemoryLayout<CGRect>.size) }
        ctx.beginPDFPage(info as CFDictionary)
        let w = box.width, h = box.height
        let upright: CGAffineTransform = rotate == 90 ? .init(a: 0, b: 1, c: -1, d: 0, tx: w, ty: 0) : rotate == 180 ? .init(a: -1, b: 0, c: 0, d: -1, tx: w, ty: h) : rotate == 270 ? .init(a: 0, b: -1, c: 1, d: 0, tx: 0, ty: h) : .identity
        ctx.concatenate(upright.concatenating(CGAffineTransform(translationX: box.minX, y: box.minY)))
        var expected: [CGRect] = []
        for (text, point, size) in lines {
            let font = CTFontCreateWithName("Helvetica-Bold" as CFString, size, nil)
            let line = CTLineCreateWithAttributedString(NSAttributedString(string: text, attributes: [NSAttributedString.Key(kCTFontAttributeName as String): font]))
            expected.append(CTLineGetImageBounds(line, nil).offsetBy(dx: point.x + box.minX, dy: point.y + box.minY))
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
        let source = try XCTUnwrap(PDFDocument(data: data as Data)); source.page(at: 0)?.rotation = rotate
        let document = try XCTUnwrap(PDFDocument(data: XCTUnwrap(source.dataRepresentation()))); documents.append(document)
        let page = try XCTUnwrap(document.page(at: 0)); XCTAssertEqual(page.rotation, rotate)
        return (page, expected)
    }
}
