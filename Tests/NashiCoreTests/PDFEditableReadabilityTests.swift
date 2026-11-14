import XCTest
import Foundation
import AppKit
import PDFKit
@testable import NashiCore

final class PDFEditableReadabilityTests: XCTestCase {
    private var root: URL!
    override func setUpWithError() throws { root = try OutputFiles.temporaryDirectory() }
    override func tearDownWithError() throws { if let root { try? FileManager.default.removeItem(at: root) } }

    func testSmallColoredPDFExportsVisibleEditableDOCXWithoutExtraPages() throws {
        let input = try fixture("Light text", color: "1 1 1")
        let original = try Data(contentsOf: input)
        let docx = try convert(input, to: "docx")
        let xml = try documentXML(docx)
        XCTAssertEqual(try xml.nodes(forXPath: "//*[local-name()='tbl']").count, 2)
        XCTAssertEqual(try xml.nodes(forXPath: "//*[local-name()='tc']").count, 8)
        XCTAssertTrue(try xml.nodes(forXPath: "//*[local-name()='drawing']").isEmpty, "The readability fix must keep text and tables editable")
        XCTAssertTrue(try xml.nodes(forXPath: "//*[local-name()='t']").contains { $0.stringValue == "Audit PDF page 1" })
        let colors = try xml.nodes(forXPath: "//*[local-name()='color']/@*[local-name()='val']").compactMap(\.stringValue)
        XCTAssertFalse(colors.contains("FFFFFF"))
        XCTAssertTrue(colors.contains("202020"))
        let margins = try xml.nodes(forXPath: "//*[local-name()='pgMar']/@*[local-name()='top']").compactMap { $0.stringValue.flatMap(Int.init) }
        XCTAssertTrue(margins.allSatisfy { $0 <= 240 }, "A 120pt page must not lose 72pt to fixed margins")

        let rendered = try PDFEngine.open(convert(docx, to: "pdf"))
        XCTAssertEqual(rendered.pageCount, 2, "Each 120pt-high source page should fit on one editable output page")
        for index in 0..<min(2, rendered.pageCount) {
            let page = try XCTUnwrap(rendered.page(at: index))
            XCTAssertTrue(page.string?.contains("Audit PDF page \(index + 1)") == true)
            for value in ["Orange", "12.5", "Lemon", "7"] { XCTAssertTrue(page.string?.contains(value) == true) }
            XCTAssertGreaterThan(try darkPixels(page), 200, "White-on-white text or table borders alone must not pass the render check")
        }
        XCTAssertEqual(try Data(contentsOf: input), original)
    }

    func testSmallColoredPDFPagesRouteAlsoKeepsReadableEditableContent() throws {
        let input = try fixture("Pages light text", color: "1 1 1")
        let pages = try convert(input, to: "pages")
        let model = try PagesCodec.model(pages, token: .init())
        let blocks = try XCTUnwrap(model["blocks"] as? [[String: Any]])
        XCTAssertEqual(blocks.filter { $0["type"] as? String == "table" }.count, 2)
        XCTAssertFalse(blocks.flatMap { $0["runs"] as? [[String: Any]] ?? [] }.contains { $0["type"] as? String == "image" })
        let rendered = try PDFEngine.open(convert(pages, to: "pdf"))
        XCTAssertEqual(rendered.pageCount, 2)
        for index in 0..<min(2, rendered.pageCount) {
            XCTAssertGreaterThan(try darkPixels(try XCTUnwrap(rendered.page(at: index))), 200)
        }
    }

    func testEditableOutputKeepsDarkReadableSourceColors() throws {
        let input = try fixture("Dark blue", color: "0 0 0.5")
        let xml = try documentXML(convert(input, to: "docx"))
        let colors = try xml.nodes(forXPath: "//*[local-name()='color']/@*[local-name()='val']").compactMap(\.stringValue)
        XCTAssertTrue(colors.contains("00007F") || colors.contains("000080"))
        XCTAssertFalse(colors.contains("202020"), "Already readable source colors should retain their hue")
    }

    func testRotatedPDFPageKeepsLinesAndLandscapeLayout() throws {
        // A portrait media box shown with /Rotate 90 and content drawn turned,
        // as landscape scans and reports usually are. Glyph geometry must be
        // read in the displayed orientation, not shredded into one paragraph
        // per character on a portrait page.
        let xml = try rotatedDocument(boxes: "/MediaBox [0 0 612 792]")
        let layout = try XCTUnwrap(xml.nodes(forXPath: "//*[local-name()='page-layout-properties']").first as? XMLElement)
        XCTAssertEqual(layout.attribute(forName: "fo:page-width")?.stringValue, "792.0pt")
        XCTAssertEqual(layout.attribute(forName: "fo:page-height")?.stringValue, "612.0pt")
        try assertRotatedContent(xml)
    }

    func testRotatedPDFPageUsesDisplayedCropBoxNotMediaBox() throws {
        // The visible page is a smaller crop box inside a media box with a
        // non-zero origin. Size and positions follow the displayed crop box.
        let xml = try rotatedDocument(boxes: "/MediaBox [-100 -50 712 842] /CropBox [50 50 500 600]")
        let layout = try XCTUnwrap(xml.nodes(forXPath: "//*[local-name()='page-layout-properties']").first as? XMLElement)
        XCTAssertEqual(layout.attribute(forName: "fo:page-width")?.stringValue, "550.0pt")
        XCTAssertEqual(layout.attribute(forName: "fo:page-height")?.stringValue, "450.0pt")
        try assertRotatedContent(xml)
        // The image spans user-space y 320...520; displayed, that is x 270...470
        // from the crop box's left edge, less the 22.5pt page margin.
        let frameParagraph = try XCTUnwrap(xml.nodes(forXPath: "//*[local-name()='p'][.//*[local-name()='frame']]").first as? XMLElement)
        let styleName = try XCTUnwrap(frameParagraph.attribute(forName: "text:style-name")?.stringValue)
        let style = try XCTUnwrap(xml.nodes(forXPath: "//*[local-name()='style'][@*[local-name()='name']='\(styleName)']/*[local-name()='paragraph-properties']").first as? XMLElement)
        XCTAssertEqual(style.attribute(forName: "fo:margin-left")?.stringValue, "247.5pt")
    }

    private func rotatedDocument(boxes: String) throws -> XMLDocument {
        let content = "q 0 1 -1 0 300 100 cm BT /F1 14 Tf 0 0 Td (Quarterly revenue summary) Tj ET Q " +
            "q 0 200 -100 0 400 320 cm /Im Do Q"
        let objects = [
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R \(boxes) /Rotate 90 /Resources << /Font << /F1 5 0 R >> /XObject << /Im 6 0 R >> >> /Contents 4 0 R >>",
            "<< /Length \(content.utf8.count) >>\nstream\n\(content)\nendstream",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>",
            "<< /Type /XObject /Subtype /Image /Width 2 /Height 1 /ColorSpace /DeviceRGB /BitsPerComponent 8 /Length 6 >>\nstream\n",
        ]
        var data = Data("%PDF-1.7\n".utf8), offsets: [Int] = []
        for (index, object) in objects.enumerated() {
            offsets.append(data.count)
            data.append(Data("\(index + 1) 0 obj\n\(object)".utf8))
            // Image samples: red then blue, left to right as displayed.
            if index == 5 { data.append(contentsOf: [0xFF, 0, 0, 0, 0, 0xFF]); data.append(Data("\nendstream".utf8)) }
            data.append(Data("\nendobj\n".utf8))
        }
        let xref = data.count
        data.append(Data("xref\n0 \(objects.count + 1)\n0000000000 65535 f \n".utf8))
        for offset in offsets { data.append(Data(String(format: "%010d 00000 n \n", offset).utf8)) }
        data.append(Data("trailer\n<< /Size \(objects.count + 1) /Root 1 0 R >>\nstartxref\n\(xref)\n%%EOF\n".utf8))

        let pdf = try XCTUnwrap(PDFDocument(data: data))
        let output = try PDFDocumentReconstruction.document(pdf, at: root.appendingPathComponent("Rotated.fodt"), options: .init(), token: .init())
        return try XMLDocument(contentsOf: output, options: .nodeLoadExternalEntitiesNever)
    }

    private func assertRotatedContent(_ xml: XMLDocument) throws {
        let paragraphs = try xml.nodes(forXPath: "//*[local-name()='p'][not(.//*[local-name()='frame'])]").compactMap(\.stringValue).filter { !$0.isEmpty }
        XCTAssertEqual(paragraphs, ["Quarterly revenue summary"])
        let frame = try XCTUnwrap(xml.nodes(forXPath: "//*[local-name()='frame']").first as? XMLElement)
        XCTAssertEqual(frame.attribute(forName: "svg:width")?.stringValue, "200.0pt")
        XCTAssertEqual(frame.attribute(forName: "svg:height")?.stringValue, "100.0pt")
        let png = try XCTUnwrap(frame.nodes(forXPath: ".//*[local-name()='binary-data']").first?.stringValue.flatMap { Data(base64Encoded: $0) }.flatMap(NSBitmapImageRep.init(data:)))
        XCTAssertGreaterThan(png.pixelsWide, png.pixelsHigh)
        let left = try XCTUnwrap(png.colorAt(x: png.pixelsWide / 4, y: png.pixelsHigh / 2)?.usingColorSpace(.deviceRGB))
        let right = try XCTUnwrap(png.colorAt(x: png.pixelsWide * 3 / 4, y: png.pixelsHigh / 2)?.usingColorSpace(.deviceRGB))
        XCTAssertGreaterThan(left.redComponent, 0.8); XCTAssertLessThan(left.blueComponent, 0.2)
        XCTAssertGreaterThan(right.blueComponent, 0.8); XCTAssertLessThan(right.redComponent, 0.2)
    }

    private func convert(_ input: URL, to format: String) throws -> URL {
        var options = OperationOptions()
        options.outputDirectory = root.appendingPathComponent(UUID().uuidString, isDirectory: true)
        return try XCTUnwrap(ConversionEngine.run(.init(inputs: [input], operation: .convert(format), options: options)).first)
    }

    private func documentXML(_ input: URL) throws -> XMLDocument {
        let package = root.appendingPathComponent(UUID().uuidString, isDirectory: true)
        try ArchiveCodec.extract(input, into: package, token: .init())
        return try XMLDocument(contentsOf: package.appendingPathComponent("word/document.xml"), options: .nodeLoadExternalEntitiesNever)
    }

    private func darkPixels(_ page: PDFPage) throws -> Int {
        let width = 480, height = 240
        let context = try XCTUnwrap(CGContext(data: nil, width: width, height: height, bitsPerComponent: 8, bytesPerRow: width * 4, space: CGColorSpaceCreateDeviceRGB(), bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue))
        context.setFillColor(CGColor(gray: 1, alpha: 1))
        context.fill(CGRect(x: 0, y: 0, width: width, height: height))
        context.scaleBy(x: 2, y: 2)
        context.drawPDFPage(try XCTUnwrap(page.pageRef))
        let data = try XCTUnwrap(context.data).assumingMemoryBound(to: UInt8.self)
        // Reconstructed table borders are #999999. Counting only substantially
        // darker pixels proves actual text visibility, not just a visible grid.
        return (0..<(width * height)).filter { data[$0 * 4] < 100 && data[$0 * 4 + 1] < 100 && data[$0 * 4 + 2] < 100 }.count
    }

    private func fixture(_ name: String, color: String) throws -> URL {
        func stream(_ value: String) -> String { "<< /Length \(value.utf8.count) >>\nstream\n\(value)\nendstream" }
        func page(_ number: Int) -> String {
            """
            1 0 0 rg 0 0 120 120 re f 0 0 1 rg 120 0 120 120 re f
            \(color) rg
            BT /F1 12 Tf 12 95 Td (Audit PDF page \(number)) Tj ET
            BT /F1 12 Tf 12 65 Td (Orange) Tj ET
            BT /F1 12 Tf 150 65 Td (12.5) Tj ET
            BT /F1 12 Tf 12 40 Td (Lemon) Tj ET
            BT /F1 12 Tf 150 40 Td (7) Tj ET
            """
        }
        let objects = [
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R 4 0 R] /Count 2 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 240 120] /Resources << /Font << /F1 5 0 R >> >> /Contents 6 0 R >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 240 120] /Resources << /Font << /F1 5 0 R >> >> /Contents 7 0 R >>",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>",
            stream(page(1)), stream(page(2))
        ]
        var data = Data("%PDF-1.7\n".utf8), offsets: [Int] = []
        for (index, object) in objects.enumerated() {
            offsets.append(data.count)
            data.append(Data("\(index + 1) 0 obj\n\(object)\nendobj\n".utf8))
        }
        let xref = data.count
        data.append(Data("xref\n0 \(objects.count + 1)\n0000000000 65535 f \n".utf8))
        for offset in offsets { data.append(Data(String(format: "%010d 00000 n \n", offset).utf8)) }
        data.append(Data("trailer\n<< /Size \(objects.count + 1) /Root 1 0 R >>\nstartxref\n\(xref)\n%%EOF\n".utf8))
        let input = root.appendingPathComponent(name).appendingPathExtension("pdf")
        try data.write(to: input)
        return input
    }
}
