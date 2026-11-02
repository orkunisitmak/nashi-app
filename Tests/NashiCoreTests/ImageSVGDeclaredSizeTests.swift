import XCTest
import ImageIO
@testable import NashiCore

final class ImageSVGDeclaredSizeTests:XCTestCase {
    func testSVGConvertsAtItsDeclaredWidthAndHeight() throws {
        let directory=try OutputFiles.temporaryDirectory();defer { try? FileManager.default.removeItem(at:directory) }
        let svg=directory.appendingPathComponent("icon.svg")
        try "<svg xmlns='http://www.w3.org/2000/svg' width='24' height='24' viewBox='0 0 24 24'><rect width='24' height='24' fill='red'/></svg>".write(to:svg,atomically:true,encoding:.utf8)
        var options=OperationOptions();options.outputDirectory=directory
        let output=try XCTUnwrap(ImageEngine.convert(svg,to:"png",options:options,token:CancellationToken()).first)
        let source=try XCTUnwrap(CGImageSourceCreateWithURL(output as CFURL,nil))
        let properties=try XCTUnwrap(CGImageSourceCopyPropertiesAtIndex(source,0,nil) as? [CFString:Any])
        XCTAssertEqual(properties[kCGImagePropertyPixelWidth] as? Int,24);XCTAssertEqual(properties[kCGImagePropertyPixelHeight] as? Int,24)
    }
}
