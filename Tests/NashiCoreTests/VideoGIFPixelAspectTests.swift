import XCTest
@testable import NashiCore

/// Records the Video to GIF filter graph through the NASHI_FFMPEG/NASHI_FFPROBE seam
/// for a canned 720 x 480 DVD/DV video whose pixels are 32:27 wide (shown at 16:9).
final class VideoGIFPixelAspectTests: XCTestCase {
    private func graph(gifMaxWidth: Int) throws -> String? {
        let root = FileManager.default.temporaryDirectory.appendingPathComponent("nashi-video-gif-sar-\(UUID().uuidString)", isDirectory: true)
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
        var options = OperationOptions(); options.outputDirectory = root.appendingPathComponent("out"); options.gifMaxWidth = gifMaxWidth
        _ = try MediaEngine.convert(input, to: "gif", options: options, token: .init())
        let argv = try String(contentsOf: log, encoding: .utf8).split(separator: "\n").map(String.init)
        return argv.firstIndex(of: "-filter_complex").map { argv[$0 + 1] }
    }

    // GIF viewers ignore the pixel aspect, so the frames must be square before the palette is built.
    func testGIFFramesAreResizedToSquarePixels() throws {
        for width in [480, 960] {
            XCTAssertEqual(try graph(gifMaxWidth: width), "[0:v:0]fps=15,pad=ceil(iw/2)*2:ceil(ih/2)*2,scale=w='min(\(width),iw)':h=-2:force_original_aspect_ratio=decrease:reset_sar=1,split[p0][p1];[p0]palettegen=stats_mode=diff[pal];[p1][pal]paletteuse=dither=sierra2_4a")
        }
    }
}
