import XCTest
import AppKit
import PDFKit
import CoreText
@testable import NashiCore

/// A page whose CropBox (100,100,200,200) shows only part of its 400pt MediaBox.
/// Content outside the CropBox is not part of the displayed page and must not
/// reappear in OCR, editable reconstruction or raster compression output.
final class PDFCompressCropBoxTests: XCTestCase {
    var root: URL!
    override func setUpWithError() throws { root = try OutputFiles.temporaryDirectory() }
    override func tearDownWithError() throws { try? FileManager.default.removeItem(at: root) }

    func testRasterCompressionKeepsOnlyTheCropBoxView() throws {
        let input = try croppedPDF("compress.pdf", vectorText: false, outsideImage: true)
        var options = OperationOptions(); options.outputDirectory = root
        let output = try XCTUnwrap(PDFEngine.tool(.compress, inputs: [input], options: options, token: .init()).first)
        XCTAssertLessThan(try Data(contentsOf: output).count, try Data(contentsOf: input).count)
        let page = try XCTUnwrap(PDFDocument(url: output)?.page(at: 0))
        XCTAssertEqual(page.bounds(for: .cropBox).size, CGSize(width: 200, height: 200))
        let thumb = page.thumbnail(of: NSSize(width: 200, height: 200), for: .cropBox)
        let rep = NSBitmapImageRep(cgImage: try XCTUnwrap(thumb.cgImage(forProposedRect: nil, context: nil, hints: nil)))
        func color(_ x: Int, _ yFromBottom: Int) -> NSColor { rep.colorAt(x: x, y: rep.pixelsHigh - 1 - yFromBottom)!.usingColorSpace(.deviceRGB)! }
        XCTAssertGreaterThan(color(25, 25).blueComponent, 0.8); XCTAssertLessThan(color(25, 25).redComponent, 0.3)
        XCTAssertFalse((0..<rep.pixelsWide).contains { color($0, 1).redComponent > 0.8 && color($0, 1).greenComponent < 0.3 }, "cropped-away band stays hidden")
    }

    private func croppedPDF(_ name: String, vectorText: Bool, outsideImage: Bool) throws -> URL {
        let url = root.appendingPathComponent(name)
        let consumer = try XCTUnwrap(CGDataConsumer(url: url as CFURL))
        var media = CGRect(x: 0, y: 0, width: 400, height: 400), crop = CGRect(x: 100, y: 100, width: 200, height: 200)
        let ctx = try XCTUnwrap(CGContext(consumer: consumer, mediaBox: &media, nil))
        ctx.beginPDFPage([kCGPDFContextMediaBox: Data(bytes: &media, count: MemoryLayout<CGRect>.size), kCGPDFContextCropBox: Data(bytes: &crop, count: MemoryLayout<CGRect>.size)] as CFDictionary)
        ctx.setFillColor(NSColor.red.cgColor); ctx.fill(CGRect(x: 0, y: 0, width: 400, height: 90))
        if outsideImage {
            // Large noisy image outside the CropBox keeps the source bigger than its raster.
            let w = 1500; var bytes = [UInt8](repeating: 0, count: w * w * 4); var seed: UInt32 = 7
            for i in 0..<bytes.count { seed = seed &* 1664525 &+ 1013904223; bytes[i] = i % 4 == 3 ? 255 : UInt8(truncatingIfNeeded: seed >> 24) }
            let image = try bytes.withUnsafeMutableBytes { raw in
                try XCTUnwrap(CGContext(data: raw.baseAddress, width: w, height: w, bitsPerComponent: 8, bytesPerRow: w * 4, space: CGColorSpaceCreateDeviceRGB(), bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue)?.makeImage())
            }
            ctx.draw(image, in: CGRect(x: 0, y: 0, width: 40, height: 40))
        }
        ctx.setFillColor(NSColor.blue.cgColor); ctx.fill(CGRect(x: 110, y: 110, width: 30, height: 30))
        let font = CTFontCreateWithName("Helvetica" as CFString, 24, nil)
        for (text, point) in [("VISIBLE", CGPoint(x: 120, y: 250)), ("HIDDEN", CGPoint(x: 10, y: 360))] {
            let line = CTLineCreateWithAttributedString(NSAttributedString(string: text, attributes: [NSAttributedString.Key(kCTFontAttributeName as String): font]))
            if vectorText { ctx.textPosition = point; CTLineDraw(line, ctx); continue }
            // Glyph outlines only, so the page has no extractable text and needs OCR.
            ctx.setFillColor(NSColor.black.cgColor)
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
        return url
    }
}
