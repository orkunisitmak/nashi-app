import XCTest
@testable import NashiCore

/// Raw ADTS AAC stores no length, so ffprobe guesses one from the bitrate and warns about it.
/// Runs through the NASHI_FFMPEG/NASHI_FFPROBE seam, without a real ffmpeg.
final class MediaEstimatedDurationTests: XCTestCase {
    private static let estimate = "[aac @ 0x1] Estimating duration from bitrate, this may be inaccurate"
    private static let measured = "out_time_us=N/A\nprogress=continue\nout_time_us=10030000\nprogress=end\n"

    /// `measurement` is the shell the fake ffmpeg runs for the null-muxer call; any other call writes its output.
    private func fixture(warning: String?, measurement: String) throws -> (input: URL, log: URL, root: URL) {
        let root = FileManager.default.temporaryDirectory.appendingPathComponent("nashi-estimated-duration-\(UUID().uuidString)", isDirectory: true)
        try FileManager.default.createDirectory(at: root, withIntermediateDirectories: true)
        addTeardownBlock { try? FileManager.default.removeItem(at: root) }
        let log = root.appendingPathComponent("argv.log")
        let ffprobe = root.appendingPathComponent("ffprobe"), ffmpeg = root.appendingPathComponent("ffmpeg")
        let probe = #"{"format":{"format_name":"aac","duration":"4.48"},"streams":[{"index":0,"codec_type":"audio","codec_name":"aac","channels":1,"sample_rate":"48000","duration":"4.48"}]}"#
        try Data("#!/bin/sh\n\(warning.map { "printf '%s\\n' '\($0)' >&2\n" } ?? "")printf '%s' '\(probe)'\n".utf8).write(to: ffprobe)
        try Data("#!/bin/sh\nfor a in \"$@\"; do printf '%s\\n' \"$a\" >> '\(log.path)'; done\nprintf '%s\\n' --END-- >> '\(log.path)'\nfor last; do :; done\nif [ \"$last\" = - ]; then\n\(measurement)\nelse printf x > \"$last\"; fi\n".utf8).write(to: ffmpeg)
        for (variable, executable) in [("NASHI_FFPROBE", ffprobe), ("NASHI_FFMPEG", ffmpeg)] {
            try FileManager.default.setAttributes([.posixPermissions: 0o755], ofItemAtPath: executable.path)
            let previous = ProcessInfo.processInfo.environment[variable]
            setenv(variable, executable.path, 1)
            addTeardownBlock { if let previous { setenv(variable, previous, 1) } else { unsetenv(variable) } }
        }
        let input = root.appendingPathComponent("Voice memo.aac"); try Data([1]).write(to: input)
        return (input, log, root)
    }

    private func calls(_ log: URL) -> [[String]] {
        ((try? String(contentsOf: log, encoding: .utf8)) ?? "").components(separatedBy: "--END--\n").filter { !$0.isEmpty }.map { $0.split(separator: "\n").map(String.init) }
    }

    private func printedMeasurement() -> String { "printf '\(Self.measured.replacingOccurrences(of: "\n", with: "\\n"))'" }

    func testEstimatedDurationIsMeasuredWithoutDecoding() throws {
        let (input, log, _) = try fixture(warning: Self.estimate, measurement: printedMeasurement())
        XCTAssertEqual(try MediaEngine.inspect(input).duration, 10.03, accuracy: 0.0001)
        let call = try XCTUnwrap(calls(log).first)
        XCTAssertEqual(Array(call.suffix(9)), ["-i", input.path, "-map", "0", "-c", "copy", "-f", "null", "-"])
    }

    func testTrimAndBleepReachPastTheEstimate() throws {
        let (input, log, root) = try fixture(warning: Self.estimate, measurement: printedMeasurement())
        var trim = OperationOptions(); trim.outputDirectory = root.appendingPathComponent("trim"); trim.start = 1; trim.end = 8
        XCTAssertEqual(try MediaEngine.tool(.trimAudio, inputs: [input], options: trim, token: .init()).count, 1)
        var toEnd = OperationOptions(); toEnd.outputDirectory = root.appendingPathComponent("end"); toEnd.start = 0.5
        XCTAssertEqual(try MediaEngine.tool(.trimAudio, inputs: [input], options: toEnd, token: .init()).count, 1)
        let render = try XCTUnwrap(calls(log).last { $0.last != "-" })
        // The end of the trim is the measured end, not the guess.
        XCTAssertEqual(try XCTUnwrap(Double(render[try XCTUnwrap(render.firstIndex(of: "-t")) + 1])), 9.53, accuracy: 0.0001)
        var bleep = OperationOptions(); bleep.outputDirectory = root.appendingPathComponent("bleep"); bleep.audioRedactions = [.init(start: 7, end: 8)]
        XCTAssertEqual(try MediaEngine.tool(.redactAudio, inputs: [input], options: bleep, token: .init()).count, 1)
    }

    func testStoredDurationRunsNoMeasurement() throws {
        for warning in [nil, "[aac @ 0x1] Some other warning"] as [String?] {
            let (input, log, _) = try fixture(warning: warning, measurement: printedMeasurement())
            XCTAssertEqual(try MediaEngine.inspect(input).duration, 4.48, accuracy: 0.0001)
            XCTAssertTrue(calls(log).isEmpty)
        }
    }

    func testFailedMeasurementKeepsTheEstimate() throws {
        for measurement in ["echo 'Invalid data found' >&2; exit 1", "printf 'out_time_us=N/A\\nprogress=end\\n'", "printf 'out_time_us=0\\nprogress=end\\n'", "printf 'out_time_us=inf\\nprogress=end\\n'", ":"] {
            let (input, log, _) = try fixture(warning: Self.estimate, measurement: measurement)
            XCTAssertEqual(try MediaEngine.inspect(input).duration, 4.48, accuracy: 0.0001, measurement)
            XCTAssertEqual(calls(log).count, 1, measurement)
        }
    }

    /// The report's fixture with the real tools: 4 s of tone and 6 s of quiet, which ffprobe guesses as about 4.5 s.
    func testVoiceMemoKeepsItsEndWithRealTools() throws {
        let directory = try OutputFiles.temporaryDirectory()
        defer { try? FileManager.default.removeItem(at: directory) }
        let memo = directory.appendingPathComponent("Voice memo.aac")
        try ProcessRunner.run(Dependencies.executable("ffmpeg"), ["-hide_banner", "-v", "error", "-nostdin", "-n", "-f", "lavfi", "-i", "sine=frequency=440:duration=4,apad=whole_dur=10", "-c:a", "aac", "-b:a", "128k", memo.path])
        XCTAssertEqual(try MediaEngine.inspect(memo).duration, 10, accuracy: 0.1)
        var range = OperationOptions(); range.outputDirectory = directory.appendingPathComponent("range"); range.start = 1; range.end = 8
        let trimmed = try XCTUnwrap(MediaEngine.tool(.trimAudio, inputs: [memo], options: range, token: .init()).first)
        XCTAssertEqual(try MediaEngine.inspect(trimmed).duration, 7, accuracy: 0.1)
        var toEnd = OperationOptions(); toEnd.outputDirectory = directory.appendingPathComponent("end"); toEnd.start = 0.5
        let tail = try XCTUnwrap(MediaEngine.tool(.trimAudio, inputs: [memo], options: toEnd, token: .init()).first)
        XCTAssertEqual(try MediaEngine.inspect(tail).duration, 9.5, accuracy: 0.1)
    }

    func testCancellationReachesTheMeasurement() throws {
        let (input, log, root) = try fixture(warning: Self.estimate, measurement: "exec sleep 30")
        let token = CancellationToken()
        let started = expectation(description: "measurement started")
        DispatchQueue.global().async { [self] in
            while calls(log).isEmpty { Thread.sleep(forTimeInterval: 0.02) }
            started.fulfill(); token.cancel()
        }
        let begin = Date()
        XCTAssertThrowsError(try MediaEngine.inspect(input, token: token)) { guard case NashiError.cancelled = $0 else { return XCTFail("\($0)") } }
        wait(for: [started], timeout: 5)
        XCTAssertLessThan(Date().timeIntervalSince(begin), 10, root.path)
    }
}
