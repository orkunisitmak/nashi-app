import XCTest
@testable import NashiCore

/// FFmpeg reads Ogg Vorbis/Opus comments as audio-stream tags, while MP3, M4A and FLAC
/// write file-level tags. Records the argv through the NASHI_FFMPEG/NASHI_FFPROBE seam.
final class MediaConvertOggTagsTests: XCTestCase {
    private func metadataMaps(_ name: String, format: String, container: String, tags: String, preserve: Bool = true) throws -> [String] {
        let root = FileManager.default.temporaryDirectory.appendingPathComponent("nashi-ogg-tags-\(UUID().uuidString)", isDirectory: true)
        try FileManager.default.createDirectory(at: root, withIntermediateDirectories: true)
        addTeardownBlock { try? FileManager.default.removeItem(at: root) }
        let log = root.appendingPathComponent("argv.log"), probe = root.appendingPathComponent("probe.json")
        try Data(#"{"format":{"duration":"10","format_name":"\#(container)"},"streams":[{"index":0,"codec_type":"audio","codec_name":"vorbis","channels":2,"sample_rate":"48000","tags":\#(tags)}]}"#.utf8).write(to: probe)
        let ffprobe = root.appendingPathComponent("ffprobe"), ffmpeg = root.appendingPathComponent("ffmpeg")
        try Data("#!/bin/sh\ncat '\(probe.path)'\n".utf8).write(to: ffprobe)
        try Data("#!/bin/sh\nfor a in \"$@\"; do printf '%s\\n' \"$a\" >> '\(log.path)'; done\nfor last; do :; done\nprintf x > \"$last\"\n".utf8).write(to: ffmpeg)
        for (variable, tool) in [("NASHI_FFPROBE", ffprobe), ("NASHI_FFMPEG", ffmpeg)] {
            try FileManager.default.setAttributes([.posixPermissions: 0o755], ofItemAtPath: tool.path)
            let previous = ProcessInfo.processInfo.environment[variable]
            setenv(variable, tool.path, 1)
            addTeardownBlock { if let previous { setenv(variable, previous, 1) } else { unsetenv(variable) } }
        }
        let input = root.appendingPathComponent(name); try Data([1]).write(to: input)
        var options = OperationOptions(); options.outputDirectory = root.appendingPathComponent("out"); options.preserveMetadata = preserve
        _ = try MediaEngine.convert(input, to: format, options: options, token: .init())
        let arguments = try String(contentsOf: log, encoding: .utf8).split(separator: "\n").map(String.init)
        return arguments.indices.filter { arguments[$0].hasPrefix("-map_metadata") }.map { "\(arguments[$0]) \(arguments[$0 + 1])" }
    }

    func testPreserveMetadataKeepsOggCommentTags() throws {
        let tags = #"{"TITLE":"Song","ARTIST":"Band"}"#
        for (name, format) in [("Song.ogg", "mp3"), ("Song.opus", "m4a"), ("Song.ogg", "flac")] {
            XCTAssertEqual(try metadataMaps(name, format: format, container: "ogg", tags: tags), ["-map_metadata 0", "-map_metadata 0:s:a:0"], "\(name) -> \(format)")
        }
        XCTAssertEqual(try metadataMaps("Plain.ogg", format: "mp3", container: "ogg", tags: "{}"), ["-map_metadata 0"])
        XCTAssertEqual(try metadataMaps("Song.m4a", format: "mp3", container: "mov,mp4,m4a,3gp,3g2,mj2", tags: #"{"handler_name":"SoundHandler"}"#), ["-map_metadata 0"])
        XCTAssertEqual(try metadataMaps("Song.ogg", format: "mp3", container: "ogg", tags: tags, preserve: false), ["-map_metadata -1", "-map_metadata:s -1"])
    }
}
