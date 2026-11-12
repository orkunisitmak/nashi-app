import XCTest
import PDFKit
@testable import NashiCore

/// A page box far beyond the raster limit must be rejected as too large; converting
/// its scaled size to Int first used to trap and end the process.
final class PDFRasterHugePageTests: XCTestCase {
    func testHugePageBoxIsRejectedInsteadOfTrapping() throws {
        for media in ["0 0 100000000000000000000 200", "0 0 200 100000000000000000000", "-100000000000000000000 0 100 200"] {
            XCTAssertThrowsError(try PDFOfficeCodec.rasterPNG(try page(media), dpi: 72, token: .init()), media) { error in
                XCTAssertTrue("\(error)".contains("too large"), "\(error)")
            }
        }
        XCTAssertThrowsError(try PDFOfficeCodec.rasterPNG(try page("0 0 200 200"), rect: CGRect(x: 0, y: 0, width: 1e20, height: 100), dpi: 72, token: .init()))
        XCTAssertNoThrow(try PDFOfficeCodec.rasterPNG(try page("0 0 200 200"), dpi: 72, token: .init()))
    }

    private func page(_ media: String) throws -> PDFPage {
        let body = "0 0 1 rg 0 0 50 50 re f"
        let objects = ["<< /Type /Catalog /Pages 2 0 R >>", "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
                       "<< /Type /Page /Parent 2 0 R /MediaBox [\(media)] /Contents 4 0 R >>", "<< /Length \(body.utf8.count) >>\nstream\n\(body)\nendstream"]
        var pdf = "%PDF-1.4\n", offsets: [Int] = []
        for (index, object) in objects.enumerated() { offsets.append(pdf.utf8.count); pdf += "\(index + 1) 0 obj\n\(object)\nendobj\n" }
        let xref = pdf.utf8.count
        pdf += "xref\n0 \(objects.count + 1)\n0000000000 65535 f \n" + offsets.map { String(format: "%010d 00000 n \n", $0) }.joined()
        pdf += "trailer\n<< /Size \(objects.count + 1) /Root 1 0 R >>\nstartxref\n\(xref)\n%%EOF\n"
        return try XCTUnwrap(PDFDocument(data: Data(pdf.utf8))?.page(at: 0))
    }
}
