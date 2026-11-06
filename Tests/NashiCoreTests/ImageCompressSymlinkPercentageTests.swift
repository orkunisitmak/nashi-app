import XCTest
import CoreGraphics
@testable import NashiCore

/// A percentage reduction used to be taken from the size of a symlink itself (a few bytes),
/// so compressing a linked image by 30% always failed with "Choose a larger target".
final class ImageCompressSymlinkPercentageTests: XCTestCase {
    /// Seeded RGB noise, so encoded sizes are large and repeatable.
    private func noise(width: Int, height: Int) throws -> CGImage {
        var state: UInt64 = 7, bytes = [UInt8](repeating: 255, count: width * height * 4)
        for pixel in 0..<(width * height) { for channel in 0..<3 { state = state &* 6364136223846793005 &+ 1442695040888963407; bytes[pixel * 4 + channel] = UInt8(truncatingIfNeeded: state >> 56) } }
        let context = try XCTUnwrap(CGContext(data: &bytes, width: width, height: height, bitsPerComponent: 8, bytesPerRow: width * 4,
            space: CGColorSpace(name: CGColorSpace.sRGB)!, bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue))
        return try XCTUnwrap(context.makeImage())
    }

    private func size(_ url: URL) throws -> Int64 { try XCTUnwrap(FileManager.default.attributesOfItem(atPath: url.path)[.size] as? NSNumber).int64Value }

    func testPercentageReductionOfASymlinkUsesTheLinkedImageSize() throws {
        let folder = try OutputFiles.temporaryDirectory(); defer { try? FileManager.default.removeItem(at: folder) }
        let images = folder.appendingPathComponent("images"), links = folder.appendingPathComponent("links")
        for directory in [images, links] { try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true) }
        let photo = images.appendingPathComponent("photo.jpg"); try ImageEngine.write(try noise(width: 300, height: 200), to: photo, quality: 0.9)
        let link = links.appendingPathComponent("photo.jpg"); try FileManager.default.createSymbolicLink(atPath: link.path, withDestinationPath: "../images/photo.jpg")
        let original = try Data(contentsOf: photo), limit = Int64(Double(original.count) * 0.7)
        var options = OperationOptions(); options.percentage = 30; options.outputDirectory = folder
        let output = try XCTUnwrap(ImageEngine.compress(link, options: options, token: .init()).first)
        XCTAssertLessThanOrEqual(try size(output), limit)
        XCTAssertEqual(try FileManager.default.destinationOfSymbolicLink(atPath: link.path), "../images/photo.jpg")
        XCTAssertEqual(try Data(contentsOf: photo), original)
    }
}
