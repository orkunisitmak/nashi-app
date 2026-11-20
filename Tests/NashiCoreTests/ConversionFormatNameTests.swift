import XCTest
import ImageIO
@testable import NashiCore

final class ConversionFormatNameTests: XCTestCase {
    func testImageToPDFAcceptsAnyCaseDotOrSpaces() throws {
        let root=FileManager.default.temporaryDirectory.appendingPathComponent("nashi-format-name-\(UUID().uuidString)")
        try FileManager.default.createDirectory(at:root,withIntermediateDirectories:true)
        defer { try? FileManager.default.removeItem(at:root) }
        let input=root.appendingPathComponent("photo.png")
        let context=try XCTUnwrap(CGContext(data:nil,width:32,height:24,bitsPerComponent:8,bytesPerRow:32*4,space:CGColorSpace(name:CGColorSpace.sRGB)!,bitmapInfo:CGImageAlphaInfo.premultipliedLast.rawValue))
        context.setFillColor(CGColor(red:1,green:0,blue:0,alpha:1));context.fill(CGRect(x:0,y:0,width:32,height:24))
        let destination=try XCTUnwrap(CGImageDestinationCreateWithURL(input as CFURL,"public.png" as CFString,1,nil))
        CGImageDestinationAddImage(destination,try XCTUnwrap(context.makeImage()),nil)
        XCTAssertTrue(CGImageDestinationFinalize(destination))

        for (index,name) in ["pdf","PDF",".pdf","Pdf"," pdf "].enumerated() {
            var options=OperationOptions();options.outputDirectory=root.appendingPathComponent("out-\(index)")
            try FileManager.default.createDirectory(at:try XCTUnwrap(options.outputDirectory),withIntermediateDirectories:true)
            let output=try XCTUnwrap(ConversionEngine.run(ConversionRequest(inputs:[input],operation:.convert(name),options:options)).first,"format \"\(name)\"")
            XCTAssertEqual(output.pathExtension,"pdf","format \"\(name)\"")
            XCTAssertEqual(try Data(contentsOf:output).prefix(5),Data("%PDF-".utf8),"format \"\(name)\"")
        }
    }
}
