import XCTest
import Foundation
import ImageIO
import PDFKit
@testable import NashiCore

final class PDFRenderResolutionTests: XCTestCase {
    func testRenderedPageImagesRecordTheRenderDPI() throws {
        let root = try OutputFiles.temporaryDirectory(); defer { try? FileManager.default.removeItem(at: root) }
        let pdf = PDFDocument(), page = PDFPage(); page.setBounds(CGRect(x: 0, y: 0, width: 595, height: 842), for: .mediaBox); pdf.insert(page, at: 0)
        let input = root.appendingPathComponent("A4.pdf"); XCTAssertTrue(pdf.write(to: input))
        for (format, dpi) in [("jpg", 300), ("png", 150)] {
            var options = OperationOptions(); options.outputDirectory = root.appendingPathComponent(format); options.dpi = dpi
            try FileManager.default.createDirectory(at: options.outputDirectory!, withIntermediateDirectories: true)
            let output = try PDFEngine.render(input, to: format, options: options, token: .init())[0]
            let properties = try XCTUnwrap(CGImageSourceCreateWithURL(output as CFURL, nil).flatMap { CGImageSourceCopyPropertiesAtIndex($0, 0, nil) } as? [CFString: Any])
            XCTAssertEqual((properties[kCGImagePropertyDPIWidth] as? NSNumber)?.doubleValue ?? 72, Double(dpi), accuracy: 0.5, format)
            XCTAssertEqual((properties[kCGImagePropertyDPIHeight] as? NSNumber)?.doubleValue ?? 72, Double(dpi), accuracy: 0.5, format)
        }
    }
}
