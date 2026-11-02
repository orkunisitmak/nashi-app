import XCTest
@testable import NashiCore

/// A fake ffmpeg (NASHI_FFMPEG) records each command line and writes outputs whose sizes come from a list.
final class MediaCompressAttemptTests: XCTestCase {
    private func compress(_ name: String, sizes: [Int], target: Int64) throws -> (commands: [String], error: Error?) {
        let root = FileManager.default.temporaryDirectory.appendingPathComponent("nashi-compress-\(UUID().uuidString)", isDirectory: true)
        try FileManager.default.createDirectory(at: root, withIntermediateDirectories: true)
        defer { try? FileManager.default.removeItem(at: root) }
        let probe = root.appendingPathComponent("probe.json"), list = root.appendingPathComponent("sizes"), count = root.appendingPathComponent("count"), log = root.appendingPathComponent("argv.log")
        try Data(#"{"format":{"duration":"600"},"streams":[{"index":0,"codec_type":"video","codec_name":"h264","width":1920,"height":1080},{"index":1,"codec_type":"audio","codec_name":"aac","channels":2,"sample_rate":"48000"}]}"#.utf8).write(to: probe)
        try Data(sizes.map { "\($0)\n" }.joined().utf8).write(to: list)
        let ffprobe = root.appendingPathComponent("ffprobe"), ffmpeg = root.appendingPathComponent("ffmpeg")
        try Data("#!/bin/sh\ncat '\(probe.path)'\n".utf8).write(to: ffprobe)
        try Data("""
        #!/bin/sh
        for last; do :; done
        line=""; for a in "$@"; do [ "$a" = "$last" ] || line="$line $a"; done
        printf '%s\\n' "$line" >> '\(log.path)'
        [ "$last" = /dev/null ] && exit 0
        n=$(cat '\(count.path)' 2>/dev/null || echo 0); n=$((n+1)); echo $n > '\(count.path)'
        size=$(sed -n "${n}p" '\(list.path)'); head -c "${size:-40000000}" /dev/zero > "$last"
        """.utf8).write(to: ffmpeg)
        for (variable, tool) in [("NASHI_FFPROBE", ffprobe), ("NASHI_FFMPEG", ffmpeg)] {
            try FileManager.default.setAttributes([.posixPermissions: 0o755], ofItemAtPath: tool.path)
            let previous = ProcessInfo.processInfo.environment[variable]
            setenv(variable, tool.path, 1)
            addTeardownBlock { if let previous { setenv(variable, previous, 1) } else { unsetenv(variable) } }
        }
        let input = root.appendingPathComponent(name); try Data(count: 50_000_000).write(to: input)
        var options = OperationOptions(); options.targetBytes = target
        options.outputDirectory = root.appendingPathComponent("out")
        var error: Error?
        do { _ = try MediaEngine.tool(.compress, inputs: [input], options: options, token: .init()) } catch let failure { error = failure }
        return (((try? String(contentsOf: log, encoding: .utf8)) ?? "").split(separator: "\n").map(String.init), error)
    }

    func testUnreachableTargetDoesNotRepeatAnIdenticalAttempt() throws {
        // Every bitrate is already at its floor, so a second attempt would encode the same file again.
        let floored = try compress("Clip.mp4", sizes: [], target: 100_000)
        XCTAssertEqual(floored.commands.count, 2, floored.commands.joined(separator: "\n"))
        guard case NashiError.failed(let message)? = floored.error else { return XCTFail("\(String(describing: floored.error))") }
        XCTAssertTrue(message.contains("cannot be reached"))

        let reachable = try compress("Clip.mp4", sizes: [9_000_000, 1_000], target: 6_000_000)
        XCTAssertNil(reachable.error)
        XCTAssertEqual(reachable.commands.map { $0.contains(" -pass 1 ") }, [true, false, true, false])
    }
}
