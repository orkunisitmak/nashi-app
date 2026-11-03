import XCTest
@testable import NashiCore

/// Album art is an attached picture stream. With Preserve metadata on, audio conversion
/// keeps it in containers that store JPEG/PNG art. Records the argv through NASHI_FFMPEG.
final class MediaConvertAlbumArtTests: XCTestCase {
    private func arguments(to format: String, art: String = "mjpeg", preserve: Bool = true) throws -> [String] {
        let root = FileManager.default.temporaryDirectory.appendingPathComponent("nashi-album-art-\(UUID().uuidString)", isDirectory: true)
        try FileManager.default.createDirectory(at: root, withIntermediateDirectories: true)
        addTeardownBlock { try? FileManager.default.removeItem(at: root) }
        let log = root.appendingPathComponent("argv.log"), probe = root.appendingPathComponent("probe.json")
        try Data(#"{"format":{"duration":"10","format_name":"mp3","tags":{"title":"Song"}},"streams":[{"index":0,"codec_type":"audio","codec_name":"mp3","channels":2,"sample_rate":"44100"},{"index":1,"codec_type":"video","codec_name":"\#(art)","width":600,"height":600,"disposition":{"attached_pic":1}}]}"#.utf8).write(to: probe)
        let ffprobe = root.appendingPathComponent("ffprobe"), ffmpeg = root.appendingPathComponent("ffmpeg")
        try Data("#!/bin/sh\ncat '\(probe.path)'\n".utf8).write(to: ffprobe)
        try Data("#!/bin/sh\nfor a in \"$@\"; do printf '%s\\n' \"$a\" >> '\(log.path)'; done\nfor last; do :; done\nprintf x > \"$last\"\n".utf8).write(to: ffmpeg)
        for (variable, tool) in [("NASHI_FFPROBE", ffprobe), ("NASHI_FFMPEG", ffmpeg)] {
            try FileManager.default.setAttributes([.posixPermissions: 0o755], ofItemAtPath: tool.path)
            let previous = ProcessInfo.processInfo.environment[variable]
            setenv(variable, tool.path, 1)
            addTeardownBlock { if let previous { setenv(variable, previous, 1) } else { unsetenv(variable) } }
        }
        let input = root.appendingPathComponent("Song.mp3"); try Data([1]).write(to: input)
        var options = OperationOptions(); options.outputDirectory = root.appendingPathComponent("out"); options.preserveMetadata = preserve
        _ = try MediaEngine.convert(input, to: format, options: options, token: .init())
        return try String(contentsOf: log, encoding: .utf8).split(separator: "\n").map(String.init)
    }

    private func maps(_ arguments: [String]) -> [String] { arguments.indices.filter { arguments[$0] == "-map" }.map { arguments[$0 + 1] } }

    func testPreserveMetadataKeepsAlbumArt() throws {
        for format in ["mp3", "m4a", "flac"] {
            let arguments = try arguments(to: format)
            XCTAssertEqual(maps(arguments), ["0:a:0", "0:1"], format)
            // -vn would also drop an explicitly mapped video stream.
            XCTAssertFalse(arguments.contains("-vn"), format)
            XCTAssertEqual(arguments.firstIndex(of: "-c:v").map { arguments[$0 + 1] }, "copy", format)
        }
        for arguments in [try arguments(to: "ogg"), try arguments(to: "wav"), try arguments(to: "m4a", preserve: false), try arguments(to: "m4a", art: "bmp")] {
            XCTAssertEqual(maps(arguments), ["0:a:0"])
            XCTAssertTrue(arguments.contains("-vn"))
        }
    }
}
