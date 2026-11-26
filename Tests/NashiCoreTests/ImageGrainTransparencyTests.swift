import XCTest
import CoreGraphics
import ImageIO
@testable import NashiCore

final class ImageGrainTransparencyTests: XCTestCase {
    private let space = CGColorSpace(name: CGColorSpace.sRGB)!

    /// 40x20: x 0..<10 transparent, 10..<20 grey at alpha 0.5, 20..<40 opaque grey.
    private func image() throws -> CGImage {
        let context = try XCTUnwrap(CGContext(data: nil, width: 40, height: 20, bitsPerComponent: 8, bytesPerRow: 0,
            space: space, bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue))
        context.setFillColor(CGColor(colorSpace: space, components: [0.5, 0.5, 0.5, 0.5])!)
        context.fill(CGRect(x: 10, y: 0, width: 10, height: 20))
        context.setFillColor(CGColor(colorSpace: space, components: [0.5, 0.5, 0.5, 1])!)
        context.fill(CGRect(x: 20, y: 0, width: 20, height: 20))
        return try XCTUnwrap(context.makeImage())
    }

    private func rgba(_ image: CGImage) throws -> [UInt8] {
        var bytes = [UInt8](repeating: 0, count: image.width * image.height * 4)
        try bytes.withUnsafeMutableBytes { data in
            let context = try XCTUnwrap(CGContext(data: data.baseAddress, width: image.width, height: image.height,
                bitsPerComponent: 8, bytesPerRow: image.width * 4, space: space,
                bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue | CGBitmapInfo.byteOrder32Big.rawValue))
            context.draw(image, in: CGRect(x: 0, y: 0, width: image.width, height: image.height))
        }
        return bytes
    }

    private func edited(grain: Double) throws -> [UInt8] {
        let directory = try OutputFiles.temporaryDirectory()
        defer { try? FileManager.default.removeItem(at: directory) }
        let input = directory.appendingPathComponent("Grain.png")
        try ImageEngine.write(try image(), to: input)
        var options = OperationOptions(); options.outputDirectory = directory; options.imageAdjustments.grain = grain
        let output = try XCTUnwrap(ImageEngine.tool(.editImage, inputs: [input], options: options, token: .init()).first)
        let reader = try XCTUnwrap(CGImageSourceCreateWithURL(output as CFURL, nil))
        return try rgba(try XCTUnwrap(CGImageSourceCreateImageAtIndex(reader, 0, nil)))
    }

    private func values(_ pixels: [UInt8], columns: Range<Int>, channel: Int) -> [UInt8] {
        (0..<20).flatMap { y in columns.map { x in pixels[(y * 40 + x) * 4 + channel] } }
    }

    func testGrainKeepsTransparentAndTranslucentPixelsAlpha() throws {
        let pixels = try edited(grain: 1)
        XCTAssertEqual(Set(values(pixels, columns: 0..<10, channel: 3)), [0], "Transparent pixels must stay transparent.")
        XCTAssertEqual(Set(values(pixels, columns: 10..<20, channel: 3)), [128], "Half-transparent pixels keep their alpha.")
        XCTAssertGreaterThan(Set(values(pixels, columns: 10..<20, channel: 0)).count, 4, "Half-transparent pixels still get grain.")
    }

    func testGrainStillTexturesOpaquePixels() throws {
        let pixels = try edited(grain: 1)
        XCTAssertEqual(Set(values(pixels, columns: 20..<40, channel: 3)), [255])
        XCTAssertGreaterThan(Set(values(pixels, columns: 20..<40, channel: 0)).count, 8)
    }
}
