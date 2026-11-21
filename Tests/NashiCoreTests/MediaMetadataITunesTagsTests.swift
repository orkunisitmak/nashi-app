import XCTest
@testable import NashiCore

/// With -movflags use_metadata_tags the MP4/MOV muxer writes QuickTime keys instead of the iTunes
/// tag list, and cover art is only written by that list. Records the argv through the
/// NASHI_FFMPEG/NASHI_FFPROBE seam.
final class MediaMetadataITunesTagsTests: XCTestCase {
    private func movflags(_ name: String, tags: String, metadata: [String: String], removeAll: Bool = false) throws -> [String] {
        let root = FileManager.default.temporaryDirectory.appendingPathComponent("nashi-itunes-tags-\(UUID().uuidString)", isDirectory: true)
        try FileManager.default.createDirectory(at: root, withIntermediateDirectories: true)
        addTeardownBlock { try? FileManager.default.removeItem(at: root) }
        let log = root.appendingPathComponent("argv.log"), probe = root.appendingPathComponent("probe.json")
        try Data(#"{"format":{"duration":"10","format_name":"mov,mp4,m4a,3gp,3g2,mj2","tags":\#(tags)},"streams":[{"index":0,"codec_type":"audio","codec_name":"aac","channels":2,"sample_rate":"44100"},{"index":1,"codec_type":"video","codec_name":"mjpeg","width":600,"height":600,"disposition":{"attached_pic":1}}]}"#.utf8).write(to: probe)
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
        var options = OperationOptions(); options.outputDirectory = root.appendingPathComponent("out")
        _ = try MediaEngine.editMetadata(input, metadata: metadata, removeAll: removeAll, options: options)
        let arguments = try String(contentsOf: log, encoding: .utf8).split(separator: "\n").map(String.init)
        return arguments.indices.filter { arguments[$0] == "-movflags" }.map { arguments[$0 + 1] }
    }

    func testEditingTagsKeepsTheITunesListUnlessQuickTimeKeysRemain() throws {
        let song = #"{"title":"Old title","artist":"Band","album":"Album","track":"3/12"}"#
        XCTAssertEqual(try movflags("Song.m4a", tags: song, metadata: ["file:title": "New title", "file:artist": "Band"]), [])
        XCTAssertEqual(try movflags("Clip.mov", tags: song, metadata: ["file:title": "New title"]), [])
        let iphone = #"{"com.apple.quicktime.location.ISO6709":"+35.6895+139.6917+040.000/","com.apple.quicktime.make":"Apple"}"#
        XCTAssertEqual(try movflags("IMG_0001.MOV", tags: iphone, metadata: ["file:title": "Trip"]), ["+use_metadata_tags"])
        XCTAssertEqual(try movflags("IMG_0001.MOV", tags: iphone, metadata: ["file:com.apple.quicktime.location.ISO6709": "", "file:com.apple.quicktime.make": ""]), [])
        XCTAssertEqual(try movflags("IMG_0001.MOV", tags: iphone, metadata: [:], removeAll: true), [])
        XCTAssertEqual(try movflags("Song.m4a", tags: song, metadata: ["file:com.apple.quicktime.title": "Song"]), ["+use_metadata_tags"])
    }
}
