import XCTest
import CoreGraphics
import ImageIO
@testable import NashiCore

final class ImageAnnotateKeepsFormatTests: XCTestCase {
    func testAnnotateKeepsTheSourceFormat() throws {
        let directory = try OutputFiles.temporaryDirectory()
        defer { try? FileManager.default.removeItem(at: directory) }
        let context = try XCTUnwrap(CGContext(data: nil, width: 64, height: 48, bitsPerComponent: 8, bytesPerRow: 0,
            space: CGColorSpace(name: CGColorSpace.sRGB)!, bitmapInfo: CGImageAlphaInfo.noneSkipLast.rawValue))
        context.setFillColor(CGColor(red: 0.2, green: 0.5, blue: 0.8, alpha: 1))
        context.fill(CGRect(x: 0, y: 0, width: 64, height: 48))
        let input = directory.appendingPathComponent("Photo.jpg")
        try ImageEngine.write(try XCTUnwrap(context.makeImage()), to: input, quality: 0.85)

        var options = OperationOptions()
        options.outputDirectory = directory
        options.imageAnnotations = [ImageAnnotation(kind: .rectangle, rect: EditRect(x: 8, y: 8, width: 24, height: 16))]
        let output = try XCTUnwrap(ImageEngine.tool(.annotateImage, inputs: [input], options: options, token: .init()).first)

        XCTAssertEqual(output.lastPathComponent, "Photo - Annotated.jpg")
        let type = CGImageSourceGetType(try XCTUnwrap(CGImageSourceCreateWithURL(output as CFURL, nil)))
        XCTAssertEqual(type as String?, "public.jpeg")
    }
}
