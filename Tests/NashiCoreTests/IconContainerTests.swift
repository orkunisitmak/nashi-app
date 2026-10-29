import XCTest
import CoreGraphics
import ImageIO
@testable import NashiCore

final class IconContainerTests: XCTestCase {
    func testTransparentPNGBecomesEverySizeWithAlpha() throws {
        let folder=try OutputFiles.temporaryDirectory();defer { try? FileManager.default.removeItem(at:folder) }
        // Left half fully transparent, right half 50% green.
        let input=folder.appendingPathComponent("Transparent.png")
        try ImageEngine.write(image(40,40) { context in context.setFillColor(CGColor(srgbRed:0,green:1,blue:0,alpha:0.5));context.fill(CGRect(x:20,y:0,width:20,height:40)) },to:input)
        let original=try Data(contentsOf:input)
        let icon=try XCTUnwrap(ImageEngine.convert(input,to:"ico",options:.init(),token:.init()).first)
        XCTAssertEqual(icon.pathExtension,"ico")
        XCTAssertEqual(try Data(contentsOf:input),original)
        let entries=try self.entries(Data(contentsOf:icon))
        XCTAssertEqual(entries.map(\.size),IconContainer.sizes)
        for (size,png) in entries {
            let pixels=try rgba(png,size:size)
            XCTAssertEqual(pixels(size/4,size/2).alpha,0,"full transparency at \(size) px")
            let half=pixels(size*3/4,size/2)
            XCTAssertEqual(Double(half.alpha),128,accuracy:4,"partial alpha at \(size) px")
            XCTAssertGreaterThan(half.green,200)
        }
    }

    func testWideImageIsFittedWithTransparentPaddingNotCropped() throws {
        let folder=try OutputFiles.temporaryDirectory();defer { try? FileManager.default.removeItem(at:folder) }
        let input=folder.appendingPathComponent("Wide.jpg")
        try ImageEngine.write(image(200,100) { context in context.setFillColor(CGColor(srgbRed:1,green:0,blue:0,alpha:1));context.fill(CGRect(x:0,y:0,width:200,height:100)) },to:input)
        let icon=try XCTUnwrap(ImageEngine.convert(input,to:"ico",options:.init(),token:.init()).first)
        for (size,png) in try entries(Data(contentsOf:icon)) {
            let pixels=try rgba(png,size:size)
            let rows=(0..<size).filter { pixels(size/2,$0).alpha>127 }.count
            let columns=(0..<size).filter { pixels($0,size/2).alpha>127 }.count
            XCTAssertEqual(columns,size,"width at \(size) px")
            XCTAssertEqual(Double(rows),Double(size/2),accuracy:1,"height at \(size) px")
            XCTAssertEqual(pixels(size/2,0).alpha,0);XCTAssertEqual(pixels(size/2,size-1).alpha,0)
            let centre=pixels(size/2,size/2);XCTAssertGreaterThan(centre.red,240);XCTAssertEqual(centre.alpha,255)
        }
    }

    func testSmallSourceStillGetsEverySize() throws {
        let folder=try OutputFiles.temporaryDirectory();defer { try? FileManager.default.removeItem(at:folder) }
        let output=folder.appendingPathComponent("Tiny.ico")
        try IconContainer.write(image(8,8) { context in context.setFillColor(CGColor(srgbRed:0,green:0,blue:1,alpha:1));context.fill(CGRect(x:0,y:0,width:8,height:8)) },to:output)
        XCTAssertEqual(try entries(Data(contentsOf:output)).map(\.size),IconContainer.sizes)
    }

    func testCancelledExportLeavesNoFileAndExistingFilesAreKept() throws {
        let folder=try OutputFiles.temporaryDirectory();defer { try? FileManager.default.removeItem(at:folder) }
        let source=try image(40,40) { _ in }
        let cancelled=folder.appendingPathComponent("Cancelled.ico"),token=CancellationToken();token.cancel()
        XCTAssertThrowsError(try IconContainer.write(source,to:cancelled,token:token))
        XCTAssertFalse(FileManager.default.fileExists(atPath:cancelled.path))
        let existing=folder.appendingPathComponent("Existing.ico");try Data("keep".utf8).write(to:existing)
        XCTAssertThrowsError(try IconContainer.write(source,to:existing))
        XCTAssertEqual(try Data(contentsOf:existing),Data("keep".utf8))
    }

    private func image(_ width:Int,_ height:Int,_ draw:(CGContext)->Void) throws -> CGImage {
        let context=try XCTUnwrap(CGContext(data:nil,width:width,height:height,bitsPerComponent:8,bytesPerRow:0,space:CGColorSpace(name:CGColorSpace.sRGB)!,bitmapInfo:CGImageAlphaInfo.premultipliedLast.rawValue))
        draw(context);return try XCTUnwrap(context.makeImage())
    }

    // Reads ICONDIR and each ICONDIRENTRY, and returns every entry's PNG bytes.
    private func entries(_ icon:Data) throws -> [(size:Int,png:Data)] {
        let bytes=[UInt8](icon)
        func u16(_ i:Int)->Int { Int(bytes[i])|Int(bytes[i+1])<<8 }
        func u32(_ i:Int)->Int { u16(i)|u16(i+2)<<16 }
        XCTAssertEqual(Array(bytes.prefix(4)),[0,0,1,0])
        let count=u16(4);var result:[(Int,Data)]=[]
        for index in 0..<count {
            let entry=6+16*index,width=bytes[entry]==0 ? 256:Int(bytes[entry]),height=bytes[entry+1]==0 ? 256:Int(bytes[entry+1])
            let length=u32(entry+8),offset=u32(entry+12)
            XCTAssertEqual(width,height);XCTAssertEqual(u16(entry+4),1);XCTAssertEqual(u16(entry+6),32)
            XCTAssertLessThanOrEqual(offset+length,bytes.count)
            let png=Data(bytes[offset..<offset+length]);XCTAssertEqual(Array(png.prefix(4)),[0x89,0x50,0x4e,0x47])
            result.append((width,png))
        }
        return result
    }

    // Straight-alpha samples, with y counted from the top as in the PNG.
    private func rgba(_ png:Data,size:Int) throws -> (Int,Int)->(red:Int,green:Int,alpha:Int) {
        let source=try XCTUnwrap(CGImageSourceCreateWithData(png as CFData,nil)),image=try XCTUnwrap(CGImageSourceCreateImageAtIndex(source,0,nil))
        XCTAssertEqual(image.width,size);XCTAssertEqual(image.height,size)
        var pixels=[UInt8](repeating:0,count:size*size*4)
        try pixels.withUnsafeMutableBytes { buffer in
            let context=try XCTUnwrap(CGContext(data:buffer.baseAddress,width:size,height:size,bitsPerComponent:8,bytesPerRow:size*4,space:CGColorSpace(name:CGColorSpace.sRGB)!,bitmapInfo:CGImageAlphaInfo.premultipliedLast.rawValue))
            context.draw(image,in:CGRect(x:0,y:0,width:size,height:size))
        }
        let copy=pixels
        return { x,y in
            let i=(y*size+x)*4,alpha=Int(copy[i+3])
            func straight(_ value:UInt8)->Int { alpha==0 ? 0:min(255,Int(value)*255/alpha) }
            return (straight(copy[i]),straight(copy[i+1]),alpha)
        }
    }
}
