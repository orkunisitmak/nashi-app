import XCTest
import PDFKit
@testable import NashiCore

/// A Form XObject that draws itself, or a chain of forms that each draw the next
/// several times, must not make the placed-image scan expand without bound.
final class PDFPlacedImagesRecursionTests: XCTestCase {
    func testRecursiveAndFanOutFormsFinishAndKeepTheirImage() throws {
        let draw = "q 50 0 0 50 10 10 cm /Im1 Do Q", image = CGRect(x: 10, y: 10, width: 50, height: 50)
        let selfDrawing = try regions([form("/F1 4 0 R /Im1 5 0 R", "\(draw) /F1 Do /F1 Do /F1 Do"), imageObject])
        XCTAssertLessThan(selfDrawing.seconds, 2); XCTAssertEqual(selfDrawing.rects, [image])
        let chain = (0..<16).map { $0 == 15 ? form("/Im1 20 0 R", draw) : form("/N \($0 + 5) 0 R", "/N Do /N Do /N Do /N Do") }
        let fanOut = try regions(chain + [imageObject])
        XCTAssertLessThan(fanOut.seconds, 2); XCTAssertEqual(fanOut.rects, [image])
        let reused = try regions([form("/Im1 5 0 R", draw), imageObject], content: "q /F1 Do Q q 1 0 0 1 100 100 cm /F1 Do Q")
        XCTAssertEqual(reused.rects, [image, image.offsetBy(dx: 100, dy: 100)], "a form drawn twice side by side is not a cycle")
    }

    private let imageObject = "<< /Type /XObject /Subtype /Image /Width 1 /Height 1 /ColorSpace /DeviceRGB /BitsPerComponent 8 /Length 3 >>\nstream\n\u{ff}\u{0}\u{0}\nendstream"
    private func stream(_ dict: String, _ body: String) -> String { "<< \(dict) /Length \(body.utf8.count) >>\nstream\n\(body)\nendstream" }
    private func form(_ resources: String, _ body: String) -> String {
        stream("/Type /XObject /Subtype /Form /BBox [0 0 200 200] /Resources << /XObject << \(resources) >> >>", body)
    }
    /// One 200pt page whose content draws form object 4; `extra` are objects 4...
    private func regions(_ extra: [String], content: String = "/F1 Do") throws -> (seconds: Double, rects: [CGRect]) {
        let objects = ["<< /Type /Catalog /Pages 2 0 R >>", "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
                       "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 200 200] /Resources << /XObject << /F1 4 0 R >> >> /Contents \(4 + extra.count) 0 R >>"] + extra + [stream("", content)]
        var pdf = "%PDF-1.4\n", offsets: [Int] = []
        for (index, object) in objects.enumerated() { offsets.append(pdf.utf8.count); pdf += "\(index + 1) 0 obj\n\(object)\nendobj\n" }
        let xref = pdf.utf8.count
        pdf += "xref\n0 \(objects.count + 1)\n0000000000 65535 f \n" + offsets.map { String(format: "%010d 00000 n \n", $0) }.joined()
        pdf += "trailer\n<< /Size \(objects.count + 1) /Root 1 0 R >>\nstartxref\n\(xref)\n%%EOF\n"
        let page = try XCTUnwrap(PDFDocument(data: Data(pdf.utf8))?.page(at: 0))
        let token = CancellationToken(); DispatchQueue.global().asyncAfter(deadline: .now() + 10) { token.cancel() }
        let start = Date(), rects = try PDFPlacedImages.regions(page, token: token)
        return (Date().timeIntervalSince(start), rects)
    }
}
