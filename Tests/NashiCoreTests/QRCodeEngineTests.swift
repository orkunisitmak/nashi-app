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
    /// The code drawn with `modules` on a fully transparent background, saved as PNG.
    private func transparentFixture(_ directory:URL,_ payload:String,modules:CGColor) throws -> URL {
        let filter=CIFilter(name:"CIQRCodeGenerator")!;filter.setValue(Data(payload.utf8),forKey:"inputMessage");filter.setValue("M",forKey:"inputCorrectionLevel")
        let code=try XCTUnwrap(filter.outputImage).transformed(by:CGAffineTransform(scaleX:10,y:10))
        let solid=code.applyingFilter("CIColorInvert").applyingFilter("CIMaskToAlpha")
        let tinted=solid.applyingFilter("CIColorMatrix",parameters:["inputRVector":CIVector(x:0,y:0,z:0,w:0),"inputGVector":CIVector(x:0,y:0,z:0,w:0),"inputBVector":CIVector(x:0,y:0,z:0,w:0),"inputBiasVector":CIVector(x:CIColor(cgColor:modules).red,y:CIColor(cgColor:modules).green,z:CIColor(cgColor:modules).blue,w:0)])
        let canvas=tinted.composited(over:CIImage(color:CIColor(red:0,green:0,blue:0,alpha:0)).cropped(to:code.extent.insetBy(dx:-40,dy:-40)))
        let image=try XCTUnwrap(CIContext().createCGImage(canvas,from:canvas.extent,format:.RGBA8,colorSpace:CGColorSpace(name:CGColorSpace.sRGB)))
        let url=directory.appendingPathComponent("transparent.png");try ImageEngine.write(image,to:url);return url
    }
    func testCodeWithTransparentBackgroundIsRead() throws {
        let directory=try OutputFiles.temporaryDirectory();defer { try? FileManager.default.removeItem(at:directory) }
        let image=try transparentFixture(directory,"Transparent code",modules:CGColor(red:0,green:0,blue:0,alpha:1))
        XCTAssertEqual(try QRCodeEngine.scan([image]).results.map(\.text),["Transparent code"])
    }
    func testWhiteModulesOnTransparentBackgroundAreStillRead() throws {
        let directory=try OutputFiles.temporaryDirectory();defer { try? FileManager.default.removeItem(at:directory) }
        let image=try transparentFixture(directory,"Light code",modules:CGColor(red:1,green:1,blue:1,alpha:1))
        XCTAssertEqual(try QRCodeEngine.scan([image]).results.map(\.text),["Light code"])
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
    private func binaryCode() throws -> CGImage {
        let filter=CIFilter(name:"CIQRCodeGenerator")!;filter.setValue(Data([0xff,0xfe,0x00,0x80,0x81,0xc3,0x28,0x00]),forKey:"inputMessage")
        let code=try XCTUnwrap(filter.outputImage).transformed(by:CGAffineTransform(scaleX:10,y:10))
        let padded=code.composited(over:CIImage(color:.white).cropped(to:code.extent.insetBy(dx:-40,dy:-40)))
        return try XCTUnwrap(CIContext().createCGImage(padded,from:padded.extent))
    }
    func testBinaryQRCodeIsReportedInsteadOfAnEmptyScan() throws {
        let directory=try OutputFiles.temporaryDirectory();defer { try? FileManager.default.removeItem(at:directory) }
        let url=directory.appendingPathComponent("binary.png");try ImageEngine.write(try binaryCode(),to:url)
        let report=try QRCodeEngine.scan([url])
        XCTAssertTrue(report.results.isEmpty);XCTAssertEqual(report.errors,["binary.png: A QR code was found, but the content is not text."])
        // The reader's "No QR codes this time." state is only for a scan with nothing to say.
        XCTAssertFalse(report.foundNothing);XCTAssertTrue(QRCodeScanReport().foundNothing)
        XCTAssertEqual(AppLocalization.text(report.errors[0],language:"tr"),"binary.png: Bir QR kodu bulundu ama içeriği metin değil.")
        XCTAssertEqual(AppLocalization.text(report.errors[0],language:"fr"),"binary.png\u{00A0}: Un code QR a été trouvé, mais son contenu n’est pas du texte.")
    }
    func testBinaryQRCodeOnAPDFPageNamesThePage() throws {
        let directory=try OutputFiles.temporaryDirectory();defer { try? FileManager.default.removeItem(at:directory) }
        let image=try binaryCode(),readable=try XCTUnwrap(NSImage(contentsOf:try fixture(directory,"Readable code")))
        let pdf=PDFDocument();pdf.insert(try XCTUnwrap(PDFPage(image:readable)),at:0)
        pdf.insert(try XCTUnwrap(PDFPage(image:NSImage(cgImage:image,size:NSSize(width:image.width,height:image.height)))),at:1)
        let document=directory.appendingPathComponent("mixed.pdf");XCTAssertTrue(pdf.write(to:document))
        let report=try QRCodeEngine.scan([document])
        XCTAssertEqual(report.results.map(\.text),["Readable code"]);XCTAssertEqual(report.errors,["mixed.pdf: A QR code on page 2 was found, but the content is not text."])
        XCTAssertEqual(AppLocalization.text(report.errors[0],language:"tr"),"mixed.pdf: 2. sayfada bir QR kodu bulundu ama içeriği metin değil.")
        XCTAssertEqual(AppLocalization.text("scan.png: 3 QR codes were found, but the content is not text.",language:"tr"),"scan.png: 3 QR kodu bulundu ama içerikleri metin değil.")
        XCTAssertEqual(AppLocalization.text("scan.pdf: 3 QR codes on page 4 were found, but the content is not text.",language:"fr"),"scan.pdf\u{00A0}: 3 codes QR ont été trouvés à la page 4, mais leur contenu n’est pas du texte.")
    }
}
