import XCTest
import AppKit
import CoreImage
import PDFKit
@testable import NashiCore

/// PDF QR scanning must read only what a viewer shows: the page's CropBox, as displayed after /Rotate.
final class QRCodeCropBoxTests:XCTestCase {
    private func code(_ payload:String) throws -> CGImage {
        let filter=try XCTUnwrap(CIFilter(name:"CIQRCodeGenerator"));filter.setValue(Data(payload.utf8),forKey:"inputMessage");filter.setValue("M",forKey:"inputCorrectionLevel")
        let image=try XCTUnwrap(filter.outputImage).transformed(by:CGAffineTransform(scaleX:8,y:8))
        let padded=image.composited(over:CIImage(color:.white).cropped(to:image.extent.insetBy(dx:-32,dy:-32)))
        return try XCTUnwrap(CIContext().createCGImage(padded,from:padded.extent))
    }
    private func pdf(_ url:URL,media:CGRect,codes:[(String,CGRect)],crop:CGRect,rotation:Int=0) throws -> URL {
        var box=media;let context=try XCTUnwrap(CGContext(url as CFURL,mediaBox:&box,nil))
        context.beginPDFPage(nil);context.setFillColor(NSColor.white.cgColor);context.fill(media)
        for (payload,rect) in codes { context.draw(try code(payload),in:rect) }
        context.endPDFPage();context.closePDF()
        let document=try XCTUnwrap(PDFDocument(url:url)),page=try XCTUnwrap(document.page(at:0));page.setBounds(crop,for:.cropBox);page.rotation=rotation
        XCTAssertTrue(document.write(to:url));return url
    }
    func testCodeOutsideCropBoxIsNotReported() throws {
        let directory=try OutputFiles.temporaryDirectory();defer { try? FileManager.default.removeItem(at:directory) }
        let document=try pdf(directory.appendingPathComponent("hidden.pdf"),media:CGRect(x:0,y:0,width:1200,height:600),codes:[("VISIBLE",CGRect(x:50,y:50,width:500,height:500)),("HIDDEN",CGRect(x:650,y:50,width:500,height:500))],crop:CGRect(x:0,y:0,width:600,height:600))
        XCTAssertEqual(try QRCodeEngine.scan([document]).results.map(\.text),["VISIBLE"])
    }
    func testSmallCropBoxOnHugeMediaBoxKeepsCodeReadable() throws {
        let directory=try OutputFiles.temporaryDirectory();defer { try? FileManager.default.removeItem(at:directory) }
        let document=try pdf(directory.appendingPathComponent("huge.pdf"),media:CGRect(x:0,y:0,width:14400,height:14400),codes:[("SMALL VISIBLE",CGRect(x:20,y:20,width:180,height:180))],crop:CGRect(x:0,y:0,width:220,height:220))
        XCTAssertEqual(try QRCodeEngine.scan([document]).results.map(\.text),["SMALL VISIBLE"])
    }
    func testRotatedLongStripKeepsCodeReadable() throws {
        let directory=try OutputFiles.temporaryDirectory();defer { try? FileManager.default.removeItem(at:directory) }
        let strip=CGRect(x:0,y:0,width:300,height:3000)
        for rotation in [0,90,180,270] {
            let document=try pdf(directory.appendingPathComponent("strip-\(rotation).pdf"),media:strip,codes:[("STRIP \(rotation)",CGRect(x:20,y:1370,width:260,height:260))],crop:strip,rotation:rotation)
            XCTAssertEqual(try XCTUnwrap(PDFDocument(url:document)?.page(at:0)).rotation,rotation)
            XCTAssertEqual(try QRCodeEngine.scan([document]).results.map(\.text),["STRIP \(rotation)"],"/Rotate \(rotation)")
        }
    }
}
