import XCTest
import PDFKit
@testable import NashiCore

/// Page editing re-parents pages under the root page-tree node. Pages must keep
/// the MediaBox, CropBox, Rotate and Resources they inherited from an
/// intermediate /Pages node, or they come out Letter-sized, unrotated and blank.
final class PDFInheritedPageAttributesTests: XCTestCase {
    private func pdf(intermediate: Bool, label: String) -> Data {
        let inherited = "/MediaBox [0 0 200 300] /CropBox [10 10 190 290] /Rotate 90 /Resources << /Font << /F1 << /Type /Font /Subtype /Type1 /BaseFont /Helvetica >> >> >>"
        var objects = ["<< /Type /Catalog /Pages 2 0 R >>"]
        objects.append("<< /Type /Pages /Kids [3 0 R] /Count 2 \(intermediate ? "" : inherited) >>")
        objects.append("<< /Type /Pages /Parent 2 0 R /Kids [4 0 R 5 0 R] /Count 2 \(intermediate ? inherited : "") >>")
        objects.append("<< /Type /Page /Parent 3 0 R /Contents 6 0 R >>")
        objects.append("<< /Type /Page /Parent 3 0 R /Contents 7 0 R >>")
        for page in ["A", "B"] {
            let stream = "BT /F1 18 Tf 30 100 Td (\(label)\(page)) Tj ET"
            objects.append("<< /Length \(stream.utf8.count) >>\nstream\n\(stream)\nendstream")
        }
        var output = "%PDF-1.7\n", offsets: [Int] = []
        for (index, body) in objects.enumerated() {
            offsets.append(output.utf8.count); output += "\(index + 1) 0 obj\n\(body)\nendobj\n"
        }
        let xref = output.utf8.count
        output += "xref\n0 \(objects.count + 1)\n0000000000 65535 f \n"
        for offset in offsets { output += String(format: "%010d 00000 n \n", offset) }
        output += "trailer\n<< /Size \(objects.count + 1) /Root 1 0 R >>\nstartxref\n\(xref)\n%%EOF\n"
        return Data(output.utf8)
    }

    private func describe(_ data: Data) throws -> [String] {
        let document = try XCTUnwrap(PDFDocument(data: data))
        return try (0..<document.pageCount).map { index in
            let page = try XCTUnwrap(document.page(at: index))
            let media = page.bounds(for: .mediaBox), crop = page.bounds(for: .cropBox)
            let text = (page.string ?? "").trimmingCharacters(in: .whitespacesAndNewlines)
            return "media=\(Int(media.width))x\(Int(media.height)) crop=\(Int(crop.minX)),\(Int(crop.minY)),\(Int(crop.width))x\(Int(crop.height)) rotation=\(page.rotation) text=\(text)"
        }
    }

    private func transform(_ sources: [(intermediate: Bool, label: String)], _ selections: [[Int]], rotations: [Int: Int] = [:]) throws -> (expected: [String], actual: [String]) {
        let directory = try OutputFiles.temporaryDirectory()
        defer { try? FileManager.default.removeItem(at: directory) }
        var inputs: [URL] = [], expected: [String] = []
        for (index, source) in sources.enumerated() {
            let url = directory.appendingPathComponent("source\(index).pdf"), data = pdf(intermediate: source.intermediate, label: source.label)
            try data.write(to: url); inputs.append(url)
            let pages = try describe(data); expected += selections[index].map { pages[$0] }
        }
        let output = try PDFPreservingCodec.transform(inputs, selections: selections, rotations: rotations)
        return (expected, try describe(output))
    }

    func testPagesKeepAttributesInheritedFromIntermediatePageTreeNodes() throws {
        let cases: [(String, [(intermediate: Bool, label: String)], [[Int]])] = [
            ("reorder", [(true, "Doc")], [[1, 0]]),
            ("unchanged order", [(true, "Doc")], [[0, 1]]),
            ("delete", [(true, "Doc")], [[1]]),
            ("duplicate", [(true, "Doc")], [[0, 0, 1]]),
            ("merge", [(true, "One"), (true, "Two")], [[1], [0, 1]]),
            ("root inheritance", [(false, "Doc")], [[1, 0]])
        ]
        for (name, sources, selections) in cases {
            let result = try transform(sources, selections)
            XCTAssertEqual(result.actual, result.expected, name)
            XCTAssertTrue(result.actual.allSatisfy { $0.contains("media=200x300") && $0.contains("rotation=90") }, "\(name): \(result.actual)")
        }
    }

    func testExplicitRotationStillOverridesInheritedRotation() throws {
        let result = try transform([(true, "Doc")], [[1, 0]], rotations: [0: 180])
        XCTAssertEqual(result.actual, [
            "media=200x300 crop=10,10,180x280 rotation=180 text=DocB",
            "media=200x300 crop=10,10,180x280 rotation=90 text=DocA"
        ])
    }
}
