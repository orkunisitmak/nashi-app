import XCTest
import AppKit
import PDFKit
@testable import NashiCore

// a cancelled PDF split removes only the " - Pages" folder it created.
// Cancellation is gated on the progress callback; no sleeps.
final class PDFSplitCancellationCleanupTests: XCTestCase {
    private var root: URL!
    override func setUpWithError() throws { root = try OutputFiles.temporaryDirectory() }
    override func tearDownWithError() throws { try? FileManager.default.removeItem(at: root) }

    private func threePagePDF() throws -> URL {
        let ctx = CGContext(data: nil, width: 40, height: 40, bitsPerComponent: 8, bytesPerRow: 0, space: CGColorSpaceCreateDeviceRGB(), bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue)!
        ctx.setFillColor(CGColor(red: 0, green: 0, blue: 1, alpha: 1)); ctx.fill(CGRect(x: 0, y: 0, width: 40, height: 40))
        let png = root.appendingPathComponent("Page.png"); try ImageEngine.write(ctx.makeImage()!, to: png)
        var options = OperationOptions(); options.outputDirectory = root
        return try PDFEngine.create(inputs: [png, png, png], options: options, token: .init())[0]
    }

    private func folder(_ name: String) throws -> URL {
        let url = root.appendingPathComponent(name); try FileManager.default.createDirectory(at: url, withIntermediateDirectories: true); return url
    }
    private func tree(_ url: URL) -> [String] { ((FileManager.default.enumerator(atPath: url.path)?.allObjects as? [String]) ?? []).sorted() }

    private func split(_ input: URL, into dir: URL, cancelAt prefix: String?) -> Error? {
        var options = OperationOptions(); options.outputDirectory = dir
        let token = CancellationToken()
        do {
            _ = try PDFEngine.tool(.splitPDF, inputs: [input], options: options, token: token) { _, label in
                if let prefix, label.hasPrefix(prefix) { token.cancel() }
            }
            return nil
        } catch { return error }
    }

    func testCancelledSplitRemovesOnlyItsOwnFolder() throws {
        let input = try threePagePDF()

        // 1. Original reproduction: cancel after the first chunk.
        let a = try folder("first")
        let errorA = split(input, into: a, cancelAt: "Saved 1 of 3")
        guard case NashiError.cancelled? = errorA else { return XCTFail("expected cancellation") }
        XCTAssertEqual(tree(a), [])

        // 2. Boundary: cancel after the second-to-last chunk.
        let b = try folder("penultimate")
        let errorB = split(input, into: b, cancelAt: "Saved 2 of 3")
        guard case NashiError.cancelled? = errorB else { return XCTFail("expected cancellation") }
        XCTAssertEqual(tree(b), [])

        // 3. Ownership: an existing " - Pages" folder from an earlier export survives.
        let c = try folder("existing")
        let earlier = c.appendingPathComponent("Page - Pages"); try FileManager.default.createDirectory(at: earlier, withIntermediateDirectories: true)
        try Data("earlier".utf8).write(to: earlier.appendingPathComponent("Pages 001-001.pdf"))
        let errorC = split(input, into: c, cancelAt: "Saved 1 of 3")
        guard case NashiError.cancelled? = errorC else { return XCTFail("expected cancellation") }
        XCTAssertEqual(tree(c), ["Page - Pages", "Page - Pages/Pages 001-001.pdf"])
        XCTAssertEqual(try Data(contentsOf: earlier.appendingPathComponent("Pages 001-001.pdf")), Data("earlier".utf8))

        // 4. An uncancelled split keeps every chunk.
        let d = try folder("complete")
        XCTAssertNil(split(input, into: d, cancelAt: nil))
        XCTAssertEqual(tree(d).filter { $0.hasSuffix(".pdf") }.count, 3)

    }
}
