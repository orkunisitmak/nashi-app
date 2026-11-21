import XCTest
@testable import NashiCore

final class ImageResourceLimitRegressionTests: XCTestCase {
    func testSVGLoadsViaVectorFallbackWhenImageIODimensionsAreUnavailable() throws {
        let directory = try OutputFiles.temporaryDirectory()
        defer { try? FileManager.default.removeItem(at: directory) }
        let svg = directory.appendingPathComponent("small.svg")
        try "<svg xmlns='http://www.w3.org/2000/svg' width='48' height='24' viewBox='0 0 48 24'><rect width='48' height='24' fill='blue'/></svg>"
            .write(to: svg, atomically: true, encoding: .utf8)

        let image = try ImageEngine.load(svg)
        XCTAssertEqual(image.width, 48)
        XCTAssertEqual(image.height, 24)
    }

    func testDeclaredPixelBudgetRejectsLargeVectorWithoutAllocatingItsPixels() throws {
        let directory = try OutputFiles.temporaryDirectory()
        defer { try? FileManager.default.removeItem(at: directory) }
        let svg = directory.appendingPathComponent("oversized.svg")
        // 11,000 x 10,000 > 100 MP, but neither edge exceeds 32,000.
        try "<svg xmlns='http://www.w3.org/2000/svg' width='11000' height='10000' viewBox='0 0 1 1'><rect width='1' height='1' fill='red'/></svg>"
            .write(to: svg, atomically: true, encoding: .utf8)

        XCTAssertThrowsError(try ImageEngine.load(svg))
    }

    func testDeclaredSideBudgetRejectsLargeVectorWithoutAllocatingItsPixels() throws {
        let directory = try OutputFiles.temporaryDirectory()
        defer { try? FileManager.default.removeItem(at: directory) }
        let svg = directory.appendingPathComponent("wide.svg")
        try "<svg xmlns='http://www.w3.org/2000/svg' width='32001' height='1' viewBox='0 0 1 1'><rect width='1' height='1'/></svg>"
            .write(to: svg, atomically: true, encoding: .utf8)

        XCTAssertThrowsError(try ImageEngine.load(svg))
    }

    func testFFmpegRefusesOversizedSourceBeforeStartingDecode() throws {
        let directory = try OutputFiles.temporaryDirectory()
        let previousProbe = ProcessInfo.processInfo.environment["NASHI_FFPROBE"]
        let previousFFmpeg = ProcessInfo.processInfo.environment["NASHI_FFMPEG"]
        defer {
            try? FileManager.default.removeItem(at: directory)
            if let previousProbe { setenv("NASHI_FFPROBE", previousProbe, 1) } else { unsetenv("NASHI_FFPROBE") }
            if let previousFFmpeg { setenv("NASHI_FFMPEG", previousFFmpeg, 1) } else { unsetenv("NASHI_FFMPEG") }
        }

        let input = directory.appendingPathComponent("unknown.img")
        try "not an image".write(to: input, atomically: true, encoding: .utf8)
        let probe = directory.appendingPathComponent("ffprobe")
        try "#!/bin/sh\nprintf '%s\\n' '{\"streams\":[{\"width\":40000,\"height\":100}]}'\n"
            .write(to: probe, atomically: true, encoding: .utf8)
        let marker = directory.appendingPathComponent("decoder-was-called")
        let ffmpeg = directory.appendingPathComponent("ffmpeg")
        try "#!/bin/sh\nprintf called > '\(marker.path)'\nexit 1\n"
            .write(to: ffmpeg, atomically: true, encoding: .utf8)
        try FileManager.default.setAttributes([.posixPermissions: 0o755], ofItemAtPath: probe.path)
        try FileManager.default.setAttributes([.posixPermissions: 0o755], ofItemAtPath: ffmpeg.path)
        setenv("NASHI_FFPROBE", probe.path, 1)
        setenv("NASHI_FFMPEG", ffmpeg.path, 1)

        XCTAssertThrowsError(try ImageEngine.loadThumbnail(input))
        XCTAssertFalse(FileManager.default.fileExists(atPath: marker.path),
                       "An oversized image must be refused before FFmpeg can decode a full frame")

        // A bounded source must still reach the decoder, even if this fake
        // decoder exits with an error rather than producing a PNG.
        try "#!/bin/sh\nprintf '%s\\n' '{\"streams\":[{\"width\":1920,\"height\":1080}]}'\n"
            .write(to: probe, atomically: true, encoding: .utf8)
        try FileManager.default.setAttributes([.posixPermissions: 0o755], ofItemAtPath: probe.path)
        XCTAssertThrowsError(try ImageEngine.loadThumbnail(input))
        XCTAssertTrue(FileManager.default.fileExists(atPath: marker.path),
                      "A bounded source should reach the fallback decoder")
    }
}
