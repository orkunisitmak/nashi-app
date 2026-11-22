import XCTest
@testable import NashiCore

/// Records the Save Frames ffmpeg arguments through the NASHI_FFMPEG/NASHI_FFPROBE seam
/// for a canned 720 x 480 DVD/DV video whose pixels are 32:27 wide (shown at 16:9).
final class VideoSnapshotPixelAspectTests: XCTestCase {
    // Image viewers ignore a PNG pixel aspect, so the frame must be saved in the shape the player shows.
    func testSavedFramesAreResizedToSquarePixels() throws {
        let root = FileManager.default.temporaryDirectory.appendingPathComponent("nashi-video-snapshot-sar-\(UUID().uuidString)", isDirectory: true)
        try FileManager.default.createDirectory(at: root, withIntermediateDirectories: true)
        addTeardownBlock { try? FileManager.default.removeItem(at: root) }
        let log = root.appendingPathComponent("argv.log"), probe = root.appendingPathComponent("probe.json")
        try Data(#"{"format":{"duration":"4"},"streams":[{"index":0,"codec_type":"video","codec_name":"h264","width":720,"height":480,"sample_aspect_ratio":"32:27","duration":"4","avg_frame_rate":"25/1"}]}"#.utf8).write(to: probe)
        let ffprobe = root.appendingPathComponent("ffprobe"), ffmpeg = root.appendingPathComponent("ffmpeg")
        try Data("#!/bin/sh\ncat '\(probe.path)'\n".utf8).write(to: ffprobe)
        try Data("#!/bin/sh\nfor a in \"$@\"; do printf '%s\\n' \"$a\" >> '\(log.path)'; done\nfor last; do :; done\nprintf x > \"$last\"\n".utf8).write(to: ffmpeg)
        for (variable, tool) in [("NASHI_FFPROBE", ffprobe), ("NASHI_FFMPEG", ffmpeg)] {
            try FileManager.default.setAttributes([.posixPermissions: 0o755], ofItemAtPath: tool.path)
            let previous = ProcessInfo.processInfo.environment[variable]
            setenv(variable, tool.path, 1)
            addTeardownBlock { if let previous { setenv(variable, previous, 1) } else { unsetenv(variable) } }
        }
        let input = root.appendingPathComponent("DV.mov"); try Data([1]).write(to: input)
        var options = OperationOptions(); options.outputDirectory = root.appendingPathComponent("out"); options.snapshotTimes = [1, 2.5]
        XCTAssertEqual(try MediaEngine.tool(.videoSnapshots, inputs: [input], options: options, token: .init()).count, 2)
        let argv = try String(contentsOf: log, encoding: .utf8).split(separator: "\n").map(String.init)
        let filters = argv.indices.filter { argv[$0] == "-vf" }.map { argv[$0 + 1] }
        XCTAssertEqual(filters, Array(repeating: "scale=-1:ih:reset_sar=1", count: 2))
    }
}
