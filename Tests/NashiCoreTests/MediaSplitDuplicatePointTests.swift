import XCTest
@testable import NashiCore

/// Split selection with stand-in ffprobe/ffmpeg executables, so segment
/// indexing is checked without depending on real encoder output.
final class MediaSplitDuplicatePointTests: XCTestCase {
    func testRepeatedSplitPointIsRejectedInsteadOfRenumberingRemovedClips() throws {
        let root = try OutputFiles.temporaryDirectory(); defer { try? FileManager.default.removeItem(at: root) }
        let log = root.appendingPathComponent("ffmpeg.log")
        let probe = "#!/bin/sh\nprintf '%s' '{\"format\":{\"duration\":\"4\"},\"streams\":[{\"index\":0,\"codec_type\":\"video\",\"codec_name\":\"h264\",\"width\":64,\"height\":64}]}'\n"
        let mpeg = "#!/bin/sh\nfor a in \"$@\"; do printf '%s\\n' \"$a\"; done >> '\(log.path)'\nfor last; do :; done\nprintf x > \"$last\"\n"
        for (name, body) in [("ffprobe", probe), ("ffmpeg", mpeg)] {
            let tool = root.appendingPathComponent(name)
            try Data(body.utf8).write(to: tool)
            try FileManager.default.setAttributes([.posixPermissions: 0o755], ofItemAtPath: tool.path)
            setenv("NASHI_" + name.uppercased(), tool.path, 1)
        }
        defer { unsetenv("NASHI_FFPROBE"); unsetenv("NASHI_FFMPEG") }
        let folder = root.appendingPathComponent("Source", isDirectory: true)
        try FileManager.default.createDirectory(at: folder, withIntermediateDirectories: false)
        let input = folder.appendingPathComponent("Clip.mp4")
        try Data("source".utf8).write(to: input)
        // Sorted [2, 2] describes segments [0,2], [2,2] and [2,4]. Removing the empty
        // middle one must never drop the retained [2,4] content.
        var options = OperationOptions(); options.splitPoints = [2, 2]; options.discardedClips = [1]
        XCTAssertThrowsError(try MediaEngine.tool(.splitVideo, inputs: [input], options: options, token: .init()))
        XCTAssertFalse(FileManager.default.fileExists(atPath: log.path))
        XCTAssertEqual(try FileManager.default.contentsOfDirectory(atPath: folder.path), ["Clip.mp4"])
        options.splitPoints = [3, 1]; options.discardedClips = [0]
        XCTAssertEqual(try MediaEngine.tool(.splitVideo, inputs: [input], options: options, token: .init()).count, 2)
        let arguments = try String(contentsOf: log, encoding: .utf8).components(separatedBy: "\n")
        XCTAssertEqual(arguments.indices.filter { arguments[$0] == "-ss" }.map { "\(arguments[$0 + 1])+\(arguments[$0 + 3])" }, ["1+2", "3+1"])
    }
}
