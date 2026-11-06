import XCTest
import PDFKit
@testable import NashiCore

/// A field without its own /Q takes the alignment default from its parents and then from
/// the AcroForm. Merged fields hang under the first document's AcroForm, so their parent
/// must state their own document's default, or they take the first document's.
final class PDFMergeFormAlignmentTests: XCTestCase {
    var root: URL!
    override func setUpWithError() throws { root = try OutputFiles.temporaryDirectory() }
    override func tearDownWithError() throws { try? FileManager.default.removeItem(at: root) }

    func testMergedFieldsKeepTheirOwnDocumentsDefaultAlignment() throws {
        let centered = try form("Centered", formQuadding: "/Q 1"), left = try form("Left", formQuadding: "")
        var options = OperationOptions(); options.outputDirectory = root
        let merged = try XCTUnwrap(PDFEngine.tool(.mergePDF, inputs: [centered, left], options: options, token: .init()).first)
        XCTAssertEqual(try quadding(merged), ["Centered": 1, "Nashi document 2.Left": 0])
    }

    /// Effective /Q of each terminal field: its own, else the nearest parent's, else the AcroForm's.
    private func quadding(_ url: URL) throws -> [String: Int] {
        let document = try XCTUnwrap(CGPDFDocument(url as CFURL)), catalog = try XCTUnwrap(document.catalog)
        var form: CGPDFDictionaryRef?, fields: CGPDFArrayRef?, formQuadding: CGPDFInteger = 0
        XCTAssertTrue(CGPDFDictionaryGetDictionary(catalog, "AcroForm", &form))
        let acroForm = try XCTUnwrap(form)
        XCTAssertTrue(CGPDFDictionaryGetArray(acroForm, "Fields", &fields))
        _ = CGPDFDictionaryGetInteger(acroForm, "Q", &formQuadding)
        var result: [String: Int] = [:]
        func visit(_ array: CGPDFArrayRef, prefix: String, inherited: Int?) {
            for index in 0..<CGPDFArrayGetCount(array) {
                var field: CGPDFDictionaryRef?, title: CGPDFStringRef?, kids: CGPDFArrayRef?, value: CGPDFInteger = 0
                guard CGPDFArrayGetDictionary(array, index, &field), let field else { continue }
                let partial = CGPDFDictionaryGetString(field, "T", &title) ? (title.flatMap { CGPDFStringCopyTextString($0) as String? } ?? "") : ""
                let name = [prefix, partial].filter { !$0.isEmpty }.joined(separator: ".")
                let quadding = CGPDFDictionaryGetInteger(field, "Q", &value) ? Int(value) : inherited
                if CGPDFDictionaryGetArray(field, "Kids", &kids), let kids { visit(kids, prefix: name, inherited: quadding) }
                else { result[name] = quadding ?? Int(formQuadding) }
            }
        }
        visit(try XCTUnwrap(fields), prefix: "", inherited: nil)
        return result
    }

    private func form(_ name: String, formQuadding: String) throws -> URL {
        let content = "BT /F1 14 Tf 20 150 Td (\(name)) Tj ET"
        let objects = [
            "<< /Type /Catalog /Pages 2 0 R /AcroForm << /Fields [6 0 R] /DA (/Helv 0 Tf 0 g) \(formQuadding) /DR << /Font << /Helv 5 0 R >> >> >> >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 300 200] /Resources << /Font << /F1 5 0 R >> >> /Contents 4 0 R /Annots [6 0 R] >>",
            "<< /Length \(content.utf8.count) >>\nstream\n\(content)\nendstream",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>",
            "<< /Type /Annot /Subtype /Widget /FT /Tx /T (\(name)) /V (Value) /P 3 0 R /Rect [20 50 220 74] >>"
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
        let url = root.appendingPathComponent(name).appendingPathExtension("pdf")
        try bytes.write(to: url)
        return url
    }
}
