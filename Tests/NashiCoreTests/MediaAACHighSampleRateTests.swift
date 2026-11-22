import XCTest
@testable import NashiCore

/// Normalize and Bleep keep the source rate with -ar. The AAC encoder stops at
/// 96 kHz, so a hi-res ALAC .m4a must still produce an AAC copy.
final class MediaAACHighSampleRateTests: XCTestCase {
    func testNormalizeAndBleepAcceptHiResALACM4A() throws {
        let directory = try OutputFiles.temporaryDirectory()
        defer { try? FileManager.default.removeItem(at: directory) }
        for sampleRate in [176400, 192000] {
            let input = directory.appendingPathComponent("Hi-Res \(sampleRate).m4a")
            try ProcessRunner.run(Dependencies.executable("ffmpeg"), ["-v", "error", "-f", "lavfi", "-i", "sine=frequency=440:sample_rate=\(sampleRate):duration=1", "-ac", "2", "-c:a", "alac", input.path])
            XCTAssertEqual(try MediaEngine.inspect(input).audio?.sampleRate, sampleRate)
            var options = OperationOptions(); options.outputDirectory = directory
            options.audioRedactions = [TimeRange(start: 0.2, end: 0.4)]
            for kind in [ToolKind.normalizeAudio, .redactAudio] {
                let output = try XCTUnwrap(MediaEngine.tool(kind, inputs: [input], options: options, token: .init()).first, "\(kind) \(sampleRate)")
                let audio = try XCTUnwrap(MediaEngine.inspect(output).audio)
                XCTAssertEqual(audio.codec, "aac", "\(kind) \(sampleRate)")
                XCTAssertEqual(audio.sampleRate, 96000, "\(kind) \(sampleRate)")
                XCTAssertEqual(audio.channels, 2, "\(kind) \(sampleRate)")
            }
        }
    }
}
