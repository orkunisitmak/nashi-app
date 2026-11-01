import XCTest
@testable import NashiCore

/// Records the redaction filter graph through the NASHI_FFMPEG/NASHI_FFPROBE seam
/// for a canned 128 x 96 ProRes 4444 video with an alpha plane.
final class VideoRedactionAlphaTests: XCTestCase {
    private func redact(_ rects: [EditRect], mode: String) throws -> String? {
        let root = FileManager.default.temporaryDirectory.appendingPathComponent("nashi-video-redaction-alpha-\(UUID().uuidString)", isDirectory: true)
        try FileManager.default.createDirectory(at: root, withIntermediateDirectories: true)
        addTeardownBlock { try? FileManager.default.removeItem(at: root) }
        let log = root.appendingPathComponent("argv.log"), probe = root.appendingPathComponent("probe.json")
        try Data(#"{"format":{"duration":"3"},"streams":[{"index":0,"codec_type":"video","codec_name":"prores","profile":"4444","pix_fmt":"yuva444p10le","width":128,"height":96,"duration":"3","avg_frame_rate":"25/1"}]}"#.utf8).write(to: probe)
        let ffprobe = root.appendingPathComponent("ffprobe"), ffmpeg = root.appendingPathComponent("ffmpeg")
        try Data("#!/bin/sh\ncat '\(probe.path)'\n".utf8).write(to: ffprobe)
        try Data("#!/bin/sh\nfor a in \"$@\"; do printf '%s\\n' \"$a\" >> '\(log.path)'; done\nfor last; do :; done\nprintf x > \"$last\"\n".utf8).write(to: ffmpeg)
        for (variable, tool) in [("NASHI_FFPROBE", ffprobe), ("NASHI_FFMPEG", ffmpeg)] {
            try FileManager.default.setAttributes([.posixPermissions: 0o755], ofItemAtPath: tool.path)
            let previous = ProcessInfo.processInfo.environment[variable]
            setenv(variable, tool.path, 1)
            addTeardownBlock { if let previous { setenv(variable, previous, 1) } else { unsetenv(variable) } }
        }
        let input = root.appendingPathComponent("Lower third.mov"); try Data([1]).write(to: input)
        var options = OperationOptions(); options.redactionMode = mode; options.backgroundColor = "#000000"
        options.outputDirectory = root.appendingPathComponent("out")
        options.videoRedactions = rects.map { VideoRedaction(rect: $0, start: 0, end: 3) }
        _ = try MediaEngine.tool(.redactVideo, inputs: [input], options: options, token: .init())
        let argv = try String(contentsOf: log, encoding: .utf8).split(separator: "\n").map(String.init)
        return argv.firstIndex(of: "-filter_complex").map { argv[$0 + 1] }
    }

    // overlay alpha-blends its second input, so a blurred or pixelated patch that keeps
    // the source's alpha lets the original pixels show through wherever it is not opaque.
    func testBlurAndPixelatePatchesDropAlphaBeforeOverlay() throws {
        let rects = [EditRect(x: 16, y: 12, width: 64, height: 32), EditRect(x: 100.5, y: 70.5, width: 27.5, height: 25.5)]
        for mode in ["blur", "pixelate"] {
            let graph = try XCTUnwrap(redact(rects, mode: mode))
            for index in 0..<rects.count {
                XCTAssertTrue(graph.contains(",format=yuv420p[effect\(index)];[base\(index)][effect\(index)]overlay="), graph)
            }
        }
        XCTAssertFalse(try XCTUnwrap(redact(rects, mode: "black")).contains("format="))
    }
}
