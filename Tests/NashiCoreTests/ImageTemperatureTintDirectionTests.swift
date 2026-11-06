import XCTest
import CoreGraphics
@testable import NashiCore

/// Positive Temperature warms and positive Tint adds magenta, as in the Windows
/// editor (PhotoCodec.Adjust) and common photo editors.
final class ImageTemperatureTintDirectionTests: XCTestCase {
    private let space=CGColorSpace(name:CGColorSpace.sRGB)!

    private func gray() throws -> CGImage {
        let ctx=try XCTUnwrap(CGContext(data:nil,width:32,height:32,bitsPerComponent:8,bytesPerRow:0,space:space,bitmapInfo:CGImageAlphaInfo.premultipliedLast.rawValue))
        ctx.setFillColor(CGColor(srgbRed:0.5,green:0.5,blue:0.5,alpha:1));ctx.fill(CGRect(x:0,y:0,width:32,height:32))
        return try XCTUnwrap(ctx.makeImage())
    }

    private func center(_ image:CGImage) throws -> (r:Int,g:Int,b:Int) {
        var bytes=[UInt8](repeating:0,count:image.width*image.height*4)
        // The buffer pointer is only valid inside this closure, so draw there.
        try bytes.withUnsafeMutableBytes { buffer in
            let ctx=try XCTUnwrap(CGContext(data:buffer.baseAddress,width:image.width,height:image.height,bitsPerComponent:8,bytesPerRow:image.width*4,space:space,bitmapInfo:CGImageAlphaInfo.premultipliedLast.rawValue))
            ctx.draw(image,in:CGRect(x:0,y:0,width:image.width,height:image.height))
        }
        let offset=(image.height/2*image.width+image.width/2)*4
        return (Int(bytes[offset]),Int(bytes[offset+1]),Int(bytes[offset+2]))
    }

    private func adjusted(_ change:(inout ImageAdjustments)->Void) throws -> (r:Int,g:Int,b:Int) {
        var options=OperationOptions();change(&options.imageAdjustments)
        return try center(ImageEngine.preview(gray(),options:options))
    }

    func testPositiveTemperatureWarmsAndNegativeCools() throws {
        let warm=try adjusted { $0.temperature=1 },cool=try adjusted { $0.temperature = -1 }
        XCTAssertGreaterThan(warm.r,warm.b,"Temperature +1 should warm the image: \(warm)")
        XCTAssertGreaterThan(cool.b,cool.r,"Temperature -1 should cool the image: \(cool)")
    }

    func testPositiveTintAddsMagentaAndNegativeAddsGreen() throws {
        let magenta=try adjusted { $0.tint=1 },green=try adjusted { $0.tint = -1 }
        XCTAssertLessThan(magenta.g,min(magenta.r,magenta.b),"Tint +1 should add magenta: \(magenta)")
        XCTAssertGreaterThan(green.g,max(green.r,green.b),"Tint -1 should add green: \(green)")
    }
}
