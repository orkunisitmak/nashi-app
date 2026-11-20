import XCTest
import CoreGraphics
import ImageIO
@testable import NashiCore

final class ImageRedactionTransparencyTests: XCTestCase {
    private let space = CGColorSpace(name: CGColorSpace.sRGB)!

    /// Opaque black 1-pixel columns, every other column, in x 16..<48, y 12..<36.
    private func columns(opaqueBackground: Bool) throws -> CGImage {
        let context = try XCTUnwrap(CGContext(data: nil, width: 64, height: 48, bitsPerComponent: 8, bytesPerRow: 0,
            space: space, bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue))
        if opaqueBackground {
            context.setFillColor(CGColor(red: 1, green: 1, blue: 1, alpha: 1))
            context.fill(CGRect(x: 0, y: 0, width: 64, height: 48))
        }
        context.setFillColor(CGColor(red: 0, green: 0, blue: 0, alpha: 1))
        for x in stride(from: 16, to: 48, by: 2) { context.fill(CGRect(x: x, y: 12, width: 1, height: 24)) }
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

    /// Positions where the column pattern is still readable: x differs from x+1 and
    /// x+1 differs from x+2 by at least 32 levels in some RGBA channel.
    private func readableColumns(_ pixels: [UInt8]) -> Int {
        func contrast(_ i: Int) -> Int { (0..<4).map { abs(Int(pixels[i + $0]) - Int(pixels[i + 4 + $0])) }.max()! }
        var count = 0
        for y in 12..<36 { for x in 16..<46 { let i = (y * 64 + x) * 4; if contrast(i) >= 32, contrast(i + 4) >= 32 { count += 1 } } }
        return count
    }

    func testBlurAndPixelateReplaceTransparentAreasInsteadOfDrawingOverThem() throws {
        let directory = try OutputFiles.temporaryDirectory()
        defer { try? FileManager.default.removeItem(at: directory) }
        // App defaults on a transparent image, and an oversized pixel size (JSON/CLI)
        // whose pixellated blocks come back transparent.
        let cases: [(name: String, opaque: Bool, style: ImageRedactionStyle, pixelSize: Double)] = [
            ("Blur", false, .blur, 16), ("Pixelate", false, .pixelate, 16), ("Large Blocks", true, .pixelate, 64),
        ]
        for item in cases {
            let source = try columns(opaqueBackground: item.opaque)
            XCTAssertEqual(readableColumns(try rgba(source)), 720, item.name)
            let input = directory.appendingPathComponent("\(item.name).png")
            try ImageEngine.write(source, to: input)
            var settings = ImageRedactionOptions(); settings.style = item.style; settings.pixelSize = item.pixelSize
            var options = OperationOptions(); options.imageRedaction = settings
            options.redactions = [EditRect(x: 16, y: 12, width: 32, height: 24)]
            let preview = try ImageEngine.preview(source, tool: .redactImage, options: options)
            let output = try XCTUnwrap(ImageEngine.tool(.redactImage, inputs: [input], options: options, token: .init()).first)
            let reader = try XCTUnwrap(CGImageSourceCreateWithURL(output as CFURL, nil))
            let exported = try XCTUnwrap(CGImageSourceCreateImageAtIndex(reader, 0, nil))
            let original = try rgba(source)
            for rendered in [preview, exported] {
                let pixels = try rgba(rendered)
                XCTAssertEqual(readableColumns(pixels), 0, "\(item.name): the redacted columns must not show through.")
                XCTAssertEqual(Array(pixels[0..<4]), Array(original[0..<4]), "\(item.name): pixels outside the area stay unchanged.")
            }
        }
    }
}
