import XCTest
import AppKit
import PDFKit
@testable import NashiCore

/// Raster compression of image-only PDFs must keep each page's displayed size,
/// orientation and whole content, including pages that carry a /Rotate entry.
final class PDFCompressRasterGeometryTests: XCTestCase {
    var root: URL!
    override func setUpWithError() throws { root = try OutputFiles.temporaryDirectory() }
    override func tearDownWithError() throws { try? FileManager.default.removeItem(at: root) }

    func testRasterCompressionKeepsDisplayedPageGeometry() throws {
        // Page k is rotated by 90 * k degrees.
        let input = try imageOnlyPDF(rotations: [0, 90, 180, 270])
        var options = OperationOptions(); options.outputDirectory = root
        let output = try XCTUnwrap(PDFEngine.tool(.compress, inputs: [input], options: options, token: .init()).first)
        XCTAssertLessThan(try Data(contentsOf: output).count, try Data(contentsOf: input).count, "raster path was taken")
        let source = try XCTUnwrap(PDFDocument(url: input)), result = try XCTUnwrap(PDFDocument(url: output))
        XCTAssertEqual(result.pageCount, 4)
        for index in 0..<4 {
            XCTAssertEqual(try appearance(XCTUnwrap(result.page(at: index))), try appearance(XCTUnwrap(source.page(at: index))), "page \(index + 1)")
        }
    }

    /// A US Letter page with a coloured square in each corner and no text or
    /// catalog extras, padded with no-op operators so its raster is smaller.
    private func imageOnlyPDF(rotations: [Int]) throws -> URL {
        let ops = "1 0 0 rg 0 692 100 100 re f 0 1 0 rg 512 692 100 100 re f 0 0 1 rg 0 0 100 100 re f 0 0 0 rg 512 0 100 100 re f " + String(repeating: "q Q ", count: 300_000)
        var objects = ["<< /Type /Catalog /Pages 2 0 R >>",
                       "<< /Type /Pages /Kids [\(rotations.indices.map { "\($0 + 4) 0 R" }.joined(separator: " "))] /Count \(rotations.count) >>",
                       "<< /Length \(ops.utf8.count) >>\nstream\n\(ops)\nendstream"]
        for rotation in rotations { objects.append("<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Rotate \(rotation) /Resources << >> /Contents 3 0 R >>") }
        var pdf = "%PDF-1.4\n", offsets: [Int] = []
        for (index, object) in objects.enumerated() { offsets.append(pdf.utf8.count); pdf += "\(index + 1) 0 obj\n\(object)\nendobj\n" }
        let xref = pdf.utf8.count
        pdf += "xref\n0 \(objects.count + 1)\n0000000000 65535 f \n" + offsets.map { String(format: "%010d 00000 n \n", $0) }.joined()
        pdf += "trailer\n<< /Size \(objects.count + 1) /Root 1 0 R >>\nstartxref\n\(xref)\n%%EOF\n"
        let url = root.appendingPathComponent("scan.pdf"); try Data(pdf.utf8).write(to: url); return url
    }

    /// Displayed size and the colour near each displayed corner.
    private func appearance(_ page: PDFPage) throws -> String {
        let box = page.bounds(for: .mediaBox)
        let shown = abs(page.rotation) % 180 == 90 ? CGSize(width: box.height, height: box.width) : box.size
        let thumb = page.thumbnail(of: NSSize(width: shown.width / 4, height: shown.height / 4), for: .mediaBox)
        let rep = NSBitmapImageRep(cgImage: try XCTUnwrap(thumb.cgImage(forProposedRect: nil, context: nil, hints: nil)))
        func name(_ x: Int, _ y: Int) -> String {
            guard let c = rep.colorAt(x: x, y: y)?.usingColorSpace(.deviceRGB) else { return "none" }
            let channels = [c.redComponent, c.greenComponent, c.blueComponent].map { $0 > 0.5 ? "1" : "0" }
            return channels.joined()
        }
        let w = rep.pixelsWide, h = rep.pixelsHigh, inset = 6
        return "\(Int(shown.width))x\(Int(shown.height)) \(name(inset, inset)) \(name(w - 1 - inset, inset)) \(name(inset, h - 1 - inset)) \(name(w - 1 - inset, h - 1 - inset))"
    }
}
