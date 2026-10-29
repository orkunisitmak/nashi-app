import XCTest
import AppKit
@testable import NashiCore

// cancelling a job interrupts the ffmpeg WebP fallback.
// The fake ffmpeg announces itself on one FIFO and blocks on another; ordering is
// gated by those FIFOs. The 10 s waits are upper bounds only, never ordering.
final class ImageWebPFallbackCancellationTests: XCTestCase {
    private var root: URL!
    private var savedFFmpeg: String?
    private var fakes = 0

    override func setUpWithError() throws {
        root = try OutputFiles.temporaryDirectory()
        savedFFmpeg = ProcessInfo.processInfo.environment["NASHI_FFMPEG"]
    }
    override func tearDownWithError() throws {
        if let savedFFmpeg { setenv("NASHI_FFMPEG", savedFFmpeg, 1) } else { unsetenv("NASHI_FFMPEG") }
        try? FileManager.default.removeItem(at: root)
    }

    private struct Fake { let started: URL, release: URL, pid: URL }

    private func makeFake() throws -> Fake {
        fakes += 1
        let dir = root.appendingPathComponent("fake\(fakes)"); try FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
        let fake = Fake(started: dir.appendingPathComponent("started"), release: dir.appendingPathComponent("release"), pid: dir.appendingPathComponent("pid"))
        XCTAssertEqual(mkfifo(fake.started.path, 0o600), 0); XCTAssertEqual(mkfifo(fake.release.path, 0o600), 0)
        let script = dir.appendingPathComponent("ffmpeg")
        try """
        #!/bin/sh
        echo $$ > '\(fake.pid.path)'
        echo started > '\(fake.started.path)'
        read line < '\(fake.release.path)'
        for last; do :; done
        printf 'RIFF0000WEBP' > "$last"
        """.write(to: script, atomically: true, encoding: .utf8)
        XCTAssertEqual(chmod(script.path, 0o755), 0)
        setenv("NASHI_FFMPEG", script.path, 1)
        return fake
    }

    private func image() -> CGImage {
        let ctx = CGContext(data: nil, width: 4, height: 4, bitsPerComponent: 8, bytesPerRow: 0, space: CGColorSpaceCreateDeviceRGB(), bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue)!
        ctx.setFillColor(CGColor(red: 1, green: 0, blue: 0, alpha: 1)); ctx.fill(CGRect(x: 0, y: 0, width: 4, height: 4))
        return ctx.makeImage()!
    }

    private func makePNG() throws -> URL {
        let url = root.appendingPathComponent("Input.png"); try ImageEngine.write(image(), to: url); return url
    }

    private func awaitStarted(_ fifo: URL) -> Bool {
        let done = DispatchSemaphore(value: 0)
        DispatchQueue.global().async {
            let fd = open(fifo.path, O_RDONLY)
            if fd >= 0 { var byte: UInt8 = 0; while read(fd, &byte, 1) > 0 {}; close(fd) }
            done.signal()
        }
        if done.wait(timeout: .now() + 10) == .success { return true }
        let fd = open(fifo.path, O_WRONLY | O_NONBLOCK); if fd >= 0 { close(fd) }
        return false
    }

    @discardableResult private func release(_ fifo: URL) -> Bool {
        let done = DispatchSemaphore(value: 0)
        DispatchQueue.global().async {
            let fd = open(fifo.path, O_WRONLY)
            if fd >= 0 { _ = "go\n".withCString { write(fd, $0, 3) }; close(fd) }
            done.signal()
        }
        if done.wait(timeout: .now() + 10) == .success { return true }
        let fd = open(fifo.path, O_RDONLY | O_NONBLOCK); if fd >= 0 { close(fd) }
        return false
    }

    private func alive(_ pidFile: URL) -> Bool {
        guard let text = try? String(contentsOf: pidFile, encoding: .utf8), let pid = Int32(text.trimmingCharacters(in: .whitespacesAndNewlines)) else { return false }
        return kill(pid, 0) == 0
    }

    private func outputDirectory(_ name: String) throws -> URL {
        let url = root.appendingPathComponent(name); try FileManager.default.createDirectory(at: url, withIntermediateDirectories: true); return url
    }

    /// Start convert, wait until ffmpeg is blocked, cancel, and report what happened.
    private func cancelWhileEncoding(_ input: URL, into name: String) throws -> (returned: Bool, cancelled: Bool, ffmpegAlive: Bool, leftovers: [String]) {
        let fake = try makeFake()
        var options = OperationOptions(); options.preserveMetadata = false; options.outputDirectory = try outputDirectory(name)
        let token = CancellationToken(), done = DispatchSemaphore(value: 0); var result: Result<[URL], Error>?
        DispatchQueue.global().async { result = Result { try ImageEngine.convert(input, to: "webp", options: options, token: token) }; done.signal() }
        XCTAssertTrue(awaitStarted(fake.started), "fake ffmpeg never started")
        token.cancel()
        let returned = done.wait(timeout: .now() + 10) == .success
        let ffmpegAlive = alive(fake.pid)
        if !returned { release(fake.release); _ = done.wait(timeout: .now() + 10) }
        var cancelled = false
        if case .failure(NashiError.cancelled)? = result { cancelled = true }
        let leftovers = (try? FileManager.default.contentsOfDirectory(atPath: options.outputDirectory!.path)) ?? []
        XCTAssertFalse(alive(fake.pid), "fake ffmpeg left running")
        return (returned, cancelled, ffmpegAlive, leftovers)
    }

    func testCancelInterruptsRunningWebPFallbackEncode() throws {
        let input = try makePNG()

        // 1. The original reproduction.
        let first = try cancelWhileEncoding(input, into: "cancel")
        XCTAssertTrue(first.returned); XCTAssertTrue(first.cancelled); XCTAssertFalse(first.ffmpegAlive); XCTAssertEqual(first.leftovers, [])

        // 2. Repeated call: a second cancelled encode behaves the same.
        let second = try cancelWhileEncoding(input, into: "repeat")
        XCTAssertTrue(second.returned); XCTAssertTrue(second.cancelled); XCTAssertFalse(second.ffmpegAlive); XCTAssertEqual(second.leftovers, [])

        // 3. Boundary: a token cancelled before the encode never launches ffmpeg.
        let fake = try makeFake()
        let preOutput = try outputDirectory("pre").appendingPathComponent("Image.webp")
        let preToken = CancellationToken(); preToken.cancel()
        XCTAssertThrowsError(try ImageEngine.write(image(), to: preOutput, token: preToken)) { error in
            guard case NashiError.cancelled = error else { return XCTFail("expected cancellation, got \(error)") }
        }
        let launched = FileManager.default.fileExists(atPath: fake.pid.path)
        XCTAssertFalse(launched); XCTAssertFalse(FileManager.default.fileExists(atPath: preOutput.path))

        // 4. Source-compatible call without a token still completes through the fallback.
        let plain = try makeFake()
        let plainOutput = try outputDirectory("plain").appendingPathComponent("Image.webp")
        let done = DispatchSemaphore(value: 0); var plainError: Error?
        DispatchQueue.global().async { do { try ImageEngine.write(self.image(), to: plainOutput) } catch { plainError = error }; done.signal() }
        XCTAssertTrue(awaitStarted(plain.started)); XCTAssertTrue(release(plain.release))
        XCTAssertEqual(done.wait(timeout: .now() + 10), .success)
        XCTAssertNil(plainError); XCTAssertTrue(FileManager.default.fileExists(atPath: plainOutput.path))
        XCTAssertFalse(alive(plain.pid))

    }
}
