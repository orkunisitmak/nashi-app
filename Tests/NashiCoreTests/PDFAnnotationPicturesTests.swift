import XCTest
import AppKit
import PDFKit
@testable import NashiCore

/// Stamps (approval marks, pasted signatures) and ink (handwriting) are
/// annotations, not placed images. Editable Word/Pages must still show them as
/// pictures, as the preserved page picture does.
final class PDFAnnotationPicturesTests: XCTestCase {
    private var root: URL!
    override func setUpWithError() throws { root = try OutputFiles.temporaryDirectory() }
    override func tearDownWithError() throws { try? FileManager.default.removeItem(at: root) }

    func testStampsAndInkBecomePicturesInEditableWord() throws {
        let signed = try images(try pdf(media: "50 40 450 340", stamp: "310 80 410 130", rotate: 90, ink: true))
        XCTAssertEqual(signed.count, 2)
        XCTAssertTrue(signed.contains { $0.width == 100 && $0.height == 50 && pixels($0, red: false) == 5000 }, "The stamp picture is its whole blue appearance")
        XCTAssertGreaterThan(signed.map { pixels($0, red: true) }.reduce(0, +), 300, "The red ink signature is pictured")
        XCTAssertTrue(try images(try pdf(stamp: "260 40 360 90", flags: 2)).isEmpty, "A hidden stamp is not shown")
    }

    private func images(_ input: URL) throws -> [CGImage] {
        let work = root.appendingPathComponent(UUID().uuidString)
        try FileManager.default.createDirectory(at: work, withIntermediateDirectories: true)
        var options = OperationOptions(); options.dpi = 72
        let xml = try String(contentsOf: PDFOfficeCodec.prepare(input, to: "docx", at: work, options: options, token: .init()), encoding: .utf8)
        return try XMLDocument(xmlString: xml).nodes(forXPath: "//*[local-name()='binary-data']").compactMap { node in
            node.stringValue.flatMap { Data(base64Encoded: $0) }.flatMap { NSBitmapImageRep(data: $0)?.cgImage }
        }
    }

    private func pixels(_ image: CGImage, red: Bool) -> Int {
        var bytes = [UInt8](repeating: 0, count: image.width * image.height * 4)
        CGContext(data: &bytes, width: image.width, height: image.height, bitsPerComponent: 8, bytesPerRow: image.width * 4, space: CGColorSpaceCreateDeviceRGB(), bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue)!.draw(image, in: CGRect(x: 0, y: 0, width: image.width, height: image.height))
        var total = 0
        for i in stride(from: 0, to: bytes.count, by: 4) {
            let (r, g, b) = (Int(bytes[i]), Int(bytes[i + 1]), Int(bytes[i + 2]))
            if red ? (r > 180 && g < 80 && b < 80) : (r < 80 && g < 80 && b > 180) { total += 1 }
        }
        return total
    }

    private func pdf(media: String = "0 0 400 300", stamp: String, flags: Int = 4, rotate: Int = 0, ink: Bool = false) throws -> URL {
        let origin = media.split(separator: " ").prefix(2).compactMap { Double($0) }
        let text = "BT /F1 14 Tf \(origin[0] + 20) \(origin[1] + 250) Td (Agreed and signed:) Tj ET"
        let appearance = "0 0 1 rg 0 0 100 50 re f"
        let objects = [
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /Rotate \(rotate) /MediaBox [\(media)] /Resources << /Font << /F1 << /Type /Font /Subtype /Type1 /BaseFont /Helvetica >> >> >> /Contents 4 0 R /Annots [5 0 R] >>",
            "<< /Length \(text.utf8.count) >>\nstream\n\(text)\nendstream",
            "<< /Type /Annot /Subtype /Stamp /Rect [\(stamp)] /F \(flags) /AP << /N 6 0 R >> >>",
            "<< /Type /XObject /Subtype /Form /FormType 1 /BBox [0 0 100 50] /Resources << >> /Length \(appearance.utf8.count) >>\nstream\n\(appearance)\nendstream",
        ]
        var data = Data("%PDF-1.4\n".utf8), offsets: [Int] = []
        for (index, object) in objects.enumerated() { offsets.append(data.count); data += Data("\(index + 1) 0 obj\n\(object)\nendobj\n".utf8) }
        let xref = data.count
        data += Data("xref\n0 \(objects.count + 1)\n0000000000 65535 f \n".utf8)
        for offset in offsets { data += Data(String(format: "%010d 00000 n \n", offset).utf8) }
        data += Data("trailer\n<< /Size \(objects.count + 1) /Root 1 0 R >>\nstartxref\n\(xref)\n%%EOF\n".utf8)
        let source = root.appendingPathComponent(UUID().uuidString + ".pdf")
        try data.write(to: source)
        guard ink else { return source }
        let document = try XCTUnwrap(PDFDocument(url: source)), page = try XCTUnwrap(document.page(at: 0))
        let signature = PDFAnnotation(bounds: CGRect(x: origin[0] + 40, y: origin[1] + 150, width: 200, height: 80), forType: .ink, withProperties: nil)
        let path = NSBezierPath(); path.move(to: CGPoint(x: 10, y: 10)); path.curve(to: CGPoint(x: 190, y: 60), controlPoint1: CGPoint(x: 60, y: 90), controlPoint2: CGPoint(x: 120, y: -20))
        signature.add(path); signature.color = .red; let border = PDFBorder(); border.lineWidth = 4; signature.border = border
        page.addAnnotation(signature)
        let input = root.appendingPathComponent(UUID().uuidString + ".pdf")
        XCTAssertTrue(document.write(to: input))
        return input
    }
}
