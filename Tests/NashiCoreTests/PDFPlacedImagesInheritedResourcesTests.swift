import XCTest
import PDFKit
@testable import NashiCore

/// PDF pages may inherit /Resources from their /Pages tree node (ISO 32000-1,
/// 7.7.3.4). Images placed through those inherited resources are still real
/// page images and must survive editable PDF reconstruction.
final class PDFPlacedImagesInheritedResourcesTests: XCTestCase {
    func testFindsImagesPlacedThroughInheritedResources() throws {
        let objects = [
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 /Resources << /XObject << /Im1 5 0 R >> >> >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 300 300] /Contents 4 0 R >>",
            stream("q 80 0 0 90 100 200 cm /Im1 Do Q", dictionary: ""),
            stream("\u{ff}\u{00}\u{00}\u{00}\u{ff}\u{00}\u{00}\u{00}\u{ff}\u{ff}\u{ff}\u{ff}", dictionary: "/Type /XObject /Subtype /Image /Width 2 /Height 2 /ColorSpace /DeviceRGB /BitsPerComponent 8")
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
        let root = try OutputFiles.temporaryDirectory(); defer { try? FileManager.default.removeItem(at: root) }
        let xml = try String(contentsOf: PDFDocumentReconstruction.document(document, at: root.appendingPathComponent("image.fodt"), options: .init(), token: .init()), encoding: .utf8)
        XCTAssertTrue(xml.contains("draw:name=\"PDF image 1\""), "the editable document keeps the placed image")
        let regions = try PDFPlacedImages.regions(page, token: .init())
        XCTAssertEqual(regions.count, 1)
        let rect = try XCTUnwrap(regions.first)
        XCTAssertEqual(rect.minX, 100, accuracy: 0.1); XCTAssertEqual(rect.minY, 200, accuracy: 0.1)
        XCTAssertEqual(rect.width, 80, accuracy: 0.1); XCTAssertEqual(rect.height, 90, accuracy: 0.1)
    }

    private func stream(_ content: String, dictionary: String) -> String {
        "<< \(dictionary) /Length \(content.unicodeScalars.count) >>\nstream\n\(content)\nendstream"
    }
}
