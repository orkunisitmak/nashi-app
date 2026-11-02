import XCTest
import Foundation
import ImageIO
import PDFKit
@testable import NashiCore

final class PDFCreateScanResolutionTests: XCTestCase {
    private var root: URL!
    override func setUpWithError() throws { root = try OutputFiles.temporaryDirectory() }
    override func tearDownWithError() throws { if let root { try? FileManager.default.removeItem(at: root) } }

    private func image(_ name: String, width: Int, height: Int, dpi: Int?) throws -> URL {
        let context = try XCTUnwrap(CGContext(data: nil, width: width, height: height, bitsPerComponent: 8, bytesPerRow: 0, space: CGColorSpace(name: CGColorSpace.sRGB)!, bitmapInfo: CGImageAlphaInfo.noneSkipLast.rawValue))
        context.setFillColor(CGColor(gray: 1, alpha: 1)); context.fill(CGRect(x: 0, y: 0, width: width, height: height))
        let url = root.appendingPathComponent(name)
        let destination = try XCTUnwrap(CGImageDestinationCreateWithURL(url as CFURL, "public.jpeg" as CFString, 1, nil))
        let properties: [CFString: Any] = dpi.map { [kCGImagePropertyDPIWidth: $0, kCGImagePropertyDPIHeight: $0] } ?? [:]
        CGImageDestinationAddImage(destination, try XCTUnwrap(context.makeImage()), properties as CFDictionary)
        XCTAssertTrue(CGImageDestinationFinalize(destination))
        return url
    }
    private func pageBox(_ input: URL) throws -> CGRect {
        var options = OperationOptions(); options.outputDirectory = root
        let output = try PDFEngine.create(inputs: [input], options: options, token: .init())[0]
        return try XCTUnwrap(PDFDocument(url: output)?.page(at: 0)?.bounds(for: .mediaBox))
    }

    func testA4ScanAt300DPIBecomesAnA4Page() throws {
        let box = try pageBox(image("Scan.jpg", width: 2480, height: 3508, dpi: 300))
        XCTAssertEqual(box.width, 595.2, accuracy: 2); XCTAssertEqual(box.height, 841.9, accuracy: 2)
    }

    func testImagesWithoutAResolutionKeepOnePixelPerPoint() throws {
        let box = try pageBox(image("Plain.jpg", width: 640, height: 480, dpi: nil))
        XCTAssertEqual(box.width, 640, accuracy: 1); XCTAssertEqual(box.height, 480, accuracy: 1)
    }
}
