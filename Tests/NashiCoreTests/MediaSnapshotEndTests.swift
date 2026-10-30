import XCTest
import CoreGraphics
@testable import NashiCore

/// The player shows the last frame for every playhead after it starts, including an audio tail.
final class MediaSnapshotEndTests: XCTestCase {
    func testPlayheadAfterTheLastFrameStartSavesTheLastFrame() throws {
        let directory = try OutputFiles.temporaryDirectory()
        defer { try? FileManager.default.removeItem(at: directory) }
        let input = directory.appendingPathComponent("Clip.mp4")
        // 25 fps video ends at 2 s (last frame starts at 1.96 s); the audio runs to 2.5 s.
        try ProcessRunner.run(try Dependencies.executable("ffmpeg"), ["-hide_banner", "-v", "error", "-nostdin", "-n", "-f", "lavfi", "-i", "testsrc=size=128x96:rate=25:duration=2", "-f", "lavfi", "-i", "sine=duration=2.5:sample_rate=48000", "-c:v", "libx264", "-pix_fmt", "yuv420p", "-c:a", "aac", input.path])
        var options = OperationOptions(); options.snapshotTimes = [1, 1.97, 1.99, 2.3]
        options.outputDirectory = directory.appendingPathComponent("out")
        let frames = try MediaEngine.tool(.videoSnapshots, inputs: [input], options: options, token: .init())
        XCTAssertEqual(frames.count, 4)
        for frame in frames { XCTAssertEqual(try ImageEngine.load(frame).width, 128, frame.lastPathComponent) }
        // Every playhead from 1.96 s on shows the same last frame; a playhead mid-video is unchanged.
        let pixels = try frames.map { try self.pixels($0) }
        XCTAssertEqual(pixels[1], pixels[2]); XCTAssertEqual(pixels[2], pixels[3]); XCTAssertNotEqual(pixels[0], pixels[1])
        let direct = directory.appendingPathComponent("Direct.png")
        try ProcessRunner.run(try Dependencies.executable("ffmpeg"), ["-hide_banner", "-v", "error", "-nostdin", "-n", "-ss", "1", "-i", input.path, "-map", "0:v:0", "-frames:v", "1", "-c:v", "png", direct.path])
        XCTAssertEqual(pixels[0], try self.pixels(direct))
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
