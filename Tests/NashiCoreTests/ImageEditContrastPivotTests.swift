import XCTest
import CoreGraphics
import ImageIO
@testable import NashiCore

final class ImageEditContrastPivotTests: XCTestCase {
    private let space = CGColorSpace(name: CGColorSpace.sRGB)!

    private func edited(gray: Int, _ configure: (inout OperationOptions) -> Void) throws -> Int {
        let directory = try OutputFiles.temporaryDirectory()
        defer { try? FileManager.default.removeItem(at: directory) }
        let context = try XCTUnwrap(CGContext(data: nil, width: 8, height: 8, bitsPerComponent: 8, bytesPerRow: 0,
            space: space, bitmapInfo: CGImageAlphaInfo.noneSkipLast.rawValue))
        let level = CGFloat(gray) / 255
        context.setFillColor(CGColor(srgbRed: level, green: level, blue: level, alpha: 1))
        context.fill(CGRect(x: 0, y: 0, width: 8, height: 8))
        let input = directory.appendingPathComponent("Gray.png")
        try ImageEngine.write(try XCTUnwrap(context.makeImage()), to: input)

        var options = OperationOptions()
        options.outputDirectory = directory
        configure(&options)
        let output = try XCTUnwrap(ImageEngine.tool(.editImage, inputs: [input], options: options, token: .init()).first)
        let image = try XCTUnwrap(CGImageSourceCreateImageAtIndex(try XCTUnwrap(CGImageSourceCreateWithURL(output as CFURL, nil)), 0, nil))
        var pixel = [UInt8](repeating: 0, count: 4)
        let read = try XCTUnwrap(CGContext(data: &pixel, width: 1, height: 1, bitsPerComponent: 8, bytesPerRow: 4, space: space,
            bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue))
        read.draw(image, in: CGRect(x: 0, y: 0, width: 1, height: 1))
        return Int(pixel[0])
    }

    func testContrastKeepsMidGrayInPlace() throws {
        XCTAssertEqual(Double(try edited(gray: 128) { $0.imageAdjustments.contrast = 1.5 }), 128, accuracy: 3)
        XCTAssertEqual(Double(try edited(gray: 128) { $0.imageAdjustments.contrast = 2 }), 128, accuracy: 3)
    }

    func testDehazeDoesNotBlackOutMidGray() throws {
        XCTAssertGreaterThan(try edited(gray: 128) { $0.imageAdjustments.dehaze = 1 }, 96)
    }

    func testUntouchedSlidersLeaveGrayAlone() throws {
        XCTAssertEqual(Double(try edited(gray: 100) { _ in }), 100, accuracy: 1)
    }
}
