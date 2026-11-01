import XCTest
@testable import NashiCore

/// Records the size filters of Convert and Compress through the NASHI_FFMPEG/NASHI_FFPROBE seam
/// for a canned 720 x 480 DVD/DV video whose pixels are 32:27 wide (shown at 16:9).
final class VideoScalePixelAspectTests: XCTestCase {
    private func filters(_ configure: (inout OperationOptions) -> Void, compress: Bool = false) throws -> [String] {
        let root = FileManager.default.temporaryDirectory.appendingPathComponent("nashi-video-scale-sar-\(UUID().uuidString)", isDirectory: true)
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
        var options = OperationOptions(); options.outputDirectory = root.appendingPathComponent("out-\(UUID().uuidString)")
        configure(&options)
        if compress { options.targetBytes = 1_000_000; _ = try MediaEngine.tool(.compress, inputs: [input], options: options, token: .init()) }
        else { _ = try MediaEngine.convert(input, to: "mp4", options: options, token: .init()) }
        let argv = try String(contentsOf: log, encoding: .utf8).split(separator: "\n").map(String.init)
        return argv.indices.filter { argv[$0] == "-vf" }.map { argv[$0 + 1] }
    }

    // setsar=1 after a size computed from the stored 3:2 frame would squeeze the 16:9 picture.
    func testSetSizesKeepTheDisplayedShapeOfAnamorphicVideo() throws {
        XCTAssertEqual(try filters { $0.videoMaxHeight = 720 }, ["pad=ceil(iw/2)*2:ceil(ih/2)*2,scale=-2:'trunc(min(720,ih)/2)*2':reset_sar=1,setsar=1"])
        XCTAssertEqual(try filters { $0.width = 1280 }, ["scale=1280:-2:reset_sar=1,setsar=1"])
        XCTAssertEqual(try filters { $0.width = 1280; $0.height = 720 }, ["scale=1280:720:force_original_aspect_ratio=decrease:reset_sar=1,pad=1280:720:(ow-iw)/2:(oh-ih)/2,setsar=1"])
        XCTAssertEqual(try filters({ $0.height = 480 }, compress: true), Array(repeating: "scale=-2:480:reset_sar=1,setsar=1", count: 2))
        // Original size leaves the pixel aspect to the container.
        XCTAssertEqual(try filters { _ in }, ["pad=ceil(iw/2)*2:ceil(ih/2)*2"])
    }
}
