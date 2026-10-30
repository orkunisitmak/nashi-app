import XCTest
@testable import NashiCore

/// Records the redaction filter graph through the NASHI_FFMPEG/NASHI_FFPROBE seam
/// for a canned 128 x 96 video.
final class VideoRedactionCoverageTests: XCTestCase {
    private func redact(_ rects: [EditRect], mode: String) throws -> String? {
        let root = FileManager.default.temporaryDirectory.appendingPathComponent("nashi-video-redaction-\(UUID().uuidString)", isDirectory: true)
        try FileManager.default.createDirectory(at: root, withIntermediateDirectories: true)
        addTeardownBlock { try? FileManager.default.removeItem(at: root) }
        let log = root.appendingPathComponent("argv.log"), probe = root.appendingPathComponent("probe.json")
        try Data(#"{"format":{"duration":"3"},"streams":[{"index":0,"codec_type":"video","codec_name":"h264","width":128,"height":96,"duration":"3","avg_frame_rate":"25/1"}]}"#.utf8).write(to: probe)
        let ffprobe = root.appendingPathComponent("ffprobe"), ffmpeg = root.appendingPathComponent("ffmpeg")
        try Data("#!/bin/sh\ncat '\(probe.path)'\n".utf8).write(to: ffprobe)
        try Data("#!/bin/sh\nfor a in \"$@\"; do printf '%s\\n' \"$a\" >> '\(log.path)'; done\nfor last; do :; done\nprintf x > \"$last\"\n".utf8).write(to: ffmpeg)
        for (variable, tool) in [("NASHI_FFPROBE", ffprobe), ("NASHI_FFMPEG", ffmpeg)] {
            try FileManager.default.setAttributes([.posixPermissions: 0o755], ofItemAtPath: tool.path)
            let previous = ProcessInfo.processInfo.environment[variable]
            setenv(variable, tool.path, 1)
            addTeardownBlock { if let previous { setenv(variable, previous, 1) } else { unsetenv(variable) } }
        }
        let input = root.appendingPathComponent("Clip.mp4"); try Data([1]).write(to: input)
        var options = OperationOptions(); options.redactionMode = mode; options.backgroundColor = "#000000"
        options.outputDirectory = root.appendingPathComponent("out")
        options.videoRedactions = rects.map { VideoRedaction(rect: $0, start: 0, end: 3) }
        _ = try MediaEngine.tool(.redactVideo, inputs: [input], options: options, token: .init())
        let argv = try String(contentsOf: log, encoding: .utf8).split(separator: "\n").map(String.init)
        return argv.firstIndex(of: "-filter_complex").map { argv[$0 + 1] }
    }

    func testBlurAndPixelateCoverEveryPixelTouchedBySelection() throws {
        for mode in ["blur", "pixelate"] {
            let inner = try XCTUnwrap(redact([EditRect(x: 16.75, y: 12.75, width: 30.5, height: 22.5)], mode: mode))
            XCTAssertTrue(inner.contains("crop=32:24:16:12"), inner); XCTAssertTrue(inner.contains("overlay=16:12"), inner)
            let edge = try XCTUnwrap(redact([EditRect(x: 100.5, y: 70.5, width: 27.5, height: 25.5)], mode: mode))
            XCTAssertTrue(edge.contains("crop=28:26:100:70"), edge); XCTAssertTrue(edge.contains("overlay=100:70"), edge)
        }
    }
}
