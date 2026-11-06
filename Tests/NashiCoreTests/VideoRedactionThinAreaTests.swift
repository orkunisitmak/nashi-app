import XCTest
@testable import NashiCore

/// Records the redaction filter graph through the NASHI_FFMPEG/NASHI_FFPROBE seam
/// for a canned 128 x 96 video.
final class VideoRedactionThinAreaTests: XCTestCase {
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

    func testThinAreaAcceptedByTheCanvasDoesNotFailTheExport() throws {
        let face = EditRect(x: 40, y: 20, width: 40, height: 40)
        // A near-horizontal drag and the 1 px minimum of the resize handles.
        let solid = try XCTUnwrap(redact([face, EditRect(x: 10, y: 70, width: 30, height: 1.16), EditRect(x: 100, y: 10, width: 1, height: 20)], mode: "solid"))
        XCTAssertTrue(solid.contains("drawbox=x=40:y=20:w=40:h=40"), solid)
        XCTAssertTrue(solid.contains("drawbox=x=10:y=70:w=30:h=2"), solid)
        XCTAssertTrue(solid.contains("drawbox=x=100:y=10:w=1:h=20"), solid)
        let blur = try XCTUnwrap(redact([face, EditRect(x: 10, y: 70, width: 30, height: 1.16)], mode: "blur"))
        XCTAssertTrue(blur.contains("crop=30:2:10:70"), blur)
        for empty in [EditRect(x: 10, y: 70, width: 30, height: 0), EditRect(x: 128, y: 10, width: 0.005, height: 10)] {
            XCTAssertThrowsError(try redact([face, empty], mode: "solid"), "\(empty)")
        }
    }

}
