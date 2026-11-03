import XCTest
import AppKit
import PDFKit
@testable import NashiCore

// Split PDF names each file by the original page numbers it holds, not by its position in the selection.
final class PDFSplitFileNamesTests: XCTestCase {
    private var root: URL!
    override func setUpWithError() throws { root = try OutputFiles.temporaryDirectory() }
    override func tearDownWithError() throws { try? FileManager.default.removeItem(at: root) }

    func testSplitNamesFollowOriginalPageNumbers() throws {
        let ctx = CGContext(data: nil, width: 40, height: 40, bitsPerComponent: 8, bytesPerRow: 0, space: CGColorSpaceCreateDeviceRGB(), bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue)!
        ctx.setFillColor(CGColor(red: 0, green: 0, blue: 1, alpha: 1)); ctx.fill(CGRect(x: 0, y: 0, width: 40, height: 40))
        let png = root.appendingPathComponent("Page.png"); try ImageEngine.write(ctx.makeImage()!, to: png)
        var options = OperationOptions(); options.outputDirectory = root
        let input = try PDFEngine.create(inputs: [png, png, png, png, png], options: options, token: .init())[0]

        options.pageRanges = "3-5"; options.pagesPerFile = 2
        let outputs = try PDFEngine.tool(.splitPDF, inputs: [input], options: options, token: .init())

        XCTAssertEqual(outputs.map(\.lastPathComponent), ["Pages 003-004.pdf", "Pages 005-005.pdf"])
        XCTAssertEqual(outputs.map { PDFDocument(url: $0)?.pageCount }, [2, 1])
    }

    func testRepeatedPagesKeepEverySplitPart() throws {
        let ctx = CGContext(data: nil, width: 40, height: 40, bitsPerComponent: 8, bytesPerRow: 0, space: CGColorSpaceCreateDeviceRGB(), bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue)!
        ctx.setFillColor(CGColor(red: 0, green: 0, blue: 1, alpha: 1)); ctx.fill(CGRect(x: 0, y: 0, width: 40, height: 40))
        let png = root.appendingPathComponent("Page.png"); try ImageEngine.write(ctx.makeImage()!, to: png)
        var options = OperationOptions(); options.outputDirectory = root
        let input = try PDFEngine.create(inputs: [png, png, png], options: options, token: .init())[0]

        options.pageRanges = "2-3,2-3"; options.pagesPerFile = 1
        let outputs = try PDFEngine.tool(.splitPDF, inputs: [input], options: options, token: .init())

        XCTAssertEqual(outputs.map(\.lastPathComponent), ["Pages 002-002.pdf", "Pages 003-003.pdf", "Pages 002-002 2.pdf", "Pages 003-003 2.pdf"])
        XCTAssertTrue(outputs.allSatisfy { FileManager.default.fileExists(atPath: $0.path) })
    }
}
