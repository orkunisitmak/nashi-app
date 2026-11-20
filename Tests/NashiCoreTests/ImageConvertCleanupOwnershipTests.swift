import XCTest
import CoreGraphics
import ImageIO
@testable import NashiCore

/// A failed image conversion may clean up only files it created, and the FFmpeg
/// fallback never writes at the output name itself. The fake encoder plays the
/// other writer: it claims the chosen name while the job is encoding, so the
/// order is fixed without threads, FIFOs or waits.
final class ImageConvertCleanupOwnershipTests:XCTestCase {
    private var root:URL!,work:URL!,savedFFmpeg:String?
    private var input:URL { work.appendingPathComponent("Photo.png") }
    private var foreign:URL { work.appendingPathComponent("Photo.webp") }
    private var argument:URL { root.appendingPathComponent("argument") }
    private var options:OperationOptions { var o=OperationOptions();o.outputDirectory=work;o.preserveMetadata=false;return o }

    override func setUpWithError() throws {
        // Where ImageIO encodes WebP itself the fallback under test never runs.
        try XCTSkipIf((CGImageDestinationCopyTypeIdentifiers() as! [String]).contains("org.webmproject.webp"),"ImageIO encodes WebP here; the FFmpeg fallback is not used.")
        savedFFmpeg=ProcessInfo.processInfo.environment["NASHI_FFMPEG"]
        root=try OutputFiles.temporaryDirectory()
        work=root.appendingPathComponent("work",isDirectory:true)
        try FileManager.default.createDirectory(at:work,withIntermediateDirectories:true)
        try ImageEngine.write(image(),to:input)
    }

    override func tearDownWithError() throws {
        if let savedFFmpeg { setenv("NASHI_FFMPEG",savedFFmpeg,1) } else { unsetenv("NASHI_FFMPEG") }
        if let root { try? FileManager.default.removeItem(at:root) }
    }

    private func image() throws -> CGImage {
        let context=try XCTUnwrap(CGContext(data:nil,width:4,height:4,bitsPerComponent:8,bytesPerRow:16,space:CGColorSpaceCreateDeviceRGB(),bitmapInfo:CGImageAlphaInfo.premultipliedLast.rawValue))
        context.setFillColor(CGColor(red:0,green:0,blue:1,alpha:1));context.fill(CGRect(x:0,y:0,width:4,height:4))
        return try XCTUnwrap(context.makeImage())
    }

    /// Records its output argument, optionally saves another writer's file at
    /// the name the job chose, writes its output and exits with `status`.
    private func fakeEncoder(claimsChosenName:Bool,status:Int) throws {
        let script=root.appendingPathComponent("ffmpeg")
        try Data("""
        #!/bin/sh
        for last; do :; done
        printf '%s' "$last" > '\(argument.path)'
        \(claimsChosenName ? "printf '%s' \"another writer's file\" > '\(foreign.path)'" : ":")
        printf 'RIFF0000WEBP' > "$last"
        exit \(status)

        """.utf8).write(to:script)
        try FileManager.default.setAttributes([.posixPermissions:0o755],ofItemAtPath:script.path)
        setenv("NASHI_FFMPEG",script.path,1)
    }

    func testFailedConvertKeepsFileAnotherWriterCreatedAtChosenName() throws {
        try fakeEncoder(claimsChosenName:true,status:1)
        XCTAssertThrowsError(try ImageEngine.convert(input,to:"webp",options:options,token:.init()))
        XCTAssertEqual(try String(contentsOf:foreign,encoding:.utf8),"another writer's file")
        XCTAssertEqual(try FileManager.default.contentsOfDirectory(atPath:work.path).sorted(),["Photo.png","Photo.webp"])
    }

    func testFailedConvertRemovesItsOwnPartialOutput() throws {
        try fakeEncoder(claimsChosenName:false,status:1)
        XCTAssertThrowsError(try ImageEngine.convert(input,to:"webp",options:options,token:.init()))
        XCTAssertEqual(try FileManager.default.contentsOfDirectory(atPath:work.path),["Photo.png"])
    }

    func testConvertNeverReplacesFileAnotherWriterCreatedAtChosenName() throws {
        try fakeEncoder(claimsChosenName:true,status:0)
        let output=try XCTUnwrap(ImageEngine.convert(input,to:"webp",options:options,token:.init()).first)
        XCTAssertEqual(output.lastPathComponent,"Photo 2.webp")
        XCTAssertEqual(try Data(contentsOf:output),Data("RIFF0000WEBP".utf8))
        XCTAssertEqual(try String(contentsOf:foreign,encoding:.utf8),"another writer's file")
        XCTAssertEqual(try FileManager.default.contentsOfDirectory(atPath:work.path).sorted(),["Photo 2.webp","Photo.png","Photo.webp"])
    }

    func testWebPEncodeNeverWritesThroughLinkAtOutputName() throws {
        try fakeEncoder(claimsChosenName:false,status:0)
        let victim=root.appendingPathComponent("outside",isDirectory:true).appendingPathComponent("victim.webp")
        try FileManager.default.createDirectory(at:victim.deletingLastPathComponent(),withIntermediateDirectories:true)
        let link=work.appendingPathComponent("Linked.webp")
        try FileManager.default.createSymbolicLink(atPath:link.path,withDestinationPath:victim.path)
        XCTAssertThrowsError(try ImageEngine.write(image(),to:link))
        XCTAssertFalse(FileManager.default.fileExists(atPath:victim.path))
        XCTAssertEqual(try FileManager.default.destinationOfSymbolicLink(atPath:link.path),victim.path)
        // The encoder is only ever handed a private path.
        XCTAssertFalse(try String(contentsOf:argument,encoding:.utf8).hasPrefix(work.path+"/"))

        let plain=work.appendingPathComponent("Plain.webp")
        try ImageEngine.write(image(),to:plain)
        XCTAssertEqual(try Data(contentsOf:plain),Data("RIFF0000WEBP".utf8))
        XCTAssertFalse(try String(contentsOf:argument,encoding:.utf8).hasPrefix(work.path+"/"))
    }
}
