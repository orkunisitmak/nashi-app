import XCTest
import Foundation
@testable import NashiCore

/// DOCX -> Pages reads page, font, spacing, table and image measurements from
/// untrusted XML. A non-finite number must never reach JSONSerialization,
/// which raises an Objective-C exception and terminates the app.
final class PagesDOCXMeasurementTests: XCTestCase {
    private var root: URL!
    private let namespaces = "xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\" xmlns:wp=\"http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing\" xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\""

    override func setUpWithError() throws { root = try OutputFiles.temporaryDirectory() }
    override func tearDownWithError() throws { if let root { try? FileManager.default.removeItem(at: root) } }

    // F1780
    func testNonFiniteMeasurementsAreIgnoredInsteadOfCrashing() throws {
        for value in ["NaN", "nan", "inf", "-Infinity", "1e999", "0x1p9999"] {
            for (site, makeBody) in sites {
                let model = try convert(name: "\(site) \(value)", body: makeBody(value))
                let setup = try XCTUnwrap(model["pageSetup"] as? [String: Any], "\(site)=\(value)")
                let width = try XCTUnwrap(setup["pageWidth"] as? NSNumber, "\(site)=\(value)").doubleValue
                XCTAssertTrue(width.isFinite && width > 0, "\(site)=\(value) pageWidth \(width)")
                XCTAssertTrue(texts(model).contains("Measured"), "\(site)=\(value)")
            }
        }
    }

    func testFiniteMeasurementsStillApply() throws {
        let model = try convert(name: "Finite", body: paragraph(properties: "<w:spacing w:before=\"240\"/>", runProperties: "<w:sz w:val=\"48\"/>") + "<w:sectPr><w:pgSz w:w=\"12240\" w:h=\"15840\"/></w:sectPr>")
        let setup = try XCTUnwrap(model["pageSetup"] as? [String: Any])
        XCTAssertEqual((setup["pageWidth"] as? NSNumber)?.doubleValue ?? 0, 612, accuracy: 0.01)
        XCTAssertEqual((setup["pageHeight"] as? NSNumber)?.doubleValue ?? 0, 792, accuracy: 0.01)
        let block = try XCTUnwrap((model["blocks"] as? [[String: Any]])?.first { ($0["runs"] as? [[String: Any]])?.isEmpty == false })
        XCTAssertEqual(((block["paragraph"] as? [String: Any])?["spaceBefore"] as? NSNumber)?.doubleValue ?? 0, 12, accuracy: 0.01)
        let run = try XCTUnwrap((block["runs"] as? [[String: Any]])?.first)
        XCTAssertEqual(((run["character"] as? [String: Any])?["fontSize"] as? NSNumber)?.doubleValue ?? 0, 24, accuracy: 0.01)
    }

    private var sites: [(String, (String) -> String)] {
        [
            ("pgSz", { self.paragraph() + "<w:sectPr><w:pgSz w:w=\"\($0)\" w:h=\"\($0)\"/></w:sectPr>" }),
            ("pgMar", { self.paragraph() + "<w:sectPr><w:pgMar w:left=\"\($0)\" w:top=\"\($0)\"/></w:sectPr>" }),
            ("sz", { self.paragraph(runProperties: "<w:sz w:val=\"\($0)\"/>") }),
            ("spacing", { self.paragraph(properties: "<w:spacing w:before=\"\($0)\" w:after=\"\($0)\" w:line=\"\($0)\"/>") }),
            ("ind", { self.paragraph(properties: "<w:ind w:left=\"\($0)\" w:right=\"\($0)\" w:hanging=\"\($0)\"/>") }),
            ("gridCol", { "<w:tbl><w:tblGrid><w:gridCol w:w=\"\($0)\"/></w:tblGrid><w:tr><w:tc><w:p><w:r><w:t>Measured</w:t></w:r></w:p></w:tc></w:tr></w:tbl>" }),
            ("extent", { self.paragraph() + "<w:p><w:r><w:drawing><wp:inline><wp:extent cx=\"\($0)\" cy=\"\($0)\"/><wp:docPr id=\"1\" name=\"Picture\"/><a:graphic><a:graphicData><a:blip r:embed=\"rIdImage\"/></a:graphicData></a:graphic></wp:inline></w:drawing></w:r></w:p>" }),
        ]
    }

    private func paragraph(properties: String = "", runProperties: String = "") -> String {
        "<w:p><w:pPr>\(properties)</w:pPr><w:r><w:rPr>\(runProperties)</w:rPr><w:t>Measured</w:t></w:r></w:p>"
    }

    private func texts(_ model: [String: Any]) -> [String] {
        (model["blocks"] as? [[String: Any]] ?? []).flatMap { block -> [String] in
            let runs = (block["runs"] as? [[String: Any]] ?? []).compactMap { $0["text"] as? String }
            let cells = (block["rows"] as? [[[String: Any]]] ?? []).flatMap { $0.compactMap { $0["text"] as? String } }
            return runs + cells
        }
    }

    private func convert(name: String, body: String) throws -> [String: Any] {
        let package = root.appendingPathComponent(name + " package", isDirectory: true)
        let word = package.appendingPathComponent("word", isDirectory: true)
        let media = word.appendingPathComponent("media", isDirectory: true)
        let relationships = word.appendingPathComponent("_rels", isDirectory: true)
        try FileManager.default.createDirectory(at: media, withIntermediateDirectories: true)
        try FileManager.default.createDirectory(at: relationships, withIntermediateDirectories: true)
        try "<w:document \(namespaces)><w:body>\(body)</w:body></w:document>".write(to: word.appendingPathComponent("document.xml"), atomically: true, encoding: .utf8)
        try XCTUnwrap(Data(base64Encoded: "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==")).write(to: media.appendingPathComponent("image1.png"))
        try "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rIdImage\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/image\" Target=\"media/image1.png\"/></Relationships>".write(to: relationships.appendingPathComponent("document.xml.rels"), atomically: true, encoding: .utf8)
        let input = root.appendingPathComponent(name).appendingPathExtension("docx")
        try ArchiveCodec.create(from: package, at: input, format: "zip", token: .init())
        let output = root.appendingPathComponent(name).appendingPathExtension("pages")
        try PagesCodec.create(fromDOCX: input, at: output, token: .init())
        return try PagesCodec.model(output, token: .init())
    }
}
