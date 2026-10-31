import XCTest
import AppKit
import PDFKit
import CoreText
@testable import NashiCore

/// Landscape and upside-down PDFs often store their text turned in page space and
/// set /Rotate so viewers show it upright. Word/Pages and Excel output must read
/// such a page like the same content on an unrotated page of the displayed size.
final class PDFRotatedTextLayerTests: XCTestCase {
    var root: URL!
    var keep: [PDFDocument] = []
    override func setUpWithError() throws { root = try OutputFiles.temporaryDirectory() }
    override func tearDownWithError() throws { try? FileManager.default.removeItem(at: root) }

    func testRotatedTextLayerPagesConvertLikeTheDisplayedPage() throws {
        let portrait = CGSize(width: 612, height: 792), landscape = CGSize(width: 792, height: 612)
        for (rotate, shown) in [(90, landscape), (180, portrait), (270, landscape)] {
            let expected = try outputs(page(shown: shown, rotate: 0)), actual = try outputs(page(shown: shown, rotate: rotate))
            XCTAssertEqual(actual.sheet, ["First line here", "Second line below", "Name|Amount", "Apples|42"], "/Rotate \(rotate) Excel rows")
            XCTAssertEqual(actual.images, ["TL=red TR=blue BL=green BR=black wide"], "/Rotate \(rotate) image orientation")
            XCTAssertTrue(actual.xml.contains("fo:page-width=\"\(Int(shown.width))pt\" fo:page-height=\"\(Int(shown.height))pt\""), "/Rotate \(rotate) Word page size")
            XCTAssertEqual(actual.xml, expected.xml, "/Rotate \(rotate) Word layout matches the unrotated page")
        }
        // Text that already reads left to right in page space keeps its page-space output.
        let flat = try outputs(page(shown: portrait, rotate: 0))
        XCTAssertEqual(try outputs(page(shown: portrait, rotate: 90, upright: false)).xml, flat.xml)
    }

    private func outputs(_ page: PDFPage) throws -> (xml: String, sheet: [String], images: [String]) {
        let dir = try OutputFiles.temporaryDirectory(); defer { try? FileManager.default.removeItem(at: dir) }
        let url = dir.appendingPathComponent("in.pdf"); try XCTUnwrap(page.document?.dataRepresentation()).write(to: url)
        var xml = try String(contentsOf: PDFDocumentReconstruction.document(XCTUnwrap(page.document), at: dir.appendingPathComponent("d.fodt"), options: .init(), token: .init()), encoding: .utf8)
        var images: [String] = []
        for match in try NSRegularExpression(pattern: "<office:binary-data>([^<]*)</office:binary-data>").matches(in: xml, range: NSRange(xml.startIndex..., in: xml)).reversed() {
            let rep = try XCTUnwrap(NSBitmapImageRep(data: XCTUnwrap(Data(base64Encoded: (xml as NSString).substring(with: match.range(at: 1))))))
            func name(_ x: Int, _ y: Int) -> String {
                let c = rep.colorAt(x: x, y: y)!.usingColorSpace(.deviceRGB)!
                return c.redComponent > 0.6 && c.blueComponent < 0.4 ? "red" : c.blueComponent > 0.6 && c.redComponent < 0.4 ? "blue" : c.greenComponent > 0.4 && c.redComponent < 0.4 ? "green" : c.redComponent + c.greenComponent + c.blueComponent < 0.6 ? "black" : "other"
            }
            let w = rep.pixelsWide, h = rep.pixelsHigh, signature = "TL=\(name(w / 8, h / 8)) TR=\(name(w * 7 / 8, h / 8)) BL=\(name(w / 8, h * 7 / 8)) BR=\(name(w * 7 / 8, h * 7 / 8)) \(w > h ? "wide" : "tall")"
            images.insert(signature, at: 0); xml = (xml as NSString).replacingCharacters(in: match.range, with: signature)
        }
        // Rounded so float noise from the page transform is not a layout difference.
        for match in try NSRegularExpression(pattern: "[0-9]+\\.[0-9]+").matches(in: xml, range: NSRange(xml.startIndex..., in: xml)).reversed() {
            xml = (xml as NSString).replacingCharacters(in: match.range, with: String(format: "%.0f", Double((xml as NSString).substring(with: match.range))!))
        }
        let fods = try String(contentsOf: PDFOfficeCodec.prepare(url, to: "xlsx", at: dir, options: .init(), token: .init()), encoding: .utf8)
        let sheet = fods.components(separatedBy: "<table:table-row>").dropFirst().map { row in
            row.components(separatedBy: "</table:table-cell>").dropLast().map { $0.replacingOccurrences(of: "<[^>]+>", with: "", options: .regularExpression) }.filter { !$0.isEmpty }.joined(separator: "|")
        }
        return (xml, sheet, images)
    }

    /// Selectable text and a four-colour image at displayed positions. With `upright` the
    /// content is turned in page space so it reads upright once /Rotate is applied.
    private func page(shown: CGSize, rotate: Int, upright: Bool = true) throws -> PDFPage {
        var media = CGRect(origin: .zero, size: upright && rotate % 180 == 90 ? CGSize(width: shown.height, height: shown.width) : shown)
        let data = NSMutableData()
        let ctx = try XCTUnwrap(CGContext(consumer: XCTUnwrap(CGDataConsumer(data: data as CFMutableData)), mediaBox: &media, nil))
        ctx.beginPDFPage(nil)
        let w = media.width, h = media.height
        ctx.concatenate(!upright ? .identity : rotate == 90 ? .init(a: 0, b: 1, c: -1, d: 0, tx: w, ty: 0) : rotate == 180 ? .init(a: -1, b: 0, c: 0, d: -1, tx: w, ty: h) : rotate == 270 ? .init(a: 0, b: -1, c: 1, d: 0, tx: 0, ty: h) : .identity)
        for (text, point, size) in [("First line here", CGPoint(x: 60, y: 500), 14.0), ("Second line below", CGPoint(x: 60, y: 482), 14), ("Name", CGPoint(x: 60, y: 300), 12), ("Amount", CGPoint(x: 300, y: 300), 12), ("Apples", CGPoint(x: 60, y: 280), 12), ("42", CGPoint(x: 300, y: 280), 12)] {
            ctx.textPosition = point; CTLineDraw(CTLineCreateWithAttributedString(NSAttributedString(string: text, attributes: [NSAttributedString.Key(kCTFontAttributeName as String): CTFontCreateWithName("Helvetica" as CFString, size, nil)])), ctx)
        }
        let pixels = try XCTUnwrap(CGContext(data: nil, width: 8, height: 4, bitsPerComponent: 8, bytesPerRow: 0, space: CGColorSpaceCreateDeviceRGB(), bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue))
        for (color, rect) in [(NSColor.red, CGRect(x: 0, y: 2, width: 4, height: 2)), (.blue, CGRect(x: 4, y: 2, width: 4, height: 2)), (.green, CGRect(x: 0, y: 0, width: 4, height: 2)), (.black, CGRect(x: 4, y: 0, width: 4, height: 2))] {
            pixels.setFillColor(color.cgColor); pixels.fill(rect)
        }
        ctx.interpolationQuality = .none; ctx.draw(try XCTUnwrap(pixels.makeImage()), in: CGRect(x: 100, y: 100, width: 80, height: 40))
        ctx.endPDFPage(); ctx.closePDF()
        let first = try XCTUnwrap(PDFDocument(data: data as Data)); first.page(at: 0)?.rotation = rotate
        let document = try XCTUnwrap(PDFDocument(data: XCTUnwrap(first.dataRepresentation()))); keep.append(document)
        return try XCTUnwrap(document.page(at: 0))
    }
}
