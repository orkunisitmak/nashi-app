import XCTest
import AppKit
import PDFKit
@testable import NashiCore

/// Fixed-layout EPUB and "preserve pages" Word export picture each PDF page.
/// That picture must show the page's annotations (stamps, highlights, filled
/// form fields) as viewers and the PNG/JPG export do.
final class PDFRasterAnnotationsTests: XCTestCase {
    var root: URL!
    override func setUpWithError() throws { root = try OutputFiles.temporaryDirectory() }
    override func tearDownWithError() throws { try? FileManager.default.removeItem(at: root) }

    func testPagePictureShowsAnnotations() throws {
        let source = root.appendingPathComponent("Source.pdf")
        var media = CGRect(x: 0, y: 0, width: 200, height: 100)
        let context = try XCTUnwrap(CGContext(consumer: try XCTUnwrap(CGDataConsumer(url: source as CFURL)), mediaBox: &media, nil))
        context.beginPDFPage(nil); context.endPDFPage(); context.closePDF()
        let document = try XCTUnwrap(PDFDocument(url: source)), page = try XCTUnwrap(document.page(at: 0))
        let stamp = PDFAnnotation(bounds: CGRect(x: 50, y: 20, width: 40, height: 30), forType: .square, withProperties: nil)
        stamp.color = .red; stamp.interiorColor = .red; page.addAnnotation(stamp)
        let input = root.appendingPathComponent("Stamped.pdf")
        XCTAssertTrue(document.write(to: input))
        let png = try PDFOfficeCodec.rasterPNG(try XCTUnwrap(PDFDocument(url: input)?.page(at: 0)), dpi: 72, token: .init())
        let image = try XCTUnwrap(NSBitmapImageRep(data: png)?.cgImage)
        XCTAssertEqual(image.width, 200); XCTAssertEqual(image.height, 100)
        let red = redPixels(image)
        XCTAssertEqual(red.width, 40, accuracy: 2); XCTAssertEqual(red.height, 30, accuracy: 2)
        XCTAssertEqual(red.minX, 50, accuracy: 2); XCTAssertEqual(red.minY, 50, accuracy: 2)
    }

    /// Bounding box of the red pixels, measured from the top-left corner.
    private func redPixels(_ image: CGImage) -> CGRect {
        var bytes = [UInt8](repeating: 0, count: image.width * image.height * 4)
        CGContext(data: &bytes, width: image.width, height: image.height, bitsPerComponent: 8, bytesPerRow: image.width * 4, space: CGColorSpaceCreateDeviceRGB(), bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue)!.draw(image, in: CGRect(x: 0, y: 0, width: image.width, height: image.height))
        var box = CGRect.null
        for y in 0..<image.height { for x in 0..<image.width {
            let i = (y * image.width + x) * 4
            if bytes[i] > 200 && bytes[i + 1] < 60 && bytes[i + 2] < 60 { box = box.union(CGRect(x: x, y: y, width: 1, height: 1)) }
        } }
        return box
    }
}
