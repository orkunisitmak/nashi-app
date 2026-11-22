import XCTest
@testable import NashiCore

/// Records the ffmpeg color through the NASHI_FFMPEG/NASHI_FFPROBE seam for a canned
/// 128 x 96 video with audio. Video colors must read like image export colors.
final class VideoColorHexTests: XCTestCase {
    private func colors(_ color: String, tool: ToolKind) throws -> [String] {
        let root = FileManager.default.temporaryDirectory.appendingPathComponent("nashi-video-color-\(UUID().uuidString)", isDirectory: true)
        try FileManager.default.createDirectory(at: root, withIntermediateDirectories: true)
        addTeardownBlock { try? FileManager.default.removeItem(at: root) }
        let log = root.appendingPathComponent("argv.log"), probe = root.appendingPathComponent("probe.json")
        try Data(#"{"format":{"duration":"3"},"streams":[{"index":0,"codec_type":"video","codec_name":"h264","width":128,"height":96,"duration":"3","avg_frame_rate":"25/1"},{"index":1,"codec_type":"audio","codec_name":"aac","channels":2,"sample_rate":"48000","duration":"3"}]}"#.utf8).write(to: probe)
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
        let still = root.appendingPathComponent("Still.png"); try Data([1]).write(to: still)
        var options = OperationOptions(); options.redactionMode = "solid"; options.backgroundColor = color
        options.outputDirectory = root.appendingPathComponent("out")
        options.videoRedactions = [VideoRedaction(rect: EditRect(x: 40, y: 20, width: 40, height: 40), start: 0, end: 3)]
        options.visualizationStyle = "image"; options.visualizationImage = still
        _ = try MediaEngine.tool(tool, inputs: [input], options: options, token: .init())
        let text = try String(contentsOf: log, encoding: .utf8), ns = text as NSString
        return try NSRegularExpression(pattern: #"color=(0x[0-9A-F]+)"#).matches(in: text, range: NSRange(location: 0, length: ns.length)).map { ns.substring(with: $0.range(at: 1)) }
    }

    func testShortAndAlphaHexColorsReadLikeImageExports() throws {
        XCTAssertEqual(try colors("#F00", tool: .redactVideo), ["0xFF0000"])
        XCTAssertEqual(try colors("#F00A", tool: .redactVideo), ["0xFF0000"])
        XCTAssertEqual(try colors("ff000080", tool: .redactVideo), ["0xFF0000"])
        XCTAssertEqual(try colors("#123", tool: .audioToVideo), ["0x112233"])
        XCTAssertEqual(try colors("#11223380", tool: .audioToVideo), ["0x11223380"])
    }

    func testUnreadableColorsAreRefusedBeforeFFmpegRuns() {
        for bad in ["", "#12", "#+12345", "#GG0000", "#\u{FF10}\u{FF10}\u{FF10}\u{FF10}\u{FF10}\u{FF10}"] {
            XCTAssertThrowsError(try colors(bad, tool: .redactVideo), bad)
        }
    }
}
