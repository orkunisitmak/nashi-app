import XCTest
import PDFKit
@testable import NashiCore

/// Small pictures are often written inline in the page content (BI ... ID ... EI,
/// ISO 32000-1, 8.9.7) instead of as Image XObjects. They are still placed page
/// images and must survive editable PDF reconstruction.
final class PDFPlacedInlineImagesTests: XCTestCase {
    func testFindsInlineImagesOnPagesAndInForms() throws {
        let inline = "BI /W 2 /H 2 /CS /RGB /BPC 8 ID \u{ff}\u{00}\u{00}\u{ff}\u{00}\u{00}\u{ff}\u{00}\u{00}\u{ff}\u{00}\u{00} EI"
        let form = "q 50 0 0 40 0 0 cm \(inline) Q"
        let objects = [
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 300 300] /Resources << /XObject << /Fm1 5 0 R >> >> /Contents 4 0 R >>",
            stream("q 80 0 0 90 100 200 cm \(inline) Q q 1 0 0 1 10 20 cm /Fm1 Do Q", dictionary: ""),
            stream(form, dictionary: "/Type /XObject /Subtype /Form /BBox [0 0 300 300]")
        ]
        var pdf = Data("%PDF-1.4\n".utf8), offsets: [Int] = []
        for (index, object) in objects.enumerated() {
            offsets.append(pdf.count)
            pdf += Data("\(index + 1) 0 obj\n".utf8) + Data(object.unicodeScalars.map { UInt8($0.value) }) + Data("\nendobj\n".utf8)
        }
        let xref = pdf.count
        pdf += Data("xref\n0 \(objects.count + 1)\n0000000000 65535 f \n".utf8)
        for offset in offsets { pdf += Data(String(format: "%010d 00000 n \n", offset).utf8) }
        pdf += Data("trailer\n<< /Size \(objects.count + 1) /Root 1 0 R >>\nstartxref\n\(xref)\n%%EOF\n".utf8)
        let document = try XCTUnwrap(PDFDocument(data: pdf)), page = try XCTUnwrap(document.page(at: 0))
        XCTAssertEqual(try PDFPlacedImages.regions(page, token: .init()), [CGRect(x: 100, y: 200, width: 80, height: 90), CGRect(x: 10, y: 20, width: 50, height: 40)])
        let root = try OutputFiles.temporaryDirectory(); defer { try? FileManager.default.removeItem(at: root) }
        let xml = try String(contentsOf: PDFDocumentReconstruction.document(document, at: root.appendingPathComponent("inline.fodt"), options: .init(), token: .init()), encoding: .utf8)
        XCTAssertTrue(xml.contains("draw:name=\"PDF image 1\"") && xml.contains("draw:name=\"PDF image 2\""), "the editable document keeps both inline pictures")
    }

    private func stream(_ content: String, dictionary: String) -> String {
        "<< \(dictionary) /Length \(content.unicodeScalars.count) >>\nstream\n\(content)\nendstream"
    }
}
