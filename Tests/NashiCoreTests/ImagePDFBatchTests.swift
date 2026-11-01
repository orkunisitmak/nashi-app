import XCTest
import CoreGraphics
import PDFKit
@testable import NashiCore

final class ImagePDFBatchTests:XCTestCase {
    private var root:URL!
    override func setUpWithError() throws { root=try OutputFiles.temporaryDirectory() }
    override func tearDownWithError() throws { try? FileManager.default.removeItem(at:root) }

    private func image(_ name:String,width:Int) throws -> URL {
        let url=root.appendingPathComponent(name)
        let context=try XCTUnwrap(CGContext(data:nil,width:width,height:12,bitsPerComponent:8,bytesPerRow:0,space:CGColorSpaceCreateDeviceRGB(),bitmapInfo:CGImageAlphaInfo.premultipliedLast.rawValue))
        context.setFillColor(CGColor(gray:1,alpha:1));context.fill(CGRect(x:0,y:0,width:CGFloat(width),height:12))
        try ImageEngine.write(XCTUnwrap(context.makeImage()),to:url)
        return url
    }

    func testPDFConversionAndCreatePDFKeepAllImagesInInputOrder() throws {
        let first=try image("First.png",width:24),second=try image("Second.jpg",width:36)
        let inputs=[second,first]
        let originals=try inputs.map { try Data(contentsOf:$0) }
        for operation in [Operation.convert("pdf"),.tool(.createPDF)] {
            var options=OperationOptions();options.outputDirectory=root.appendingPathComponent(UUID().uuidString);options.combinePDFs=true
            let outputs=try ConversionEngine.run(.init(inputs:inputs,operation:operation,options:options))
            XCTAssertEqual(outputs.count,1)
            let pdf=try PDFEngine.open(XCTUnwrap(outputs.first))
            XCTAssertEqual(pdf.pageCount,2)
            XCTAssertEqual(try XCTUnwrap(pdf.page(at:0)).bounds(for:.mediaBox).width,36,accuracy:0.1)
            XCTAssertEqual(try XCTUnwrap(pdf.page(at:1)).bounds(for:.mediaBox).width,24,accuracy:0.1)
        }
        XCTAssertEqual(try inputs.map { try Data(contentsOf:$0) },originals)
    }

    func testSingleImageStillCreatesOnePage() throws {
        let input=try image("Single.png",width:24)
        let outputs=try ConversionEngine.run(.init(inputs:[input],operation:.convert("pdf")))
        XCTAssertEqual(outputs.count,1)
        XCTAssertEqual(try PDFEngine.open(XCTUnwrap(outputs.first)).pageCount,1)
    }

    func testPDFConversionDefaultsToSeparateFiles() throws {
        let inputs=try [image("First.png",width:24),image("Second.png",width:36)]
        for combine in [nil,false] as [Bool?] {
            var options=OperationOptions();options.combinePDFs=combine
            let outputs=try ConversionEngine.run(.init(inputs:inputs,operation:.convert("pdf"),options:options))
            XCTAssertEqual(outputs.count,2)
            XCTAssertEqual(try outputs.map { try PDFEngine.open($0).pageCount },[1,1])
        }
    }

    func testDocumentsCanBeCombinedOrKeptSeparate() throws {
        let inputs=[root.appendingPathComponent("First.html"),root.appendingPathComponent("Second.html")]
        for (index,input) in inputs.enumerated() {
            try "<!doctype html><html><body>Document \(index+1)</body></html>".write(to:input,atomically:true,encoding:.utf8)
        }
        for combine in [false,true] {
            var options=OperationOptions();options.combinePDFs=combine;options.outputDirectory=root.appendingPathComponent(UUID().uuidString)
            let outputs=try ConversionEngine.run(.init(inputs:inputs,operation:.convert("pdf"),options:options))
            XCTAssertEqual(outputs.count,combine ? 1:2)
            if combine {
                let pdf=try PDFEngine.open(XCTUnwrap(outputs.first))
                XCTAssertEqual(pdf.pageCount,2)
                XCTAssertTrue(pdf.page(at:0)?.string?.contains("Document 1") == true)
                XCTAssertTrue(pdf.page(at:1)?.string?.contains("Document 2") == true)
                XCTAssertEqual(try FileManager.default.contentsOfDirectory(at:options.outputDirectory!,includingPropertiesForKeys:nil).count,1)
            }
        }
    }

    func testOtherImageConversionsStillProduceSeparateFiles() throws {
        let inputs=try [image("First.png",width:24),image("Second.png",width:36)]
        var options=OperationOptions();options.combinePDFs=true
        let outputs=try ConversionEngine.run(.init(inputs:inputs,operation:.convert("jpg"),options:options))
        XCTAssertEqual(outputs.count,2)
        XCTAssertEqual(try outputs.map { try ImageEngine.load($0).width },[24,36])
    }

    func testMixedImageAndDocumentCanBeCombined() throws {
        let picture=try image("Picture.png",width:24),document=root.appendingPathComponent("Document.html")
        try "<html><body>Document page</body></html>".write(to:document,atomically:true,encoding:.utf8)
        var options=OperationOptions();options.combinePDFs=true
        let outputs=try ConversionEngine.run(.init(inputs:[picture,document],operation:.convert("pdf"),options:options))
        XCTAssertEqual(outputs.count,1)
        let pdf=try PDFEngine.open(XCTUnwrap(outputs.first))
        XCTAssertEqual(pdf.pageCount,2)
        XCTAssertTrue(pdf.page(at:1)?.string?.contains("Document page") == true)
    }

    func testInvalidImageDoesNotPublishPartialPDF() throws {
        let input=try image("First.png",width:24),invalid=root.appendingPathComponent("Broken.png")
        try Data("not an image".utf8).write(to:invalid)
        let document=root.appendingPathComponent("Document.html")
        try "<html><body>Document page</body></html>".write(to:document,atomically:true,encoding:.utf8)
        for first in [input,document] {
            var options=OperationOptions();options.outputDirectory=root.appendingPathComponent(UUID().uuidString);options.combinePDFs=true
            XCTAssertThrowsError(try ConversionEngine.run(.init(inputs:[first,invalid],operation:.convert("pdf"),options:options)))
            let files=(try? FileManager.default.contentsOfDirectory(at:options.outputDirectory!,includingPropertiesForKeys:nil)) ?? []
            XCTAssertTrue(files.isEmpty)
        }
    }
}
