import XCTest
@testable import NashiCore

// SIGTERM to nashi-cli while ffmpeg runs must stop ffmpeg and remove the hidden staging folder.
// The fake ffmpeg announces itself on one FIFO and blocks on another; ordering is
// gated by those FIFOs. The 30 s waits are upper bounds only, never ordering.
final class CommandLineInterruptionTests: XCTestCase {
    private var root: URL!
    private var saved: [String: String] = [:]
    private let variables = ["NASHI_FFMPEG", "NASHI_FFPROBE"]

    override func setUpWithError() throws {
        root = try OutputFiles.temporaryDirectory()
        for name in variables { saved[name] = ProcessInfo.processInfo.environment[name] }
    }
    override func tearDownWithError() throws {
        for name in variables { if let value = saved[name] { setenv(name, value, 1) } else { unsetenv(name) } }
        try? FileManager.default.removeItem(at: root)
    }

    func testTerminatedConvertStopsItsToolAndRemovesStaging() throws {
        let started = root.appendingPathComponent("started"), release = root.appendingPathComponent("release")
        XCTAssertEqual(mkfifo(started.path, 0o600), 0); XCTAssertEqual(mkfifo(release.path, 0o600), 0)
        let ffprobe = root.appendingPathComponent("ffprobe"), ffmpeg = root.appendingPathComponent("ffmpeg")
        try """
        #!/bin/sh
        echo '{"format":{"duration":"10.0"},"streams":[{"index":0,"codec_type":"video","codec_name":"h264","width":64,"height":48}]}'
        """.write(to: ffprobe, atomically: true, encoding: .utf8)
        try """
        #!/bin/sh
        for last; do :; done
        printf 'partial output' > "$last"
        echo $$ > '\(started.path)'
        read line < '\(release.path)'
        """.write(to: ffmpeg, atomically: true, encoding: .utf8)
        XCTAssertEqual(chmod(ffprobe.path, 0o755), 0); XCTAssertEqual(chmod(ffmpeg.path, 0o755), 0)
        setenv("NASHI_FFPROBE", ffprobe.path, 1); setenv("NASHI_FFMPEG", ffmpeg.path, 1)
        let input = root.appendingPathComponent("clip.mov"), output = root.appendingPathComponent("Output")
        try Data("clip".utf8).write(to: input); try FileManager.default.createDirectory(at: output, withIntermediateDirectories: true)
        let options = String(decoding: try JSONSerialization.data(withJSONObject: ["outputDirectory": output.path]), as: UTF8.self)

        // Without the CLI's own handling, this no-op handler keeps the test process alive.
        let previous = signal(SIGTERM) { _ in }
        defer { signal(SIGTERM, previous) }
        let done = DispatchSemaphore(value: 0); var status: Int32 = -1
        DispatchQueue.global().async { status = NashiCommandLine.run(arguments: ["nashi-cli", "convert", "mp4", input.path, "--options", options]); done.signal() }

        let fd = open(started.path, O_RDONLY); XCTAssertGreaterThanOrEqual(fd, 0)
        var bytes = [UInt8](repeating: 0, count: 32); let count = read(fd, &bytes, bytes.count); close(fd)
        let tool = pid_t(String(decoding: bytes.prefix(max(0, count)), as: UTF8.self).trimmingCharacters(in: .whitespacesAndNewlines)) ?? 0
        XCTAssertGreaterThan(tool, 0)
        XCTAssertEqual(try FileManager.default.contentsOfDirectory(atPath: output.path).filter { $0.hasPrefix(".Nashi-") }.count, 1)

        kill(getpid(), SIGTERM)
        let returned = done.wait(timeout: .now() + 30) == .success
        if !returned {
            let writer = open(release.path, O_WRONLY); if writer >= 0 { _ = write(writer, "\n", 1); close(writer) }
            _ = done.wait(timeout: .now() + 30)
        }
        XCTAssertTrue(returned, "nashi-cli kept waiting for its tool after SIGTERM")
        XCTAssertEqual(status, 128 + SIGTERM)
        XCTAssertNotEqual(kill(tool, 0), 0, "ffmpeg was left running")
        XCTAssertEqual(try FileManager.default.contentsOfDirectory(atPath: output.path), [])
    }
}
