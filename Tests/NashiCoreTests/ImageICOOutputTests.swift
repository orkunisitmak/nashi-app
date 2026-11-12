import XCTest
import ImageIO
@testable import NashiCore

final class ImageICOOutputTests:XCTestCase {
    func testICOHoldsSevenPNGFramesAndKeepsTheAspectRatio() throws {
        let directory=try OutputFiles.temporaryDirectory();defer { try? FileManager.default.removeItem(at:directory) }
        let svg=directory.appendingPathComponent("wide.svg")
        try "<svg xmlns='http://www.w3.org/2000/svg' width='64' height='32' viewBox='0 0 64 32'><rect width='64' height='32' fill='red'/></svg>".write(to:svg,atomically:true,encoding:.utf8)
        let original=try Data(contentsOf:svg)
        var options=OperationOptions();options.outputDirectory=directory
        let output=try XCTUnwrap(ImageEngine.convert(svg,to:"ico",options:options,token:CancellationToken()).first)
        XCTAssertEqual(output.pathExtension,"ico")
        let bytes=[UInt8](try Data(contentsOf:output))
        XCTAssertEqual(Array(bytes[0..<6]),[0,0,1,0,7,0],"ICONDIR: reserved, type 1, seven images")
        let expected=[16,24,32,48,64,128,256]
        var previousEnd=6+16*expected.count
        for (index,size) in expected.enumerated() {
            let entry=6+16*index
            XCTAssertEqual(Int(bytes[entry]),size==256 ? 0:size);XCTAssertEqual(Int(bytes[entry+1]),size==256 ? 0:size)
            XCTAssertEqual(Int(bytes[entry+4])|Int(bytes[entry+5])<<8,1);XCTAssertEqual(Int(bytes[entry+6])|Int(bytes[entry+7])<<8,32)
            let length=Int(bytes[entry+8])|Int(bytes[entry+9])<<8|Int(bytes[entry+10])<<16|Int(bytes[entry+11])<<24
            let offset=Int(bytes[entry+12])|Int(bytes[entry+13])<<8|Int(bytes[entry+14])<<16|Int(bytes[entry+15])<<24
            XCTAssertEqual(offset,previousEnd);previousEnd=offset+length
            XCTAssertEqual(Array(bytes[offset..<offset+8]),[0x89,0x50,0x4E,0x47,0x0D,0x0A,0x1A,0x0A])
            let image=try XCTUnwrap(CGImageSourceCreateWithData(Data(bytes[offset..<offset+length]) as CFData,nil).flatMap { CGImageSourceCreateImageAtIndex($0,0,nil) })
            XCTAssertEqual(image.width,size);XCTAssertEqual(image.height,size)
            // A 2:1 image fills the width and leaves transparent bands above and below.
            let context=try XCTUnwrap(CGContext(data:nil,width:size,height:size,bitsPerComponent:8,bytesPerRow:0,space:CGColorSpaceCreateDeviceRGB(),bitmapInfo:CGImageAlphaInfo.premultipliedLast.rawValue))
            context.draw(image,in:CGRect(x:0,y:0,width:size,height:size))
            let pixels=try XCTUnwrap(context.data).assumingMemoryBound(to:UInt8.self),stride=context.bytesPerRow
            XCTAssertEqual(pixels[2*stride+size/2*4+3],0,"\(size) px: the top row is transparent")
            XCTAssertGreaterThan(pixels[(size/2)*stride+size/2*4+3],200,"\(size) px: the centre is opaque")
        }
        XCTAssertEqual(previousEnd,bytes.count)
        XCTAssertEqual(try Data(contentsOf:svg),original,"Converting to ICO changed the source SVG.")
    }

    /// The progress callback for the second file runs after the first ICO is staged, so the
    /// cancel is deterministic and exercises removal of an already written ICO.
    func testCancellingBetweenICOFilesLeavesOnlyTheSource() throws {
        let directory=try OutputFiles.temporaryDirectory();defer { try? FileManager.default.removeItem(at:directory) }
        let svg=directory.appendingPathComponent("wide.svg")
        let source="<svg xmlns='http://www.w3.org/2000/svg' width='64' height='32' viewBox='0 0 64 32'><rect width='64' height='32' fill='red'/></svg>"
        try source.write(to:svg,atomically:true,encoding:.utf8)
        var options=OperationOptions();options.outputDirectory=directory
        let token=CancellationToken()
        XCTAssertThrowsError(try ConversionEngine.run(.init(inputs:[svg,svg],operation:.convert("ico"),options:options),token:token,progress:{ amount,_ in
            if amount>0&&amount<1 { token.cancel() }
        })) { error in
            guard case NashiError.cancelled=error else { return XCTFail("Expected cancellation, received \(error)") }
        }
        XCTAssertEqual(try String(contentsOf:svg,encoding:.utf8),source,"The original SVG changed.")
        XCTAssertEqual(try FileManager.default.contentsOfDirectory(atPath:directory.path),["wide.svg"],"A cancelled ICO was left behind.")
    }
}
