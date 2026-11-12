import XCTest
import CoreGraphics
@testable import NashiCore

/// A failed image conversion may clean up only files it created. A FIFO gate
/// holds the encoder while another writer claims the chosen output name.
final class ImageConvertCleanupOwnershipTests:XCTestCase {
    private var root:URL!,work:URL!,gate:URL!
    private var input:URL { work.appendingPathComponent("Photo.png") }
    private var options:OperationOptions { var o=OperationOptions();o.outputDirectory=work;o.preserveMetadata=false;return o }

    override func setUpWithError() throws {
        root=try OutputFiles.temporaryDirectory()
        let tools=root.appendingPathComponent("tools",isDirectory:true)
        work=root.appendingPathComponent("work",isDirectory:true);gate=root.appendingPathComponent("gate",isDirectory:true)
        for directory in [tools,work,gate] { try FileManager.default.createDirectory(at:directory!,withIntermediateDirectories:true) }
        // Behaves like non-interactive ffmpeg without -y: refuses an existing
        // destination; otherwise writes a partial file and fails.
        let script=tools.appendingPathComponent("ffmpeg")
        try Data("""
        #!/bin/sh
        for last; do :; done
        if [ -n "$NASHI_TEST_GATE" ]; then echo started > "$NASHI_TEST_GATE/started"; read _ < "$NASHI_TEST_GATE/release"; fi
        if [ -e "$last" ] || [ -L "$last" ]; then echo "File '$last' already exists. Exiting." >&2; exit 1; fi
        printf 'partial encoder output' > "$last"; echo "encoder failed" >&2; exit 1

        """.utf8).write(to:script)
        try FileManager.default.setAttributes([.posixPermissions:0o755],ofItemAtPath:script.path)
        setenv("NASHI_FFMPEG",script.path,1);unsetenv("NASHI_TEST_GATE")
        let context=try XCTUnwrap(CGContext(data:nil,width:4,height:4,bitsPerComponent:8,bytesPerRow:16,space:CGColorSpaceCreateDeviceRGB(),bitmapInfo:CGImageAlphaInfo.premultipliedLast.rawValue))
        context.setFillColor(CGColor(red:0,green:0,blue:1,alpha:1));context.fill(CGRect(x:0,y:0,width:4,height:4))
        try ImageEngine.write(XCTUnwrap(context.makeImage()),to:input)
    }

    override func tearDownWithError() throws {
        unsetenv("NASHI_FFMPEG");unsetenv("NASHI_TEST_GATE")
        try? FileManager.default.removeItem(at:root)
    }

    func testFailedConvertKeepsFileAnotherWriterCreatedAtChosenName() throws {
        let started=gate.appendingPathComponent("started"),release=gate.appendingPathComponent("release")
        XCTAssertEqual(mkfifo(started.path,0o600),0);XCTAssertEqual(mkfifo(release.path,0o600),0)
        setenv("NASHI_TEST_GATE",gate.path,1)
        let foreign=OutputFiles.unique(for:input,extension:"webp",directory:work)
        let done=DispatchSemaphore(value:0),input=input,options=options
        nonisolated(unsafe) var jobError:Error?
        Thread.detachNewThread {
            do { _=try ImageEngine.convert(input,to:"webp",options:options,token:.init()) } catch { jobError=error }
            done.signal()
        }
        let reader=try XCTUnwrap(FileHandle(forReadingAtPath:started.path));_=reader.readDataToEndOfFile();try reader.close()
        try Data("another writer's file".utf8).write(to:foreign)
        let writer=try XCTUnwrap(FileHandle(forWritingAtPath:release.path));writer.write(Data("go\n".utf8));try writer.close()
        XCTAssertEqual(done.wait(timeout:.now()+60),.success)
        XCTAssertNotNil(jobError)
        XCTAssertEqual(try String(contentsOf:foreign,encoding:.utf8),"another writer's file")
        XCTAssertEqual(try FileManager.default.contentsOfDirectory(atPath:work.path).sorted(),["Photo.png","Photo.webp"])
    }

    func testFailedConvertRemovesItsOwnPartialOutput() throws {
        XCTAssertThrowsError(try ImageEngine.convert(input,to:"webp",options:options,token:.init()))
        XCTAssertEqual(try FileManager.default.contentsOfDirectory(atPath:work.path),["Photo.png"])
    }
}
