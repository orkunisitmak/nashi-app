import XCTest
@testable import NashiCore

/// The video tools copy the input's metadata, but the MP4/MOV muxer writes an iPhone video's
/// com.apple.quicktime.* keys only with use_metadata_tags. Records the argv through the NASHI_FFMPEG/NASHI_FFPROBE seam.
final class MediaVideoToolsQuickTimeKeysTests: XCTestCase {
    private func movflags(_ kind: ToolKind, tags: String, setup: (inout OperationOptions) -> Void = { _ in }) throws -> [String] {
        let root = FileManager.default.temporaryDirectory.appendingPathComponent("nashi-video-tools-keys-\(UUID().uuidString)", isDirectory: true)
        try FileManager.default.createDirectory(at: root, withIntermediateDirectories: true)
        addTeardownBlock { try? FileManager.default.removeItem(at: root) }
        let log = root.appendingPathComponent("argv.log"), probe = root.appendingPathComponent("probe.json")
        try Data(#"{"format":{"duration":"10","format_name":"mov,mp4,m4a,3gp,3g2,mj2","tags":\#(tags)},"streams":[{"index":0,"codec_type":"video","codec_name":"hevc","width":1920,"height":1080,"r_frame_rate":"30/1"},{"index":1,"codec_type":"audio","codec_name":"aac","channels":2,"sample_rate":"44100"}]}"#.utf8).write(to: probe)
        let ffprobe = root.appendingPathComponent("ffprobe"), ffmpeg = root.appendingPathComponent("ffmpeg")
        try Data("#!/bin/sh\ncat '\(probe.path)'\n".utf8).write(to: ffprobe)
        try Data("#!/bin/sh\nfor a in \"$@\"; do printf '%s\\n' \"$a\" >> '\(log.path)'; done\nfor last; do :; done\nprintf x > \"$last\"\n".utf8).write(to: ffmpeg)
        for (variable, tool) in [("NASHI_FFPROBE", ffprobe), ("NASHI_FFMPEG", ffmpeg)] {
            try FileManager.default.setAttributes([.posixPermissions: 0o755], ofItemAtPath: tool.path)
            let previous = ProcessInfo.processInfo.environment[variable]
            setenv(variable, tool.path, 1)
            addTeardownBlock { if let previous { setenv(variable, previous, 1) } else { unsetenv(variable) } }
        }
        let input = root.appendingPathComponent("IMG_0001.MOV"); try Data([1]).write(to: input)
        var options = OperationOptions(); options.outputDirectory = root.appendingPathComponent("out"); setup(&options)
        _ = try MediaEngine.tool(kind, inputs: [input], options: options, token: .init())
        let arguments = try String(contentsOf: log, encoding: .utf8).split(separator: "\n").map(String.init)
        return arguments.indices.filter { arguments[$0] == "-movflags" }.map { arguments[$0 + 1] }
    }

    func testVideoToolsKeepQuickTimeKeys() throws {
        let iphone = #"{"com.apple.quicktime.location.ISO6709":"+35.6895+139.6917+040.000/","com.apple.quicktime.make":"Apple","com.apple.quicktime.creationdate":"2026-05-01T10:00:00+0300"}"#
        XCTAssertEqual(try movflags(.trimVideo, tags: iphone) { $0.start = 1; $0.end = 5 }, ["+faststart+use_metadata_tags"])
        XCTAssertEqual(try movflags(.cropVideo, tags: iphone) { $0.crop = EditRect(x: 0, y: 0, width: 960, height: 540) }, ["+faststart+use_metadata_tags"])
        XCTAssertEqual(try movflags(.changeVideoSpeed, tags: iphone) { $0.speed = 2 }, ["+faststart+use_metadata_tags"])
        XCTAssertEqual(try movflags(.redactVideo, tags: iphone) { $0.redactions = [EditRect(x: 10, y: 10, width: 100, height: 100)] }, ["+faststart+use_metadata_tags"])
        XCTAssertEqual(try movflags(.muteVideo, tags: iphone), ["+use_metadata_tags"])
    }

    func testVideoToolsLeaveFlagsAloneWithoutQuickTimeKeys() throws {
        let plain = #"{"title":"Holiday"}"#
        XCTAssertEqual(try movflags(.trimVideo, tags: plain) { $0.start = 1; $0.end = 5 }, ["+faststart"])
        XCTAssertEqual(try movflags(.muteVideo, tags: plain), [])
    }
}
