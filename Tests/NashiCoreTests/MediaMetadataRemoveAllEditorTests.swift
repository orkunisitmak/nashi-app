import XCTest
@testable import NashiCore

/// The metadata editor's Remove All sends every listed field as a blank. Those blanks must not
/// address the artwork, data tracks or chapters that Remove All itself drops.
/// Records the argv through the NASHI_FFMPEG/NASHI_FFPROBE seam.
final class MediaMetadataRemoveAllEditorTests: XCTestCase {
    private func removeAll(_ name: String, probe json: String, metadata: [String: String]) throws -> [String] {
        let root = FileManager.default.temporaryDirectory.appendingPathComponent("nashi-remove-all-editor-\(UUID().uuidString)", isDirectory: true)
        try FileManager.default.createDirectory(at: root, withIntermediateDirectories: true)
        addTeardownBlock { try? FileManager.default.removeItem(at: root) }
        let log = root.appendingPathComponent("argv.log"), probe = root.appendingPathComponent("probe.json")
        try Data(json.utf8).write(to: probe)
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
        options.metadata = metadata; options.metadataRemoveAll = true
        _ = try ConversionEngine.run(.init(inputs: [input], operation: .tool(.removeMetadata), options: options))
        return try String(contentsOf: log, encoding: .utf8).split(separator: "\n").map(String.init)
    }

    func testRemoveAllIgnoresTheEditorsBlankFields() throws {
        let song = #"{"format":{"duration":"10","format_name":"mp3","tags":{"title":"Private title"}},"streams":[{"index":0,"codec_type":"audio","codec_name":"mp3","channels":2,"sample_rate":"44100"},{"index":1,"codec_type":"video","codec_name":"mjpeg","width":32,"height":32,"disposition":{"attached_pic":1},"tags":{"comment":"Cover (front)"}}]}"#
        let arguments = try removeAll("Song.mp3", probe: song, metadata: ["file:title": "", "track[1]:comment": ""])
        XCTAssertTrue(arguments.contains("-0:1"))
        XCTAssertFalse(arguments.contains { $0.hasPrefix("-metadata") })

        let movie = #"{"format":{"duration":"10","format_name":"mov,mp4,m4a,3gp,3g2,mj2","tags":{"title":"Holiday"}},"streams":[{"index":0,"codec_type":"video","codec_name":"h264","width":64,"height":64}],"chapters":[{"id":0,"start_time":"0","end_time":"5","tags":{"title":"Arrival"}}]}"#
        let chaptered = try removeAll("Holiday.mp4", probe: movie, metadata: ["file:title": "", "chapter[0]:title": "", "file:comment": "Shared copy"])
        XCTAssertTrue(chaptered.contains("-map_chapters"))
        XCTAssertFalse(chaptered.contains { $0.hasPrefix("-metadata:c:") })
        XCTAssertEqual(chaptered.filter { $0.hasPrefix("-metadata") }, ["-metadata"])
        XCTAssertTrue(chaptered.contains("comment=Shared copy"))
    }
}
