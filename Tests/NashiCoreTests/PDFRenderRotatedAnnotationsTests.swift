import XCTest
import AppKit
import PDFKit
@testable import NashiCore

/// Exporting PDF pages as images must show their annotations (stamps,
/// highlights, filled form fields) where viewers show them, including on
/// rotated pages.
final class PDFRenderRotatedAnnotationsTests: XCTestCase {
    var root: URL!
    override func setUpWithError() throws { root = try OutputFiles.temporaryDirectory() }
    override func tearDownWithError() throws { try? FileManager.default.removeItem(at: root) }

    func testRotatedPageKeepsItsAnnotationsInImageExport() throws {
        let source = root.appendingPathComponent("Source.pdf")
        var media = CGRect(x: 0, y: 0, width: 400, height: 200)
        let context = try XCTUnwrap(CGContext(consumer: try XCTUnwrap(CGDataConsumer(url: source as CFURL)), mediaBox: &media, nil))
        context.beginPDFPage(nil); context.endPDFPage(); context.closePDF()
        let document = try XCTUnwrap(PDFDocument(url: source)), page = try XCTUnwrap(document.page(at: 0))
        page.rotation = 90
        let stamp = PDFAnnotation(bounds: CGRect(x: 50, y: 20, width: 40, height: 30), forType: .square, withProperties: nil)
        stamp.color = .red; stamp.interiorColor = .red; page.addAnnotation(stamp)
        let input = root.appendingPathComponent("Rotated.pdf")
        XCTAssertTrue(document.write(to: input))
        var options = OperationOptions(); options.dpi = 72
        let image = try ImageEngine.load(try XCTUnwrap(PDFEngine.render(input, to: "png", options: options, token: .init()).first))
        XCTAssertEqual(image.width, 200); XCTAssertEqual(image.height, 400)
        // Viewers turn the page a quarter clockwise: the stamp lands at x 20-50, 50-90 points from the top.
        let red = redPixels(image)
        XCTAssertEqual(red.width, 30, accuracy: 2); XCTAssertEqual(red.height, 40, accuracy: 2)
        XCTAssertEqual(red.minX, 20, accuracy: 2); XCTAssertEqual(red.minY, 50, accuracy: 2)
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
