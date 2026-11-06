import XCTest
import CoreGraphics
import ImageIO
@testable import NashiCore

final class ImageUnequalDPIRegressionTests: XCTestCase {
    private let sRGB = CGColorSpace(name: CGColorSpace.sRGB)!

    /// 80 x 120 pixels with four differently coloured quadrants.
    private func quadrants() -> CGImage {
        let context = CGContext(data: nil, width: 80, height: 120, bitsPerComponent: 8, bytesPerRow: 0,
                                space: sRGB, bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue)!
        let colors = [CGColor(red: 1, green: 0, blue: 0, alpha: 1), CGColor(red: 0, green: 1, blue: 0, alpha: 1),
                      CGColor(red: 0, green: 0, blue: 1, alpha: 1), CGColor(red: 1, green: 1, blue: 0, alpha: 1)]
        for (index, color) in colors.enumerated() {
            context.setFillColor(color)
            context.fill(CGRect(x: (index % 2) * 40, y: (index / 2) * 60, width: 40, height: 60))
        }
        return context.makeImage()!
    }

    private func pixels(_ image: CGImage) -> [UInt8] {
        var bytes = [UInt8](repeating: 0, count: image.width * image.height * 4)
        let context = CGContext(data: &bytes, width: image.width, height: image.height, bitsPerComponent: 8,
                                bytesPerRow: image.width * 4, space: sRGB,
                                bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue)!
        context.draw(image, in: CGRect(x: 0, y: 0, width: image.width, height: image.height))
        return bytes
    }

    private func save(_ url: URL, type: String, dpi: (Int, Int), orientation: Int) throws {
        let destination = try XCTUnwrap(CGImageDestinationCreateWithURL(url as CFURL, type as CFString, 1, nil))
        let properties: [CFString: Any] = [kCGImagePropertyDPIWidth: dpi.0, kCGImagePropertyDPIHeight: dpi.1,
                                           kCGImagePropertyOrientation: orientation,
                                           kCGImageDestinationLossyCompressionQuality: 1]
        CGImageDestinationAddImage(destination, quadrants(), properties as CFDictionary)
        XCTAssertTrue(CGImageDestinationFinalize(destination))
    }

    func testUnequalHorizontalAndVerticalDPIKeepsStoredPixels() throws {
        let directory = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
        defer { try? FileManager.default.removeItem(at: directory) }
        let cases: [(String, String, (Int, Int), Int)] = [
            ("jpg", "public.jpeg", (300, 72), 1), ("jpg", "public.jpeg", (72, 300), 6),
            ("png", "public.png", (72, 144), 1), ("tif", "public.tiff", (204, 98), 8), ("jpg", "public.jpeg", (1, 65535), 3)
        ]
        for (index, (ext, type, dpi, orientation)) in cases.enumerated() {
            let reference = directory.appendingPathComponent("reference\(index).\(ext)")
            let scan = directory.appendingPathComponent("scan\(index).\(ext)")
            try save(reference, type: type, dpi: (72, 72), orientation: orientation)
            try save(scan, type: type, dpi: dpi, orientation: orientation)
            let expected = try ImageEngine.load(reference), loaded = try ImageEngine.load(scan)
            let label = "\(ext) \(dpi.0)x\(dpi.1) dpi, orientation \(orientation)"
            XCTAssertEqual(orientation >= 5 ? [120, 80] : [80, 120], [expected.width, expected.height], label)
            XCTAssertEqual([loaded.width, loaded.height], [expected.width, expected.height], label)
            if loaded.width == expected.width && loaded.height == expected.height {
                let difference = zip(pixels(loaded), pixels(expected)).map { abs(Int($0) - Int($1)) }.max() ?? 0
                XCTAssertLessThanOrEqual(difference, 2, label)
            }
            var options = OperationOptions()
            options.outputDirectory = directory
            options.preserveMetadata = false
            let converted = try ImageEngine.convert(scan, to: "png", options: options, token: .init())[0]
            let output = try XCTUnwrap(CGImageSourceCreateWithURL(converted as CFURL, nil).flatMap { CGImageSourceCreateImageAtIndex($0, 0, nil) })
            XCTAssertEqual([output.width, output.height], [expected.width, expected.height], label)
        }
    }
}
