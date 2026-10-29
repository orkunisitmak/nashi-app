import XCTest
@testable import NashiCore

final class OperationProgressTests:XCTestCase {
    final class Events:@unchecked Sendable {
        let lock=NSLock()
        private var storage:[(Double,String)]=[]
        func add(_ value:Double,_ label:String) { lock.lock();storage.append((value,label));lock.unlock() }
        var values:[(Double,String)] { lock.lock();defer { lock.unlock() };return storage }
    }
    func testChunkedMicrosecondProgressRejectsMalformedAndRegressingValues() {
        let events=Events(),parser=FFmpegProgressParser(duration:10,progress:{ events.add($0,$1) })
        for chunk in ["out_ti","me_us=1000000\nframe=1\nout_time_us=", "5000000\nout_time_us=2000000\nout_time_us=N/A\n", "out_time_us=nan\nout_time_us=-4\nout_time_us=20000000\nprogress=end\n"] { parser.append(Data(chunk.utf8)) }
        XCTAssertEqual(events.values.map(\.0),[0.1,0.5,0.995])
    }
    func testUnknownDurationAndNestedScopesNeverInventPercentage() {
        for duration in [Double?.none,0,-1,.infinity,.nan] {
            let events=Events(),parser=FFmpegProgressParser(duration:duration,progress:{ events.add($0,$1) })
            parser.append(Data("out_time_us=5000000\nprogress=end\n".utf8))
            XCTAssertTrue(events.values.isEmpty)
        }
        XCTAssertEqual(OperationProgress.map(-1,from:0.5,to:0.98),-1)
        XCTAssertEqual(OperationProgress.map(.nan,from:0,to:1),-1)
        XCTAssertEqual(OperationProgress.map(0.5,from:0.49,to:0.98),0.735,accuracy:0.000001)
    }
    func testProcessOutputArrivesBeforeExitWithoutAFullPipeBuffer() throws {
        let seen=DispatchSemaphore(value:0),release=DispatchSemaphore(value:0)
        let queue=DispatchQueue(label:"progress-process-test")
        let finished=expectation(description:"process finished")
        queue.async {
            defer { finished.fulfill() }
            do {
                let result=try ProcessRunner.run(URL(fileURLWithPath:"/bin/sh"),["-c","printf 'small-first-chunk\\n'; sleep 0.6; printf 'last\\n'"],onOutput:{ data in
                    if String(decoding:data,as:UTF8.self).contains("small-first-chunk") { seen.signal();_ = release.wait(timeout:.now()+2) }
                })
                XCTAssertTrue(result.text.contains("last"))
            } catch { XCTFail("\(error)") }
        }
        XCTAssertEqual(seen.wait(timeout:.now()+0.5),.success,"A short progress line was buffered until process exit")
        release.signal();wait(for:[finished],timeout:4)
    }
    func testTerminateAllStopsRunningChildrenWithoutTheirTokens() throws {
        // App termination cannot wait for the token-polling thread; terminateAll must stop the child itself.
        let directory=try OutputFiles.temporaryDirectory();defer { try? FileManager.default.removeItem(at:directory) }
        let ready=directory.appendingPathComponent("ready"),hold=directory.appendingPathComponent("hold")
        XCTAssertEqual(mkfifo(ready.path,0o600),0);XCTAssertEqual(mkfifo(hold.path,0o600),0)
        let outcome=Events(),finished=DispatchSemaphore(value:0)
        DispatchQueue.global().async {
            defer { finished.signal() }
            do { try ProcessRunner.run(URL(fileURLWithPath:"/bin/sh"),["-c","echo $$ > \"$0\"; read -r _ < \"$1\"",ready.path,hold.path]);outcome.add(0,"completed") }
            catch { outcome.add(1,"\(error)") }
        }
        let reader=try XCTUnwrap(FileHandle(forReadingAtPath:ready.path))   // blocks until the child has started
        let pid=try XCTUnwrap(pid_t(String(decoding:reader.readDataToEndOfFile(),as:UTF8.self).trimmingCharacters(in:.whitespacesAndNewlines)))
        try reader.close()
        ProcessRunner.terminateAll()
        if finished.wait(timeout:.now()+10) == .timedOut {
            let fd=open(hold.path,O_WRONLY|O_NONBLOCK);if fd>=0 { close(fd) }   // release the child so it cannot leak
            _=finished.wait(timeout:.now()+5);return XCTFail("terminateAll left the child running")
        }
        XCTAssertEqual(outcome.values.map(\.0),[1],"\(outcome.values)")
        XCTAssertFalse(kill(pid,0)==0)
        ProcessRunner.terminateAll()   // nothing left to stop; must be a no-op
    }
    func testDownloadProcessStreamsShortProgressAndStageLines() throws {
        let seen=DispatchSemaphore(value:0),events=Events()
        let finished=expectation(description:"download helper finished")
        DispatchQueue.global().async {
            defer { finished.fulfill() }
            do {
                _=try WebDownloadProcess.run(URL(fileURLWithPath:"/bin/sh"),["-c","printf 'NASHI_PROGRESS:25%%\\n'; sleep 1.2; printf 'NASHI_STAGE:Finishing media…\\n'"],token:CancellationToken(),timeout:4,progress:{ value,label in
                    events.add(value,label)
                    if value==0.25 { seen.signal() }
                })
            } catch { XCTFail("\(error)") }
        }
        XCTAssertEqual(seen.wait(timeout:.now()+0.8),.success,"Short download progress was buffered until exit")
        wait(for:[finished],timeout:5)
        XCTAssertEqual(events.values.map(\.0),[0.25,OperationProgress.indeterminate])
        XCTAssertEqual(events.values.last?.1,"Finishing media…")
    }

    func testRealMediaProgressAndCancellationKeepTransactionalOutputsSafe() throws {
        let directory=try OutputFiles.temporaryDirectory();defer { try? FileManager.default.removeItem(at:directory) }
        let input=directory.appendingPathComponent("source.wav")
        try ProcessRunner.run(Dependencies.executable("ffmpeg"),["-v","error","-f","lavfi","-i","sine=duration=90:sample_rate=48000","-c:a","pcm_s16le",input.path])
        let events=Events()
        let outputs=try ConversionEngine.run(.init(inputs:[input],operation:.convert("mp3")),progress:{ events.add($0,$1) })
        XCTAssertEqual(outputs.count,1)
        let fractions=events.values.map(\.0).filter{$0>=0}
        XCTAssertTrue(fractions.contains{$0>0 && $0<0.98},"No intermediate encoder timestamp reached the caller")
        XCTAssertEqual(fractions.last,1)
        XCTAssertTrue(zip(fractions,fractions.dropFirst()).allSatisfy{$0 <= $1})
        let token=CancellationToken(),cancelledEvents=Events()
        XCTAssertThrowsError(try ConversionEngine.run(.init(inputs:[input],operation:.convert("flac")),token:token,progress:{ value,label in
            cancelledEvents.add(value,label)
            if value>0 && value<0.98 { token.cancel() }
        }))
        XCTAssertFalse(cancelledEvents.values.contains{$0.0==1})
        XCTAssertFalse(try FileManager.default.contentsOfDirectory(at:directory,includingPropertiesForKeys:nil).contains{$0.pathExtension=="flac"})
    }
}
