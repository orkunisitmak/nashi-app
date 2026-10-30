import XCTest
import CoreGraphics
@testable import NashiCore

/// The player shows the last frame from its start to the end of the file, including an audio tail.
final class MediaSnapshotEndTests: XCTestCase {
    func testPlayheadAfterTheLastFrameStartSavesTheLastFrame() throws {
        let directory = try OutputFiles.temporaryDirectory()
        defer { try? FileManager.default.removeItem(at: directory) }
        let ffmpeg = try Dependencies.executable("ffmpeg")
        let mp4 = directory.appendingPathComponent("Clip.mp4"), mkv = directory.appendingPathComponent("Clip.mkv")
        // 25 fps video ends at 2 s (last frame starts at 1.96 s); the audio runs to 2.5 s.
        try ProcessRunner.run(ffmpeg, ["-hide_banner", "-v", "error", "-nostdin", "-n", "-f", "lavfi", "-i", "testsrc=size=128x96:rate=25:duration=2", "-f", "lavfi", "-i", "sine=duration=2.5:sample_rate=48000", "-c:v", "libx264", "-pix_fmt", "yuv420p", "-c:a", "aac", mp4.path])
        // Matroska stores no video stream duration, so the end is found by decoding.
        try ProcessRunner.run(ffmpeg, ["-hide_banner", "-v", "error", "-nostdin", "-n", "-i", mp4.path, "-c", "copy", mkv.path])
        for input in [mp4, mkv] {
            let ext = input.pathExtension
            // Independent references: the frame ffmpeg finds at 1 s, and the last frame of a full decode.
            let middle = directory.appendingPathComponent("\(ext) middle.png"), last = directory.appendingPathComponent("\(ext) last.png")
            try ProcessRunner.run(ffmpeg, ["-hide_banner", "-v", "error", "-nostdin", "-n", "-ss", "1", "-i", input.path, "-map", "0:v:0", "-frames:v", "1", "-c:v", "png", middle.path])
            try ProcessRunner.run(ffmpeg, ["-hide_banner", "-v", "error", "-nostdin", "-n", "-i", input.path, "-map", "0:v:0", "-update", "1", "-c:v", "png", last.path])
            var options = OperationOptions(); options.snapshotTimes = [1, 1.97, 1.99, 2.3, try MediaEngine.inspect(input).duration]
            options.outputDirectory = directory.appendingPathComponent("\(ext) frames")
            let frames = try MediaEngine.tool(.videoSnapshots, inputs: [input], options: options, token: .init())
            XCTAssertEqual(frames.count, 5, ext)
            XCTAssertEqual(try pixels(frames[0]), try pixels(middle), "\(ext) mid-video snapshot moved")
            for frame in frames.dropFirst() { XCTAssertEqual(try pixels(frame), try pixels(last), "\(ext) \(frame.lastPathComponent)") }
            XCTAssertNotEqual(try pixels(middle), try pixels(last), ext)
        }
    }

    private func pixels(_ url: URL) throws -> [UInt8] {
        let image = try ImageEngine.load(url)
        var bytes = [UInt8](repeating: 0, count: image.width * image.height * 4)
        try bytes.withUnsafeMutableBytes { buffer in
            let context = try XCTUnwrap(CGContext(data: buffer.baseAddress, width: image.width, height: image.height, bitsPerComponent: 8, bytesPerRow: image.width * 4, space: CGColorSpaceCreateDeviceRGB(), bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue))
            context.draw(image, in: CGRect(x: 0, y: 0, width: image.width, height: image.height))
        }
        return bytes
    }
}
