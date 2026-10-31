import XCTest
import AppKit
import PDFKit
@testable import NashiCore

/// Compressing a password-protected PDF must not produce a copy that opens
/// without the password, including scans with no selectable text.
final class PDFCompressPasswordTests: XCTestCase {
    var root: URL!
    override func setUpWithError() throws { root = try OutputFiles.temporaryDirectory() }
    override func tearDownWithError() throws { try? FileManager.default.removeItem(at: root) }

    func testCompressingAPasswordProtectedScanKeepsItsPassword() throws {
        // One large photo-like image stored losslessly and no text, as in a scan.
        let width = 1200, height = 900
        var pixels = [UInt8](repeating: 0, count: width * height * 4)
        for y in 0..<height { for x in 0..<width {
            let i = (y * width + x) * 4
            pixels[i] = UInt8((x * 255) / width); pixels[i + 1] = UInt8((y * 255) / height); pixels[i + 2] = UInt8(((x + y) * 7) % 256); pixels[i + 3] = 255
        } }
        let context = try XCTUnwrap(CGContext(data: &pixels, width: width, height: height, bitsPerComponent: 8, bytesPerRow: width * 4, space: CGColorSpaceCreateDeviceRGB(), bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue))
        let document = PDFDocument()
        document.insert(try XCTUnwrap(PDFPage(image: NSImage(cgImage: try XCTUnwrap(context.makeImage()), size: NSSize(width: 600, height: 450)))), at: 0)
        let input = root.appendingPathComponent("Protected scan.pdf")
        XCTAssertTrue(document.write(to: input, withOptions: [.ownerPasswordOption: "synthetic-owner", .userPasswordOption: "synthetic-password"]))
        XCTAssertTrue(try XCTUnwrap(PDFDocument(url: input)).isLocked)
        let before = try Data(contentsOf: input)

        var options = OperationOptions()
        options.password = "synthetic-password"
        options.outputDirectory = root.appendingPathComponent("Compressed", isDirectory: true)
        let output = try XCTUnwrap(ConversionEngine.run(.init(inputs: [input], operation: .tool(.compress), options: options)).first)
        let compressed = try XCTUnwrap(PDFDocument(url: output))
        XCTAssertTrue(compressed.isLocked)
        XCTAssertFalse(compressed.unlock(withPassword: "incorrect"))
        XCTAssertTrue(compressed.unlock(withPassword: "synthetic-password"))
        XCTAssertEqual(compressed.pageCount, 1)
        XCTAssertEqual(try Data(contentsOf: input), before)
    }
}
