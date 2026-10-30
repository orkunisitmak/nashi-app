import XCTest
@testable import NashiCore

/// Recordings of a selected screen area are often odd-sized, while H.264 4:2:0 output needs even dimensions.
final class MediaOddDimensionTests: XCTestCase {
    func testOddSizedVideoCanBeTrimmedSplitSpedUpAndRedacted() throws {
        let directory = try OutputFiles.temporaryDirectory()
        defer { try? FileManager.default.removeItem(at: directory) }
        let input = directory.appendingPathComponent("Odd area.mov")
        try ProcessRunner.run(try Dependencies.executable("ffmpeg"), ["-hide_banner", "-v", "error", "-nostdin", "-n", "-f", "lavfi", "-i", "testsrc=size=97x65:rate=12:duration=2", "-f", "lavfi", "-i", "sine=duration=2:sample_rate=48000", "-c:v", "libx264", "-pix_fmt", "yuv444p", "-c:a", "aac", "-shortest", input.path])
        XCTAssertEqual(try MediaEngine.inspect(input).video?.width, 97)
        var trim = OperationOptions(); trim.start = 0.25; trim.end = 1.25
        var speed = OperationOptions(); speed.speed = 2
        var redact = OperationOptions(); redact.redactions = [EditRect(x: 0, y: 0, width: 32, height: 32)]
        var split = OperationOptions(); split.equalClips = 2
        for (kind, options) in [(ToolKind.trimVideo, trim), (.changeVideoSpeed, speed), (.redactVideo, redact), (.splitVideo, split)] {
            var options = options; options.outputDirectory = directory.appendingPathComponent(kind.rawValue)
            XCTAssertNoThrow(try {
                let outputs = try MediaEngine.tool(kind, inputs: [input], options: options, token: .init())
                for output in outputs {
                    let video = try MediaEngine.inspect(output).video
                    XCTAssertEqual(video?.width, 98, "\(kind)"); XCTAssertEqual(video?.height, 66, "\(kind)")
                }
            }(), "\(kind)")
        }
    }

    /// VP9 and MPEG-4 accept odd sizes, so WebM and AVI edits keep the original frame.
    func testOddSizedWebMAndAVIKeepTheirSize() throws {
        let directory = try OutputFiles.temporaryDirectory()
        defer { try? FileManager.default.removeItem(at: directory) }
        for (ext, codec) in [("webm", "libvpx-vp9"), ("avi", "mpeg4")] {
            let input = directory.appendingPathComponent("Odd area.\(ext)")
            try ProcessRunner.run(try Dependencies.executable("ffmpeg"), ["-hide_banner", "-v", "error", "-nostdin", "-n", "-f", "lavfi", "-i", "testsrc=size=97x65:rate=12:duration=2", "-c:v", codec, input.path])
            var options = OperationOptions(); options.start = 0.25; options.end = 1.25; options.outputDirectory = directory.appendingPathComponent(ext)
            let video = try MediaEngine.inspect(try XCTUnwrap(MediaEngine.tool(.trimVideo, inputs: [input], options: options, token: .init()).first)).video
            XCTAssertEqual(video?.width, 97, ext); XCTAssertEqual(video?.height, 65, ext)
        }
    }
}
