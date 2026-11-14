import XCTest
import CoreGraphics
@testable import NashiCore

/// The frame shadow used to be cast by filling the image area with opaque black, so
/// every transparent pixel of a logo or window screenshot came out black.
final class ImageFrameShadowTransparencyTests: XCTestCase {
    /// 100 × 100 with a red 20 × 20 square in the centre; the rest is `fill`, or transparent.
    private func source(fill: CGColor?) throws -> CGImage {
        let context = try XCTUnwrap(CGContext(data: nil, width: 100, height: 100, bitsPerComponent: 8, bytesPerRow: 0,
            space: CGColorSpace(name: CGColorSpace.sRGB)!, bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue))
        if let fill { context.setFillColor(fill); context.fill(CGRect(x: 0, y: 0, width: 100, height: 100)) }
        context.setFillColor(CGColor(red: 1, green: 0, blue: 0, alpha: 1)); context.fill(CGRect(x: 40, y: 40, width: 20, height: 20))
        return try XCTUnwrap(context.makeImage())
    }

    /// RGBA measured from the top left.
    private func pixel(_ image: CGImage, _ x: Int, _ y: Int) throws -> [Int] {
        var pixel = [UInt8](repeating: 0, count: 4)
        try pixel.withUnsafeMutableBytes { data in
            let context = try XCTUnwrap(CGContext(data: data.baseAddress, width: 1, height: 1, bitsPerComponent: 8, bytesPerRow: 4,
                space: CGColorSpace(name: CGColorSpace.sRGB)!, bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue | CGBitmapInfo.byteOrder32Big.rawValue))
            context.draw(try XCTUnwrap(image.cropping(to: CGRect(x: x, y: y, width: 1, height: 1))), in: CGRect(x: 0, y: 0, width: 1, height: 1))
        }
        return pixel.map(Int.init)
    }

    private func framed(_ image: CGImage, background: String = "#FFFFFF") throws -> CGImage {
        var frame = ImageFrameOptions(); frame.padding = 40; frame.cornerRadius = 0; frame.backgroundColor = background
        XCTAssertGreaterThan(frame.shadowRadius, 0); XCTAssertGreaterThan(frame.shadowOpacity, 0)
        var options = OperationOptions(); options.imageFrame = frame
        return try ImageEngine.framedImage(image, options: options)
    }

    /// A 101 × 60 image on a Square 421 × 421 canvas has 180.5 px of free space above and below.
    /// Drawn half a pixel off the grid, every pixel was blended with its neighbour.
    func testImageOnAnOddSizedCanvasStaysOnWholePixels() throws {
        let context = try XCTUnwrap(CGContext(data: nil, width: 101, height: 60, bitsPerComponent: 8, bytesPerRow: 0,
            space: CGColorSpace(name: CGColorSpace.sRGB)!, bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue))
        for y in 0..<60 { for x in 0..<101 { context.setFillColor(CGColor(gray: CGFloat((x + y) % 2), alpha: 1)); context.fill(CGRect(x: x, y: 59 - y, width: 1, height: 1)) } }
        var frame = ImageFrameOptions(); frame.padding = 160; frame.cornerRadius = 0; frame.shadowRadius = 0; frame.backgroundColor = "#FF0000"; frame.aspectRatio = 1
        var options = OperationOptions(); options.imageFrame = frame
        let result = try ImageEngine.framedImage(try XCTUnwrap(context.makeImage()), options: options)
        XCTAssertEqual(result.width, 421); XCTAssertEqual(result.height, 421)
        let left = 160, top = 181
        for (x, y) in [(0, 0), (1, 0), (0, 1), (50, 30), (51, 30), (100, 59), (99, 59)] {
            let value = (x + y) % 2 == 1 ? 255 : 0
            XCTAssertEqual(try pixel(result, left + x, top + y), [value, value, value, 255], "image pixel \(x),\(y)")
        }
        XCTAssertEqual(try pixel(result, left - 1, top), [255, 0, 0, 255]); XCTAssertEqual(try pixel(result, left + 101, top + 59), [255, 0, 0, 255])
        XCTAssertEqual(try pixel(result, left, top - 1), [255, 0, 0, 255]); XCTAssertEqual(try pixel(result, left + 100, top + 60), [255, 0, 0, 255])
    }

    func testTransparentPixelsShowTheFrameBackgroundBehindTheDefaultShadow() throws {
        let result = try framed(try source(fill: nil))
        XCTAssertEqual(try pixel(result, 45, 45), [255, 255, 255, 255], "A transparent source pixel must not turn black")
        XCTAssertEqual(try pixel(result, 90, 90)[0], 255, "The visible content is still drawn")
        XCTAssertLessThan(try pixel(result, 90, 104)[0], 250, "The visible content still casts a shadow")
        let glass = try framed(try source(fill: nil), background: "#FFFFFF00")
        XCTAssertLessThan(try pixel(glass, 45, 45)[3], 40, "A transparent frame keeps the image's transparency")
    }

    func testOpaqueImagesKeepTheirShadow() throws {
        let result = try framed(try source(fill: CGColor(red: 0, green: 0, blue: 1, alpha: 1)))
        XCTAssertGreaterThan(try pixel(result, 45, 45)[2], 240)
        XCTAssertLessThan(try pixel(result, 90, 145)[0], 240, "The shadow below the image is still painted")
        XCTAssertEqual(try pixel(result, 5, 5), [255, 255, 255, 255])
    }
}
