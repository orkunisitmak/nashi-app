import XCTest
@testable import NashiCore

/// GoPro MP4s carry codec-less data tracks (timecode and firmware data) that the MP4 muxer
/// cannot write; copying them made every metadata edit fail. Records the argv through the
/// NASHI_FFMPEG/NASHI_FFPROBE seam.
final class MediaMetadataCodecLessDataTracksTests: XCTestCase {
    private func edit(_ name: String, metadata: [String: String]) throws -> [String] {
        let root = FileManager.default.temporaryDirectory.appendingPathComponent("nashi-codecless-data-\(UUID().uuidString)", isDirectory: true)
        try FileManager.default.createDirectory(at: root, withIntermediateDirectories: true)
        addTeardownBlock { try? FileManager.default.removeItem(at: root) }
        let log = root.appendingPathComponent("argv.log"), probe = root.appendingPathComponent("probe.json")
        try Data(#"{"format":{"duration":"10","format_name":"mov,mp4,m4a,3gp,3g2,mj2","tags":{"major_brand":"mp41"}},"streams":[{"index":0,"codec_type":"video","codec_name":"h264","width":1920,"height":1080},{"index":1,"codec_type":"audio","codec_name":"aac","channels":2,"sample_rate":"48000"},{"index":2,"codec_type":"data","codec_tag_string":"tmcd","tags":{"handler_name":"GoPro TCD"}},{"index":3,"codec_type":"data","codec_name":"bin_data","codec_tag_string":"gpmd","tags":{"handler_name":"GoPro MET"}},{"index":4,"codec_type":"data","codec_tag_string":"fdsc","tags":{"handler_name":"GoPro SOS"}}]}"#.utf8).write(to: probe)
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
        _ = try MediaEngine.editMetadata(input, metadata: metadata, options: options)
        return try String(contentsOf: log, encoding: .utf8).split(separator: "\n").map(String.init)
    }

    func testEditingGoProMP4LeavesOutCodecLessDataTracks() throws {
        let fields = ["file:title": "Ride", "track[2]:handler_name": "GoPro TCD", "track[3]:handler_name": "GoPro MET", "track[4]:handler_name": "GoPro SOS"]
        let arguments = try edit("GX010001.MP4", metadata: fields)
        let excluded = arguments.indices.filter { arguments[$0] == "-map" }.map { arguments[$0 + 1] }
        XCTAssertEqual(excluded, ["0", "-0:2", "-0:4"])
        let streamTags = arguments.indices.filter { arguments[$0].hasPrefix("-metadata:s:") }.map { "\(arguments[$0]) \(arguments[$0 + 1])" }
        XCTAssertEqual(streamTags, ["-metadata:s:2 handler_name=GoPro MET"])
        let mov = try edit("GX010001.MOV", metadata: fields)
        XCTAssertEqual(mov.indices.filter { mov[$0] == "-map" }.map { mov[$0 + 1] }, ["0"])
    }
}
