import XCTest
@testable import NashiCore

final class FeatureCatalogTests: XCTestCase {
    private static let videoInputs = ["mp4", "mov", "mkv", "webm", "avi", "wmv"]
    private static let audioTargets = ["m4a", "wav", "flac", "ogg"]

    func testVideoInputsAdvertiseEveryAudioTarget() {
        for input in Self.videoInputs {
            let targets = FeatureCatalog.conversionMatrix[input] ?? []
            for target in Self.audioTargets {
                XCTAssertTrue(targets.contains(target), "\(input) should offer \(target), got \(targets)")
            }
            XCTAssertTrue(targets.contains("mp3"), "\(input) should still offer mp3, got \(targets)")
        }
    }

    func testVideoFormatsResolveToAudioTargetsForTheWheel() {
        for input in Self.videoInputs {
            let url = URL(fileURLWithPath: "/tmp/nashi-catalog-sample.\(input)")
            let formats = FeatureCatalog.formats(for: [url])
            for target in Self.audioTargets {
                XCTAssertTrue(formats.contains(target), "\(input) formats should include \(target), got \(formats)")
            }
        }
    }

    func testMediaEngineConvertsVideoToAdvertisedAudioTargets() throws {
        let directory = try OutputFiles.temporaryDirectory()
        defer { try? FileManager.default.removeItem(at: directory) }
        let video = directory.appendingPathComponent("Catalog.mp4")
        try ProcessRunner.run(Dependencies.executable("ffmpeg"), [
            "-v", "error",
            "-f", "lavfi", "-i", "testsrc2=size=160x90:rate=24:duration=0.6",
            "-f", "lavfi", "-i", "sine=frequency=440:sample_rate=44100:duration=0.6",
            "-map", "0:v", "-map", "1:a",
            "-c:v", "libx264", "-c:a", "aac",
            video.path,
        ])

        let catalog = FeatureCatalog.conversionMatrix["mp4"] ?? []
        for target in Self.audioTargets where catalog.contains(target) {
            let outputs = try MediaEngine.convert(video, to: target, options: .init(), token: .init())
            let output = try XCTUnwrap(outputs.first)
            XCTAssertEqual(output.pathExtension, target)
            let info = try MediaEngine.inspect(output)
            XCTAssertNotNil(info.audio, "\(target) output must contain an audio stream")
            XCTAssertNil(info.video, "\(target) output must be audio only")
            XCTAssertEqual(info.duration, 0.6, accuracy: 0.15)
        }
    }
}
