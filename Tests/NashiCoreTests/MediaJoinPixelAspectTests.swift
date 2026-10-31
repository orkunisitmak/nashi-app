import XCTest
@testable import NashiCore

/// Records the per-clip ffmpeg filters of Join Videos through the NASHI_FFMPEG/NASHI_FFPROBE seam.
/// DV, HDV and DVD clips store non-square pixels; the join canvas uses square pixels.
final class MediaJoinPixelAspectTests: XCTestCase {
    private func clipFilters(_ probes: [(String, String)]) throws -> [String] {
        let root = FileManager.default.temporaryDirectory.appendingPathComponent("nashi-join-sar-\(UUID().uuidString)", isDirectory: true)
        try FileManager.default.createDirectory(at: root, withIntermediateDirectories: true)
        addTeardownBlock { try? FileManager.default.removeItem(at: root) }
        let log = root.appendingPathComponent("argv.log")
        let ffprobe = root.appendingPathComponent("ffprobe"), ffmpeg = root.appendingPathComponent("ffmpeg")
        try Data("#!/bin/sh\nfor last; do :; done\ncat \"$last.json\"\n".utf8).write(to: ffprobe)
        try Data("#!/bin/sh\nfor a in \"$@\"; do printf '%s\\n' \"$a\" >> '\(log.path)'; done\nfor last; do :; done\nprintf x > \"$last\"\n".utf8).write(to: ffmpeg)
        for (variable, executable) in [("NASHI_FFPROBE", ffprobe), ("NASHI_FFMPEG", ffmpeg)] {
            try FileManager.default.setAttributes([.posixPermissions: 0o755], ofItemAtPath: executable.path)
            let previous = ProcessInfo.processInfo.environment[variable]
            setenv(variable, executable.path, 1)
            addTeardownBlock { if let previous { setenv(variable, previous, 1) } else { unsetenv(variable) } }
        }
        let inputs = try probes.map { name, probe in
            let input = root.appendingPathComponent(name); try Data([1]).write(to: input)
            try Data(probe.utf8).write(to: URL(fileURLWithPath: input.path + ".json"))
            return input
        }
        var options = OperationOptions(); options.outputDirectory = root.appendingPathComponent("out")
        _ = try MediaEngine.tool(.joinVideos, inputs: inputs, options: options, token: .init())
        let lines = try String(contentsOf: log, encoding: .utf8).split(separator: "\n").map(String.init)
        return lines.indices.filter { lines[$0] == "-vf" }.map { lines[$0 + 1] }
    }

    private func clip(_ width: Int, _ height: Int, sar: String) -> String {
        "{\"format\":{\"duration\":\"2\"},\"streams\":[{\"index\":0,\"codec_type\":\"video\",\"codec_name\":\"h264\",\"width\":\(width),\"height\":\(height),\"sample_aspect_ratio\":\"\(sar)\",\"avg_frame_rate\":\"25/1\"}]}"
    }

    func testAnamorphicClipsAreSquaredBeforeFittingTheCanvas() throws {
        let filters = try clipFilters([("DV.mov", clip(720, 480, sar: "32:27")), ("Phone.mp4", clip(1920, 1080, sar: "1:1"))])
        // Fitting keeps the stored 3:2 ratio and setsar=1 drops 32:27, which would squeeze 16:9 to 3:2.
        XCTAssertEqual(filters, Array(repeating: "scale=trunc(iw*sar/2)*2:ih,setsar=1,scale=720:480:force_original_aspect_ratio=decrease,pad=720:480:(ow-iw)/2:(oh-ih)/2,setsar=1,fps=30,setpts=PTS-STARTPTS", count: 2))
    }
}
