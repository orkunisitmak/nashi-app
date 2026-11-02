import XCTest
@testable import NashiCore

/// Interval splitting with stand-in ffprobe/ffmpeg executables, so the range
/// arithmetic is checked without depending on real encoder output.
final class MediaSplitIntervalTailTests: XCTestCase {
    func testIntervalSplitMergesARemainderShorterThanTheMinimumClip() throws {
        let root = try OutputFiles.temporaryDirectory(); defer { try? FileManager.default.removeItem(at: root) }
        let log = root.appendingPathComponent("ffmpeg.log")
        let probe = "#!/bin/sh\nprintf '%s' '{\"format\":{\"duration\":\"'\"$NASHI_TEST_DURATION\"'\"},\"streams\":[{\"index\":0,\"codec_type\":\"video\",\"codec_name\":\"h264\",\"width\":64,\"height\":64}]}'\n"
        let mpeg = "#!/bin/sh\nfor a in \"$@\"; do printf '%s\\n' \"$a\"; done >> '\(log.path)'\nfor last; do :; done\nprintf x > \"$last\"\n"
        for (name, body) in [("ffprobe", probe), ("ffmpeg", mpeg)] {
            let tool = root.appendingPathComponent(name)
            try Data(body.utf8).write(to: tool)
            try FileManager.default.setAttributes([.posixPermissions: 0o755], ofItemAtPath: tool.path)
            setenv("NASHI_" + name.uppercased(), tool.path, 1)
        }
        defer { unsetenv("NASHI_FFPROBE"); unsetenv("NASHI_FFMPEG"); unsetenv("NASHI_TEST_DURATION") }
        func ranges(duration: String) throws -> [String] {
            try? FileManager.default.removeItem(at: log)
            let folder = root.appendingPathComponent(UUID().uuidString)
            try FileManager.default.createDirectory(at: folder, withIntermediateDirectories: false)
            let input = folder.appendingPathComponent("Clip.mp4")
            try Data("source".utf8).write(to: input)
            setenv("NASHI_TEST_DURATION", duration, 1)
            // Default options split every 30 seconds, as `nashi-cli tool splitVideo FILE` does.
            let outputs = try MediaEngine.tool(.splitVideo, inputs: [input], options: OperationOptions(), token: .init())
            let arguments = try String(contentsOf: log, encoding: .utf8).components(separatedBy: "\n")
            let result = arguments.indices.filter { arguments[$0] == "-ss" }.map { "\(arguments[$0 + 1])+\(arguments[$0 + 3])" }
            XCTAssertEqual(outputs.count, result.count)
            return result
        }
        XCTAssertEqual(try ranges(duration: "60.02"), ["0+30", "30+30.02"])
        XCTAssertEqual(try ranges(duration: "60.05"), ["0+30", "30+30", "60+0.05"])
        XCTAssertEqual(try ranges(duration: "60.5"), ["0+30", "30+30", "60+0.5"])
    }
}
