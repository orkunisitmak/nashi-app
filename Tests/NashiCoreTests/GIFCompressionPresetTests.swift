import XCTest
@testable import NashiCore

/// Records the ffmpeg arguments of a GIF export through the NASHI_FFMPEG/NASHI_FFPROBE seam.
final class GIFCompressionPresetTests: XCTestCase {
    private func gifFilter(preset: String) throws -> String {
        let root = FileManager.default.temporaryDirectory.appendingPathComponent("nashi-gif-preset-\(UUID().uuidString)", isDirectory: true)
        try FileManager.default.createDirectory(at: root, withIntermediateDirectories: true)
        addTeardownBlock { try? FileManager.default.removeItem(at: root) }
        let log = root.appendingPathComponent("argv.log")
        let probe = root.appendingPathComponent("probe.json")
        try Data(#"{"format":{"duration":"5","format_name":"mov,mp4,m4a,3gp,3g2,mj2"},"streams":[{"index":0,"codec_type":"video","codec_name":"h264","width":1280,"height":720,"avg_frame_rate":"30/1","duration":"5"}]}"#.utf8).write(to: probe)
        let ffprobe = root.appendingPathComponent("ffprobe"), ffmpeg = root.appendingPathComponent("ffmpeg")
        try Data("#!/bin/sh\ncat '\(probe.path)'\n".utf8).write(to: ffprobe)
        try Data("#!/bin/sh\nfor a in \"$@\"; do printf '%s\\n' \"$a\" >> '\(log.path)'; done\nfor last; do :; done\nprintf x > \"$last\"\n".utf8).write(to: ffmpeg)
        for (variable, executable) in [("NASHI_FFPROBE", ffprobe), ("NASHI_FFMPEG", ffmpeg)] {
            try FileManager.default.setAttributes([.posixPermissions: 0o755], ofItemAtPath: executable.path)
            let previous = ProcessInfo.processInfo.environment[variable]
            setenv(variable, executable.path, 1)
            addTeardownBlock { if let previous { setenv(variable, previous, 1) } else { unsetenv(variable) } }
        }
        let input = root.appendingPathComponent("Clip.mp4"); try Data([1]).write(to: input)
        var options = OperationOptions(); options.compressionPreset = preset
        options.outputDirectory = root.appendingPathComponent("out")
        _ = try MediaEngine.convert(input, to: "gif", options: options, token: .init())
        let lines = try String(contentsOf: log, encoding: .utf8).split(separator: "\n").map(String.init)
        let index = try XCTUnwrap(lines.firstIndex(of: "-filter_complex"))
        return lines[index + 1]
    }

    func testCompressionPresetChangesTheGIFEncode() throws {
        let fast = try gifFilter(preset: "fast"), balanced = try gifFilter(preset: "balanced"), maximum = try gifFilter(preset: "maximum")
        XCTAssertEqual(Set([fast, balanced, maximum]).count, 3)
        XCTAssertTrue(balanced.hasSuffix("palettegen=stats_mode=diff[pal];[p1][pal]paletteuse=dither=sierra2_4a"))
        XCTAssertTrue(fast.contains("dither=bayer"))
        XCTAssertTrue(maximum.contains("max_colors=128") && maximum.contains("diff_mode=rectangle"))
    }
}
