import XCTest
import AppKit
import PDFKit
import ImageIO
import CoreText
import CryptoKit
@testable import NashiCore

final class PDFEngineTests:XCTestCase {
    var root:URL!
    override func setUpWithError() throws { root=try OutputFiles.temporaryDirectory() }
    override func tearDownWithError() throws { try? FileManager.default.removeItem(at:root) }

    func testRasterExportIncludesEveryPageAt300DPI() throws {
        let input=try fixture(),before=try Data(contentsOf:input)
        let outputs=try PDFEngine.render(input,to:"png",options:.init(),token:.init())
        XCTAssertEqual(outputs.count,2);XCTAssertEqual(Set(outputs.map{$0.deletingLastPathComponent()}).count,1)
        let first=try ImageEngine.load(outputs[0]);XCTAssertEqual(first.width,1000);XCTAssertEqual(first.height,600)
        let second=try ImageEngine.load(outputs[1]);XCTAssertEqual(second.width,1500);XCTAssertEqual(second.height,900)
        XCTAssertEqual(try Data(contentsOf:input),before)
    }
    func testRasterExportsShowOnlyTheCropBox() throws {
        // A cropped page hides everything outside its CropBox in viewers; image,
        // EPUB and slide exports must not reveal that hidden red border.
        let url=root.appendingPathComponent("Cropped.pdf"),context=CGContext(consumer:CGDataConsumer(url:url as CFURL)!,mediaBox:nil,nil)!
        var media=CGRect(x:0,y:0,width:400,height:400),crop=CGRect(x:100,y:100,width:200,height:200)
        context.beginPDFPage([kCGPDFContextMediaBox:Data(bytes:&media,count:MemoryLayout<CGRect>.size),kCGPDFContextCropBox:Data(bytes:&crop,count:MemoryLayout<CGRect>.size)] as CFDictionary)
        context.setFillColor(CGColor(red:1,green:0,blue:0,alpha:1));context.fill(media);context.setFillColor(CGColor(red:0,green:1,blue:0,alpha:1));context.fill(crop)
        context.endPDFPage();context.closePDF()
        func redPixels(_ image:CGImage)->Int {
            var bytes=[UInt8](repeating:0,count:image.width*image.height*4)
            CGContext(data:&bytes,width:image.width,height:image.height,bitsPerComponent:8,bytesPerRow:image.width*4,space:CGColorSpaceCreateDeviceRGB(),bitmapInfo:CGImageAlphaInfo.premultipliedLast.rawValue)!.draw(image,in:CGRect(x:0,y:0,width:image.width,height:image.height))
            return stride(from:0,to:bytes.count,by:4).filter{bytes[$0]>200&&bytes[$0+1]<60&&bytes[$0+2]<60}.count
        }
        var options=OperationOptions();options.dpi=72
        let rendered=try ImageEngine.load(try PDFEngine.render(url,to:"png",options:options,token:.init())[0])
        XCTAssertEqual(rendered.width,200);XCTAssertEqual(rendered.height,200);XCTAssertEqual(redPixels(rendered),0)
        let raster=NSBitmapImageRep(data:try PDFOfficeCodec.rasterPNG(PDFDocument(url:url)!.page(at:0)!,dpi:72,token:.init()))!.cgImage!
        XCTAssertEqual(raster.width,200);XCTAssertEqual(raster.height,200);XCTAssertEqual(redPixels(raster),0)
    }
    func testMergeSplitAndOrganizeKeepEditableTextAndOriginals() throws {
        let source=try fixture(),before=try Data(contentsOf:source)
        let merged=try PDFEngine.tool(.mergePDF,inputs:[source,source],options:.init(),token:.init())
        XCTAssertEqual(try PDFEngine.open(merged[0]).pageCount,4)
        let split=try PDFEngine.tool(.splitPDF,inputs:[source],options:.init(),token:.init())
        XCTAssertEqual(split.count,2);XCTAssertNotEqual(split[0].deletingLastPathComponent(),source.deletingLastPathComponent())
        XCTAssertTrue(try PDFEngine.open(split[0]).string!.contains("Page One"));XCTAssertTrue(try PDFEngine.open(split[1]).string!.contains("Page Two"))
        var options=OperationOptions();options.pdfPageOrder=[1,0,1];options.pdfPageRotations=[0:90,1:0,2:180]
        let output=try PDFEngine.tool(.organizePDF,inputs:[source],options:options,token:.init())[0],pdf=try PDFEngine.open(output)
        XCTAssertEqual(pdf.pageCount,3);XCTAssertEqual(pdf.page(at:0)?.rotation,90);XCTAssertEqual(pdf.page(at:2)?.rotation,180)
        XCTAssertTrue(pdf.page(at:0)!.string!.contains("Page Two"));XCTAssertEqual(try Data(contentsOf:source),before)
    }
    func testUniformWidthScalesPagesProportionallyWithoutRasterizingText() throws {
        let input=try fixture();var options=OperationOptions();options.pdfUniformWidth=true
        let output=try PDFEngine.tool(.organizePDF,inputs:[input],options:options,token:.init())[0],pdf=try PDFEngine.open(output)
        XCTAssertEqual(pdf.pageCount,2)
        for page in (0..<2).compactMap({pdf.page(at:$0)}) { XCTAssertEqual(page.bounds(for:.mediaBox).width,240,accuracy:0.1);XCTAssertEqual(page.bounds(for:.mediaBox).height,144,accuracy:0.1) }
        XCTAssertTrue(pdf.string!.contains("Page One"));XCTAssertTrue(pdf.string!.contains("Page Two"))
    }
    func testPDFMetadataEditRemoveAndPercentCompressionHaveRealEffects() throws {
        let source=try fixture();var edit=OperationOptions();edit.metadata=["Title":"Private report","Author":"Fixture Author"]
        let tagged=try PDFEngine.tool(.removeMetadata,inputs:[source],options:edit,token:.init())[0]
        XCTAssertTrue(String(describing:try PDFEngine.open(tagged).documentAttributes).contains("Private report"))
        let stripped=try PDFEngine.tool(.removeMetadata,inputs:[tagged],options:.init(),token:.init())[0]
        XCTAssertFalse(String(describing:try PDFEngine.open(stripped).documentAttributes).contains("Private report"))
        let image=try noisyImage(),pdf=try PDFEngine.create(inputs:[image,image],options:.init(),token:.init())[0]
        let bytes=try Data(contentsOf:pdf).count
        var options=OperationOptions();options.percentage=75
        let compressed=try PDFEngine.tool(.compress,inputs:[pdf],options:options,token:.init())[0]
        XCTAssertLessThanOrEqual(try Data(contentsOf:compressed).count,bytes/4)
        XCTAssertEqual(try PDFEngine.open(compressed).pageCount,2)
    }
    func testImageCompressionReductionMeansReducedBytesAcrossCategories() throws {
        let source=try noisyImage(),original=try Data(contentsOf:source)
        var options=OperationOptions();options.percentage=75
        let out=try ImageEngine.compress(source,options:options,token:.init())[0]
        XCTAssertLessThanOrEqual(try Data(contentsOf:out).count,original.count/4)
        XCTAssertEqual(try Data(contentsOf:source),original)
    }
    func testInvalidPageRangesTargetsAndCancellationFailClearly() throws {
        XCTAssertEqual(try PDFEngine.pageOrder("3-1,2",count:3),[2,1,0,1])
        XCTAssertThrowsError(try PDFEngine.pageOrder("0,7",count:3))
        let input=try fixture();var options=OperationOptions();options.targetBytes = -1
        XCTAssertThrowsError(try PDFEngine.tool(.compress,inputs:[input],options:options,token:.init()))
        let token=CancellationToken();token.cancel();XCTAssertThrowsError(try PDFEngine.render(input,to:"jpg",options:.init(),token:token))
    }
    func testDefaultCompressionDoesNotEnlargeSmallImagesOrRasterizeSmallVectorPDFs() throws {
        let image=root.appendingPathComponent("Small.png")
        let context=try XCTUnwrap(CGContext(data:nil,width:8,height:8,bitsPerComponent:8,bytesPerRow:32,space:CGColorSpaceCreateDeviceRGB(),bitmapInfo:CGImageAlphaInfo.premultipliedLast.rawValue))
        context.setFillColor(NSColor.red.cgColor);context.fill(CGRect(x:0,y:0,width:8,height:8))
        let destination=try XCTUnwrap(CGImageDestinationCreateWithURL(image as CFURL,"public.png" as CFString,1,nil))
        CGImageDestinationAddImage(destination,try XCTUnwrap(context.makeImage()),nil);XCTAssertTrue(CGImageDestinationFinalize(destination))
        let imageBytes=try Data(contentsOf:image),output=try XCTUnwrap(ImageEngine.compress(image,options:.init(),token:.init()).first)
        XCTAssertLessThanOrEqual(try Data(contentsOf:output).count,imageBytes.count)
        XCTAssertEqual(try ImageEngine.load(output).width,8);XCTAssertEqual(try ImageEngine.load(output).height,8)
        XCTAssertEqual(try Data(contentsOf:image),imageBytes)
        let pdf=try fixture(),original=try Data(contentsOf:pdf)
        let compressed=try XCTUnwrap(PDFEngine.tool(.compress,inputs:[pdf],options:.init(),token:.init()).first)
        XCTAssertLessThanOrEqual(try Data(contentsOf:compressed).count,original.count)
        XCTAssertTrue(try PDFEngine.open(compressed).string?.contains("Page One") == true)
        XCTAssertEqual(try PDFEngine.open(compressed).pageCount,2);XCTAssertEqual(try Data(contentsOf:pdf),original)
    }
    private func fixture() throws -> URL {
        let url=root.appendingPathComponent("Source.pdf"),consumer=CGDataConsumer(url:url as CFURL)!,context=CGContext(consumer:consumer,mediaBox:nil,nil)!
        for index in 0..<2 { var rect=CGRect(x:0,y:0,width:index==0 ? 240:360,height:index==0 ? 144:216);let data=Data(bytes:&rect,count:MemoryLayout<CGRect>.size);context.beginPDFPage([kCGPDFContextMediaBox:data] as CFDictionary)
            let font=CTFontCreateWithName("Helvetica" as CFString,15,nil)
            let line=CTLineCreateWithAttributedString(NSAttributedString(string:index==0 ? "Page One":"Page Two",attributes:[NSAttributedString.Key(kCTFontAttributeName as String):font]));context.textPosition=CGPoint(x:20,y:90);CTLineDraw(line,context);context.endPDFPage()
        };context.closePDF();return url
    }
    private func noisyImage() throws -> URL {
        let w=600,h=400;var bytes=[UInt8](repeating:0,count:w*h*4);var seed:UInt32=12345
        for n in bytes.indices { seed=1664525 &* seed &+ 1013904223;bytes[n]=n%4==3 ? 255:UInt8(truncatingIfNeeded:seed>>24) }
        let data=Data(bytes),provider=CGDataProvider(data:data as CFData)!
        let image=CGImage(width:w,height:h,bitsPerComponent:8,bitsPerPixel:32,bytesPerRow:w*4,space:CGColorSpace(name:CGColorSpace.sRGB)!,bitmapInfo:CGBitmapInfo(rawValue:CGImageAlphaInfo.last.rawValue),provider:provider,decode:nil,shouldInterpolate:false,intent:.defaultIntent)!
        let url=root.appendingPathComponent("Noise.png");try ImageEngine.write(image,to:url);return url
    }
}
