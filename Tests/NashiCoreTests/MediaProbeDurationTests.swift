import XCTest
@testable import NashiCore

/// ffprobe may print "inf" or "nan" for a duration. Such values must not reach
/// JSONSerialization, which raises an Objective-C exception and aborts the CLI.
final class MediaProbeDurationTests: XCTestCase {
    func testNonFiniteProbeDurationIsTreatedAsUnknown() throws {
        let root = FileManager.default.temporaryDirectory.appendingPathComponent("nashi-probe-duration-\(UUID().uuidString)", isDirectory: true)
        try FileManager.default.createDirectory(at: root, withIntermediateDirectories: true)
        defer { try? FileManager.default.removeItem(at: root) }
        let probe = root.appendingPathComponent("probe.json"), ffprobe = root.appendingPathComponent("ffprobe")
        try Data("#!/bin/sh\ncat '\(probe.path)'\n".utf8).write(to: ffprobe)
        try FileManager.default.setAttributes([.posixPermissions: 0o755], ofItemAtPath: ffprobe.path)
        let previous = ProcessInfo.processInfo.environment["NASHI_FFPROBE"]
        setenv("NASHI_FFPROBE", ffprobe.path, 1)
        addTeardownBlock { if let previous { setenv("NASHI_FFPROBE", previous, 1) } else { unsetenv("NASHI_FFPROBE") } }
        let audio = root.appendingPathComponent("Song.wav")
        try Data([1]).write(to: audio)

        for (format, stream) in [("\"inf\"", "\"10\""), ("\"nan\"", "\"10\""), ("\"-inf\"", "\"inf\"")] {
            try Data(#"{"format":{"duration":\#(format)},"streams":[{"index":0,"codec_type":"audio","codec_name":"pcm_s16le","channels":2,"sample_rate":"44100","duration":\#(stream)}],"chapters":[{"start_time":"nan","end_time":"inf"}]}"#.utf8).write(to: probe)
            let info = try MediaEngine.inspect(audio)
            XCTAssertTrue(info.duration.isFinite, "format \(format), stream \(stream)")
            XCTAssertTrue(info.chapters.allSatisfy { $0.start.isFinite && $0.end.isFinite })
            XCTAssertEqual(NashiCommandLine.run(arguments: ["nashi-cli", "inspect", audio.path]), 0)
        }
        XCTAssertEqual(try MediaEngine.inspect(audio).duration, 0)
        try Data(#"{"format":{"duration":"inf"},"streams":[{"index":0,"codec_type":"audio","codec_name":"pcm_s16le","channels":2,"sample_rate":"44100","duration":"10"}]}"#.utf8).write(to: probe)
        XCTAssertEqual(try MediaEngine.inspect(audio).duration, 10)
    }
}
