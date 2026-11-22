import XCTest
@testable import NashiCore

/// Records the per-clip ffmpeg arguments of Join Videos through the NASHI_FFMPEG/NASHI_FFPROBE seam.
/// Live recordings can lack a container duration, which inspection reports as 0.
final class MediaJoinUnknownDurationTests: XCTestCase {
    private func clipArguments(_ probes: [(String, String)]) throws -> [[String]] {
        let root = FileManager.default.temporaryDirectory.appendingPathComponent("nashi-join-duration-\(UUID().uuidString)", isDirectory: true)
        try FileManager.default.createDirectory(at: root, withIntermediateDirectories: true)
        addTeardownBlock { try? FileManager.default.removeItem(at: root) }
        let log = root.appendingPathComponent("argv.log")
        let ffprobe = root.appendingPathComponent("ffprobe"), ffmpeg = root.appendingPathComponent("ffmpeg")
        try Data("#!/bin/sh\nfor last; do :; done\ncat \"$last.json\"\n".utf8).write(to: ffprobe)
        try Data("#!/bin/sh\nfor a in \"$@\"; do printf '%s\\n' \"$a\" >> '\(log.path)'; done\nprintf '%s\\n' --END-- >> '\(log.path)'\nfor last; do :; done\nprintf x > \"$last\"\n".utf8).write(to: ffmpeg)
        for (variable, executable) in [("NASHI_FFPROBE", ffprobe), ("NASHI_FFMPEG", ffmpeg)] {
            try FileManager.default.setAttributes([.posixPermissions: 0o755], ofItemAtPath: executable.path)
            let previous = ProcessInfo.processInfo.environment[variable]
            setenv(variable, executable.path, 1)
            addTeardownBlock { if let previous { setenv(variable, previous, 1) } else { unsetenv(variable) } }
        }
        let inputs = try probes.map { name, probe in
            let input = root.appendingPathComponent(name); try Data([1]).write(to: input)
            try Data(probe.utf8).write(to: URL(fileURLWithPath: input.path + ".json"))
            return input
        }
        var options = OperationOptions(); options.outputDirectory = root.appendingPathComponent("out")
        _ = try MediaEngine.tool(.joinVideos, inputs: inputs, options: options, token: .init())
        let calls = try String(contentsOf: log, encoding: .utf8).components(separatedBy: "--END--\n").filter { !$0.isEmpty }.map { $0.split(separator: "\n").map(String.init) }
        return Array(calls.prefix(probes.count))
    }

    private func clip(duration: String?, audio: Bool) -> String {
        let d = duration.map { ",\"duration\":\"\($0)\"" } ?? ""
        let a = audio ? #",{"index":1,"codec_type":"audio","codec_name":"aac","channels":2,"sample_rate":"48000"}"# : ""
        return "{\"format\":{\(d.dropFirst())},\"streams\":[{\"index\":0,\"codec_type\":\"video\",\"codec_name\":\"h264\",\"width\":1280,\"height\":720,\"avg_frame_rate\":\"30/1\"}\(a)]}"
    }

    private func bound(_ call: [String]) -> String? { call.firstIndex(of: "-t").map { call[$0 + 1] } }

    func testClipWithUnknownDurationIsNotCutToZero() throws {
        let calls = try clipArguments([("Intro.mp4", clip(duration: "4", audio: true)), ("Recording.mkv", clip(duration: nil, audio: false)), ("Outro.mkv", clip(duration: nil, audio: true))])
        XCTAssertEqual(calls.count, 3)
        XCTAssertEqual(bound(calls[0]), "4")
        XCTAssertFalse(calls[0].contains("-shortest"))
        // `-t 0` stops the output before its first frame; the video bounds the generated silence instead.
        XCTAssertNil(bound(calls[1]))
        XCTAssertTrue(calls[1].contains { $0.hasPrefix("anullsrc") })
        XCTAssertTrue(calls[1].contains("-shortest"))
        XCTAssertNil(bound(calls[2]))
        XCTAssertFalse(calls[2].contains("-shortest"))
    }
}
