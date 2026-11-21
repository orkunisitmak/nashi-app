import XCTest
import CoreGraphics
@testable import NashiCore

final class ImageVectorizerTests: XCTestCase {
    func testTracesBlackShapesIntoPhysicallySizedSVG() throws {
        // 20x10 white canvas, black 6x6 square with a 2x2 hole, plus a transparent corner that must stay white.
        let context = try XCTUnwrap(CGContext(data: nil, width: 20, height: 10, bitsPerComponent: 8, bytesPerRow: 0,
                                              space: CGColorSpace(name: CGColorSpace.sRGB)!,
                                              bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue))
        context.setFillColor(red: 1, green: 1, blue: 1, alpha: 1); context.fill(CGRect(x: 0, y: 0, width: 20, height: 10))
        context.clear(CGRect(x: 18, y: 0, width: 2, height: 2))
        context.setFillColor(red: 0, green: 0, blue: 0, alpha: 1); context.fill(CGRect(x: 2, y: 2, width: 6, height: 6))
        context.setFillColor(red: 1, green: 1, blue: 1, alpha: 1); context.fill(CGRect(x: 4, y: 4, width: 2, height: 2))
        let svg = try ImageVectorizer.svg(try XCTUnwrap(context.makeImage()), dpi: 254)

        XCTAssertTrue(svg.contains("width=\"2mm\" height=\"1mm\" viewBox=\"0 0 20 10\""), svg)
        XCTAssertTrue(svg.contains("fill-rule=\"evenodd\""))
        let d = try XCTUnwrap(svg.components(separatedBy: " d=\"").last?.components(separatedBy: "\"").first)
        XCTAssertEqual(d.components(separatedBy: "M").count - 1, 2, "outer square + hole: \(d)")
        for corner in ["2 2", "8 2", "8 8", "2 8", "4 4", "6 4", "6 6", "4 6"] { XCTAssertTrue(d.contains(corner), "\(corner) missing in \(d)") }
    }

    func testSimplifyStraightensStaircase() {
        var loop: [(Double, Double)] = []
        for i in 0..<10 { loop += [(Double(i), Double(i)), (Double(i + 1), Double(i))] }
        loop += [(10, 10), (0, 10)]
        XCTAssertLessThanOrEqual(ImageVectorizer.simplify(loop, epsilon: 0.75).count, 4)
    }
}
