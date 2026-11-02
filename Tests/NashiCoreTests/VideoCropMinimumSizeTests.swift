import XCTest
@testable import NashiCore

/// Runs Crop Video against a canned 128 x 96 video through the NASHI_FFMPEG/NASHI_FFPROBE seam.
final class VideoCropMinimumSizeTests: XCTestCase {
    private func crop(_ rect: EditRect) throws -> [String] {
        let root = FileManager.default.temporaryDirectory.appendingPathComponent("nashi-video-crop-\(UUID().uuidString)", isDirectory: true)
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
        var options = OperationOptions(); options.crop = rect
        options.outputDirectory = root.appendingPathComponent("out")
        _ = try MediaEngine.tool(.cropVideo, inputs: [input], options: options, token: .init())
        return try String(contentsOf: log, encoding: .utf8).split(separator: "\n").map(String.init)
    }

    func testMinimumSideIsAcceptedAndOnePixelIsRefused() throws {
        let side = MediaEngine.minimumVideoCropSide
        let argv = try crop(EditRect(x: 0, y: 0, width: side, height: side))
        XCTAssertTrue(try XCTUnwrap(argv.firstIndex(of: "-vf").map { argv[$0 + 1] }).hasPrefix("crop=2:2:0:0"), "\(argv)")
        XCTAssertThrowsError(try crop(EditRect(x: 0, y: 0, width: side - 1, height: 50)))
        XCTAssertThrowsError(try crop(EditRect(x: 0, y: 0, width: 50, height: side - 1)))
    }
}
