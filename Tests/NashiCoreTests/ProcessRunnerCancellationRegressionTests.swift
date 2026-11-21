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
    private func pid(_ dir: URL, _ name: String) -> Int32 { Int32((try? String(contentsOf: dir.appendingPathComponent(name), encoding: .utf8))?.trimmingCharacters(in: .whitespacesAndNewlines) ?? "") ?? -1 }
    private func run(_ tool: URL, _ dir: URL, token: CancellationToken, ready: DispatchSemaphore, done: DispatchSemaphore, box: Box) {
        DispatchQueue.global().async {
            do { box.result = try ProcessRunner.run(tool, [dir.path], token: token, onOutput: { if String(decoding: $0, as: UTF8.self).contains("ready") { ready.signal() } }) }
            catch { box.error = error }
            box.completed = true
            done.signal()
        }
    }
    private func waitUntilReaped(_ pid: Int32) { while kill(pid, 0) == 0 { usleep(1000) } }

    func testCancellationAfterToolExitDoesNotWaitForDescendantHoldingPipes() throws {
        try withFakeTool("/bin/sleep 120 &\necho $! > \"$1/descendant.pid\"\necho $$ > \"$1/child.pid\"\necho ready\nexit 0\n") { dir, tool in
            let token = CancellationToken(), ready = DispatchSemaphore(value: 0), done = DispatchSemaphore(value: 0), box = Box()
            run(tool, dir, token: token, ready: ready, done: done, box: box)
            XCTAssertEqual(ready.wait(timeout: .now() + 30), .success)
            waitUntilReaped(pid(dir, "child.pid"))
            token.cancel()
            let returned = done.wait(timeout: .now() + 10) == .success
            kill(pid(dir, "descendant.pid"), SIGKILL)
            if !returned { _ = done.wait(timeout: .now() + 30) }
            XCTAssertTrue(returned, "cancellation waited for a descendant that inherited the pipes")
            guard case NashiError.cancelled? = box.error as? NashiError else { return XCTFail("expected cancellation, got \(String(describing: box.error))") }
        }
    }

    func testUncancelledRunStillCapturesOutputWrittenAfterToolExit() throws {
        try withFakeTool("( while [ ! -f \"$1/go\" ]; do /bin/sleep 0.01; done; echo late-output ) &\necho $$ > \"$1/child.pid\"\necho ready\nexit 0\n") { dir, tool in
            let token = CancellationToken(), ready = DispatchSemaphore(value: 0), done = DispatchSemaphore(value: 0), box = Box()
            run(tool, dir, token: token, ready: ready, done: done, box: box)
            XCTAssertEqual(ready.wait(timeout: .now() + 30), .success)
            waitUntilReaped(pid(dir, "child.pid"))
            XCTAssertFalse(box.completed)
            try Data().write(to: dir.appendingPathComponent("go"))
            XCTAssertEqual(done.wait(timeout: .now() + 30), .success)
            XCTAssertNil(box.error)
            XCTAssertEqual(box.result?.text, "ready\nlate-output\n")
        }
    }
}
