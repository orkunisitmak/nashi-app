import XCTest
import AppKit
import PDFKit
@testable import NashiCore

// the rasterising PDF compressor removes its own oversized pass output when
// cancelled, and never removes a file it did not write. Cancellation is gated on the
// progress callback; no sleeps.
final class PDFCompressCancellationCleanupTests: XCTestCase {
    private var root: URL!
    override func setUpWithError() throws { root = try OutputFiles.temporaryDirectory() }
    override func tearDownWithError() throws { try? FileManager.default.removeItem(at: root) }

    private func imageOnlyPDF() throws -> URL {
        let ctx = CGContext(data: nil, width: 300, height: 200, bitsPerComponent: 8, bytesPerRow: 0, space: CGColorSpaceCreateDeviceRGB(), bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue)!
        var seed: UInt32 = 7
        for y in 0..<200 { for x in 0..<300 where (x + y) % 3 == 0 {
            seed = seed &* 1664525 &+ 1013904223
            ctx.setFillColor(CGColor(red: CGFloat(seed & 255) / 255, green: CGFloat((seed >> 8) & 255) / 255, blue: CGFloat((seed >> 16) & 255) / 255, alpha: 1))
            ctx.fill(CGRect(x: x, y: y, width: 1, height: 1))
        } }
        let png = root.appendingPathComponent("Scan.png"); try ImageEngine.write(ctx.makeImage()!, to: png)
        var options = OperationOptions(); options.outputDirectory = root
        return try PDFEngine.create(inputs: [png, png], options: options, token: .init())[0]
    }

    private func folder(_ name: String) throws -> URL {
        let url = root.appendingPathComponent(name); try FileManager.default.createDirectory(at: url, withIntermediateDirectories: true); return url
    }
    private func contents(_ url: URL) -> [String] { ((try? FileManager.default.contentsOfDirectory(atPath: url.path)) ?? []).sorted() }

    /// Cancels when the progress label contains `marker`; returns whether the job ended cancelled.
    private func compress(_ input: URL, into dir: URL, target: Int64?, cancelAt marker: String?) -> Error? {
        var options = OperationOptions(); options.outputDirectory = dir; options.targetBytes = target
        let token = CancellationToken()
        do {
            _ = try PDFEngine.tool(.compress, inputs: [input], options: options, token: token) { _, label in
                if let marker, label.contains(marker) { token.cancel() }
            }
            return nil
        } catch { return error }
    }

    func testCancelledCompressionLeavesNoOutputAndKeepsForeignFiles() throws {
        let input = try imageOnlyPDF()

        // 1. Original reproduction: cancel during the second pass.
        let a = try folder("second-pass")
        let errorA = compress(input, into: a, target: 1, cancelAt: "attempt 2 ")
        guard case NashiError.cancelled? = errorA else { return XCTFail("expected cancellation") }
        XCTAssertEqual(contents(a), [])

        // 2. Boundary: cancel on the first page of the first pass (nothing written yet).
        //    A foreign file already in the folder must survive.
        let b = try folder("first-page")
        let foreign = b.appendingPathComponent("Scan - Compressed.pdf"); try Data("foreign".utf8).write(to: foreign)
        let errorB = compress(input, into: b, target: 1, cancelAt: "attempt 1 · page 1")
        guard case NashiError.cancelled? = errorB else { return XCTFail("expected cancellation") }
        XCTAssertEqual(contents(b), ["Scan - Compressed.pdf"]); XCTAssertEqual(try Data(contentsOf: foreign), Data("foreign".utf8))

        // 3. Cancel during the last page of a later pass; a foreign sibling survives.
        let c = try folder("late")
        let sibling = c.appendingPathComponent("Other.pdf"); try Data("other".utf8).write(to: sibling)
        let errorC = compress(input, into: c, target: 1, cancelAt: "attempt 4 · page 2")
        guard case NashiError.cancelled? = errorC else { return XCTFail("expected cancellation") }
        XCTAssertEqual(contents(c), ["Other.pdf"])

        // 4. Unreachable target still fails cleanly; reachable (no target) still keeps output.
        let d = try folder("unreachable")
        let errorD = compress(input, into: d, target: 1, cancelAt: nil)
        XCTAssertNotNil(errorD); XCTAssertEqual(contents(d), [])
        let e = try folder("success")
        XCTAssertNil(compress(input, into: e, target: nil, cancelAt: nil))
        XCTAssertEqual(contents(e), ["Scan - Compressed.pdf"])
        XCTAssertEqual(try PDFEngine.open(e.appendingPathComponent("Scan - Compressed.pdf")).pageCount, 2)

    }
}
