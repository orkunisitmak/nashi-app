import XCTest
import Foundation
@testable import NashiCore

final class PagesDOCXStrikethroughTests: XCTestCase {
    private var root: URL!
    override func setUpWithError() throws { root = try OutputFiles.temporaryDirectory() }
    override func tearDownWithError() throws { if let root { try? FileManager.default.removeItem(at: root) } }

    /// OOXML on/off values: "0", "false" and "off" all turn the property off.
    func testDisabledStrikethroughStaysOff() throws {
        let runs = ["false", "off", "0", "true", nil].enumerated().map { index, value in
            "<w:r><w:rPr><w:strike\(value.map { " w:val=\"\($0)\"" } ?? "")/></w:rPr><w:t>Run\(index)</w:t></w:r>"
        }.joined()
        let model = try pages("<w:p>\(runs)</w:p>")
        let blocks = try XCTUnwrap(model["blocks"] as? [[String: Any]])
        var strike: [String: Int] = [:]
        for run in blocks.flatMap({ $0["runs"] as? [[String: Any]] ?? [] }) {
            guard let text = run["text"] as? String else { continue }
            strike[text] = (run["character"] as? [String: Any])?["strikethru"] as? Int
        }
        XCTAssertEqual(strike, ["Run0": 0, "Run1": 0, "Run2": 0, "Run3": 1, "Run4": 1])
    }

    private func pages(_ body: String) throws -> [String: Any] {
        let package = root.appendingPathComponent("Package", isDirectory: true)
        let word = package.appendingPathComponent("word", isDirectory: true)
        try FileManager.default.createDirectory(at: word, withIntermediateDirectories: true)
        try "<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:body>\(body)</w:body></w:document>".write(to: word.appendingPathComponent("document.xml"), atomically: true, encoding: .utf8)
        let input = root.appendingPathComponent("Strike.docx")
        try ArchiveCodec.create(from: package, at: input, format: "zip", token: .init())
        let output = root.appendingPathComponent("Strike.pages")
        try PagesCodec.create(fromDOCX: input, at: output, token: .init())
        return try PagesCodec.model(output, token: .init())
    }
}
