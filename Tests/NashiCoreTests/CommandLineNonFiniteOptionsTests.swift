import XCTest
@testable import NashiCore

/// JSONSerialization reads a too-large negative number as -infinity. Re-encoding that
/// value for the defaults overlay raised an Objective-C exception and aborted the CLI.
final class CommandLineNonFiniteOptionsTests:XCTestCase {
    func testNegativeOverflowingOptionNumberIsRejectedInsteadOfAborting() throws {
        let root=try OutputFiles.temporaryDirectory();defer{try? FileManager.default.removeItem(at:root)}
        let input=root.appendingPathComponent("clip.srt"),destination=root.appendingPathComponent("Output")
        try "1\n00:00:01,000 --> 00:00:02,000\nHello\n".write(to:input,atomically:true,encoding:.utf8)
        try FileManager.default.createDirectory(at:destination,withIntermediateDirectories:true)
        for fragment in ["\"quality\":-1e400","\"imageFrame\":{\"gradientAngle\":-1e999}","\"snapshotTimes\":[0,-1e309]"] {
            XCTAssertEqual(NashiCommandLine.run(arguments:["nashi-cli","convert","vtt",input.path,"--options","{\(fragment),\"outputDirectory\":\"\(destination.path)\"}"]),1,fragment)
            XCTAssertEqual(try FileManager.default.contentsOfDirectory(atPath:destination.path),[],fragment)
        }
        XCTAssertEqual(NashiCommandLine.run(arguments:["nashi-cli","convert","vtt",input.path,"--options","{\"quality\":-1e300,\"outputDirectory\":\"\(destination.path)\"}"]),0)
        XCTAssertEqual(try FileManager.default.contentsOfDirectory(atPath:destination.path),["clip.vtt"])
    }
}
