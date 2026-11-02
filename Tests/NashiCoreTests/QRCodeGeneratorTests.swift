import XCTest
import AppKit
@testable import NashiCore

final class QRCodeGeneratorTests:XCTestCase {
    func testRoundTripUnicodeAllCorrectionLevelsAndWhiteQuietZone()throws {
        let root=try OutputFiles.temporaryDirectory();defer { try? FileManager.default.removeItem(at:root) }
        for level in ["L","M","Q","H"] {
            let text="İstanbul ığüş 🍐 https://example.com/qr?x=1&y=2"
            let output=try QRCodeGenerator.export(text:text,correction:level,directory:root)
            XCTAssertEqual(try QRCodeEngine.scan([output]).results.map(\.text),[text])
            let image=try ImageEngine.load(output),bitmap=NSBitmapImageRep(cgImage:image)
            XCTAssertEqual(image.width,512);XCTAssertEqual(image.height,512)
            for (x,y) in [(0,0),(511,511),(0,256),(256,0),(511,256),(256,511)] {
                let color=try XCTUnwrap(bitmap.colorAt(x:x,y:y)?.usingColorSpace(.deviceRGB));XCTAssertEqual(color.redComponent,1,accuracy:0.001)
            }
            for x in stride(from:0,to:512,by:7) { for y in stride(from:0,to:512,by:7) {
                let value=try XCTUnwrap(bitmap.colorAt(x:x,y:y)?.usingColorSpace(.deviceRGB)).redComponent
                XCTAssertTrue(value<0.001 || value>0.999,"QR contains interpolated pixels")
            } }
        }
    }
    func testUniqueNamesCancellationAndInvalidRequests()throws {
        let root=try OutputFiles.temporaryDirectory();defer { try? FileManager.default.removeItem(at:root) }
        let existing=root.appendingPathComponent("QR Code.png");try Data("keep".utf8).write(to:existing)
        let output=try QRCodeGenerator.export(text:"hello",directory:root)
        XCTAssertEqual(output.lastPathComponent,"QR Code 2.png");XCTAssertEqual(try Data(contentsOf:existing),Data("keep".utf8))
        let token=CancellationToken();token.cancel()
        XCTAssertThrowsError(try QRCodeGenerator.export(text:"hello",directory:root,token:token))
        for (text,size,level) in [("",512,"M"),(String(repeating:"a",count:2001),512,"M"),("hello",32,"M"),("hello",512,"X")] { XCTAssertThrowsError(try QRCodeGenerator.image(text:text,size:size,correction:level)) }
        XCTAssertThrowsError(try QRCodeGenerator.export(text:"hello",directory:root,filename:"../escape.png"))
        XCTAssertEqual(try FileManager.default.contentsOfDirectory(atPath:root.path).sorted(),["QR Code 2.png","QR Code.png"])
    }
}
