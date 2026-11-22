import XCTest
import ImageIO
import UniformTypeIdentifiers
@testable import NashiCore

final class CompressBatchSizePresetTests: XCTestCase {
    private func write(_ width:Int,_ height:Int,to url:URL) throws {
        let context=try XCTUnwrap(CGContext(data:nil,width:width,height:height,bitsPerComponent:8,bytesPerRow:0,space:CGColorSpace(name:CGColorSpace.sRGB)!,bitmapInfo:CGImageAlphaInfo.premultipliedLast.rawValue))
        context.setFillColor(CGColor(red:0.2,green:0.4,blue:0.8,alpha:1));context.fill(CGRect(x:0,y:0,width:width,height:height))
        let destination=try XCTUnwrap(CGImageDestinationCreateWithURL(url as CFURL,UTType.png.identifier as CFString,1,nil))
        CGImageDestinationAddImage(destination,try XCTUnwrap(context.makeImage()),nil)
        XCTAssertTrue(CGImageDestinationFinalize(destination))
    }

    private func size(of url:URL) throws -> (Int,Int) {
        let image=try ImageEngine.load(url);return (image.width,image.height)
    }

    func testScaleAppliesToEachImageSeparately() throws {
        let directory=FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString);try FileManager.default.createDirectory(at:directory,withIntermediateDirectories:true)
        defer { try? FileManager.default.removeItem(at:directory) }
        let big=directory.appendingPathComponent("big.png"),small=directory.appendingPathComponent("small.png")
        try write(400,300,to:big);try write(100,50,to:small)
        var options=OperationOptions();options.imageScale=0.5
        let bigOut=try XCTUnwrap(ImageEngine.compress(big,options:options,token:CancellationToken()).first)
        let smallOut=try XCTUnwrap(ImageEngine.compress(small,options:options,token:CancellationToken()).first)
        XCTAssertEqual(try size(of:bigOut).0,200);XCTAssertEqual(try size(of:bigOut).1,150)
        XCTAssertEqual(try size(of:smallOut).0,50);XCTAssertEqual(try size(of:smallOut).1,25)
    }

    func testMaxEdgeShrinksOnlyImagesThatExceedIt() throws {
        let directory=FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString);try FileManager.default.createDirectory(at:directory,withIntermediateDirectories:true)
        defer { try? FileManager.default.removeItem(at:directory) }
        let big=directory.appendingPathComponent("big.png"),small=directory.appendingPathComponent("small.png")
        try write(400,200,to:big);try write(100,50,to:small)
        var options=OperationOptions();options.imageMaxEdge=200
        let bigOut=try XCTUnwrap(ImageEngine.compress(big,options:options,token:CancellationToken()).first)
        let smallOut=try XCTUnwrap(ImageEngine.compress(small,options:options,token:CancellationToken()).first)
        XCTAssertEqual(try size(of:bigOut).0,200);XCTAssertEqual(try size(of:bigOut).1,100)
        XCTAssertEqual(try size(of:smallOut).0,100);XCTAssertEqual(try size(of:smallOut).1,50)
    }
}
