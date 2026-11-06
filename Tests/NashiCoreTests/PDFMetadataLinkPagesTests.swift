import XCTest
import PDFKit
@testable import NashiCore

final class PDFMetadataLinkPagesTests:XCTestCase {
    /// Two pages; the link on page 1 jumps to page 2.
    private func linkedPDF(_ directory:URL) throws -> URL {
        let bodies=["<< /Type /Catalog /Pages 2 0 R >>","<< /Type /Pages /Kids [3 0 R 4 0 R] /Count 2 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents 5 0 R /Annots [6 0 R] >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents 5 0 R >>","STREAM",
            "<< /Type /Annot /Subtype /Link /Rect [72 690 200 720] /Border [0 0 0] /Dest [4 0 R /Fit] >>"]
        var out=Data("%PDF-1.7\n".utf8);var offsets:[Int]=[]
        for (index,body) in bodies.enumerated() {
            offsets.append(out.count)
            if body=="STREAM" { let text="BT /F1 24 Tf 72 700 Td (Contents) Tj ET";out.append(Data("5 0 obj\n<< /Length \(text.utf8.count) >>\nstream\n\(text)\nendstream\nendobj\n".utf8)) }
            else { out.append(Data("\(index+1) 0 obj\n\(body)\nendobj\n".utf8)) }
        }
        let xref=out.count
        var table="xref\n0 \(bodies.count+1)\n0000000000 65535 f \n"
        for offset in offsets { table+=String(format:"%010d 00000 n \n",offset) }
        out.append(Data((table+"trailer\n<< /Size \(bodies.count+1) /Root 1 0 R >>\nstartxref\n\(xref)\n%%EOF\n").utf8))
        let url=directory.appendingPathComponent("Links.pdf");try out.write(to:url);return url
    }
    private func linkTarget(_ url:URL) -> Int? {
        guard let document=PDFDocument(url:url),let link=document.page(at:0)?.annotations.first(where:{ $0.type=="Link" }) else { return nil }
        let page=(link.action as? PDFActionGoTo)?.destination.page ?? link.destination?.page
        return page.map { document.index(for:$0) }
    }
    func testRemoveMetadataKeepsTheLinkTargetPage() throws {
        let directory=try OutputFiles.temporaryDirectory();defer { try? FileManager.default.removeItem(at:directory) }
        let source=try linkedPDF(directory)
        XCTAssertEqual(linkTarget(source),1)
        var options=OperationOptions();options.outputDirectory=directory;options.metadataRemoveAll=true
        let output=try XCTUnwrap(PDFEngine.tool(.removeMetadata,inputs:[source],options:options,token:CancellationToken()).first)
        XCTAssertEqual(linkTarget(output),1)
    }
}
