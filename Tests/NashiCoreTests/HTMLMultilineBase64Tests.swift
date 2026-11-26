import XCTest
@testable import NashiCore

final class HTMLMultilineBase64Tests: XCTestCase {
    private let wrappedPNG = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAAB\r\nCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQ\n  DwAEhQGAhKmMIQAAAABJRU5ErkJggg=="

    func testEmbedHTMLImagesKeepsLineWrappedBase64DataURIs() throws {
        let root = try OutputFiles.temporaryDirectory()
        defer { try? FileManager.default.removeItem(at: root) }
        let input = root.appendingPathComponent("wrapped.html")
        let html = """
        <html><head><style>.bg{background:url('data:image/png;base64,\(wrappedPNG)')}</style></head>
        <body><img src="data:image/png;base64,\(wrappedPNG)"><img src="data:image/png;base64,not!valid"></body></html>
        """
        try html.write(to: input, atomically: true, encoding: .utf8)
        try DocumentEngine.embedHTMLImages(input, token: .init())
        let output = try String(contentsOf: input, encoding: .utf8)
        XCTAssertTrue(output.contains("url('data:image/png;base64,iVBOR"))
        XCTAssertTrue(output.contains("src=\"data:image/png;base64,iVBOR"))
        XCTAssertTrue(output.contains("src=\"\""))
    }

    func testEPUBCreateKeepsLineWrappedBase64Images() throws {
        let root = try OutputFiles.temporaryDirectory()
        defer { try? FileManager.default.removeItem(at: root) }
        let input = root.appendingPathComponent("wrapped.html")
        let epub = root.appendingPathComponent("wrapped.epub")
        let html = """
        <!DOCTYPE html><html><head><meta charset="utf-8"><title>Wrapped</title></head>
        <body><img src="data:image/png;base64,\(wrappedPNG)"><img src="data:image/png;base64,not!valid"></body></html>
        """
        try html.write(to: input, atomically: true, encoding: .utf8)
        try EPUBCodec.create(fromHTML: input, title: "Wrapped", at: epub, token: .init())
        let extracted = root.appendingPathComponent("extracted", isDirectory: true)
        try ArchiveCodec.extract(epub, into: extracted, token: .init())
        let image = extracted.appendingPathComponent("OEBPS/image-1.png")
        XCTAssertTrue(FileManager.default.fileExists(atPath: image.path))
        XCTAssertGreaterThan((try Data(contentsOf: image)).count, 0)
        let xhtml = try String(contentsOf: extracted.appendingPathComponent("OEBPS/book.xhtml"), encoding: .utf8)
        XCTAssertTrue(xhtml.contains("src=\"image-1.png\""))
    }
}
