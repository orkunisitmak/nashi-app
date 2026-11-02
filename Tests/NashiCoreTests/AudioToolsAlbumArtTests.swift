import XCTest
@testable import NashiCore

/// Runs the audio tools against a canned MP3 with a cover picture through the NASHI_FFMPEG/NASHI_FFPROBE seam.
final class AudioToolsAlbumArtTests: XCTestCase {
    private func run(_ tool: ToolKind, ext: String = "mp3", withArt: Bool = true, _ setup: (inout OperationOptions) -> Void = { _ in }) throws -> [String] {
        let root = FileManager.default.temporaryDirectory.appendingPathComponent("nashi-audio-art-\(UUID().uuidString)", isDirectory: true)
        try FileManager.default.createDirectory(at: root, withIntermediateDirectories: true)
        addTeardownBlock { try? FileManager.default.removeItem(at: root) }
        let log = root.appendingPathComponent("argv.log"), probe = root.appendingPathComponent("probe.json")
        let art = withArt ? #",{"index":1,"codec_type":"video","codec_name":"mjpeg","width":600,"height":600,"disposition":{"attached_pic":1}}"# : ""
        try Data(#"{"format":{"duration":"30","format_name":"\#(ext)"},"streams":[{"index":0,"codec_type":"audio","codec_name":"\#(ext)","channels":2,"sample_rate":"44100","duration":"30"}\#(art)]}"#.utf8).write(to: probe)
        let ffprobe = root.appendingPathComponent("ffprobe"), ffmpeg = root.appendingPathComponent("ffmpeg")
        try Data("#!/bin/sh\ncat '\(probe.path)'\n".utf8).write(to: ffprobe)
        // Loudness measurement reads a JSON block from stderr; every run records its arguments.
        try Data("#!/bin/sh\necho ARGV >> '\(log.path)'\nfor a in \"$@\"; do printf '%s\\n' \"$a\" >> '\(log.path)'; done\necho '{\"input_i\":\"-20\",\"input_tp\":\"-3\",\"input_lra\":\"5\",\"input_thresh\":\"-30\",\"target_offset\":\"0\"}' >&2\nfor last; do :; done\n[ \"$last\" = - ] || printf x > \"$last\"\n".utf8).write(to: ffmpeg)
        for (variable, tool) in [("NASHI_FFPROBE", ffprobe), ("NASHI_FFMPEG", ffmpeg)] {
            try FileManager.default.setAttributes([.posixPermissions: 0o755], ofItemAtPath: tool.path)
            let previous = ProcessInfo.processInfo.environment[variable]
            setenv(variable, tool.path, 1)
            addTeardownBlock { if let previous { setenv(variable, previous, 1) } else { unsetenv(variable) } }
        }
        let input = root.appendingPathComponent("Song.\(ext)"); try Data([1]).write(to: input)
        var options = OperationOptions(); options.outputDirectory = root.appendingPathComponent("out"); setup(&options)
        _ = try MediaEngine.tool(tool, inputs: [input], options: options, token: .init())
        return String(decoding: try Data(contentsOf: log), as: UTF8.self).components(separatedBy: "ARGV\n").last!.split(separator: "\n").map(String.init)
    }
    private func keepsArt(_ argv: [String]) -> Bool {
        let pairs = Array(zip(argv, argv.dropFirst()))
        return pairs.contains { $0 == "-map" && $1 == "0:1" } && pairs.contains { $0 == "-c:v" && $1 == "copy" } && !argv.contains("-vn")
    }

    func testAudioToolsKeepAnMP3sCoverPicture() throws {
        XCTAssertTrue(keepsArt(try run(.trimAudio) { $0.start = 5; $0.end = 20 }))
        XCTAssertTrue(keepsArt(try run(.normalizeAudio)))
        XCTAssertTrue(keepsArt(try run(.audioChannels) { $0.channels = 1 }))
        XCTAssertTrue(keepsArt(try run(.redactAudio) { $0.audioRedactions = [TimeRange(start: 2, end: 3)] }))
    }

    func testToolsStillDropPicturesWhenMetadataIsNotKeptOrThereIsNoCover() throws {
        let stripped = try run(.trimAudio) { $0.start = 5; $0.end = 20; $0.preserveMetadata = false }
        XCTAssertFalse(stripped.contains("0:1")); XCTAssertTrue(stripped.contains("-vn"))
        let plain = try run(.trimAudio, withArt: false) { $0.start = 5; $0.end = 20 }
        XCTAssertFalse(plain.contains("0:1")); XCTAssertTrue(plain.contains("-vn"))
    }
}
