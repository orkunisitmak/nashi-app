import XCTest
@testable import NashiCore

/// Fixed-layout EPUB converted the page size to Int before any limit, so a huge
/// page box trapped and ended the process instead of reporting an error.
final class EPUBFixedHugePageTests: XCTestCase {
    func testFixedLayoutEPUBRejectsHugePageInsteadOfTrapping() throws {
        let root = try OutputFiles.temporaryDirectory(); defer { try? FileManager.default.removeItem(at: root) }
        var options = OperationOptions(); options.epubLayout = "fixed"; options.dpi = 36; options.outputDirectory = root
        for (name, media) in [("wide", "0 0 100000000000000000000 200"), ("tall", "0 0 200 100000000000000000000"), ("over", "0 0 64001 20")] {
            XCTAssertThrowsError(try DocumentEngine.convert(try pdf(name, media, in: root), to: "epub", options: options, token: .init()), name) { error in
                XCTAssertTrue("\(error.localizedDescription)".contains("too large"), "\(error)")
            }
        }
        XCTAssertNoThrow(try DocumentEngine.convert(try pdf("edge", "0 0 64000 20", in: root), to: "epub", options: options, token: .init()))
    }

    private func pdf(_ name: String, _ media: String, in root: URL) throws -> URL {
        let body = "0 0 1 rg 0 0 50 50 re f"
        let objects = ["<< /Type /Catalog /Pages 2 0 R >>", "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
                       "<< /Type /Page /Parent 2 0 R /MediaBox [\(media)] /Contents 4 0 R >>", "<< /Length \(body.utf8.count) >>\nstream\n\(body)\nendstream"]
        var text = "%PDF-1.4\n", offsets: [Int] = []
        for (index, object) in objects.enumerated() { offsets.append(text.utf8.count); text += "\(index + 1) 0 obj\n\(object)\nendobj\n" }
        let xref = text.utf8.count
        text += "xref\n0 \(objects.count + 1)\n0000000000 65535 f \n" + offsets.map { String(format: "%010d 00000 n \n", $0) }.joined()
        text += "trailer\n<< /Size \(objects.count + 1) /Root 1 0 R >>\nstartxref\n\(xref)\n%%EOF\n"
        let url = root.appendingPathComponent(name + ".pdf"); try Data(text.utf8).write(to: url); return url
    }
}
