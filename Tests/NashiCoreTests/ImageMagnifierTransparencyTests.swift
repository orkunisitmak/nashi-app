import XCTest
import CoreGraphics
import ImageIO
@testable import NashiCore

final class ImageMagnifierTransparencyTests: XCTestCase {
    private let space = CGColorSpace(name: CGColorSpace.sRGB)!

    /// 40x40 with a red block at x 24..<30, y 18..<22 and a half-transparent blue band at x 4..<12
    /// (top-left pixels), on a transparent or opaque white background.
    private func image(opaque: Bool) throws -> CGImage {
        let context = try XCTUnwrap(CGContext(data: nil, width: 40, height: 40, bitsPerComponent: 8, bytesPerRow: 0,
            space: space, bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue))
        if opaque {
            context.setFillColor(CGColor(colorSpace: space, components: [1, 1, 1, 1])!)
            context.fill(CGRect(x: 0, y: 0, width: 40, height: 40))
        }
        context.setFillColor(CGColor(colorSpace: space, components: [1, 0, 0, 1])!)
        context.fill(CGRect(x: 24, y: 18, width: 6, height: 4))
        context.setFillColor(CGColor(colorSpace: space, components: [0, 0, 1, 0.5])!)
        context.fill(CGRect(x: 4, y: 0, width: 8, height: 40))
        return try XCTUnwrap(context.makeImage())
    }

    /// Straight RGBA of a top-left pixel.
    private func pixel(_ image: CGImage, x: Int, y: Int) throws -> [Int] {
        var bytes = [UInt8](repeating: 0, count: image.width * image.height * 4)
        try bytes.withUnsafeMutableBytes { data in
            let context = try XCTUnwrap(CGContext(data: data.baseAddress, width: image.width, height: image.height,
                bitsPerComponent: 8, bytesPerRow: image.width * 4, space: space,
                bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue | CGBitmapInfo.byteOrder32Big.rawValue))
            context.draw(image, in: CGRect(x: 0, y: 0, width: image.width, height: image.height))
        }
        let index = (y * image.width + x) * 4, alpha = Int(bytes[index + 3])
        return (0..<3).map { alpha == 0 ? 0 : Int((Double(bytes[index + $0]) * 255 / Double(alpha)).rounded()) } + [alpha]
    }

    /// Exports the default 2x magnifier over the whole image, so lens x shows source x 10 + x/2.
    private func magnified(_ source: CGImage, name: String, in directory: URL) throws -> CGImage {
        let input = directory.appendingPathComponent("\(name).png")
        try ImageEngine.write(source, to: input)
        var options = OperationOptions(); options.outputDirectory = directory
        options.imageAnnotations = [ImageAnnotation(kind: .magnifier, rect: EditRect(x: 0, y: 0, width: 40, height: 40))]
        let output = try XCTUnwrap(ImageEngine.tool(.annotateImage, inputs: [input], options: options, token: .init()).first)
        let reader = try XCTUnwrap(CGImageSourceCreateWithURL(output as CFURL, nil))
        return try XCTUnwrap(CGImageSourceCreateImageAtIndex(reader, 0, nil))
    }

    func testMagnifierShowsTransparentMagnifiedPixelsInsteadOfTheOriginalUnderneath() throws {
        let directory = try OutputFiles.temporaryDirectory()
        defer { try? FileManager.default.removeItem(at: directory) }
        let output = try magnified(try image(opaque: false), name: "Transparent", in: directory)
        // Lens (25,20) shows transparent source (22.75,20) above the original red block;
        // lens (8,20) shows transparent source (14,20) above the original blue band.
        XCTAssertEqual(try pixel(output, x: 25, y: 20)[3], 0, "The unmagnified red block must not show inside the lens.")
        XCTAssertEqual(try pixel(output, x: 8, y: 20)[3], 0, "The unmagnified blue band must not show inside the lens.")
        XCTAssertEqual(try pixel(output, x: 33, y: 20), [255, 0, 0, 255], "The magnified red block is drawn.")
        XCTAssertEqual(try pixel(output, x: 6, y: 1), [0, 0, 255, 128], "Pixels outside the lens stay unchanged.")
    }

    func testMagnifierOnOpaqueImagesShowsOnlyTheMagnifiedArea() throws {
        let directory = try OutputFiles.temporaryDirectory()
        defer { try? FileManager.default.removeItem(at: directory) }
        let output = try magnified(try image(opaque: true), name: "Opaque", in: directory)
        XCTAssertEqual(try pixel(output, x: 25, y: 20), [255, 255, 255, 255])
        XCTAssertEqual(try pixel(output, x: 33, y: 20), [255, 0, 0, 255])
        XCTAssertEqual(try pixel(output, x: 1, y: 1), [255, 255, 255, 255])
    }
}
