import XCTest
import CoreGraphics
@testable import NashiCore

final class ImageLegacyCollageSizeTests: XCTestCase {
    /// Without collage settings (the CLI default), cell size and gap come from width,
    /// height and padding. Values too large for the canvas must be refused, not overflow.
    func testOversizedLegacyCollageCellsAndGapsAreRejectedNotOverflowed() throws {
        let dir = FileManager.default.temporaryDirectory.appendingPathComponent("NashiLegacyCollage-\(UUID().uuidString)")
        try FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
        defer { try? FileManager.default.removeItem(at: dir) }
        let context = try XCTUnwrap(CGContext(data: nil, width: 4, height: 4, bitsPerComponent: 8, bytesPerRow: 0,
                                              space: CGColorSpace(name: CGColorSpace.sRGB)!,
                                              bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue))
        let image = try XCTUnwrap(context.makeImage())
        let inputs = try ["a.png", "b.png"].map { name -> URL in
            let url = dir.appendingPathComponent(name); try ImageEngine.write(image, to: url); return url
        }
        for (width, padding) in [(Int.max / 2, 0), (600, Int.max / 2), (0, 0)] {
            var options = OperationOptions(); options.width = width; options.padding = padding; options.outputDirectory = dir
            XCTAssertThrowsError(try ImageEngine.tool(.createCollage, inputs: inputs, options: options, token: .init())) { error in
                guard case NashiError.invalidInput = error else { return XCTFail("Unexpected error: \(error)") }
            }
        }
        var options = OperationOptions(); options.width = 10; options.height = 20; options.padding = 5; options.outputDirectory = dir
        let output = try XCTUnwrap(ImageEngine.tool(.createCollage, inputs: inputs, options: options, token: .init()).first)
        let collage = try ImageEngine.load(output)
        XCTAssertEqual([collage.width, collage.height], [35, 30])
    }
}
