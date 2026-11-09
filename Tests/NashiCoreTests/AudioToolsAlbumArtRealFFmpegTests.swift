import XCTest
@testable import NashiCore

/// Real FFmpeg encodes: the cover must survive every audio tool, including a trim that starts after 0.
final class AudioToolsAlbumArtRealFFmpegTests: XCTestCase {
    private func song(_ ext: String, in directory: URL) throws -> URL {
        let cover = directory.appendingPathComponent("cover.jpg"), out = directory.appendingPathComponent("Song.\(ext)")
        let ffmpeg = try Dependencies.executable("ffmpeg")
        try ProcessRunner.run(ffmpeg, ["-v", "error", "-f", "lavfi", "-i", "testsrc2=size=64x64:rate=1:duration=1", "-frames:v", "1", cover.path])
        let codec: [String] = ext == "m4a" ? ["-c:a", "aac"] : ext == "flac" ? ["-c:a", "flac"] : ["-c:a", "libmp3lame"]
        try ProcessRunner.run(ffmpeg, ["-v", "error", "-f", "lavfi", "-i", "sine=frequency=440:duration=30", "-i", cover.path, "-map", "0:a", "-map", "1:v", "-c:v", "copy", "-disposition:v", "attached_pic"] + codec + [out.path])
        return out
    }

    func testEveryAudioToolKeepsTheCoverIncludingATrimThatStartsLater() throws {
        for ext in ["mp3", "m4a", "flac"] {
            let directory = try OutputFiles.temporaryDirectory(); defer { try? FileManager.default.removeItem(at: directory) }
            let input = try song(ext, in: directory)
            XCTAssertTrue(try MediaEngine.inspect(input).streams.contains { $0.isAttachedPicture }, ext)
            let cases: [(ToolKind, (inout OperationOptions) -> Void)] = [
                (.trimAudio, { $0.start = 5; $0.end = 20 }),
                (.trimAudio, { $0.start = 0; $0.end = 20 }),
                (.trimAudio, { $0.start = 5; $0.end = 20; $0.trimSilence = true }),
                (.normalizeAudio, { _ in }),
                (.audioChannels, { $0.channels = 1 }),
            ]
            for (tool, setup) in cases {
                var options = OperationOptions(); options.outputDirectory = directory.appendingPathComponent("out-\(UUID().uuidString)"); setup(&options)
                let output = try XCTUnwrap(try MediaEngine.tool(tool, inputs: [input], options: options, token: .init()).first)
                let info = try MediaEngine.inspect(output)
                XCTAssertTrue(info.streams.contains { $0.isAttachedPicture }, "\(tool) \(ext) lost the cover")
                if tool == .trimAudio, !options.trimSilence { XCTAssertEqual(info.duration, (options.end ?? 0) - (options.start ?? 0), accuracy: 0.3, "\(ext) trim length") }
            }
        }
    }
}
