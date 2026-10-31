import XCTest
import AppKit
import PDFKit
@testable import NashiCore

/// Page editing checked a symlinked PDF as the link itself, which is never a regular file,
/// so Merge, Split and Organize refused it as "empty, not a regular file, or over the limit".
final class PDFPageEditingSymlinkTests: XCTestCase {
    func testMergeAndSplitAcceptASymlinkedPDF() throws {
        let folder = try OutputFiles.temporaryDirectory(); defer { try? FileManager.default.removeItem(at: folder) }
        let documents = folder.appendingPathComponent("documents"), links = folder.appendingPathComponent("links"), output = folder.appendingPathComponent("output")
        for directory in [documents, links, output] { try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true) }
        func pdf(_ name: String, pages: Int) throws -> URL {
            let document = PDFDocument(), image = NSImage(size: NSSize(width: 200, height: 100))
            image.lockFocus(); NSColor.systemTeal.setFill(); NSRect(x: 0, y: 0, width: 200, height: 100).fill(); image.unlockFocus()
            for _ in 0..<pages { document.insert(try XCTUnwrap(PDFPage(image: image)), at: document.pageCount) }
            let url = documents.appendingPathComponent(name); XCTAssertTrue(document.write(to: url)); return url
        }
        let first = try pdf("first.pdf", pages: 2), second = try pdf("second.pdf", pages: 1)
        let link = links.appendingPathComponent("first.pdf"); try FileManager.default.createSymbolicLink(atPath: link.path, withDestinationPath: "../documents/first.pdf")
        var options = OperationOptions(); options.outputDirectory = output

        let merged = try XCTUnwrap(PDFEngine.tool(.mergePDF, inputs: [link, second], options: options, token: .init()).first)
        XCTAssertEqual(PDFDocument(url: merged)?.pageCount, 3)
        let pages = try PDFEngine.tool(.splitPDF, inputs: [link], options: options, token: .init())
        XCTAssertEqual(pages.map { PDFDocument(url: $0)?.pageCount }, [1, 1])

        let empty = documents.appendingPathComponent("empty.pdf"); try Data().write(to: empty)
        let emptyLink = links.appendingPathComponent("empty.pdf"); try FileManager.default.createSymbolicLink(atPath: emptyLink.path, withDestinationPath: "../documents/empty.pdf")
        XCTAssertThrowsError(try PDFPreservingCodec.transform([emptyLink], selections: [[0]])) {
            XCTAssertEqual($0.localizedDescription, "This PDF is empty, is not a regular file, or exceeds the 100 MB page-editing limit.")
        }
        XCTAssertEqual(try FileManager.default.destinationOfSymbolicLink(atPath: link.path), "../documents/first.pdf")
        XCTAssertNotNil(PDFDocument(url: first))
    }
}
