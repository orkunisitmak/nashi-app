import XCTest
@testable import NashiCore

/// A percentage reduction used to be taken from the size of a symlink itself (a few bytes),
/// so compressing a linked PDF by 30% always failed with "The requested PDF size could not be reached."
final class PDFCompressSymlinkPercentageTests: XCTestCase {
    /// An image-only page (no text) padded with no-op operators, so the raster path can shrink it.
    private func imageOnlyPDF(_ url: URL) throws {
        let ops = "1 0 0 rg 0 692 100 100 re f 0 0 1 rg 512 0 100 100 re f " + String(repeating: "q Q ", count: 300_000)
        let objects = ["<< /Type /Catalog /Pages 2 0 R >>", "<< /Type /Pages /Kids [4 0 R] /Count 1 >>",
                       "<< /Length \(ops.utf8.count) >>\nstream\n\(ops)\nendstream",
                       "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << >> /Contents 3 0 R >>"]
        var pdf = "%PDF-1.4\n", offsets: [Int] = []
        for (index, object) in objects.enumerated() { offsets.append(pdf.utf8.count); pdf += "\(index + 1) 0 obj\n\(object)\nendobj\n" }
        let xref = pdf.utf8.count
        pdf += "xref\n0 \(objects.count + 1)\n0000000000 65535 f \n" + offsets.map { String(format: "%010d 00000 n \n", $0) }.joined()
        pdf += "trailer\n<< /Size \(objects.count + 1) /Root 1 0 R >>\nstartxref\n\(xref)\n%%EOF\n"
        try Data(pdf.utf8).write(to: url)
    }

    func testPercentageReductionOfASymlinkUsesTheLinkedPDFSize() throws {
        let folder = try OutputFiles.temporaryDirectory(); defer { try? FileManager.default.removeItem(at: folder) }
        let documents = folder.appendingPathComponent("documents"), links = folder.appendingPathComponent("links")
        for directory in [documents, links] { try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true) }
        let scan = documents.appendingPathComponent("scan.pdf"); try imageOnlyPDF(scan)
        let link = links.appendingPathComponent("scan.pdf"); try FileManager.default.createSymbolicLink(atPath: link.path, withDestinationPath: "../documents/scan.pdf")
        let original = try Data(contentsOf: scan), limit = Int(Double(original.count) * 0.7)
        var options = OperationOptions(); options.percentage = 30; options.outputDirectory = folder
        let output = try XCTUnwrap(PDFEngine.tool(.compress, inputs: [link], options: options, token: .init()).first)
        XCTAssertLessThanOrEqual(try Data(contentsOf: output).count, limit)
        XCTAssertEqual(try FileManager.default.destinationOfSymbolicLink(atPath: link.path), "../documents/scan.pdf")
        XCTAssertEqual(try Data(contentsOf: scan), original)
    }
}
