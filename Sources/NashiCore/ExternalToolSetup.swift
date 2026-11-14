import Foundation
import CryptoKit

/// Public packages keep large upstream runtimes outside the signed app bundle.
/// Setup acquires exact upstream versions, verifies hashes, stages them and only
/// then publishes a complete tool directory. No credential store is involved.
public enum ExternalToolSetup {
    public static var root: URL {
        FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask)[0].appendingPathComponent("Nashi/Tools", isDirectory:true)
    }
    public static func executable(_ name:String)->URL? {
        let relative:String
        switch name {
        case "ffmpeg","ffprobe","yt-dlp","deno": relative="media-v1/\(name)"
        case "soffice": relative="office-26.8.1/LibreOffice.app/Contents/MacOS/soffice"
        default:return nil
        }
        let url=root.appendingPathComponent(relative)
        return usable(url) ? url:nil
    }
    private static func usable(_ url:URL)->Bool {
        guard let values=try? url.resolvingSymlinksInPath().resourceValues(forKeys:[.isRegularFileKey,.fileSizeKey]) else{return false}
        return FileManager.default.isExecutableFile(atPath:url.path) && values.isRegularFile == true && (values.fileSize ?? 0)>0
    }
    public static var status:ToolSetupStatus { ToolSetupStatus { try? Dependencies.executable($0) } }
    public static var mediaReady:Bool { status.mediaReady }
    public static var officeReady:Bool { status.officeReady }
    public static func installMedia(token:CancellationToken,progress:@escaping @Sendable (Double?,String)->Void) async throws {
        try token.check();try Task.checkCancellation()
        if mediaReady { progress(1,"Media tools are ready.");return }
        let fm=FileManager.default;try fm.createDirectory(at:root,withIntermediateDirectories:true)
        let stage=root.appendingPathComponent(".media-\(UUID().uuidString)",isDirectory:true)
        try fm.createDirectory(at:stage,withIntermediateDirectories:false)
        defer { try? fm.removeItem(at:stage) }
        let packages:[(String,String,String,String?)] = [
            ("ffmpeg","https://www.osxexperts.net/ffmpeg9arm.zip","591260c945d0eef150e3bf82b0ef988bd36a9cecc18ff05d6679617159f0a95e","binary"),
            ("ffprobe","https://www.osxexperts.net/ffprobe9arm.zip","e11c17e8200b3ee4c4c186d245e2b4053f01d56957336c1817fca0b997469106","binary"),
            ("yt-dlp","https://github.com/yt-dlp/yt-dlp/releases/download/2026.10.26/yt-dlp_macos","0f922c15ccc0b83eefa1c8bc9104e434318c4bf16e408e1ee93b7745a6efe0aa",nil),
            ("deno","https://github.com/denoland/deno/releases/download/v2.9.7/deno-aarch64-apple-darwin.zip","5cd46d6268f6f78f5d88bdc7159d20bd44cdaa4b3303474839f87ec6fe7ae25c","archive")
        ]
        for (index,package) in packages.enumerated() {
            try token.check();try Task.checkCancellation()
            let archive=stage.appendingPathComponent("download-\(index)")
            try await CheckedToolDownload.fetch(URL(string:package.1)!,to:archive,maximum:400*1024*1024) { fraction in
                progress(fraction.map{(Double(index)+$0)/Double(packages.count)},"Downloading \(package.0)…")
            }
            if package.3 != "binary" { try verify(archive,digest:package.2) }
            let output=stage.appendingPathComponent(package.0)
            if package.3 != nil {
                let unpack=stage.appendingPathComponent("unpack-\(index)",isDirectory:true)
                try ArchiveCodec.extract(archive,into:unpack,token:token)
                let candidate=unpack.appendingPathComponent(package.0)
                let values=try candidate.resourceValues(forKeys:[.isRegularFileKey,.isSymbolicLinkKey])
                guard values.isRegularFile == true,values.isSymbolicLink != true else { throw NashiError.failed("Upstream package has no regular \(package.0) executable.") }
                if package.3 == "binary" { try verify(candidate,digest:package.2) }
                try fm.moveItem(at:candidate,to:output);try fm.removeItem(at:unpack)
            } else { try fm.moveItem(at:archive,to:output) }
            try fm.setAttributes([.posixPermissions:0o755],ofItemAtPath:output.path)
            if fm.fileExists(atPath:archive.path) { try fm.removeItem(at:archive) }
        }
        try token.check();try Task.checkCancellation()
        let target=root.appendingPathComponent("media-v1",isDirectory:true)
        try ToolInstallation.publish(stage,to:target) { directory in ["ffmpeg","ffprobe","yt-dlp","deno"].allSatisfy { usable(directory.appendingPathComponent($0)) } }
        progress(1,"Media tools are ready.")
    }
    public static func installOffice(token:CancellationToken,progress:@escaping @Sendable (Double?,String)->Void) async throws {
        try token.check();try Task.checkCancellation()
        if officeReady { progress(1,"Office tools are ready.");return }
        let fm=FileManager.default;try fm.createDirectory(at:root,withIntermediateDirectories:true)
        let stage=root.appendingPathComponent(".office-\(UUID().uuidString)",isDirectory:true)
        try fm.createDirectory(at:stage,withIntermediateDirectories:false);defer { try? fm.removeItem(at:stage) }
        let image=stage.appendingPathComponent("LibreOffice.dmg")
        try await CheckedToolDownload.fetch(URL(string:"https://download.documentfoundation.org/libreoffice/stable/26.8.1/mac/aarch64/LibreOffice_26.8.1_MacOS_aarch64.dmg")!,to:image,maximum:400*1024*1024) { progress($0.map{$0*0.8},"Downloading LibreOffice 26.8.1…") }
        try verify(image,digest:"ca074e0b13571efb30a31fc531c5f95e997030637325e5d5de92d67aefdf0dfc")
        try token.check();try Task.checkCancellation();progress(nil,"Verifying and preparing office tools…")
        let mount=stage.appendingPathComponent("mount",isDirectory:true);try fm.createDirectory(at:mount,withIntermediateDirectories:false)
        try ProcessRunner.run(URL(fileURLWithPath:"/usr/bin/hdiutil"),["attach","-nobrowse","-readonly","-mountpoint",mount.path,image.path],token:token)
        defer { _ = try? ProcessRunner.run(URL(fileURLWithPath:"/usr/bin/hdiutil"),["detach",mount.path]) }
        let content=stage.appendingPathComponent("content",isDirectory:true);try fm.createDirectory(at:content,withIntermediateDirectories:false)
        try ProcessRunner.run(URL(fileURLWithPath:"/usr/bin/ditto"),[mount.appendingPathComponent("LibreOffice.app").path,content.appendingPathComponent("LibreOffice.app").path],token:token)
        try token.check();try Task.checkCancellation()
        let target=root.appendingPathComponent("office-26.8.1",isDirectory:true)
        try ToolInstallation.publish(content,to:target) { usable($0.appendingPathComponent("LibreOffice.app/Contents/MacOS/soffice")) }
        progress(1,"Office tools are ready.")
    }
    private static func verify(_ file:URL,digest:String) throws {
        let input=try FileHandle(forReadingFrom:file);defer{try? input.close()};var hasher=SHA256()
        while let bytes=try input.read(upToCount:1024*1024),!bytes.isEmpty {hasher.update(data:bytes)}
        let actual=hasher.finalize().map{String(format:"%02x",$0)}.joined()
        guard actual == digest else {throw NashiError.failed("The upstream download checksum did not match. Nothing was installed.")}
    }
}

private final class CheckedToolDownload:NSObject,URLSessionDownloadDelegate,@unchecked Sendable {
    private let target:URL;private let maximum:Int64;private let progress:@Sendable (Double?)->Void
    private var continuation:CheckedContinuation<Void,Error>?;private var failure:Error?
    private init(target:URL,maximum:Int64,progress:@escaping @Sendable (Double?)->Void) { self.target=target;self.maximum=maximum;self.progress=progress }
    static func fetch(_ url:URL,to target:URL,maximum:Int64,progress:@escaping @Sendable (Double?)->Void) async throws {
        let delegate=CheckedToolDownload(target:target,maximum:maximum,progress:progress)
        let session=URLSession(configuration:.ephemeral,delegate:delegate,delegateQueue:nil)
        let task=session.downloadTask(with:url)
        defer{session.finishTasksAndInvalidate()}
        try await withTaskCancellationHandler(operation:{
            try await withCheckedThrowingContinuation { (continuation:CheckedContinuation<Void,Error>) in
                delegate.continuation=continuation
                if Task.isCancelled {continuation.resume(throwing:CancellationError());delegate.continuation=nil;return}
                task.resume()
            }
        },onCancel:{task.cancel()})
    }
    func urlSession(_ session:URLSession,task:URLSessionTask,willPerformHTTPRedirection response:HTTPURLResponse,newRequest request:URLRequest,completionHandler:@escaping (URLRequest?)->Void) { completionHandler(request.url?.scheme == "https" ? request:nil) }
    func urlSession(_ session:URLSession,downloadTask:URLSessionDownloadTask,didWriteData bytesWritten:Int64,totalBytesWritten:Int64,totalBytesExpectedToWrite:Int64) {
        if totalBytesWritten>maximum || totalBytesExpectedToWrite>maximum {failure=NashiError.failed("The upstream download exceeds its size limit.");downloadTask.cancel();return}
        progress(totalBytesExpectedToWrite>0 ? Double(totalBytesWritten)/Double(totalBytesExpectedToWrite):nil)
    }
    func urlSession(_ session:URLSession,downloadTask:URLSessionDownloadTask,didFinishDownloadingTo location:URL) {
        do {
            guard let response=downloadTask.response as? HTTPURLResponse,(200...299).contains(response.statusCode),response.url?.scheme == "https" else {throw NashiError.failed("The upstream server did not return a successful HTTPS download.")}
            let size=try location.resourceValues(forKeys:[.fileSizeKey]).fileSize ?? 0
            guard size>0,size<=maximum else {throw NashiError.failed("The upstream download has an invalid size.")}
            try FileManager.default.moveItem(at:location,to:target)
        }catch{failure=error}
    }
    func urlSession(_ session:URLSession,task:URLSessionTask,didCompleteWithError error:Error?) {
        guard let continuation else{return};self.continuation=nil
        if let failure=failure ?? error {continuation.resume(throwing:failure)}else{continuation.resume()}
    }
}
