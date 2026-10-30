import XCTest
import AppKit
@testable import NashiCore

final class CommandLineTests:XCTestCase {
    func testDependencyDiscoveryReturnsTheExifToolScriptInsteadOfItsDirectory() throws {
        let script=try Dependencies.executable("exiftool")
        XCTAssertTrue(try script.resourceValues(forKeys:[.isRegularFileKey]).isRegularFile == true)
        let result=try ProcessRunner.run(URL(fileURLWithPath:"/usr/bin/perl"),[script.path,"-config","","-ver"])
        XCTAssertEqual(result.text.trimmingCharacters(in:.whitespacesAndNewlines),"13.59")
    }
    func testPartialNestedImageOptionsUseDefaultsAndPerformRealEdits() throws {
        let root=try OutputFiles.temporaryDirectory();defer{try? FileManager.default.removeItem(at:root)}
        let input=root.appendingPathComponent("pattern.png"),destination=root.appendingPathComponent("Output")
        try FileManager.default.createDirectory(at:destination,withIntermediateDirectories:true)
        let context=try XCTUnwrap(CGContext(data:nil,width:64,height:64,bitsPerComponent:8,bytesPerRow:256,space:CGColorSpace(name:CGColorSpace.sRGB)!,bitmapInfo:CGImageAlphaInfo.premultipliedLast.rawValue))
        context.setFillColor(CGColor(gray:0,alpha:1));context.fill(CGRect(x:0,y:0,width:64,height:64))
        context.setFillColor(CGColor(gray:1,alpha:1));context.fill(CGRect(x:32,y:0,width:32,height:64))
        try ImageEngine.write(XCTUnwrap(context.makeImage()),to:input)
        let bytes=try Data(contentsOf:input)
        let options=String(decoding:try JSONSerialization.data(withJSONObject:["outputDirectory":destination.path,"imageAdjustments":["blur":2]]),as:UTF8.self)
        XCTAssertEqual(NashiCommandLine.run(arguments:["Nashi","tool","editImage",input.path,"--options",options]),0)
        let edited=try XCTUnwrap(FileManager.default.contentsOfDirectory(at:destination,includingPropertiesForKeys:nil).first)
        var pixels=[UInt8](repeating:0,count:64*64*4)
        try pixels.withUnsafeMutableBytes { buffer in
            let rendered=try XCTUnwrap(CGContext(data:buffer.baseAddress,width:64,height:64,bitsPerComponent:8,bytesPerRow:256,space:CGColorSpace(name:CGColorSpace.sRGB)!,bitmapInfo:CGImageAlphaInfo.premultipliedLast.rawValue))
            rendered.draw(try ImageEngine.load(edited),in:CGRect(x:0,y:0,width:64,height:64))
        }
        XCTAssertGreaterThan(pixels[(32*64+31)*4],10)
        var tags=ImageMetadataOptions();tags.updates=["author":"Private Author"]
        let tagged=try ImageEngine.editMetadata(input,changes:tags)
        let taggedBytes=try Data(contentsOf:tagged)
        let strip=String(decoding:try JSONSerialization.data(withJSONObject:["outputDirectory":destination.path,"imageMetadata":["removeAll":true]]),as:UTF8.self)
        XCTAssertEqual(NashiCommandLine.run(arguments:["Nashi","tool","removeMetadata",tagged.path,"--options",strip]),0)
        let clean=try XCTUnwrap(FileManager.default.contentsOfDirectory(at:destination,includingPropertiesForKeys:nil).first{$0.lastPathComponent.contains("Clean")})
        XCTAssertNil(ImageEngine.metadata(clean)["{TIFF}.Artist"])
        XCTAssertEqual(try Data(contentsOf:input),bytes);XCTAssertEqual(try Data(contentsOf:tagged),taggedBytes)
    }
    func testPlainPathAndFileURLDestinationsPreserveSourceAndExistingFiles() throws {
        let root=try OutputFiles.temporaryDirectory();defer{try? FileManager.default.removeItem(at:root)}
        let input=root.appendingPathComponent("source ' $().srt"),destination=root.appendingPathComponent("Output files")
        try FileManager.default.createDirectory(at:destination,withIntermediateDirectories:true)
        let source=Data("1\n00:00:00,125 --> 00:00:01,250\nMerhaba dünya\n".utf8);try source.write(to:input)
        for path in [destination.path,destination.absoluteString] {
            let options=String(decoding:try JSONSerialization.data(withJSONObject:["outputDirectory":path,"revealInFinder":false]),as:UTF8.self)
            XCTAssertEqual(NashiCommandLine.run(arguments:["Nashi","convert","vtt",input.path,"--options",options,"--json"]),0)
        }
        let outputs=try FileManager.default.contentsOfDirectory(at:destination,includingPropertiesForKeys:nil)
        XCTAssertEqual(outputs.count,2)
        for output in outputs { let text=try String(contentsOf:output,encoding:.utf8);XCTAssertTrue(text.contains("00:00:00.125 --> 00:00:01.250"));XCTAssertTrue(text.contains("Merhaba dünya")) }
        XCTAssertEqual(try Data(contentsOf:input),source)
    }
    func testInspectFailsForMissingFilesOfEveryCategory() throws {
        let root=try OutputFiles.temporaryDirectory();defer{try? FileManager.default.removeItem(at:root)}
        let present=root.appendingPathComponent("present.txt");try Data("hello".utf8).write(to:present)
        XCTAssertEqual(NashiCommandLine.run(arguments:["nashi-cli","inspect",present.path]),0)
        for name in ["absent.txt","absent.docx","absent.zip","absent.bin","absent.png"] {
            XCTAssertNotEqual(NashiCommandLine.run(arguments:["nashi-cli","inspect",root.appendingPathComponent(name).path]),0,name)
        }
    }
}
