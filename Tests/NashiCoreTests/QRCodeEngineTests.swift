import XCTest
import AppKit
import CoreImage
import PDFKit
@testable import NashiCore

final class QRCodeEngineTests:XCTestCase {
    private func fixture(_ directory:URL,_ payload:String) throws -> URL {
        let filter=CIFilter(name:"CIQRCodeGenerator")!;filter.setValue(Data(payload.utf8),forKey:"inputMessage");filter.setValue("M",forKey:"inputCorrectionLevel")
        let code=try XCTUnwrap(filter.outputImage).transformed(by:CGAffineTransform(scaleX:10,y:10))
        let padded=code.composited(over:CIImage(color:.white).cropped(to:code.extent.insetBy(dx:-40,dy:-40)))
        let image=try XCTUnwrap(CIContext().createCGImage(padded,from:padded.extent))
        let url=directory.appendingPathComponent("qr.png");try ImageEngine.write(image,to:url);return url
    }
    func testImagePDFMultiPageAndExportReadActualPayloads() throws {
        let directory=try OutputFiles.temporaryDirectory();defer { try? FileManager.default.removeItem(at:directory) }
        let payload="https://example.com/nashi-local-qr?value=İstanbul"
        let image=try fixture(directory,payload),original=try Data(contentsOf:image)
        let pdf=PDFDocument();let source=try XCTUnwrap(NSImage(contentsOf:image))
        pdf.insert(try XCTUnwrap(PDFPage(image:source)),at:0);pdf.insert(try XCTUnwrap(PDFPage(image:source)),at:1)
        let document=directory.appendingPathComponent("two.pdf");XCTAssertTrue(pdf.write(to:document))
        let report=try QRCodeEngine.scan([image,document])
        XCTAssertEqual(report.results.map(\.text),[payload,payload,payload]);XCTAssertTrue(report.errors.isEmpty)
        XCTAssertNil(report.results[0].page);XCTAssertEqual(report.results[1].page,1);XCTAssertEqual(report.results[2].page,2)
        XCTAssertTrue(FeatureCatalog.tools(for:[image,document]).contains(.readQRCode))
        let output=try XCTUnwrap(ConversionEngine.run(.init(inputs:[image,document],operation:.tool(.readQRCode))).first)
        XCTAssertEqual(try JSONDecoder().decode(QRCodeScanReport.self,from:Data(contentsOf:output)).results.count,3)
        XCTAssertEqual(try Data(contentsOf:image),original)
    }
    func testPerFileErrorsEmptyResultsAndCancellationAreExplicit() throws {
        let directory=try OutputFiles.temporaryDirectory();defer { try? FileManager.default.removeItem(at:directory) }
        let image=try fixture(directory,"Readable code")
        let bad=directory.appendingPathComponent("broken.pdf");try Data("invalid PDF".utf8).write(to:bad)
        let report=try QRCodeEngine.scan([bad,image]);XCTAssertEqual(report.errors.count,1);XCTAssertEqual(report.results.first?.text,"Readable code")
        let empty=directory.appendingPathComponent("blank.png")
        let bitmap=NSBitmapImageRep(bitmapDataPlanes:nil,pixelsWide:32,pixelsHigh:32,bitsPerSample:8,samplesPerPixel:3,hasAlpha:false,isPlanar:false,colorSpaceName:.deviceRGB,bytesPerRow:0,bitsPerPixel:0)!
        try ImageEngine.write(bitmap.cgImage!,to:empty)
        XCTAssertTrue(try QRCodeEngine.scan([empty]).results.isEmpty)
        let cancelled=CancellationToken();cancelled.cancel();XCTAssertThrowsError(try QRCodeEngine.scan([image],token:cancelled))
    }
    func testBinaryQRCodeIsReportedInsteadOfAnEmptyScan() throws {
        let directory=try OutputFiles.temporaryDirectory();defer { try? FileManager.default.removeItem(at:directory) }
        let filter=CIFilter(name:"CIQRCodeGenerator")!;filter.setValue(Data([0xff,0xfe,0x00,0x80,0x81,0xc3,0x28,0x00]),forKey:"inputMessage")
        let code=try XCTUnwrap(filter.outputImage).transformed(by:CGAffineTransform(scaleX:10,y:10))
        let padded=code.composited(over:CIImage(color:.white).cropped(to:code.extent.insetBy(dx:-40,dy:-40)))
        let url=directory.appendingPathComponent("binary.png");try ImageEngine.write(try XCTUnwrap(CIContext().createCGImage(padded,from:padded.extent)),to:url)
        let report=try QRCodeEngine.scan([url])
        XCTAssertTrue(report.results.isEmpty);XCTAssertEqual(report.errors,["binary.png: A QR code was found, but the content is not text."])
    }
}
