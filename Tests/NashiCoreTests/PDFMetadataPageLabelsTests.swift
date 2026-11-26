import XCTest
import PDFKit
@testable import NashiCore

/// Metadata cleanup rebuilds the PDF from copied pages. Custom page numbering
/// (i, ii, A-1) lives in the catalog, so cleanup carries it into the clean copy,
/// as Windows does, and refuses only labels it cannot copy faithfully.
final class PDFMetadataPageLabelsTests: XCTestCase {
    var root: URL!
    override func setUpWithError() throws { root = try OutputFiles.temporaryDirectory() }
    override func tearDownWithError() throws { try? FileManager.default.removeItem(at: root) }

    func testRemovingMetadataKeepsRomanAndPrefixedPageLabels() throws {
        // The number tree is split over indirect nodes, as book and LaTeX PDFs write it.
        let labelled = try fixture("Labelled.pdf", catalog: "/PageLabels 14 0 R /Metadata 17 0 R", extra: [
            "<< /Kids [15 0 R 16 0 R] >>",
            "<< /Limits [0 0] /Nums [0 << /S /r >>] >>",
            "<< /Limits [2 2] /Nums [2 << /Type /PageLabel /S /D /St 5 /P (APX-) >>] >>",
            xmp
        ])
        let before = try Data(contentsOf: labelled), expected = ["i", "ii", "APX-5", "APX-6"]
        for edit in [false, true] {
            var options = OperationOptions()
            if edit { options.metadata = ["Title": "Edited"] } else { options.metadataRemoveAll = true }
            options.outputDirectory = try folder()
            let output = try XCTUnwrap(ConversionEngine.run(.init(inputs: [labelled], operation: .tool(.removeMetadata), options: options)).first)
            let cleaned = try XCTUnwrap(PDFDocument(url: output))
            XCTAssertEqual((0..<cleaned.pageCount).map { cleaned.page(at: $0)?.label }, expected)
            XCTAssertEqual(cleaned.page(at: 2)?.string?.trimmingCharacters(in: .whitespacesAndNewlines), "Page 3")
            XCTAssertNil(cleaned.documentAttributes?[PDFDocumentAttribute.authorAttribute])
            XCTAssertEqual(cleaned.documentAttributes?[PDFDocumentAttribute.titleAttribute] as? String, edit ? "Edited" : nil)
            let catalog = try XCTUnwrap(cleaned.documentRef?.catalog)
            var object: CGPDFObjectRef?
            XCTAssertTrue(CGPDFDictionaryGetObject(catalog, "PageLabels", &object))
            XCTAssertFalse(CGPDFDictionaryGetObject(catalog, "Metadata", &object))
            let bytes = try Data(contentsOf: output)
            // Core Graphics repairs a broken cross-reference table silently, so check the appended one byte by byte.
            let text = try XCTUnwrap(String(data: bytes, encoding: .isoLatin1))
            func numbers(_ pattern: String) throws -> [Int] {
                let match = try XCTUnwrap(NSRegularExpression(pattern: pattern).matches(in: text, range: NSRange(text.startIndex..., in: text)).last, pattern)
                return (1..<match.numberOfRanges).compactMap { Range(match.range(at: $0), in: text).flatMap { Int(text[$0]) } }
            }
            func at(_ offset: Int, _ expected: String) -> Bool { bytes.dropFirst(offset).starts(with: Data(expected.utf8)) }
            let update = try numbers(#"startxref\s+(\d+)\s+%%EOF\s*$"#)[0]
            let entry = try numbers(#"xref\n(\d+) 1\n(\d{10}) 00000 n \ntrailer"#)
            XCTAssertTrue(at(update, "xref\n\(entry[0]) 1\n"))
            XCTAssertTrue(at(entry[1], "\(entry[0]) 0 obj\n<< /Type /Catalog"))
            XCTAssertTrue(at(try numbers(#"/Prev (\d+) >>\s+startxref"#)[0], "xref\n0 "))
            XCTAssertEqual(try numbers(#"/Root (\d+) 0 R"#)[0], entry[0])
            for secret in ["Private author", "Labelled report", "Private XMP creator"] {
                XCTAssertNil(bytes.range(of: Data(secret.utf8)), secret)
            }
        }
        XCTAssertEqual(try Data(contentsOf: labelled), before)
    }

    func testRemovingMetadataStillRefusesWhatItCannotPreserve() throws {
        let cases = [
            // Attachments are refused as before, labels or not.
            ("Attachment.pdf", "/PageLabels << /Nums [0 << /S /r >>] >> /Names << /EmbeddedFiles << /Names [] >> >>", "embedded files"),
            ("Odd.pdf", "/PageLabels << /Nums [0 << /S /r >> 2] >>", "custom page labels"),
            ("Unordered.pdf", "/PageLabels << /Nums [2 << /S /D >> 0 << /S /r >>] >>", "custom page labels"),
            ("Stream.pdf", "/PageLabels << /Nums [0 << /S /r /P 13 0 R >>] >>", "custom page labels"),
            ("Loop.pdf", "/PageLabels 14 0 R", "custom page labels")
        ]
        for (name, catalog, reason) in cases {
            let input = try fixture(name, catalog: catalog, extra: ["<< /Kids [14 0 R] >>"]), before = try Data(contentsOf: input)
            var options = OperationOptions()
            options.metadataRemoveAll = true
            options.outputDirectory = try folder()
            XCTAssertThrowsError(try ConversionEngine.run(.init(inputs: [input], operation: .tool(.removeMetadata), options: options)), name) { error in
                XCTAssertTrue(error.localizedDescription.contains(reason), "\(name): \(error.localizedDescription)")
            }
            XCTAssertEqual(try FileManager.default.contentsOfDirectory(atPath: options.outputDirectory!.path), [], name)
            XCTAssertEqual(try Data(contentsOf: input), before, name)
        }
    }

    func testRemovingMetadataFromUnlabelledPDFAddsNoLabels() throws {
        var options = OperationOptions()
        options.metadataRemoveAll = true
        options.outputDirectory = try folder()
        let output = try XCTUnwrap(ConversionEngine.run(.init(inputs: [try fixture("Plain.pdf", catalog: "", extra: [])], operation: .tool(.removeMetadata), options: options)).first)
        let cleaned = try XCTUnwrap(PDFDocument(url: output))
        XCTAssertEqual((0..<cleaned.pageCount).map { cleaned.page(at: $0)?.label }, ["1", "2", "3", "4"])
        XCTAssertNil(cleaned.documentAttributes?[PDFDocumentAttribute.authorAttribute])
    }

    private var xmp: String {
        stream("<?xpacket begin='' id='W5M0MpCehiHzreSzNTczkc9d'?><x:xmpmeta xmlns:x='adobe:ns:meta/'><rdf:RDF xmlns:rdf='http://www.w3.org/1999/02/22-rdf-syntax-ns#'><rdf:Description xmlns:dc='http://purl.org/dc/elements/1.1/'><dc:creator><rdf:Seq><rdf:li>Private XMP creator</rdf:li></rdf:Seq></dc:creator></rdf:Description></rdf:RDF></x:xmpmeta><?xpacket end='w'?>", dictionary: "/Type /Metadata /Subtype /XML")
    }
    private func stream(_ body: String, dictionary: String = "") -> String { "<< \(dictionary) /Length \(body.utf8.count) >>\nstream\n\(body)\nendstream" }
    private func folder() throws -> URL {
        let folder = root.appendingPathComponent(UUID().uuidString, isDirectory: true)
        try FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true)
        return folder
    }

    /// Four pages (objects 3–10), a font, the Info dictionary (12) and a stream (13); `extra` starts at object 14.
    private func fixture(_ name: String, catalog: String, extra: [String]) throws -> URL {
        var objects = [
            "<< /Type /Catalog /Pages 2 0 R \(catalog) >>",
            "<< /Type /Pages /Kids [3 0 R 5 0 R 7 0 R 9 0 R] /Count 4 >>"
        ]
        for page in 1...4 {
            objects.append("<< /Type /Page /Parent 2 0 R /MediaBox [0 0 200 200] /Resources << /Font << /F1 11 0 R >> >> /Contents \(objects.count + 2) 0 R >>")
            objects.append(stream("BT /F1 12 Tf 20 100 Td (Page \(page)) Tj ET"))
        }
        objects += [
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>",
            "<< /Title (Labelled report) /Author (Private author) >>",
            stream("not a label")
        ] + extra
        var data = Data("%PDF-1.7\n".utf8), offsets = [Int]()
        for (index, object) in objects.enumerated() { offsets.append(data.count); data.append(Data("\(index + 1) 0 obj\n\(object)\nendobj\n".utf8)) }
        let xref = data.count
        data.append(Data("xref\n0 \(objects.count + 1)\n0000000000 65535 f \n".utf8))
        for offset in offsets { data.append(Data(String(format: "%010d 00000 n \n", offset).utf8)) }
        data.append(Data("trailer\n<< /Size \(objects.count + 1) /Root 1 0 R /Info 12 0 R >>\nstartxref\n\(xref)\n%%EOF\n".utf8))
        let url = root.appendingPathComponent(name)
        try data.write(to: url)
        return url
    }
}
