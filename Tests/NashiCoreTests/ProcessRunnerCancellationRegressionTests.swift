import XCTest
@testable import NashiCore

/// A descendant of an external tool can inherit its output pipes and keep them
/// open after the tool exits. Cancellation must still return promptly, while an
/// uncancelled run keeps draining the pipes. Ordering is gated, never timed.
final class ProcessRunnerCancellationRegressionTests: XCTestCase {
    private final class Box: @unchecked Sendable { var result: ProcessResult?; var error: Error?; var completed = false }

    private func withFakeTool(_ body: String, _ test: (URL, URL) throws -> Void) throws {
        let dir = FileManager.default.temporaryDirectory.appendingPathComponent("NashiProcessRunner-\(UUID().uuidString)")
        try FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
        defer { try? FileManager.default.removeItem(at: dir) }
        let tool = dir.appendingPathComponent("fake-tool")
        try ("#!/bin/sh\n" + body).write(to: tool, atomically: true, encoding: .utf8)
        try FileManager.default.setAttributes([.posixPermissions: 0o755], ofItemAtPath: tool.path)
        try test(dir, tool)
    }
    /// nil for a missing, unreadable or non-positive value: `kill(-1, …)` and
    /// `kill(0, …)` would signal every process the runner may signal, or its group.
    private func pid(_ dir: URL, _ name: String) -> Int32? {
        guard let text = try? String(contentsOf: dir.appendingPathComponent(name), encoding: .utf8),
              let value = Int32(text.trimmingCharacters(in: .whitespacesAndNewlines)), value > 0 else { return nil }
        return value
    }
    private func run(_ tool: URL, _ dir: URL, token: CancellationToken, ready: DispatchSemaphore, done: DispatchSemaphore, box: Box) {
        DispatchQueue.global().async {
            do { box.result = try ProcessRunner.run(tool, [dir.path], token: token, onOutput: { if String(decoding: $0, as: UTF8.self).contains("ready") { ready.signal() } }) }
            catch { box.error = error }
            box.completed = true
            done.signal()
        }
    }
    /// Waits for the tool itself to exit and be reaped, so the remaining pipe
    /// holder is the descendant. Fails instead of hanging.
    private func waitForToolExit(_ dir: URL, ready: DispatchSemaphore, box: Box, file: StaticString = #filePath, line: UInt = #line) -> Bool {
        guard ready.wait(timeout: .now() + 30) == .success else {
            XCTFail("fake tool never printed ready (error: \(String(describing: box.error)))", file: file, line: line); return false
        }
        guard let child = pid(dir, "child.pid") else { XCTFail("fake tool wrote no valid child.pid", file: file, line: line); return false }
        let deadline = Date().addingTimeInterval(30)
        while kill(child, 0) == 0 {
            guard Date() < deadline else { XCTFail("fake tool \(child) was not reaped within 30 s", file: file, line: line); return false }
            usleep(1000)
        }
        return true
    }

    func testCancellationAfterToolExitDoesNotWaitForDescendantHoldingPipes() throws {
        try withFakeTool("/bin/sleep 120 &\necho $! > \"$1/descendant.pid\"\necho $$ > \"$1/child.pid\"\necho ready\nexit 0\n") { dir, tool in
            let token = CancellationToken(), ready = DispatchSemaphore(value: 0), done = DispatchSemaphore(value: 0), box = Box()
            run(tool, dir, token: token, ready: ready, done: done, box: box)
            defer {
                if let descendant = pid(dir, "descendant.pid") { kill(descendant, SIGKILL) }
                if !box.completed { _ = done.wait(timeout: .now() + 30) }
            }
            guard waitForToolExit(dir, ready: ready, box: box) else { return }
            XCTAssertNotNil(pid(dir, "descendant.pid"), "fake tool wrote no valid descendant.pid")
            token.cancel()
            let returned = done.wait(timeout: .now() + 10) == .success
            XCTAssertTrue(returned, "cancellation waited for a descendant that inherited the pipes")
            guard returned else { return }
            guard case NashiError.cancelled? = box.error as? NashiError else { return XCTFail("expected cancellation, got \(String(describing: box.error))") }
        }
    }

    func testUncancelledRunStillCapturesOutputWrittenAfterToolExit() throws {
        try withFakeTool("( while [ ! -f \"$1/go\" ]; do /bin/sleep 0.01; done; echo late-output ) &\necho $$ > \"$1/child.pid\"\necho ready\nexit 0\n") { dir, tool in
            let token = CancellationToken(), ready = DispatchSemaphore(value: 0), done = DispatchSemaphore(value: 0), box = Box()
            run(tool, dir, token: token, ready: ready, done: done, box: box)
            defer {
                // Releases the descendant on every path, so it never outlives the test.
                try? Data().write(to: dir.appendingPathComponent("go"))
                if !box.completed { _ = done.wait(timeout: .now() + 30) }
            }
            guard waitForToolExit(dir, ready: ready, box: box) else { return }
            XCTAssertFalse(box.completed)
            try Data().write(to: dir.appendingPathComponent("go"))
            XCTAssertEqual(done.wait(timeout: .now() + 30), .success, "run did not finish after the descendant closed the pipes")
            XCTAssertNil(box.error)
            XCTAssertEqual(box.result?.text, "ready\nlate-output\n")
        }
    }
}
