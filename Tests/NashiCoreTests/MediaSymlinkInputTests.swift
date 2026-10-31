import XCTest
@testable import NashiCore

/// Media given as a symbolic link used to be checked and sized as the link itself:
/// every media tool refused it as "not a readable local media file", and its size was a few bytes.
final class MediaSymlinkInputTests: XCTestCase {
    func testSymlinkedMediaIsInspectedAndCompressedAsTheLinkedFile() throws {
        let root = try OutputFiles.temporaryDirectory(); defer { try? FileManager.default.removeItem(at: root) }
        let tools = root.appendingPathComponent("tools"), media = root.appendingPathComponent("media"), links = root.appendingPathComponent("links")
        for directory in [tools, media, links] { try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true) }
        let probe = #"{"format":{"duration":"10","format_name":"mp3"},"streams":[{"index":0,"codec_type":"audio","codec_name":"mp3","channels":2,"sample_rate":"44100","duration":"10"}]}"#
        try Data("#!/bin/sh\necho '\(probe)'\n".utf8).write(to: tools.appendingPathComponent("ffprobe"))
        try Data("#!/bin/sh\nfor a; do last=\"$a\"; done\nhead -c 1000 /dev/zero | tr '\\0' 'x' > \"$last\"\n".utf8).write(to: tools.appendingPathComponent("ffmpeg"))
        for name in ["ffprobe", "ffmpeg"] {
            let tool = tools.appendingPathComponent(name), variable = "NASHI_" + name.uppercased()
            try FileManager.default.setAttributes([.posixPermissions: 0o755], ofItemAtPath: tool.path)
            let previous = ProcessInfo.processInfo.environment[variable]
            setenv(variable, tool.path, 1)
            addTeardownBlock { if let previous { setenv(variable, previous, 1) } else { unsetenv(variable) } }
        }
        let song = media.appendingPathComponent("Song.mp3"); try Data(repeating: 7, count: 100_000).write(to: song)
        let link = links.appendingPathComponent("Song.mp3"); try FileManager.default.createSymbolicLink(atPath: link.path, withDestinationPath: "../media/Song.mp3")
        let missing = links.appendingPathComponent("Missing.mp3"); try FileManager.default.createSymbolicLink(atPath: missing.path, withDestinationPath: "../media/none.mp3")

        XCTAssertEqual(try MediaEngine.inspect(link).size, 100_000)
        var options = OperationOptions(); options.percentage = 30; options.outputDirectory = root
        let output = try XCTUnwrap(MediaEngine.tool(.compress, inputs: [link], options: options, token: .init()).first)
        XCTAssertEqual(output.lastPathComponent, "Song Compressed.mp3")
        XCTAssertThrowsError(try MediaEngine.inspect(missing)) { XCTAssertEqual($0.localizedDescription, "Choose a readable local media file.") }
        XCTAssertEqual(try FileManager.default.destinationOfSymbolicLink(atPath: link.path), "../media/Song.mp3")
    }
}
