import Foundation
import Darwin

/// Web extraction runs in its own process group so Cancel also stops ffmpeg.
/// All arguments are argv entries; no shell or user configuration is involved.
enum WebDownloadProcess {
    static func run(_ executable:URL,_ arguments:[String],token:CancellationToken,
                    timeout:TimeInterval,progress:ProgressHandler?=nil) throws -> Data {
        try token.check()
        let output=Pipe(),errors=Pipe(),capture=Capture(progress:progress)
        var actions:posix_spawn_file_actions_t?,attributes:posix_spawnattr_t?
        posix_spawn_file_actions_init(&actions);posix_spawnattr_init(&attributes)
        defer { posix_spawn_file_actions_destroy(&actions);posix_spawnattr_destroy(&attributes) }
        posix_spawn_file_actions_adddup2(&actions,output.fileHandleForWriting.fileDescriptor,STDOUT_FILENO)
        posix_spawn_file_actions_adddup2(&actions,errors.fileHandleForWriting.fileDescriptor,STDERR_FILENO)
        posix_spawn_file_actions_addopen(&actions,STDIN_FILENO,"/dev/null",O_RDONLY,0)
        posix_spawn_file_actions_addclose(&actions,output.fileHandleForReading.fileDescriptor)
        posix_spawn_file_actions_addclose(&actions,errors.fileHandleForReading.fileDescriptor)
        posix_spawn_file_actions_addclose(&actions,output.fileHandleForWriting.fileDescriptor)
        posix_spawn_file_actions_addclose(&actions,errors.fileHandleForWriting.fileDescriptor)
        // Only the three standard streams reach the child: an inherited pipe of another
        // run would otherwise stay open (no EOF) until this download ends.
        posix_spawnattr_setflags(&attributes,Int16(POSIX_SPAWN_SETPGROUP|POSIX_SPAWN_CLOEXEC_DEFAULT))
        posix_spawnattr_setpgroup(&attributes,0)
        let args=([executable.path]+arguments).map{strdup($0)}+[nil]
        let environment=ProcessInfo.processInfo.environment.map{strdup("\($0.key)=\($0.value)")}+[nil]
        defer { for p in args { free(p) };for p in environment { free(p) } }
        var pid:pid_t=0
        let status=args.withUnsafeBufferPointer { argv in environment.withUnsafeBufferPointer { env in
            posix_spawn(&pid,executable.path,&actions,&attributes,UnsafeMutablePointer(mutating:argv.baseAddress!),UnsafeMutablePointer(mutating:env.baseAddress!))
        } }
        try? output.fileHandleForWriting.close();try? errors.fileHandleForWriting.close()
        guard status==0 else { throw NashiError.failed("The download component could not start (\(status)).") }
        let group=DispatchGroup()
        for (handle,isOutput) in [(output.fileHandleForReading,true),(errors.fileHandleForReading,false)] {
            group.enter();DispatchQueue.global().async {
                while true {
                    let data=handle.availableData
                    guard !data.isEmpty else { break }
                    capture.append(data,output:isOutput)
                }
                try? handle.close();group.leave()
            }
        }
        let deadline=Date().addingTimeInterval(timeout)
        var waitStatus:Int32=0,stoppingAt:Date?,timedOut=false
        while true {
            let waited=waitpid(pid,&waitStatus,WNOHANG)
            if waited==pid { break }
            if waited<0 { if errno==EINTR { continue };throw NashiError.failed("The download process stopped unexpectedly.") }
            if token.isCancelled || Date()>deadline || capture.overflow {
                if stoppingAt==nil { timedOut=Date()>deadline;stoppingAt=Date();kill(-pid,SIGTERM) }
                else if Date().timeIntervalSince(stoppingAt!)>1 { kill(-pid,SIGKILL) }
            }
            Thread.sleep(forTimeInterval:0.03)
        }
        // A child must not keep the pipes or a partial export alive after its parent exits.
        kill(-pid,SIGTERM)
        if group.wait(timeout:.now()+2) == .timedOut { kill(-pid,SIGKILL);group.wait() }
        try token.check()
        if timedOut { throw NashiError.failed("The site took too long to respond. Try the link again.") }
        if capture.overflow { throw NashiError.failed("The site returned too much metadata. Try an individual media link.") }
        guard waitStatus==0 else {
            let diagnostic=capture.errorText
            throw WebDownloadFailure(message:WebDownloadEngine.friendlyError(diagnostic),details:WebDownloadEngine.diagnosticDetails(diagnostic))
        }
        return capture.output
    }

    private final class Capture:@unchecked Sendable {
        let lock=NSLock();private var bytes=Data(),errors=Data(),outputLine=Data(),errorLine=Data(),tooLarge=false
        let progress:ProgressHandler?
        init(progress:ProgressHandler?) { self.progress=progress }
        var output:Data { lock.lock();defer{lock.unlock()};return bytes }
        var errorText:String { lock.lock();defer{lock.unlock()};return String(decoding:errors,as:UTF8.self) }
        var overflow:Bool { lock.lock();defer{lock.unlock()};return tooLarge }
        func append(_ data:Data,output:Bool) {
            lock.lock()
            if output { if bytes.count+data.count<=8_000_000 { bytes.append(data) } else { tooLarge=true } }
            else {
                errors.append(data)
                if errors.count>16_000 {
                    // Keep whole lines: a cut line can be a progress fragment or a link that lost its scheme.
                    let cut=errors.index(errors.endIndex,offsetBy:-16_000)
                    let start=errors[errors.index(before:cut)]==10 ? cut:errors[cut...].firstIndex(of:10).map{errors.index(after:$0)} ?? cut
                    errors.removeSubrange(..<start)
                }
            }
            var line=output ? outputLine:errorLine
            line.append(data)
            var updates:[(Double,String)]=[]
            while let end=line.firstIndex(of:10) {
                let text=String(decoding:line[..<end],as:UTF8.self);line.removeSubrange(...end)
                if let range=text.range(of:"NASHI_PROGRESS:"),let percent=Double(text[range.upperBound...].trimmingCharacters(in:.whitespacesAndNewlines).replacingOccurrences(of:"%",with:"")),percent.isFinite { updates.append((max(0,min(100,percent))/100,"Downloading media…")) }
                if let range=text.range(of:"NASHI_STAGE:") { updates.append((OperationProgress.indeterminate,String(text[range.upperBound...]))) }
            }
            if line.count>64_000 { line.removeAll() }
            if output { outputLine=line } else { errorLine=line }
            lock.unlock()
            for (fraction,label) in updates { progress?(fraction,label) }
        }
    }
}
