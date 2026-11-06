import XCTest
import PDFKit
@testable import NashiCore

/// Compression keeps a PDF's title, author, subject and keywords whether it
/// rewrites the file or rasterizes image-only pages.
final class PDFCompressDocumentInfoTests: XCTestCase {
    var root: URL!
    override func setUpWithError() throws { root = try OutputFiles.temporaryDirectory() }
    override func tearDownWithError() throws { try? FileManager.default.removeItem(at: root) }

    func testRasterCompressionKeepsTitleAuthorSubjectAndKeywords() throws {
        // Vector strokes only: no text, so compression rasterizes the page.
        var content = "0 0 1 rg 10 10 300 300 re f\n"
        for i in 0..<20_000 { content += "\(i % 400) \(i % 397) m \((i * 7) % 400) \((i * 13) % 400) l S\n" }
        let objects = [
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 400 400] /Contents 4 0 R >>",
            "<< /Length \(content.utf8.count) >>\nstream\n\(content)\nendstream",
            "<< /Title (Quarterly scan) /Author <FEFF00410079015F0065> /Subject (Sales) /Keywords (q3) >>"
        ]
        var data = Data("%PDF-1.7\n".utf8), offsets = [Int]()
        for (index, object) in objects.enumerated() { offsets.append(data.count); data.append(Data("\(index + 1) 0 obj\n\(object)\nendobj\n".utf8)) }
        let xref = data.count
        data.append(Data("xref\n0 \(objects.count + 1)\n0000000000 65535 f \n".utf8))
        for offset in offsets { data.append(Data(String(format: "%010d 00000 n \n", offset).utf8)) }
        data.append(Data("trailer\n<< /Size \(objects.count + 1) /Root 1 0 R /Info 5 0 R >>\nstartxref\n\(xref)\n%%EOF\n".utf8))
        let input = root.appendingPathComponent("Scan.pdf")
        try data.write(to: input)

        for target in [nil, Int64(30_000)] {
            var options = OperationOptions()
            options.targetBytes = target
            options.outputDirectory = root.appendingPathComponent(UUID().uuidString, isDirectory: true)
            let output = try XCTUnwrap(ConversionEngine.run(.init(inputs: [input], operation: .tool(.compress), options: options)).first)
            XCTAssertLessThan(try Data(contentsOf: output).count, data.count)
            let pdf = try XCTUnwrap(PDFDocument(url: output))
            XCTAssertEqual(pdf.string ?? "", "")
            let attributes = pdf.documentAttributes ?? [:]
            XCTAssertEqual(attributes[PDFDocumentAttribute.titleAttribute] as? String, "Quarterly scan")
            XCTAssertEqual(attributes[PDFDocumentAttribute.authorAttribute] as? String, "Ayşe")
            XCTAssertEqual(attributes[PDFDocumentAttribute.subjectAttribute] as? String, "Sales")
            XCTAssertEqual((attributes[PDFDocumentAttribute.keywordsAttribute] as? [Any])?.map { "\($0)" }, ["q3"])
        }
    }
}
