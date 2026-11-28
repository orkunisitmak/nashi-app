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
                                             (90, letter, CGRect(x: 100, y: 200, width: 300, height: 300), [("CROPPED", CGPoint(x: 40, y: 150), 30)]),
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

    func testRotatedScanWithoutRecognizedTextKeepsTheUprightPageImageOnTheLandscapePage() throws {
        // A letter page shown with /Rotate 90 whose only content is a full-page
        // image drawn turned, so it reads upright: red on the displayed left,
        // blue on the right. OCR finds nothing, so the image is the page.
        let content = "q 0 792 -612 0 612 0 cm /Im Do Q"
        let objects = [
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Rotate 90 /Resources << /XObject << /Im 5 0 R >> >> /Contents 4 0 R >>",
            "<< /Length \(content.utf8.count) >>\nstream\n\(content)\nendstream",
            "<< /Type /XObject /Subtype /Image /Width 2 /Height 1 /ColorSpace /DeviceRGB /BitsPerComponent 8 /Length 6 >>\nstream\n",
        ]
        var data = Data("%PDF-1.7\n".utf8), offsets: [Int] = []
        for (index, object) in objects.enumerated() {
            offsets.append(data.count)
            data.append(Data("\(index + 1) 0 obj\n\(object)".utf8))
            if index == 4 { data.append(contentsOf: [0xFF, 0, 0, 0, 0, 0xFF]); data.append(Data("\nendstream".utf8)) }
            data.append(Data("\nendobj\n".utf8))
        }
        let xref = data.count
        data.append(Data("xref\n0 \(objects.count + 1)\n0000000000 65535 f \n".utf8))
        for offset in offsets { data.append(Data(String(format: "%010d 00000 n \n", offset).utf8)) }
        data.append(Data("trailer\n<< /Size \(objects.count + 1) /Root 1 0 R >>\nstartxref\n\(xref)\n%%EOF\n".utf8))
        let pdf = try XCTUnwrap(PDFDocument(data: data))
        XCTAssertTrue(try PDFOfficeCodec.recognizedCells(XCTUnwrap(pdf.page(at: 0)), token: .init()).isEmpty, "the scan has no recognizable text")

        let root = try OutputFiles.temporaryDirectory(); defer { try? FileManager.default.removeItem(at: root) }
        let xml = try XMLDocument(contentsOf: PDFDocumentReconstruction.document(pdf, at: root.appendingPathComponent("scan.fodt"), options: .init(), token: .init()), options: .nodeLoadExternalEntitiesNever)
        let layout = try XCTUnwrap(xml.nodes(forXPath: "//*[local-name()='page-layout-properties']").first as? XMLElement)
        XCTAssertEqual(layout.attribute(forName: "fo:page-width")?.stringValue, "792.0pt")
        XCTAssertEqual(layout.attribute(forName: "fo:page-height")?.stringValue, "612.0pt")
        let margin = try XCTUnwrap(Double(XCTUnwrap(layout.attribute(forName: "fo:margin")?.stringValue).dropLast(2)))
        let frame = try XCTUnwrap(xml.nodes(forXPath: "//*[local-name()='frame']").first as? XMLElement)
        let width = try XCTUnwrap(Double(XCTUnwrap(frame.attribute(forName: "svg:width")?.stringValue).dropLast(2)))
        let height = try XCTUnwrap(Double(XCTUnwrap(frame.attribute(forName: "svg:height")?.stringValue).dropLast(2)))
        XCTAssertEqual(width / height, 792.0 / 612.0, accuracy: 0.01, "the image keeps the displayed landscape shape")
        XCTAssertLessThanOrEqual(width, 792 - margin * 2 + 0.01)
        XCTAssertLessThanOrEqual(height, 612 - margin * 2 + 0.01, "the image fits the landscape page")
        let png = try XCTUnwrap(frame.nodes(forXPath: ".//*[local-name()='binary-data']").first?.stringValue.flatMap { Data(base64Encoded: $0) }.flatMap(NSBitmapImageRep.init(data:)))
        XCTAssertGreaterThan(png.pixelsWide, png.pixelsHigh)
        let left = try XCTUnwrap(png.colorAt(x: png.pixelsWide / 4, y: png.pixelsHigh / 2)?.usingColorSpace(.deviceRGB))
        let right = try XCTUnwrap(png.colorAt(x: png.pixelsWide * 3 / 4, y: png.pixelsHigh / 2)?.usingColorSpace(.deviceRGB))
        XCTAssertGreaterThan(left.redComponent, 0.8); XCTAssertLessThan(left.blueComponent, 0.2)
        XCTAssertGreaterThan(right.blueComponent, 0.8); XCTAssertLessThan(right.redComponent, 0.2)
    }

    func testDisplayedFrameMapsThroughTheCropBox() throws {
        // The crop box's lower-left corner is the displayed top-left of a
        // /Rotate 90 page. Through the media box it would land at (200, 512).
        let (page, _) = try scannedPage(media: CGRect(x: 0, y: 0, width: 612, height: 792), crop: CGRect(x: 100, y: 200, width: 300, height: 300), rotate: 90, lines: [])
        let shown = PDFOfficeCodec.displayed(page)
        XCTAssertEqual(shown.bounds, CGRect(x: 0, y: 0, width: 300, height: 300))
        let corner = CGPoint(x: 100, y: 200).applying(shown.transform)
        XCTAssertEqual(corner.x, 0, accuracy: 0.001); XCTAssertEqual(corner.y, 300, accuracy: 0.001)
    }

    /// Outline-only text (no text layer) drawn so it reads upright once /Rotate is applied.
    /// Returns each line's box in the displayed frame: zero-origin on a rotated page,
    /// crop-box coordinates on an unrotated one.
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
        let origin = rotate == 0 ? box.origin : .zero
        var expected: [CGRect] = []
        for (text, point, size) in lines {
            let font = CTFontCreateWithName("Helvetica-Bold" as CFString, size, nil)
            let line = CTLineCreateWithAttributedString(NSAttributedString(string: text, attributes: [NSAttributedString.Key(kCTFontAttributeName as String): font]))
            expected.append(CTLineGetImageBounds(line, nil).offsetBy(dx: point.x + origin.x, dy: point.y + origin.y))
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
