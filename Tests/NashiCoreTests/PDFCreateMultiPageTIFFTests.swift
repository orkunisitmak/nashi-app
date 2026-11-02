import XCTest
import AppKit
import PDFKit
import ImageIO
import UniformTypeIdentifiers
@testable import NashiCore

/// Creating a PDF from a multi-page TIFF (a scanned or faxed document) must
/// keep every page, in order, while an animated GIF stays a single page.
final class PDFCreateMultiPageTIFFTests: XCTestCase {
    var root: URL!
    override func setUpWithError() throws { root = try OutputFiles.temporaryDirectory() }
    override func tearDownWithError() throws { try? FileManager.default.removeItem(at: root) }

    func testMultiPageTIFFKeepsEveryPageInCreatedPDF() throws {
        let pages: [(width: Int, height: Int, colour: [CGFloat])] = [(40, 30, [1, 0, 0]), (50, 20, [0, 1, 0]), (30, 60, [0, 0, 1])]
        let tiff = try write(pages.map { image($0.width, $0.height, $0.colour) }, name: "Scan.tiff", type: .tiff)
        let png = try write([image(20, 20, [0, 0, 0])], name: "Cover.png", type: .png)
        let gif = try write(pages.map { image(40, 30, $0.colour) }, name: "Animation.gif", type: .gif)
        var options = OperationOptions(); options.outputDirectory = root

        let created = try XCTUnwrap(PDFDocument(url: PDFEngine.create(inputs: [png, tiff], options: options, token: .init())[0]))
        XCTAssertEqual(created.pageCount, 4)
        XCTAssertEqual(try appearance(XCTUnwrap(created.page(at: 0))), "20x20 0,0,0")
        for (index, page) in pages.enumerated() {
            let expected = "\(page.width)x\(page.height) " + page.colour.map { String(Int($0 * 255)) }.joined(separator: ",")
            XCTAssertEqual(try appearance(XCTUnwrap(created.page(at: index + 1))), expected, "TIFF page \(index + 1)")
        }
        let converted = try XCTUnwrap(PDFDocument(url: ImageEngine.convert(tiff, to: "pdf", options: options, token: .init())[0]))
        XCTAssertEqual(converted.pageCount, 3)
        XCTAssertEqual(try XCTUnwrap(PDFDocument(url: PDFEngine.create(inputs: [gif], options: options, token: .init())[0])).pageCount, 1)
    }

    private func image(_ width: Int, _ height: Int, _ colour: [CGFloat]) -> CGImage {
        let context = CGContext(data: nil, width: width, height: height, bitsPerComponent: 8, bytesPerRow: 0, space: CGColorSpace(name: CGColorSpace.sRGB)!, bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue)!
        context.setFillColor(CGColor(srgbRed: colour[0], green: colour[1], blue: colour[2], alpha: 1)); context.fill(CGRect(x: 0, y: 0, width: width, height: height))
        return context.makeImage()!
    }

    private func write(_ images: [CGImage], name: String, type: UTType) throws -> URL {
        let url = root.appendingPathComponent(name)
        let destination = try XCTUnwrap(CGImageDestinationCreateWithURL(url as CFURL, type.identifier as CFString, images.count, nil))
        for image in images { CGImageDestinationAddImage(destination, image, nil) }
        XCTAssertTrue(CGImageDestinationFinalize(destination)); return url
    }

    /// Page size in points and the centre colour, rounded to 0 or 255 per channel.
    private func appearance(_ page: PDFPage) throws -> String {
        let box = page.bounds(for: .mediaBox)
        let context = try XCTUnwrap(CGContext(data: nil, width: 1, height: 1, bitsPerComponent: 8, bytesPerRow: 4, space: CGColorSpace(name: CGColorSpace.sRGB)!, bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue))
        context.translateBy(x: -box.midX, y: -box.midY); page.draw(with: .mediaBox, to: context)
        let pixel = try XCTUnwrap(context.data).assumingMemoryBound(to: UInt8.self)
        return "\(Int(box.width))x\(Int(box.height)) " + (0..<3).map { pixel[$0] > 127 ? "255" : "0" }.joined(separator: ",")
    }
}
