import XCTest
import CoreGraphics
@testable import NashiCore

/// When quality alone cannot meet a target size, Compress shrinks the image. Each step used to
/// truncate the previous step's sides, so a thin strip kept losing width after its height hit
/// one pixel: 2000 × 3 came out as 1280 × 1, nearly twice as wide for its height.
final class ImageCompressResizeFallbackTests: XCTestCase {
    /// Seeded RGB noise, so PNG sizes shrink predictably with the pixel count.
    private func noise(width: Int, height: Int) throws -> CGImage {
        var state: UInt64 = 7, bytes = [UInt8](repeating: 255, count: width * height * 4)
        for pixel in 0..<(width * height) { for channel in 0..<3 { state = state &* 6364136223846793005 &+ 1442695040888963407; bytes[pixel * 4 + channel] = UInt8(truncatingIfNeeded: state >> 56) } }
        let context = try XCTUnwrap(CGContext(data: &bytes, width: width, height: height, bitsPerComponent: 8, bytesPerRow: width * 4,
            space: CGColorSpace(name: CGColorSpace.sRGB)!, bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue))
        return try XCTUnwrap(context.makeImage())
    }

    private func size(_ url: URL) throws -> Int64 { try XCTUnwrap(FileManager.default.attributesOfItem(atPath: url.path)[.size] as? NSNumber).int64Value }

    /// True when one common scale rounds to both sides; a 1-pixel side also covers any smaller value.
    private func keepsShape(_ image: CGImage, of width: Int, _ height: Int) -> Bool {
        func scales(_ side: Int, _ source: Int) -> ClosedRange<Double> { (side == 1 ? 0 : (Double(side) - 0.5) / Double(source))...((Double(side) + 0.5) / Double(source)) }
        return scales(image.width, width).overlaps(scales(image.height, height))
    }

    func testTargetSizeResizeKeepsTheAspectRatioOfThinImages() throws {
        let folder = try OutputFiles.temporaryDirectory(); defer { try? FileManager.default.removeItem(at: folder) }
        for (width, height) in [(2000, 3), (4, 1500), (300, 200)] {
            let input = folder.appendingPathComponent("\(width)x\(height).png"); try ImageEngine.write(try noise(width: width, height: height), to: input)
            var options = OperationOptions(); options.outputDirectory = folder; options.targetBytes = try size(input) / 4
            let output = try XCTUnwrap(ImageEngine.compress(input, options: options, token: .init()).first), result = try ImageEngine.load(output)
            XCTAssertLessThanOrEqual(try size(output), options.targetBytes!)
            XCTAssertTrue(keepsShape(result, of: width, height), "\(width) × \(height) became \(result.width) × \(result.height)")
        }
    }

    func testTargetBelowTheOnePixelEdgeIsRefusedWithoutOutput() throws {
        let folder = try OutputFiles.temporaryDirectory(); defer { try? FileManager.default.removeItem(at: folder) }
        let input = folder.appendingPathComponent("strip.png"); try ImageEngine.write(try noise(width: 2000, height: 3), to: input)
        var options = OperationOptions(); options.outputDirectory = folder; options.targetBytes = 10
        XCTAssertThrowsError(try ImageEngine.compress(input, options: options, token: .init()))
        XCTAssertEqual(try FileManager.default.contentsOfDirectory(atPath: folder.path), ["strip.png"])
    }
}
