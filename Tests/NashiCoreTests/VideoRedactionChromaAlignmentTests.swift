import XCTest
@testable import NashiCore

/// Records the redaction filter graph through the NASHI_FFMPEG/NASHI_FFPROBE seam
/// for a canned 128 x 96 yuv420p video.
final class VideoRedactionChromaAlignmentTests: XCTestCase {
    private func redact(_ rects: [EditRect], mode: String) throws -> String? {
        let root = FileManager.default.temporaryDirectory.appendingPathComponent("nashi-video-redaction-chroma-\(UUID().uuidString)", isDirectory: true)
        try FileManager.default.createDirectory(at: root, withIntermediateDirectories: true)
        addTeardownBlock { try? FileManager.default.removeItem(at: root) }
        let log = root.appendingPathComponent("argv.log"), probe = root.appendingPathComponent("probe.json")
        try Data(#"{"format":{"duration":"3"},"streams":[{"index":0,"codec_type":"video","codec_name":"h264","pix_fmt":"yuv420p","width":128,"height":96,"duration":"3","avg_frame_rate":"25/1"}]}"#.utf8).write(to: probe)
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

    // overlay rounds its x/y down to even values for 4:2:0 video, so a patch placed at an
    // odd origin would land one pixel early and leave the selection's last column/row visible.
    func testBlurAndPixelatePatchesStartOnEvenPixelsAndCoverOddSelections() throws {
        for mode in ["blur", "pixelate"] {
            let odd = try XCTUnwrap(redact([EditRect(x: 17.4, y: 13.2, width: 29.4, height: 22.6)], mode: mode))
            XCTAssertTrue(odd.contains("crop=31:24:16:12:exact=1"), odd); XCTAssertTrue(odd.contains("overlay=16:12:"), odd)
            let corner = try XCTUnwrap(redact([EditRect(x: 127.2, y: 95.2, width: 0.5, height: 0.5)], mode: mode))
            XCTAssertTrue(corner.contains("crop=2:2:126:94:exact=1"), corner); XCTAssertTrue(corner.contains("overlay=126:94:"), corner)
        }
        let solid = try XCTUnwrap(redact([EditRect(x: 17, y: 13, width: 30, height: 23)], mode: "solid"))
        XCTAssertTrue(solid.contains("drawbox=x=17:y=13:w=30:h=23:"), solid)
    }
}
