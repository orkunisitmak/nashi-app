import XCTest
@testable import NashiCore

/// libopus only accepts 3-8 channel audio in Vorbis channel order, while FLAC and
/// AC-3 decode 5.1 as 5.1(side). Records the argv through the NASHI_FFMPEG/NASHI_FFPROBE seam.
final class MediaOpusSurroundLayoutTests: XCTestCase {
    private func arguments(_ name: String, channels: Int, layout: String, video: Bool = false, _ run: (URL, OperationOptions) throws -> Void) throws -> [String] {
        let root = FileManager.default.temporaryDirectory.appendingPathComponent("nashi-opus-layout-\(UUID().uuidString)", isDirectory: true)
        try FileManager.default.createDirectory(at: root, withIntermediateDirectories: true)
        addTeardownBlock { try? FileManager.default.removeItem(at: root) }
        let log = root.appendingPathComponent("argv.log"), probe = root.appendingPathComponent("probe.json")
        let picture = video ? #"{"index":0,"codec_type":"video","codec_name":"h264","width":1280,"height":720,"avg_frame_rate":"24/1"},"# : ""
        try Data(#"{"format":{"duration":"60"},"streams":[\#(picture){"index":\#(video ? 1 : 0),"codec_type":"audio","codec_name":"flac","channels":\#(channels),"channel_layout":"\#(layout)","sample_rate":"48000"}]}"#.utf8).write(to: probe)
        let ffprobe = root.appendingPathComponent("ffprobe"), ffmpeg = root.appendingPathComponent("ffmpeg")
        try Data("#!/bin/sh\ncat '\(probe.path)'\n".utf8).write(to: ffprobe)
        try Data("#!/bin/sh\n: > '\(log.path)'\nfor a in \"$@\"; do printf '%s\\n' \"$a\" >> '\(log.path)'; done\nfor last; do :; done\nprintf x > \"$last\"\n".utf8).write(to: ffmpeg)
        for (variable, tool) in [("NASHI_FFPROBE", ffprobe), ("NASHI_FFMPEG", ffmpeg)] {
            try FileManager.default.setAttributes([.posixPermissions: 0o755], ofItemAtPath: tool.path)
            let previous = ProcessInfo.processInfo.environment[variable]
            setenv(variable, tool.path, 1)
            addTeardownBlock { if let previous { setenv(variable, previous, 1) } else { unsetenv(variable) } }
        }
        let input = root.appendingPathComponent(name); try Data(repeating: 1, count: 4096).write(to: input)
        var options = OperationOptions(); options.outputDirectory = root.appendingPathComponent("out")
        try run(input, options)
        return try String(contentsOf: log, encoding: .utf8).split(separator: "\n").map(String.init)
    }

    private func layout(_ arguments: [String]) -> String? { arguments.lastIndex(of: "-ch_layout").map { arguments[$0 + 1] } }

    func testOpusOutputsUseAVorbisChannelLayout() throws {
        let convert: (String) -> (URL, OperationOptions) throws -> Void = { format in { input, options in _ = try MediaEngine.convert(input, to: format, options: options, token: .init()) } }
        XCTAssertEqual(layout(try arguments("Surround.flac", channels: 6, layout: "5.1(side)", convert("opus"))), "5.1")
        XCTAssertEqual(layout(try arguments("Five.flac", channels: 5, layout: "5.0(side)", convert("opus"))), "5.0")
        XCTAssertEqual(layout(try arguments("Movie.mkv", channels: 6, layout: "5.1(side)", video: true, convert("webm"))), "5.1")
        for (channels, expected) in [3: "3.0", 4: "quad", 7: "6.1", 8: "7.1"] {
            XCTAssertEqual(layout(try arguments("Multi.wav", channels: channels, layout: "\(channels)c", convert("opus"))), expected)
        }
        XCTAssertNil(layout(try arguments("Stereo.flac", channels: 2, layout: "stereo", convert("opus"))))
        XCTAssertNil(layout(try arguments("Surround.flac", channels: 6, layout: "5.1(side)", convert("m4a"))))
        // A small Compress target still downmixes Opus to mono instead of keeping 5.1.
        let small = try arguments("Talk.opus", channels: 6, layout: "5.1") { input, options in
            var options = options; options.targetBytes = 100_000
            _ = try MediaEngine.tool(.compress, inputs: [input], options: options, token: .init())
        }
        XCTAssertNil(layout(small), small.joined(separator: " "))
        XCTAssertEqual(small.lastIndex(of: "-ac").map { small[$0 + 1] }, "1")
    }
}
