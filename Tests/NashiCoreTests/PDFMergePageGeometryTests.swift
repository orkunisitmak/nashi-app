import XCTest
import PDFKit
@testable import NashiCore

/// Merged pages are copied under the first document's page tree. A page that
/// leaves /Rotate or /CropBox at its default must not take the first document's
/// inherited rotation or crop, or it comes out turned and cropped.
final class PDFMergePageGeometryTests: XCTestCase {
    var root: URL!
    override func setUpWithError() throws { root = try OutputFiles.temporaryDirectory() }
    override func tearDownWithError() throws { try? FileManager.default.removeItem(at: root) }

    func testMergedPagesDoNotInheritTheFirstDocumentsRotationOrCrop() throws {
        let first = try source("First", pages: "/MediaBox [0 0 400 300] /Rotate 90 /CropBox [0 0 100 100]", page: "")
        let second = try source("Second", pages: "", page: "/MediaBox [0 0 400 300]")
        var options = OperationOptions(); options.outputDirectory = root
        let merged = try XCTUnwrap(PDFDocument(url: try XCTUnwrap(PDFEngine.tool(.mergePDF, inputs: [first, second], options: options, token: .init()).first)))
        let a = try XCTUnwrap(merged.page(at: 0)), b = try XCTUnwrap(merged.page(at: 1))
        XCTAssertEqual(a.rotation, 90); XCTAssertEqual(a.bounds(for: .cropBox), CGRect(x: 0, y: 0, width: 100, height: 100))
        XCTAssertEqual(b.rotation, 0); XCTAssertEqual(b.bounds(for: .cropBox), CGRect(x: 0, y: 0, width: 400, height: 300))
    }

    private func source(_ name: String, pages: String, page: String) throws -> URL {
        let content = "1 0 0 rg 120 130 40 20 re f"
        let objects = ["<< /Type /Catalog /Pages 2 0 R >>", "<< /Type /Pages /Kids [3 0 R] /Count 1 \(pages) >>",
                       "<< /Type /Page /Parent 2 0 R /Contents 4 0 R /Resources << >> \(page) >>",
                       "<< /Length \(content.utf8.count) >>\nstream\n\(content)\nendstream"]
        var output = "%PDF-1.7\n", offsets: [Int] = []
        for (index, body) in objects.enumerated() { offsets.append(output.utf8.count); output += "\(index + 1) 0 obj\n\(body)\nendobj\n" }
        let xref = output.utf8.count
        output += "xref\n0 \(objects.count + 1)\n0000000000 65535 f \n"
        for offset in offsets { output += String(format: "%010d 00000 n \n", offset) }
        output += "trailer\n<< /Size \(objects.count + 1) /Root 1 0 R >>\nstartxref\n\(xref)\n%%EOF\n"
        let url = root.appendingPathComponent("\(name).pdf"); try Data(output.utf8).write(to: url); return url
    }
}
