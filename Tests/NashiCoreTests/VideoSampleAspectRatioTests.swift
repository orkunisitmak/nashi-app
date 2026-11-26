import XCTest
@testable import NashiCore

/// Reads canned ffprobe output through the NASHI_FFPROBE seam.
final class VideoSampleAspectRatioTests: XCTestCase {
    private func inspect(_ stream: String) throws -> MediaStream? {
        let root = FileManager.default.temporaryDirectory.appendingPathComponent("nashi-video-sar-\(UUID().uuidString)", isDirectory: true)
        try FileManager.default.createDirectory(at: root, withIntermediateDirectories: true)
        addTeardownBlock { try? FileManager.default.removeItem(at: root) }
        let probe = root.appendingPathComponent("probe.json"), ffprobe = root.appendingPathComponent("ffprobe")
        try Data(#"{"format":{"duration":"3"},"streams":[{"index":0,"codec_type":"video","codec_name":"h264","width":128,"height":96\#(stream),"duration":"3","avg_frame_rate":"25/1"}]}"#.utf8).write(to: probe)
        try Data("#!/bin/sh\ncat '\(probe.path)'\n".utf8).write(to: ffprobe)
        try FileManager.default.setAttributes([.posixPermissions: 0o755], ofItemAtPath: ffprobe.path)
        let previous = ProcessInfo.processInfo.environment["NASHI_FFPROBE"]
        setenv("NASHI_FFPROBE", ffprobe.path, 1)
        defer { if let previous { setenv("NASHI_FFPROBE", previous, 1) } else { unsetenv("NASHI_FFPROBE") } }
        let input = root.appendingPathComponent("Clip.mov"); try Data([1]).write(to: input)
        return try MediaEngine.inspect(input).video
    }

    // Players stretch non-square pixels, so the editor's crop and redaction canvas needs the
    // ratio to line up with the picture; otherwise a box drawn over a face lands elsewhere.
    func testInspectReportsSampleAspectRatioInTheDisplayedOrientation() throws {
        let wide = try XCTUnwrap(inspect(#","sample_aspect_ratio":"2:1""#))
        XCTAssertEqual(wide.width, 128); XCTAssertEqual(wide.height, 96); XCTAssertEqual(wide.sampleAspectRatio, 2)
        XCTAssertEqual(try XCTUnwrap(inspect(#","sample_aspect_ratio":"32:27""#)).sampleAspectRatio, 32.0 / 27.0, accuracy: 1e-12)
        let rotated = try XCTUnwrap(inspect(#","sample_aspect_ratio":"2:1","side_data_list":[{"rotation":-90}]"#))
        XCTAssertEqual(rotated.width, 96); XCTAssertEqual(rotated.height, 128); XCTAssertEqual(rotated.sampleAspectRatio, 0.5)
        for square in [#","sample_aspect_ratio":"1:1""#, #","sample_aspect_ratio":"0:1""#, #","sample_aspect_ratio":"N/A""#, ""] {
            XCTAssertEqual(try XCTUnwrap(inspect(square)).sampleAspectRatio, 1, square)
        }
    }
}
