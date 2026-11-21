import XCTest
import PDFKit
@testable import NashiCore

/// Metadata cleanup rebuilds the PDF from copied pages. Custom page numbering
/// (i, ii, A-1) lives in the catalog and would be lost, so it must refuse.
final class PDFMetadataPageLabelsTests: XCTestCase {
    var root: URL!
    override func setUpWithError() throws { root = try OutputFiles.temporaryDirectory() }
    override func tearDownWithError() throws { try? FileManager.default.removeItem(at: root) }

    func testRemovingMetadataRefusesCustomPageLabelsInsteadOfRenumberingPages() throws {
        let labelled = try fixture("Labelled.pdf", labels: true), before = try Data(contentsOf: labelled)
        let source = try XCTUnwrap(PDFDocument(url: labelled))
        XCTAssertEqual(source.page(at: 1)?.label, "APX-ii")
        for edit in [false, true] {
            var options = OperationOptions()
            if edit { options.metadata = ["Title": "Edited"] } else { options.metadataRemoveAll = true }
            let folder = root.appendingPathComponent(UUID().uuidString, isDirectory: true)
            try FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true)
            options.outputDirectory = folder
            XCTAssertThrowsError(try ConversionEngine.run(.init(inputs: [labelled], operation: .tool(.removeMetadata), options: options))) { error in
                XCTAssertTrue(error.localizedDescription.contains("custom page labels"), error.localizedDescription)
            }
            XCTAssertEqual(try FileManager.default.contentsOfDirectory(atPath: folder.path), [])
        }
        XCTAssertEqual(try Data(contentsOf: labelled), before)

        var options = OperationOptions()
        options.metadataRemoveAll = true
        options.outputDirectory = root.appendingPathComponent("Plain", isDirectory: true)
        let output = try XCTUnwrap(ConversionEngine.run(.init(inputs: [try fixture("Plain.pdf", labels: false)], operation: .tool(.removeMetadata), options: options)).first)
        let cleaned = try XCTUnwrap(PDFDocument(url: output))
        XCTAssertEqual(cleaned.pageCount, 2)
        XCTAssertNil(cleaned.documentAttributes?[PDFDocumentAttribute.authorAttribute])
    }

    private func fixture(_ name: String, labels: Bool) throws -> URL {
        func stream(_ body: String) -> String { "<< /Length \(body.utf8.count) >>\nstream\n\(body)\nendstream" }
        let objects = [
            "<< /Type /Catalog /Pages 2 0 R \(labels ? "/PageLabels << /Nums [0 << /S /r /P (APX-) >>] >>" : "") >>",
            "<< /Type /Pages /Kids [3 0 R 6 0 R] /Count 2 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 200 200] /Resources << /Font << /F1 4 0 R >> >> /Contents 5 0 R >>",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>",
            stream("BT /F1 12 Tf 20 100 Td (Preface) Tj ET"),
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 200 200] /Resources << /Font << /F1 4 0 R >> >> /Contents 7 0 R >>",
            stream("BT /F1 12 Tf 20 100 Td (Appendix) Tj ET"),
            "<< /Title (Labelled report) /Author (Private author) >>"
        ]
        var data = Data("%PDF-1.7\n".utf8), offsets = [Int]()
        for (index, object) in objects.enumerated() { offsets.append(data.count); data.append(Data("\(index + 1) 0 obj\n\(object)\nendobj\n".utf8)) }
        let xref = data.count
        data.append(Data("xref\n0 \(objects.count + 1)\n0000000000 65535 f \n".utf8))
        for offset in offsets { data.append(Data(String(format: "%010d 00000 n \n", offset).utf8)) }
        data.append(Data("trailer\n<< /Size \(objects.count + 1) /Root 1 0 R /Info 8 0 R >>\nstartxref\n\(xref)\n%%EOF\n".utf8))
        let url = root.appendingPathComponent(name)
        try data.write(to: url)
        return url
    }
}
