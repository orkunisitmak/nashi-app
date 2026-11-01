import XCTest
import CoreGraphics
import ImageIO
@testable import NashiCore

final class ImageAVIFMaximumQualityTests: XCTestCase {
    /// Quality 1 is the top of the documented range (and the value larger ones are clamped to).
    /// ImageIO's AVIF encoder used to fail there, so the export threw "Could not write".
    func testAVIFExportAtQualityOneWritesTheBestLossyQuality() throws {
        let dir = FileManager.default.temporaryDirectory.appendingPathComponent("NashiAVIFQuality-\(UUID().uuidString)")
        try FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
        defer { try? FileManager.default.removeItem(at: dir) }
        let context = try XCTUnwrap(CGContext(data: nil, width: 33, height: 17, bitsPerComponent: 8, bytesPerRow: 0,
                                              space: CGColorSpace(name: CGColorSpace.sRGB)!,
                                              bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue))
        context.setFillColor(red: 0.8, green: 0.3, blue: 0.1, alpha: 0.5)
        context.fill(CGRect(x: 0, y: 0, width: 33, height: 17))
        let image = try XCTUnwrap(context.makeImage())
        let best = dir.appendingPathComponent("best.avif"), reference = dir.appendingPathComponent("reference.avif")
        try ImageEngine.write(image, to: reference, quality: 0.99)
        for quality in [1, 1.5] {
            try? FileManager.default.removeItem(at: best)
            try ImageEngine.write(image, to: best, quality: quality)
            XCTAssertEqual(try Data(contentsOf: best), try Data(contentsOf: reference), "quality \(quality)")
        }
        XCTAssertEqual(try FileManager.default.contentsOfDirectory(atPath: dir.path).sorted(), ["best.avif", "reference.avif"])

        var options = OperationOptions()
        options.quality = 1
        options.outputDirectory = dir.appendingPathComponent("edited")
        try FileManager.default.createDirectory(at: options.outputDirectory!, withIntermediateDirectories: true)
        let outputs = try ImageEngine.tool(.editImage, inputs: [reference], options: options, token: .init())
        let source = try XCTUnwrap(CGImageSourceCreateWithURL(try XCTUnwrap(outputs.first) as CFURL, nil))
        let edited = try XCTUnwrap(CGImageSourceCreateImageAtIndex(source, 0, nil))
        XCTAssertEqual([edited.width, edited.height], [33, 17])
    }
}
