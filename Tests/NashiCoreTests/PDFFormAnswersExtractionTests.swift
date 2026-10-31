import XCTest
import AppKit
import PDFKit
@testable import NashiCore

/// Filled form fields and FreeText notes are annotations, not page text. Editable
/// PDF to text, Word/Pages and Excel must keep them beside the page text, as the
/// page picture and OCR of a page without a text layer already do.
final class PDFFormAnswersExtractionTests: XCTestCase {
    private var root: URL!
    override func setUpWithError() throws { root = try OutputFiles.temporaryDirectory() }
    override func tearDownWithError() throws { try? FileManager.default.removeItem(at: root) }

    func testFilledFormAnswersAndNotesReachEditableOutputs() throws {
        let input = try form()
        let text = try output(input, "txt")
        XCTAssertEqual(text, "Applicant name: Zelda Quixote\n☒ I agree\nReviewed by Marlow\nColour: Green")
        XCTAssertTrue(try output(input, "md").contains("Applicant name: Zelda Quixote"))
        let document = try XMLDocument(xmlString: try output(input, "docx"))
        let blocks = try document.nodes(forXPath: "//*[local-name()='p' or local-name()='table-row']").compactMap(\.stringValue)
        XCTAssertTrue(blocks.contains { $0.contains("Applicant name:") && $0.contains("Zelda Quixote") }, "\(blocks)")
        XCTAssertTrue(blocks.contains { $0.contains("Colour:") && $0.contains("Green") }, "\(blocks)")
        let sheet = try XMLDocument(xmlString: try output(input, "xlsx"))
        let rows = try sheet.nodes(forXPath: "//*[local-name()='table-row']").map { try $0.nodes(forXPath: "*[local-name()='table-cell']").compactMap(\.stringValue) }
        XCTAssertTrue(rows.contains { $0.contains("Applicant name:") && $0.contains("Zelda Quixote") }, "\(rows)")
        for hidden in ["Hidden note", "hunter2"] { XCTAssertFalse(text.contains(hidden)) }
    }

    private func output(_ input: URL, _ format: String) throws -> String {
        let work = root.appendingPathComponent(format)
        try FileManager.default.createDirectory(at: work, withIntermediateDirectories: true)
        return try String(contentsOf: PDFOfficeCodec.prepare(input, to: format, at: work, options: OperationOptions(), token: .init()), encoding: .utf8)
    }

    private func form() throws -> URL {
        let source = root.appendingPathComponent("Blank.pdf")
        var media = CGRect(x: 0, y: 0, width: 400, height: 300)
        let context = try XCTUnwrap(CGContext(consumer: try XCTUnwrap(CGDataConsumer(url: source as CFURL)), mediaBox: &media, nil))
        context.beginPDFPage(nil)
        NSGraphicsContext.saveGraphicsState(); NSGraphicsContext.current = NSGraphicsContext(cgContext: context, flipped: false)
        for (label, x, y) in [("Applicant name:", 20.0, 250.0), ("I agree", 45.0, 200.0), ("Colour:", 20.0, 60.0)] {
            (label as NSString).draw(at: CGPoint(x: x, y: y), withAttributes: [.font: NSFont.systemFont(ofSize: 14)])
        }
        NSGraphicsContext.restoreGraphicsState()
        context.endPDFPage(); context.closePDF()
        let document = try XCTUnwrap(PDFDocument(url: source)), page = try XCTUnwrap(document.page(at: 0))
        let name = PDFAnnotation(bounds: CGRect(x: 150, y: 245, width: 200, height: 24), forType: .widget, withProperties: nil)
        name.widgetFieldType = .text; name.fieldName = "name"; name.widgetStringValue = "Zelda Quixote"
        let agree = PDFAnnotation(bounds: CGRect(x: 20, y: 199, width: 18, height: 18), forType: .widget, withProperties: nil)
        agree.widgetFieldType = .button; agree.widgetControlType = .checkBoxControl; agree.fieldName = "agree"; agree.buttonWidgetState = .onState
        let colour = PDFAnnotation(bounds: CGRect(x: 150, y: 56, width: 120, height: 24), forType: .widget, withProperties: nil)
        colour.widgetFieldType = .choice; colour.choices = ["Red", "Green"]; colour.widgetStringValue = "Green"; colour.fieldName = "colour"
        let note = PDFAnnotation(bounds: CGRect(x: 20, y: 110, width: 300, height: 40), forType: .freeText, withProperties: nil)
        note.contents = "Reviewed by Marlow"; note.font = NSFont.systemFont(ofSize: 16); note.fontColor = .black; note.color = .clear
        let password = PDFAnnotation(bounds: CGRect(x: 280, y: 20, width: 100, height: 20), forType: .widget, withProperties: nil)
        password.widgetFieldType = .text; password.fieldName = "pw"; password.widgetStringValue = "hunter2"; password.setValue(NSNumber(value: 8192), forAnnotationKey: .widgetFieldFlags)
        let hidden = PDFAnnotation(bounds: CGRect(x: 20, y: 10, width: 100, height: 20), forType: .freeText, withProperties: nil)
        hidden.contents = "Hidden note"; hidden.setValue(NSNumber(value: 2), forAnnotationKey: .flags)
        for annotation in [name, agree, colour, note, password, hidden] { page.addAnnotation(annotation) }
        let input = root.appendingPathComponent("Form.pdf")
        XCTAssertTrue(document.write(to: input))
        return input
    }
}
