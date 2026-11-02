import XCTest
import AppKit
import PDFKit
@testable import NashiCore

/// Photographed documents: the paper is found, cut out and straightened
/// before OCR, Cyrillic text is read with Russian, and pages that do not need
/// it (born-digital text, flatbed scans) are left alone. All text is
/// fictitious sample text.
final class PDFScanRectificationTests: XCTestCase {
    private var root: URL!
    override func setUpWithError() throws { root = try OutputFiles.temporaryDirectory() }
    override func tearDownWithError() throws { if let root { try? FileManager.default.removeItem(at: root) } }

    private let lines = ["ПРИМЕР ДОКУМЕНТА", "Получатель: ООО Образец", "Итого к оплате 1234.56 руб.", "Sample text 2026"]

    /// A phone-style picture: a sheet of paper turned by `degrees` on a
    /// wood-coloured desk, with dark text on it, saved as an image-only PDF.
    private func photographed(_ name: String, degrees: CGFloat, paperShare: CGFloat = 0.7) throws -> PDFDocument {
        let width = 1600, height = 2000
        let context = try XCTUnwrap(CGContext(data: nil, width: width, height: height, bitsPerComponent: 8, bytesPerRow: 0, space: CGColorSpaceCreateDeviceRGB(), bitmapInfo: CGImageAlphaInfo.noneSkipLast.rawValue))
        context.setFillColor(CGColor(srgbRed: 0.78, green: 0.66, blue: 0.45, alpha: 1)); context.fill(CGRect(x: 0, y: 0, width: width, height: height))
        let paper = CGSize(width: CGFloat(width) * paperShare, height: CGFloat(width) * paperShare * 1.35)
        context.translateBy(x: CGFloat(width) / 2, y: CGFloat(height) / 2); context.rotate(by: degrees * .pi / 180)
        context.setFillColor(CGColor(srgbRed: 0.97, green: 0.97, blue: 0.96, alpha: 1))
        context.fill(CGRect(x: -paper.width / 2, y: -paper.height / 2, width: paper.width, height: paper.height))
        NSGraphicsContext.current = NSGraphicsContext(cgContext: context, flipped: false)
        let font = NSFont(name: "Times New Roman", size: 46) ?? .systemFont(ofSize: 46)
        for (index, line) in lines.enumerated() {
            NSAttributedString(string: line, attributes: [.font: font, .foregroundColor: NSColor(white: 0.08, alpha: 1)])
                .draw(at: CGPoint(x: -paper.width / 2 + 60, y: paper.height / 2 - 130 - CGFloat(index) * 110))
        }
        NSGraphicsContext.current = nil
        let image = try XCTUnwrap(context.makeImage())
        let url = root.appendingPathComponent(name).appendingPathExtension("pdf")
        var box = CGRect(x: 0, y: 0, width: 960, height: 1200)
        let pdf = try XCTUnwrap(CGContext(url as CFURL, mediaBox: &box, nil))
        pdf.beginPDFPage(nil); pdf.draw(image, in: box); pdf.endPDFPage(); pdf.closePDF()
        return try XCTUnwrap(PDFDocument(url: url))
    }

    func testPhotographedPageIsCutToThePaperAndStraightened() throws {
        let source = try photographed("Photo", degrees: 6)
        let flat = try XCTUnwrap(try PDFScanRectification.rectified(source, token: .init()), "A tilted sheet on a desk is flattened")
        let page = try XCTUnwrap(flat.page(at: 0))
        let bounds = page.bounds(for: .cropBox)
        XCTAssertEqual(max(bounds.width, bounds.height), PDFScanRectification.pageLongSide, accuracy: 1)
        XCTAssertEqual(bounds.height / bounds.width, 1.35, accuracy: 0.06, "The page has the paper's proportions, without the desk")
        let cells = try PDFOfficeCodec.recognizedCells(page, token: .init())
        let text = cells.map(\.text).joined(separator: "\n")
        XCTAssertTrue(text.contains("ПРИМЕР ДОКУМЕНТА"), text)
        XCTAssertTrue(text.contains("1234.56"), text)
        XCTAssertTrue(text.contains("Sample text 2026"), text)
        // Straightened, every line is horizontal and the lines keep their order.
        XCTAssertEqual(cells.sorted { $0.rect.midY > $1.rect.midY }.first?.text, "ПРИМЕР ДОКУМЕНТА")
    }

    func testPhotographedCyrillicPageBecomesEditableWordText() throws {
        let url = root.appendingPathComponent("Photo route.pdf")
        try XCTUnwrap(try photographed("Photo route", degrees: -4).dataRepresentation()).write(to: url)
        let work = root.appendingPathComponent("work", isDirectory: true)
        try FileManager.default.createDirectory(at: work, withIntermediateDirectories: true)
        let fodt = try PDFOfficeCodec.prepare(url, to: "docx", at: work, options: OperationOptions(), token: .init())
        let xml = try String(contentsOf: fodt, encoding: .utf8)
        XCTAssertTrue(xml.contains("Получатель: ООО Образец"), "Russian lines are recognised as Cyrillic text")
        XCTAssertTrue(xml.contains("Original page artwork"))
        XCTAssertTrue(xml.contains("fo:page-height=\"842"), "The Word page is the flattened sheet, not the photo")
    }

    func testBornDigitalAndFlatbedPagesAreLeftAlone() throws {
        let url = root.appendingPathComponent("Text.pdf")
        var box = CGRect(x: 0, y: 0, width: 595, height: 842)
        let context = try XCTUnwrap(CGContext(url as CFURL, mediaBox: &box, nil))
        context.beginPDFPage(nil)
        NSGraphicsContext.current = NSGraphicsContext(cgContext: context, flipped: false)
        NSAttributedString(string: "Ordinary text page", attributes: [.font: NSFont.systemFont(ofSize: 14)]).draw(at: CGPoint(x: 60, y: 700))
        context.setStrokeColor(NSColor.black.cgColor); context.stroke(CGRect(x: 100, y: 200, width: 300, height: 300))
        NSGraphicsContext.current = nil
        context.endPDFPage(); context.closePDF()
        XCTAssertNil(try PDFScanRectification.rectified(try XCTUnwrap(PDFDocument(url: url)), token: .init()))
        // Paper filling the whole picture, upright: nothing to cut.
        XCTAssertNil(try PDFScanRectification.rectified(try photographed("Flatbed", degrees: 0, paperShare: 1.0), token: .init()))
    }

    func testDominantScript() {
        XCTAssertEqual(PDFOfficeCodec.dominantScript(["Счет-квитанция на оплату", "ЛС 720010630"]), .cyrillic)
        XCTAssertEqual(PDFOfficeCodec.dominantScript(["Sözleşme metni", "IBAN TR00 0000"]), .latin)
        XCTAssertEqual(PDFOfficeCodec.dominantScript(["Total", "Итого"]), .cyrillic)
    }

    func testRectifiedScanEnablesBoxDetection() throws {
        // After rectification, the Word output should contain artwork (box/frame
        // detection is enabled), not just plain text. The artwork title appears
        // only when the artwork layer is generated.
        let url = root.appendingPathComponent("Rectified route.pdf")
        try XCTUnwrap(try photographed("Rectified route", degrees: -4).dataRepresentation()).write(to: url)
        let work = root.appendingPathComponent("work2", isDirectory: true)
        try FileManager.default.createDirectory(at: work, withIntermediateDirectories: true)
        let fodt = try PDFOfficeCodec.prepare(url, to: "docx", at: work, options: OperationOptions(), token: .init())
        let xml = try String(contentsOf: fodt, encoding: .utf8)
        XCTAssertTrue(xml.contains("Original page artwork"), "Artwork is generated for rectified scans")
        // The artwork should be JPEG (photographic content)
        XCTAssertTrue(xml.contains("image/jpeg"), "Photographic artwork uses JPEG")
    }

    func testQuadGeometry() {
        let square = PDFScanRectification.Quad(topLeft: CGPoint(x: 0, y: 10), topRight: CGPoint(x: 10, y: 10), bottomLeft: .zero, bottomRight: CGPoint(x: 10, y: 0))
        XCTAssertEqual(PDFScanRectification.area(square), 100, accuracy: 0.001)
        XCTAssertEqual(PDFScanRectification.tilt(square), 0, accuracy: 0.001)
        let turned = PDFScanRectification.Quad(topLeft: CGPoint(x: 0, y: 10), topRight: CGPoint(x: 10, y: 11), bottomLeft: .zero, bottomRight: CGPoint(x: 10, y: 1))
        XCTAssertEqual(PDFScanRectification.tilt(turned), atan(0.1) * 180 / .pi, accuracy: 0.01)
    }
}
