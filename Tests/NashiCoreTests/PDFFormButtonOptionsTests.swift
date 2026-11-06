import XCTest
import PDFKit
@testable import NashiCore

/// A radio group's /Opt holds one export value per widget, in /Kids order. When a
/// page edit drops a widget, its export value must go too, or the remaining buttons
/// report the dropped buttons' values and the stored answer changes.
final class PDFFormButtonOptionsTests: XCTestCase {
    var root: URL!
    override func setUpWithError() throws { root = try OutputFiles.temporaryDirectory() }
    override func tearDownWithError() throws { try? FileManager.default.removeItem(at: root) }

    func testRemovingAPageKeepsRadioExportValuesAlignedWithTheRemainingButtons() throws {
        let input = try radio()
        var options = OperationOptions(); options.pageRanges = "2"
        XCTAssertEqual(try exportValues(export(.splitPDF, input, options)), ["Beta"])
        options = OperationOptions(); options.pdfPageOrder = [1, 1]
        XCTAssertEqual(try exportValues(export(.organizePDF, input, options)), ["Beta", "Beta"])
        options = OperationOptions(); options.pdfPageOrder = [1, 0]
        XCTAssertEqual(try exportValues(export(.organizePDF, input, options)), ["Alpha", "Beta"])
    }

    private func export(_ tool: ToolKind, _ input: URL, _ options: OperationOptions) throws -> URL {
        var options = options
        options.outputDirectory = root.appendingPathComponent(UUID().uuidString, isDirectory: true)
        try FileManager.default.createDirectory(at: options.outputDirectory!, withIntermediateDirectories: true)
        return try XCTUnwrap(PDFEngine.tool(tool, inputs: [input], options: options, token: .init()).first)
    }

    /// Every radio group's /Opt entries, each checked against its group's /Kids count.
    private func exportValues(_ url: URL) throws -> [String] {
        let document = try XCTUnwrap(CGPDFDocument(url as CFURL)), catalog = try XCTUnwrap(document.catalog)
        var form: CGPDFDictionaryRef?, fields: CGPDFArrayRef?, values: [String] = []
        XCTAssertTrue(CGPDFDictionaryGetDictionary(catalog, "AcroForm", &form))
        XCTAssertTrue(CGPDFDictionaryGetArray(try XCTUnwrap(form), "Fields", &fields))
        func visit(_ array: CGPDFArrayRef) {
            for index in 0..<CGPDFArrayGetCount(array) {
                var field: CGPDFDictionaryRef?, kids: CGPDFArrayRef?, opt: CGPDFArrayRef?
                guard CGPDFArrayGetDictionary(array, index, &field), let field, CGPDFDictionaryGetArray(field, "Kids", &kids), let kids else { continue }
                guard CGPDFDictionaryGetArray(field, "Opt", &opt), let opt else { visit(kids); continue }
                XCTAssertEqual(CGPDFArrayGetCount(opt), CGPDFArrayGetCount(kids))
                for item in 0..<CGPDFArrayGetCount(opt) {
                    var value: CGPDFStringRef?
                    if CGPDFArrayGetString(opt, item, &value), let value, let text = CGPDFStringCopyTextString(value) { values.append(text as String) }
                }
            }
        }
        visit(try XCTUnwrap(fields))
        return values.sorted()
    }

    private func radio() throws -> URL {
        func stream(_ content: String, _ attributes: String = "") -> String { "<< /Length \(content.utf8.count) \(attributes) >>\nstream\n\(content)\nendstream" }
        let on = stream("0 0 1 rg 2 2 10 10 re f", "/Type /XObject /Subtype /Form /BBox [0 0 14 14]")
        let off = stream("", "/Type /XObject /Subtype /Form /BBox [0 0 14 14]")
        let objects = [
            "<< /Type /Catalog /Pages 2 0 R /AcroForm << /Fields [8 0 R] >> >>",
            "<< /Type /Pages /Kids [3 0 R 4 0 R] /Count 2 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 300 200] /Resources << /Font << /F1 7 0 R >> >> /Contents 5 0 R /Annots [9 0 R] >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 300 200] /Resources << /Font << /F1 7 0 R >> >> /Contents 6 0 R /Annots [10 0 R] >>",
            stream("BT /F1 14 Tf 40 52 Td (Alpha) Tj ET"), stream("BT /F1 14 Tf 40 52 Td (Beta) Tj ET"),
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>",
            "<< /FT /Btn /Ff 49152 /T (Choice) /V /1 /Opt [(Alpha) (Beta)] /Kids [9 0 R 10 0 R] >>",
            "<< /Type /Annot /Subtype /Widget /Parent 8 0 R /P 3 0 R /Rect [20 50 34 64] /AS /Off /AP << /N << /0 11 0 R /Off 12 0 R >> >> >>",
            "<< /Type /Annot /Subtype /Widget /Parent 8 0 R /P 4 0 R /Rect [20 50 34 64] /AS /1 /AP << /N << /1 13 0 R /Off 12 0 R >> >> >>",
            on, off, on
        ]
        var bytes = Data("%PDF-1.7\n".utf8), offsets: [Int] = []
        for (index, object) in objects.enumerated() {
            offsets.append(bytes.count)
            bytes.append(Data("\(index + 1) 0 obj\n\(object)\nendobj\n".utf8))
        }
        let xref = bytes.count
        bytes.append(Data("xref\n0 \(objects.count + 1)\n0000000000 65535 f \n".utf8))
        for offset in offsets { bytes.append(Data(String(format: "%010d 00000 n \n", offset).utf8)) }
        bytes.append(Data("trailer\n<< /Size \(objects.count + 1) /Root 1 0 R >>\nstartxref\n\(xref)\n%%EOF\n".utf8))
        let url = root.appendingPathComponent("Radio.pdf")
        try bytes.write(to: url)
        return url
    }
}
