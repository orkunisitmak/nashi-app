import XCTest
import CoreGraphics
@testable import NashiCore

/// Short #RGBA codes used to be read as #00RGBA (so "#F00A" painted green), and an
/// unreadable code silently became white or another color in the exported image.
final class ImageHexColorTests: XCTestCase {
    private func rgba(_ hex: String, file: StaticString = #filePath, line: UInt = #line) throws -> [Int] {
        let color = try XCTUnwrap(ImageEngine.parsedColor(hex), "\(hex) was not read", file: file, line: line)
        return try XCTUnwrap(color.components).map { Int(($0 * 255).rounded()) }
    }

    func testEveryHexLengthReadsRedGreenBlueAndAlpha() throws {
        XCTAssertEqual(try rgba("#F00"), [255, 0, 0, 255])
        XCTAssertEqual(try rgba("#F00A"), [255, 0, 0, 170])
        XCTAssertEqual(try rgba("#FF0000"), [255, 0, 0, 255])
        XCTAssertEqual(try rgba("#FF000080"), [255, 0, 0, 128])
        XCTAssertEqual(try rgba("00ff00"), [0, 255, 0, 255])
    }

    func testUnreadableCodesAreRejected() {
        for hex in ["", "#", "#12", "#12345", "#GG0000", "#+FFFFF", "red", "#ＦＦ0000", "#FF0000FF00"] {
            XCTAssertNil(ImageEngine.parsedColor(hex), hex)
        }
        // Pickers and swatches keep their lenient white fallback.
        XCTAssertEqual(ImageEngine.color("red").components, [1, 1, 1, 1])
    }

    func testExportsRefuseAnUnreadableColor() throws {
        let image = try XCTUnwrap(CGContext(data: nil, width: 8, height: 8, bitsPerComponent: 8, bytesPerRow: 0,
            space: CGColorSpace(name: CGColorSpace.sRGB)!, bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue)?.makeImage())
        var frame = ImageFrameOptions(); frame.backgroundColor = "#12"
        var options = OperationOptions(); options.imageFrame = frame
        XCTAssertThrowsError(try ImageEngine.framedImage(image, options: options)) { error in
            guard case NashiError.invalidInput(let message) = error else { return XCTFail("\(error)") }
            XCTAssertTrue(message.contains("#12"), message)
        }
        var collage = CollageOptions(); collage.width = 64; collage.backgroundColor = "white"
        XCTAssertThrowsError(try ImageEngine.collageImage([image, image], settings: collage))
        var annotation = ImageAnnotation(kind: .rectangle, rect: EditRect(x: 1, y: 1, width: 4, height: 4)); annotation.color = "#ZZZ"
        options = OperationOptions(); options.imageAnnotations = [annotation]
        XCTAssertThrowsError(try ImageEngine.annotatedImage(image, options: options))
    }

    func testShortRGBAFrameBackgroundKeepsItsColorAndOpacity() throws {
        let image = try XCTUnwrap(CGContext(data: nil, width: 8, height: 8, bitsPerComponent: 8, bytesPerRow: 0,
            space: CGColorSpace(name: CGColorSpace.sRGB)!, bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue)?.makeImage())
        var frame = ImageFrameOptions(); frame.backgroundColor = "#F008"; frame.padding = 4; frame.shadowRadius = 0
        var options = OperationOptions(); options.imageFrame = frame
        let framed = try ImageEngine.framedImage(image, options: options)
        var pixel = [UInt8](repeating: 0, count: 4)
        try pixel.withUnsafeMutableBytes { data in
            let context = try XCTUnwrap(CGContext(data: data.baseAddress, width: 1, height: 1, bitsPerComponent: 8, bytesPerRow: 4,
                space: CGColorSpace(name: CGColorSpace.sRGB)!, bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue | CGBitmapInfo.byteOrder32Big.rawValue))
            context.draw(try XCTUnwrap(framed.cropping(to: CGRect(x: 0, y: 0, width: 1, height: 1))), in: CGRect(x: 0, y: 0, width: 1, height: 1))
        }
        // Premultiplied red at alpha 0x88: no green, and translucent rather than opaque.
        XCTAssertEqual(Double(pixel[3]), 136, accuracy: 1)
        XCTAssertEqual(Double(pixel[0]), 136, accuracy: 1)
        XCTAssertEqual(pixel[1], 0)
    }
}
