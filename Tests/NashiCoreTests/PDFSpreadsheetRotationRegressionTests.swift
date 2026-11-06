import XCTest
import Foundation
import PDFKit
import CoreText
@testable import NashiCore

/// PDF -> spreadsheet reads rows and columns from the page as displayed, so a
/// page saved with /Rotate must not come out transposed or split per letter.
final class PDFSpreadsheetRotationRegressionTests: XCTestCase {
    private var root: URL!
    override func setUpWithError() throws { root = try OutputFiles.temporaryDirectory() }
    override func tearDownWithError() throws { if let root { try? FileManager.default.removeItem(at: root) } }

    func testRotatedPagesKeepDisplayedTableRowsAndColumns() throws {
        let expected = [["Name", "Amount"], ["Apple", "12"], ["Pear", "7"]]
        for rotation in [0, 90, 180, 270] {
            XCTAssertEqual(try sheets(table(rotations: [rotation])), [expected], "rotation \(rotation)")
        }
        XCTAssertEqual(try sheets(table(rotations: [90], origin: CGPoint(x: 40, y: 25))), [expected], "offset media box")
        XCTAssertEqual(try sheets(table(rotations: [0, 90, 270])), [expected, expected, expected], "mixed rotations")
    }

    /// Draws the same table upright for each page as it will be displayed with its rotation.
    private func table(rotations: [Int], origin: CGPoint = .zero) throws -> URL {
        let url = root.appendingPathComponent("Rotated \(rotations.map(String.init).joined(separator: "-")) \(Int(origin.x)).pdf")
        var box = CGRect(x: origin.x, y: origin.y, width: 612, height: 792)
        let context = try XCTUnwrap(CGContext(url as CFURL, mediaBox: &box, nil))
        for rotation in rotations {
            context.beginPDFPage(nil); context.saveGState()
            let upright: CGAffineTransform
            switch rotation {
            case 90: upright = CGAffineTransform(a: 0, b: 1, c: -1, d: 0, tx: box.width, ty: 0)
            case 180: upright = CGAffineTransform(a: -1, b: 0, c: 0, d: -1, tx: box.width, ty: box.height)
            case 270: upright = CGAffineTransform(a: 0, b: -1, c: 1, d: 0, tx: 0, ty: box.height)
            default: upright = .identity
            }
            context.concatenate(upright.concatenating(CGAffineTransform(translationX: box.minX, y: box.minY)))
            let top: CGFloat = rotation % 180 == 90 ? 540 : 700
            for (value, x, y) in [("Name", 50.0, top), ("Amount", 250.0, top), ("Apple", 50.0, top - 30), ("12", 250.0, top - 30), ("Pear", 50.0, top - 60), ("7", 250.0, top - 60)] {
                let font = CTFontCreateWithName("Helvetica" as CFString, 14, nil)
                let text = NSAttributedString(string: value, attributes: [NSAttributedString.Key(kCTFontAttributeName as String): font])
                context.textPosition = CGPoint(x: x, y: y)
                CTLineDraw(CTLineCreateWithAttributedString(text), context)
            }
            context.restoreGState(); context.endPDFPage()
        }
        context.closePDF()
        let pdf = try XCTUnwrap(PDFDocument(url: url))
        for (index, rotation) in rotations.enumerated() { pdf.page(at: index)!.rotation = rotation }
        XCTAssertTrue(pdf.write(to: url))
        return url
    }

    private func sheets(_ input: URL) throws -> [[[String]]] {
        let work = root.appendingPathComponent(UUID().uuidString, isDirectory: true)
        try FileManager.default.createDirectory(at: work, withIntermediateDirectories: true)
        let fods = try PDFOfficeCodec.prepare(input, to: "xlsx", at: work, options: .init(), token: .init())
        let xml = try XMLDocument(contentsOf: fods, options: [.nodeLoadExternalEntitiesNever])
        return try xml.nodes(forXPath: "//*[local-name()='table']").map { table in
            try table.nodes(forXPath: "./*[local-name()='table-row']").map { row in
                try row.nodes(forXPath: "./*[local-name()='table-cell']").map { $0.stringValue ?? "" }
            }
        }
    }
}
