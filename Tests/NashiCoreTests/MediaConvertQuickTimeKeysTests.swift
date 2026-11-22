import XCTest
@testable import NashiCore

/// FFmpeg reads Apple's QuickTime metadata (location, make, model, capture date) as
/// com.apple.quicktime.* keys, which the MP4/MOV muxer writes only with use_metadata_tags.
/// Records the argv through the NASHI_FFMPEG/NASHI_FFPROBE seam.
final class MediaConvertQuickTimeKeysTests: XCTestCase {
    private func movflags(_ name: String, format: String, tags: String, preserve: Bool = true) throws -> [String] {
        let root = FileManager.default.temporaryDirectory.appendingPathComponent("nashi-quicktime-keys-\(UUID().uuidString)", isDirectory: true)
        try FileManager.default.createDirectory(at: root, withIntermediateDirectories: true)
        addTeardownBlock { try? FileManager.default.removeItem(at: root) }
        let log = root.appendingPathComponent("argv.log"), probe = root.appendingPathComponent("probe.json")
        try Data(#"{"format":{"duration":"10","format_name":"mov,mp4,m4a,3gp,3g2,mj2","tags":\#(tags)},"streams":[{"index":0,"codec_type":"video","codec_name":"hevc","width":1920,"height":1080},{"index":1,"codec_type":"audio","codec_name":"aac","channels":2,"sample_rate":"44100"}]}"#.utf8).write(to: probe)
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
        return arguments.indices.filter { arguments[$0] == "-movflags" }.map { arguments[$0 + 1] }
    }

    func testPreserveMetadataKeepsQuickTimeKeysInMP4AndMOV() throws {
        let iphone = #"{"com.apple.quicktime.location.ISO6709":"+35.6895+139.6917+040.000/","com.apple.quicktime.make":"Apple","com.apple.quicktime.model":"iPhone 15"}"#
        XCTAssertEqual(try movflags("IMG_0001.MOV", format: "mp4", tags: iphone), ["+faststart+use_metadata_tags"])
        XCTAssertEqual(try movflags("IMG_0001.MOV", format: "mov", tags: iphone), ["+faststart+use_metadata_tags"])
        XCTAssertEqual(try movflags("IMG_0001.MOV", format: "mkv", tags: iphone), [])
        XCTAssertEqual(try movflags("IMG_0001.MOV", format: "mp4", tags: iphone, preserve: false), ["+faststart"])
        XCTAssertEqual(try movflags("Clip.mp4", format: "mov", tags: #"{"location":"+35.6895+139.6917/"}"#), ["+faststart"])
    }
}
