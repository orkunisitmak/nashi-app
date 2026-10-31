import XCTest
@testable import NashiCore

/// A damaged audio header can make ffprobe report any channel count or sample
/// rate. Such values must be treated as unknown instead of overflowing bitrate
/// arithmetic or building an inverted channel range, which traps the process.
final class MediaProbeAudioLayoutTests: XCTestCase {
    func testImpossibleChannelCountsAndSampleRatesAreTreatedAsUnknown() throws {
        let root = FileManager.default.temporaryDirectory.appendingPathComponent("nashi-probe-layout-\(UUID().uuidString)", isDirectory: true)
        try FileManager.default.createDirectory(at: root, withIntermediateDirectories: true)
        defer { try? FileManager.default.removeItem(at: root) }
        let log = root.appendingPathComponent("argv.log"), probe = root.appendingPathComponent("probe.json")
        let ffprobe = root.appendingPathComponent("ffprobe"), ffmpeg = root.appendingPathComponent("ffmpeg")
        try Data("#!/bin/sh\ncat '\(probe.path)'\n".utf8).write(to: ffprobe)
        try Data("#!/bin/sh\n: > '\(log.path)'\nfor a in \"$@\"; do printf '%s\\n' \"$a\" >> '\(log.path)'; done\nfor last; do :; done\nprintf x > \"$last\"\n".utf8).write(to: ffmpeg)
        for (variable, tool) in [("NASHI_FFPROBE", ffprobe), ("NASHI_FFMPEG", ffmpeg)] {
            try FileManager.default.setAttributes([.posixPermissions: 0o755], ofItemAtPath: tool.path)
            let previous = ProcessInfo.processInfo.environment[variable]
            setenv(variable, tool.path, 1)
            addTeardownBlock { if let previous { setenv(variable, previous, 1) } else { unsetenv(variable) } }
        }
        func probeAudio(channels: String, sampleRate: String) throws {
            try Data(#"{"format":{"duration":"10"},"streams":[{"index":0,"codec_type":"audio","codec_name":"pcm_s16le","channels":\#(channels),"sample_rate":\#(sampleRate)}]}"#.utf8).write(to: probe)
        }
        func value(after flag: String) throws -> String? {
            let argv = try String(contentsOf: log, encoding: .utf8).split(separator: "\n").map(String.init)
            return argv.firstIndex(of: flag).map { argv[$0 + 1] }
        }
        let audio = root.appendingPathComponent("Song.wav")
        try Data([1]).write(to: audio)
        var options = OperationOptions()
        options.outputDirectory = root.appendingPathComponent("out")

        for (channels, sampleRate) in [("2", "\"4000000000000000000\""), ("2147483647", "\"2147483647\""), ("100000000000000000", "\"48000\"")] {
            try probeAudio(channels: channels, sampleRate: sampleRate)
            XCTAssertEqual(try MediaEngine.convert(audio, to: "m4a", options: options, token: .init()).count, 1)
            XCTAssertEqual(try value(after: "-b:a"), "256000", "channels \(channels), sample rate \(sampleRate)")
            XCTAssertEqual(try MediaEngine.convert(audio, to: "opus", options: options, token: .init()).count, 1)
        }
        options.audioRedactions = [TimeRange(start: 1, end: 2)]
        for channels in ["-1", "0", "100000"] {
            try probeAudio(channels: channels, sampleRate: "\"44100\"")
            XCTAssertNil(try MediaEngine.inspect(audio).audio?.channels)
            XCTAssertEqual(try MediaEngine.tool(.redactAudio, inputs: [audio], options: options, token: .init()).count, 1)
            XCTAssertEqual(try value(after: "-ac"), "2", "channels \(channels)")
        }
        try probeAudio(channels: "6", sampleRate: "\"96000\"")
        XCTAssertEqual(try MediaEngine.inspect(audio).audio?.channels, 6)
        XCTAssertEqual(try MediaEngine.inspect(audio).audio?.sampleRate, 96000)
    }
}
