import XCTest
import CoreGraphics
@testable import NashiCore

/// Raising Gamma brightens mid-tones and lowering it darkens them, as in the
/// Windows editor (PhotoCodec.Adjust) and Levels-style gamma controls.
final class ImageGammaDirectionTests: XCTestCase {
    private let space=CGColorSpace(name:CGColorSpace.sRGB)!

    private func gray() throws -> CGImage {
        let ctx=try XCTUnwrap(CGContext(data:nil,width:32,height:32,bitsPerComponent:8,bytesPerRow:0,space:space,bitmapInfo:CGImageAlphaInfo.premultipliedLast.rawValue))
        ctx.setFillColor(CGColor(srgbRed:0.5,green:0.5,blue:0.5,alpha:1));ctx.fill(CGRect(x:0,y:0,width:32,height:32))
        return try XCTUnwrap(ctx.makeImage())
    }

    private func center(_ image:CGImage) throws -> Int {
        var bytes=[UInt8](repeating:0,count:image.width*image.height*4)
        try bytes.withUnsafeMutableBytes { buffer in
            let ctx=try XCTUnwrap(CGContext(data:buffer.baseAddress,width:image.width,height:image.height,bitsPerComponent:8,bytesPerRow:image.width*4,space:space,bitmapInfo:CGImageAlphaInfo.premultipliedLast.rawValue))
            ctx.draw(image,in:CGRect(x:0,y:0,width:image.width,height:image.height))
        }
        return Int(bytes[(image.height/2*image.width+image.width/2)*4])
    }

    private func adjusted(gamma:Double) throws -> Int {
        var options=OperationOptions();options.imageAdjustments.gamma=gamma
        return try center(ImageEngine.preview(gray(),options:options))
    }

    func testRaisingGammaBrightensAndLoweringDarkens() throws {
        let neutral=try adjusted(gamma:1),bright=try adjusted(gamma:1.6),dark=try adjusted(gamma:0.5)
        XCTAssertGreaterThan(bright,neutral,"Gamma 1.6 should brighten mid-gray: \(bright) vs \(neutral)")
        XCTAssertLessThan(dark,neutral,"Gamma 0.5 should darken mid-gray: \(dark) vs \(neutral)")
    }
}
