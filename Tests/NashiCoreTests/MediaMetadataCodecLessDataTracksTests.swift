import XCTest
@testable import NashiCore

/// GoPro MP4s carry codec-less data tracks (timecode and firmware data) that the MP4 muxer
/// cannot write; copying them made every metadata edit fail. Records the argv through the
/// NASHI_FFMPEG/NASHI_FFPROBE seam.
final class MediaMetadataCodecLessDataTracksTests: XCTestCase {
    private static let goPro = #"{"format":{"duration":"10","format_name":"mov,mp4,m4a,3gp,3g2,mj2","tags":{"major_brand":"mp41"}},"streams":[{"index":0,"codec_type":"video","codec_name":"h264","width":1920,"height":1080},{"index":1,"codec_type":"audio","codec_name":"aac","channels":2,"sample_rate":"48000"},{"index":2,"codec_type":"data","codec_tag_string":"tmcd","tags":{"handler_name":"GoPro TCD"}},{"index":3,"codec_type":"data","codec_name":"bin_data","codec_tag_string":"gpmd","tags":{"handler_name":"GoPro MET"}},{"index":4,"codec_type":"data","codec_tag_string":"fdsc","tags":{"handler_name":"GoPro SOS"}}]}"#
    /// The editor's unchanged fields for the tracks above.
    private static let listed = ["track[2]:handler_name": "GoPro TCD", "track[3]:handler_name": "GoPro MET", "track[4]:handler_name": "GoPro SOS"]

    /// The ffmpeg argv, empty when ffmpeg never ran, and the folder the output goes to.
    private func edit(_ name: String, probe json: String = goPro, metadata: [String: String]) throws -> (arguments: () -> [String], output: URL, run: () throws -> Void) {
        let root = FileManager.default.temporaryDirectory.appendingPathComponent("nashi-codecless-data-\(UUID().uuidString)", isDirectory: true)
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
        let arguments = { ((try? String(contentsOf: log, encoding: .utf8)) ?? "").split(separator: "\n").map(String.init) }
        return (arguments, options.outputDirectory!, { _ = try MediaEngine.editMetadata(input, metadata: metadata, options: options) })
    }

    private func maps(_ arguments: [String]) -> [String] { arguments.indices.filter { arguments[$0] == "-map" }.map { arguments[$0 + 1] } }
    private func streamTags(_ arguments: [String]) -> [String] { arguments.indices.filter { arguments[$0].hasPrefix("-metadata:s:") }.map { "\(arguments[$0]) \(arguments[$0 + 1])" } }

    func testEditingGoProMP4LeavesOutCodecLessDataTracks() throws {
        let fields = Self.listed.merging(["file:title": "Ride"]) { $1 }
        let mp4 = try edit("GX010001.MP4", metadata: fields)
        try mp4.run()
        XCTAssertEqual(maps(mp4.arguments()), ["0", "-0:2", "-0:4"])
        XCTAssertEqual(streamTags(mp4.arguments()), ["-metadata:s:2 handler_name=GoPro MET"])
        XCTAssertTrue(mp4.arguments().contains("title=Ride"))
        let mov = try edit("GX010001.MOV", metadata: fields)
        try mov.run()
        XCTAssertEqual(maps(mov.arguments()), ["0"])
        XCTAssertEqual(streamTags(mov.arguments()), ["-metadata:s:2 handler_name=GoPro TCD", "-metadata:s:3 handler_name=GoPro MET", "-metadata:s:4 handler_name=GoPro SOS"])
    }

    func testChangingAFieldOfALeftOutTrackFailsInsteadOfSavingWithoutIt() throws {
        for change in [["track[2]:handler_name": "My timecode"], ["track[2]:handler_name": ""], ["track[4]:language": "eng"]] {
            let job = try edit("GX010001.MP4", metadata: Self.listed.merging(change) { $1 })
            XCTAssertThrowsError(try job.run()) { error in
                guard case NashiError.invalidInput(let message) = error else { return XCTFail("\(error)") }
                let key = change.keys.first!
                XCTAssertTrue(message.contains(key) && message.contains("Track \(key.dropFirst(6).prefix(1))"), message)
            }
            XCTAssertEqual(job.arguments(), [])
            XCTAssertEqual((try? FileManager.default.contentsOfDirectory(atPath: job.output.path)) ?? [], [])
        }
        // A blank for a tag the track never had is not a change.
        let blank = try edit("GX010001.MP4", metadata: Self.listed.merging(["track[4]:language": ""]) { $1 })
        try blank.run()
        XCTAssertEqual(streamTags(blank.arguments()), ["-metadata:s:2 handler_name=GoPro MET"])
    }

    func testTracksAfterALeftOutTrackAreRenumbered() throws {
        let between = #"{"format":{"duration":"10","format_name":"mov,mp4,m4a,3gp,3g2,mj2"},"streams":[{"index":0,"codec_type":"video","codec_name":"h264","width":1920,"height":1080},{"index":1,"codec_type":"data","codec_name":"none","codec_tag_string":"tmcd","tags":{"handler_name":"Timecode"}},{"index":2,"codec_type":"audio","codec_name":"aac","channels":2,"sample_rate":"48000"},{"index":3,"codec_type":"data","codec_tag_string":"fdsc"},{"index":4,"codec_type":"subtitle","codec_name":"mov_text"}]}"#
        let job = try edit("Clip.m4v", probe: between, metadata: ["track[0]:language": "eng", "track[1]:handler_name": "Timecode", "track[2]:language": "tur", "track[4]:language": "deu"])
        try job.run()
        XCTAssertEqual(maps(job.arguments()), ["0", "-0:1", "-0:3"])
        XCTAssertEqual(streamTags(job.arguments()), ["-metadata:s:0 language=eng", "-metadata:s:1 language=tur", "-metadata:s:2 language=deu"])
    }
}
