import XCTest
@testable import NashiCore

final class MediaInfoAttachedPictureTests: XCTestCase {
    private func inspect(_ probeJSON: String) throws -> MediaInfo {
        let root = FileManager.default.temporaryDirectory.appendingPathComponent("nashi-attached-pic-\(UUID().uuidString)", isDirectory: true)
        try FileManager.default.createDirectory(at: root, withIntermediateDirectories: true)
        addTeardownBlock { try? FileManager.default.removeItem(at: root) }
        let probe = root.appendingPathComponent("probe.json")
        try Data(probeJSON.utf8).write(to: probe)
        let ffprobe = root.appendingPathComponent("ffprobe")
        try Data("#!/bin/sh\ncat '\(probe.path)'\n".utf8).write(to: ffprobe)
        try FileManager.default.setAttributes([.posixPermissions: 0o755], ofItemAtPath: ffprobe.path)
        let previous = ProcessInfo.processInfo.environment["NASHI_FFPROBE"]
        setenv("NASHI_FFPROBE", ffprobe.path, 1)
        addTeardownBlock { if let previous { setenv("NASHI_FFPROBE", previous, 1) } else { unsetenv("NASHI_FFPROBE") } }
        let input = root.appendingPathComponent("Track.mp3")
        try Data([1]).write(to: input)
        return try MediaEngine.inspect(input, token: .init())
    }

    func testAttachedPictureIsNotReportedAsVideoTrack() throws {
        let info = try inspect(#"{"format":{"duration":"12","format_name":"mp3"},"streams":[{"index":0,"codec_type":"audio","codec_name":"mp3","channels":2,"sample_rate":"44100"},{"index":1,"codec_type":"video","codec_name":"mjpeg","width":600,"height":600,"disposition":{"attached_pic":1}}]}"#)
        XCTAssertTrue(info.hasAudio)
        XCTAssertFalse(info.hasVideo)
        XCTAssertNil(info.video)
        XCTAssertTrue(info.streams.contains { $0.isAttachedPicture })
    }

    func testAttachedPictureBeforeMainVideoDoesNotShadowRealVideoStream() throws {
        let info = try inspect(#"{"format":{"duration":"12","format_name":"mp4"},"streams":[{"index":0,"codec_type":"video","codec_name":"mjpeg","width":600,"height":600,"disposition":{"attached_pic":1}},{"index":1,"codec_type":"video","codec_name":"h264","width":1920,"height":1080,"disposition":{"attached_pic":0}},{"index":2,"codec_type":"audio","codec_name":"aac","channels":2,"sample_rate":"48000"}]}"#)
        XCTAssertTrue(info.hasVideo)
        XCTAssertEqual(info.video?.index, 1)
        XCTAssertEqual(info.video?.codec, "h264")
        XCTAssertEqual(info.video?.width, 1920)
        XCTAssertEqual(info.video?.height, 1080)
    }
}
