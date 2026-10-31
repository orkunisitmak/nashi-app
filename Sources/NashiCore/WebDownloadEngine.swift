import Foundation
import UniformTypeIdentifiers

public enum WebMediaKind:String,Codable,Sendable { case image,video,audio }
public enum WebDownloadMode:String,CaseIterable,Sendable { case original,video,audioMP3
    public var title:String { switch self { case .original:return "Original file";case .video:return "Video";case .audioMP3:return "Audio · MP3" } }
}
public struct WebMediaInfo:Sendable {
    public let url:URL
    public let title:String
    public let kind:WebMediaKind
    public let site:String
    public let duration:Double?
    public let heights:[Int]
    public let direct:Bool
    public let filename:String?
    public let byteCount:Int64?
    public var imageWidth:Int? = nil
    public var imageHeight:Int? = nil
    public var photos:[WebPhoto] = []
    public var skippedVideoCount:Int = 0
    public var modes:[WebDownloadMode] { kind == .image ? [.original]:direct || kind == .audio ? [.original,.audioMP3]:[.video,.audioMP3] }
}

public enum WebDownloadEngine {
    public static func link(_ text:String) throws -> URL {
        let value=text.trimmingCharacters(in:.whitespacesAndNewlines)
        guard value.utf8.count<=16_384,!value.unicodeScalars.contains(where:{CharacterSet.whitespacesAndNewlines.contains($0) || CharacterSet.controlCharacters.contains($0)}),
              let url=URL(string:value),let scheme=url.scheme?.lowercased(),["http","https"].contains(scheme),
              let host=url.host,!host.isEmpty,url.user==nil,url.password==nil else {
            throw NashiError.invalidInput("Paste one complete http:// or https:// media link.")
        }
        return url
    }
    public static func inspect(_ url:URL,token:CancellationToken = .init()) async throws -> WebMediaInfo {
        let url=try link(url.absoluteString);try token.check()
        let session=URLSession(configuration:.ephemeral)
        defer { session.invalidateAndCancel() }
        if WebMediaDragPayload.isInstagramPhotoPost(url) {
            let args=try baseArguments(photoPost:true)+["--ignore-no-formats-error","--skip-download","--dump-single-json","--no-progress","--",url.absoluteString]
            let data=try await Task.detached { try WebDownloadProcess.run(Dependencies.executable("yt-dlp"),args,token:token,timeout:100) }.value
            try token.check();try Task.checkCancellation()
            return try InstagramPhotos.parse(data,url:url)
        }
        if let cover=try await YouTubeThumbnail.resolve(url,token:token,fetch:{ candidate in
            var request=URLRequest(url:candidate);request.timeoutInterval=12
            let (data,response)=try await session.data(for:request)
            guard let response=response as? HTTPURLResponse,(200...299).contains(response.statusCode),
                  response.mimeType?.lowercased().hasPrefix("image/")==true else { return nil }
            return data
        }) {
            return WebMediaInfo(url:cover.url,title:"YouTube thumbnail",kind:.image,site:"YouTube",duration:nil,heights:[],direct:true,
                                filename:cover.url.lastPathComponent,byteCount:Int64(cover.byteCount),imageWidth:cover.width,imageHeight:cover.height)
        }
        var request=URLRequest(url:url);request.httpMethod="HEAD";request.timeoutInterval=15
        if let (_,response)=try? await session.data(for:request),let http=response as? HTTPURLResponse,(200...299).contains(http.statusCode),
           let kind=directKind(mime:response.mimeType,extension:url.pathExtension) {
            try token.check();try Task.checkCancellation()
            return WebMediaInfo(url:url,title:cleanFilename(response.suggestedFilename ?? url.lastPathComponent,mime:response.mimeType),kind:kind,site:url.host ?? "Web",duration:nil,heights:[],direct:true,filename:response.suggestedFilename,byteCount:response.expectedContentLength>0 ? response.expectedContentLength:nil)
        }
        try Task.checkCancellation();try token.check()
        let args=try baseArguments()+["--skip-download","--dump-single-json","--no-progress","--",url.absoluteString]
        let data=try await Task.detached { try WebDownloadProcess.run(Dependencies.executable("yt-dlp"),args,token:token,timeout:100) }.value
        try Task.checkCancellation();try token.check()
        return try parseMetadata(data,url:url)
    }

    public static func parseMetadata(_ data:Data,url:URL) throws -> WebMediaInfo {
        guard let root=try JSONSerialization.jsonObject(with:data) as? [String:Any] else { throw NashiError.failed("The site did not return readable media details.") }
        let item=(root["entries"] as? [[String:Any]])?.first ?? root
        if item["is_live"] as? Bool==true || ["is_live","is_upcoming"].contains(item["live_status"] as? String ?? "") { throw NashiError.unsupported("This is a live or upcoming stream. Use a finished video link.") }
        let rawFormats=item["formats"] as? [[String:Any]] ?? []
        let formats=rawFormats.filter{$0["has_drm"] as? Bool != true}
        guard item["has_drm"] as? Bool != true,(rawFormats.isEmpty || !formats.isEmpty),!formats.isEmpty || item["url"] as? String != nil else { throw NashiError.unsupported("No downloadable public media was found. Login-required and protected media are not supported.") }
        let heights=Array(Set(formats.compactMap{$0["height"] as? Int}.filter{$0>0 && $0<=16_384})).sorted()
        let audioExtensions=["mp3","m4a","wav","flac","ogg","opus","aac","aiff"]
        let audioOnly=item["vcodec"] as? String == "none" || (!formats.isEmpty && formats.allSatisfy { $0["vcodec"] as? String == "none" || audioExtensions.contains($0["ext"] as? String ?? "") })
        // Generic HTML5 videos often omit both height and codec metadata.
        let video = !heights.isEmpty || !audioOnly
        let duration=(item["duration"] as? Double).flatMap{$0.isFinite && $0>0 && $0<31_536_000 ? $0:nil}
        return WebMediaInfo(url:url,title:String((item["title"] as? String ?? "Web media").prefix(300)),kind:video ? .video:.audio,site:item["extractor_key"] as? String ?? url.host ?? "Web",duration:duration,heights:heights,direct:false,filename:nil,byteCount:nil)
    }

    public static func download(_ info:WebMediaInfo,mode:WebDownloadMode,maximumHeight:Int?,destination:URL,selectedPhotos:Set<Int>? = nil,
                                token:CancellationToken = .init(),progress:@escaping ProgressHandler) async throws -> [URL] {
        _=try link(info.url.absoluteString)
        guard destination.isFileURL,info.modes.contains(mode) else { throw NashiError.invalidInput("Choose an available media type and a local destination folder.") }
        if let maximumHeight { guard (2...16_384).contains(maximumHeight) else { throw NashiError.invalidInput("Choose a valid video quality.") } }
        try token.check();try Task.checkCancellation()
        let transaction=ExportTransaction();let area=try transaction.makeArea(destination:destination)
        defer { transaction.cleanUp() }
        let results:[URL]
        if !info.photos.isEmpty {
            results=try await InstagramPhotos.download(info,selected:selectedPhotos,directory:area.directory,token:token,progress:progress)
        } else if info.direct {
            results=try await downloadDirect(info,mode:mode,directory:area.directory,token:token,progress:progress)
        } else {
            let arguments=try downloadArguments(info,mode:mode,maximumHeight:maximumHeight,directory:area.directory)
            let data=try await Task.detached { try WebDownloadProcess.run(Dependencies.executable("yt-dlp"),arguments,token:token,timeout:21_600,progress:progress) }.value
            results=String(decoding:data,as:UTF8.self).split(separator:"\n").compactMap { line in
                guard line.hasPrefix("NASHI_FILE:"),let encoded=String(line.dropFirst("NASHI_FILE:".count)).data(using:.utf8),
                      let path=(try? JSONSerialization.jsonObject(with:encoded,options:.fragmentsAllowed)) as? String else { return nil }
                return URL(fileURLWithPath:path)
            }
        }
        try Task.checkCancellation();try token.check()
        if mode == .video,let maximumHeight {
            try await Task.detached {
                try enforceVideoHeight(results,maximum:maximumHeight,directory:area.directory,token:token,progress:progress)
            }.value
        }
        progress(OperationProgress.indeterminate,"Finishing your file…")
        try transaction.record(results,in:area)
        let output=try transaction.publish(token:token)
        progress(1,"Saved to \(destination.lastPathComponent)")
        return output
    }

    // Some sites omit dimensions or report stale values for their direct formats.
    // Verify the actual local file instead of trusting the format selector alone.
    static func enforceVideoHeight(_ files:[URL],maximum:Int,directory:URL,token:CancellationToken,progress:@escaping ProgressHandler) throws {
        for file in files {
            try token.check()
            let media=try MediaEngine.inspect(file,token:token)
            guard let height=media.video?.height else { throw NashiError.failed("The downloaded file has no readable video.") }
            guard height>maximum else { continue }
            progress(OperationProgress.indeterminate,"Fitting video to the selected size…")
            var options=OperationOptions();options.outputDirectory=directory;options.videoMaxHeight=maximum
            let converted=try MediaEngine.convert(file,to:"mp4",options:options,token:token,progress:{ value,_ in progress(value,"Fitting video to the selected size…") })
            guard converted.count==1,let replacement=converted.first,
                  let outputHeight=try MediaEngine.inspect(replacement,token:token).video?.height,outputHeight<=maximum else {
                throw NashiError.failed("The selected video size could not be applied.")
            }
            try token.check()
            try FileManager.default.removeItem(at:file)
            try FileManager.default.moveItem(at:replacement,to:file)
        }
    }

    static func baseArguments(photoPost:Bool=false) throws -> [String] {
        ["--ignore-config","--no-plugin-dirs","--no-update","--no-cache-dir","--no-remote-components",
         "--no-js-runtimes","--js-runtimes","deno:\(try Dependencies.executable("deno").path)",
         "--ffmpeg-location",try Dependencies.executable("ffmpeg").deletingLastPathComponent().path,
         "--socket-timeout","20","--retries","2","--extractor-retries","1","--no-colors"] +
        (photoPost ? ["--yes-playlist","--playlist-end","51"]:["--no-playlist","--playlist-items","1"])
    }
    static func downloadArguments(_ info:WebMediaInfo,mode:WebDownloadMode,maximumHeight:Int?,directory:URL) throws -> [String] {
        var args=try baseArguments()+["--no-simulate","--no-overwrites","--windows-filenames","--trim-filenames","180",
             "--downloader","native","--newline","--progress","--progress-template","download:NASHI_PROGRESS:%(progress._percent_str)s",
             "--print","before_dl:NASHI_STAGE:Preparing media…","--print","post_process:NASHI_STAGE:Finishing media…",
             // yt-dlp tests formats in a temporary file under its home path, which defaults to the
             // working directory. Apps start in "/", which is read-only, so keep everything in the stage.
             "--paths","home:\(directory.path)","--paths","temp:\(directory.path)",
             "--output",directory.appendingPathComponent("%(title).120B [%(id).40B].%(ext)s").path,
             "--print","after_move:NASHI_FILE:%(filepath)j"]
        switch mode {
        case .audioMP3:args += ["--format","bestaudio/best","--extract-audio","--audio-format","mp3","--audio-quality","0"]
        case .video:
            let limit=maximumHeight.map{"[height<=?\($0)]"} ?? ""
            args += ["--format","bestvideo[ext=mp4]\(limit)+bestaudio[ext=m4a]/best[ext=mp4]\(limit)/bestvideo\(limit)+bestaudio/best\(limit)","--merge-output-format","mp4","--remux-video","mp4"]
        case .original:args += ["--format","bestaudio/best"]
        }
        return args+["--",info.url.absoluteString]
    }

    static func downloadDirect(_ info:WebMediaInfo,mode:WebDownloadMode,directory:URL,token:CancellationToken,
                                       progress:@escaping ProgressHandler) async throws -> [URL] {
        let delegate=TransferDelegate(token:token,progress:progress)
        let configuration=URLSessionConfiguration.ephemeral;configuration.timeoutIntervalForRequest=45;configuration.timeoutIntervalForResource=21_600
        let session=URLSession(configuration:configuration,delegate:delegate,delegateQueue:nil)
        let watchdog=Task.detached {
            while !Task.isCancelled {
                if token.isCancelled { delegate.cancel();return }
                try? await Task.sleep(nanoseconds:100_000_000)
            }
        }
        defer { watchdog.cancel();session.invalidateAndCancel() }
        // The async convenience download API installs an internal download delegate
        // and suppresses didWriteData, even with a task delegate. Use the real
        // delegate lifecycle so progress arrives while the transfer is running.
        let (temporary,response)=try await withTaskCancellationHandler {
            try await delegate.download(info.url,session:session)
        } onCancel:{ delegate.cancel() }
        defer { try? FileManager.default.removeItem(at:temporary) }
        try token.check();try Task.checkCancellation()
        guard let http=response as? HTTPURLResponse,(200...299).contains(http.statusCode),
              directKind(mime:response.mimeType,extension:info.url.pathExtension) != nil else { throw NashiError.failed("The link no longer returns a media file. Copy its current media or share link and try again.") }
        let name=cleanFilename(response.suggestedFilename ?? info.filename ?? info.title,mime:response.mimeType)
        let raw=directory.appendingPathComponent(name)
        try FileManager.default.moveItem(at:temporary,to:raw)
        try OutputFiles.validate([raw])
        if info.kind == .image { _=try ImageEngine.load(raw) }
        else { _=try await Task.detached { try MediaEngine.inspect(raw,token:token) }.value }
        if mode == .audioMP3,raw.pathExtension.lowercased() != "mp3" {
            var options=OperationOptions();options.outputDirectory=directory
            return try await Task.detached { try ConversionEngine.run(.init(inputs:[raw],operation:.convert("mp3"),options:options),token:token,progress:{ value,_ in progress(value,"Converting downloaded audio…") }) }.value
        }
        return [raw]
    }

    public static func directKind(mime:String?,extension ext:String)->WebMediaKind? {
        let mime=(mime ?? "").lowercased(),ext=ext.lowercased()
        if ["m3u8","mpd"].contains(ext) || mime.contains("mpegurl") || mime.contains("dash+xml") { return nil }
        if mime.hasPrefix("image/") { return .image }
        if mime.hasPrefix("video/") { return .video }
        if mime.hasPrefix("audio/") { return .audio }
        guard mime.isEmpty || mime=="application/octet-stream" else { return nil }
        if ["png","jpg","jpeg","webp","heic","heif","avif","bmp","tif","tiff"].contains(ext) { return .image }
        if ["mp4","mov","webm","mkv","m4v","gif"].contains(ext) { return .video }
        if ["mp3","m4a","wav","flac","ogg","opus","aac","aiff"].contains(ext) { return .audio }
        return nil
    }
    public static func cleanFilename(_ proposed:String,mime:String?)->String {
        var name=(proposed as NSString).lastPathComponent
        name=String(name.unicodeScalars.filter{!CharacterSet.controlCharacters.contains($0) && !CharacterSet(charactersIn:"/:\\").contains($0)}.prefix(170))
        while name.hasPrefix(".") { name.removeFirst() }
        if name.isEmpty { name="Downloaded media" }
        let ext=(name as NSString).pathExtension.lowercased()
        let preferred=mime.flatMap{UTType(mimeType:$0)?.preferredFilenameExtension}
        let allowed=["jpg","jpeg","png","webp","heic","heif","avif","bmp","tif","tiff","svg","gif","mp4","mov","webm","mkv","m4v","mp3","m4a","wav","flac","ogg","opus","aac","aiff"]
        if !allowed.contains(ext),let preferred,allowed.contains(preferred) { name=(name as NSString).deletingPathExtension+"."+preferred }
        return name
    }
    public static func friendlyError(_ diagnostic:String)->String {
        let lower=diagnostic.lowercased()
        if lower.contains("sign in") || lower.contains("login") || lower.contains("cookies") || lower.contains("private") || lower.contains("age-restricted") { return "This link requires a login or is restricted. Try a public share link; Nashi does not read your browser sessions." }
        if lower.contains("drm") || lower.contains("protected") { return "This media is protected and cannot be downloaded here." }
        if lower.contains("unsupported url") || lower.contains("no video") || lower.contains("no media") { return "No downloadable media was found. Use the video's share link, or Copy Image Address for a photo." }
        if lower.contains("429") || lower.contains("rate limit") || lower.contains("too many requests") { return "The site is limiting requests. Wait a little and try again." }
        if lower.contains("403") || lower.contains("forbidden") || lower.contains("not available") || lower.contains("unavailable") { return "The site did not allow this download. The link may have expired or need a login." }
        return "The site could not provide this media. Try a fresh public share link or a direct media URL."
    }
    /// The last meaningful lines of yt-dlp's diagnostics, ERROR lines first, for a Details disclosure.
    /// Links are removed because they can carry signed tokens, and the home folder is shortened to ~.
    public static func diagnosticDetails(_ diagnostic:String,home:String=NSHomeDirectory())->String? {
        let links=try! NSRegularExpression(pattern:#"[A-Za-z][A-Za-z0-9+.-]*://[^\s'"<>]+"#)
        let lines=diagnostic.split(whereSeparator:\.isNewline).compactMap { raw -> String? in
            var line=raw.trimmingCharacters(in:.whitespaces)
            guard !line.isEmpty,!line.hasPrefix("NASHI_"),!line.hasPrefix("[debug]") else { return nil }
            line=links.stringByReplacingMatches(in:line,range:NSRange(line.startIndex...,in:line),withTemplate:"<link>")
            if !home.isEmpty,home != "/" { line=line.replacingOccurrences(of:home,with:"~") }
            return String(line.unicodeScalars.filter{!CharacterSet.controlCharacters.contains($0)}.prefix(300))
        }
        let errors=lines.filter{$0.hasPrefix("ERROR:")}
        let others=lines.filter{!$0.hasPrefix("ERROR:") && !$0.hasPrefix("[download]")}
        let selected=Array(errors.suffix(4))+Array(others.suffix(max(0,6-min(errors.count,4))))
        guard !selected.isEmpty else { return nil }
        return String(selected.joined(separator:"\n").prefix(1_500))
    }

    private final class TransferDelegate:NSObject,URLSessionDownloadDelegate,@unchecked Sendable {
        let token:CancellationToken,progress:ProgressHandler
        init(token:CancellationToken,progress:@escaping ProgressHandler) { self.token=token;self.progress=progress }
        private let transferLock=NSLock()
        private var cancelled=false
        private var task:URLSessionDownloadTask?
        private var continuation:CheckedContinuation<(URL,URLResponse),Error>?
        private var downloaded:(URL,URLResponse)?
        private var downloadError:Error?
        func download(_ url:URL,session:URLSession) async throws -> (URL,URLResponse) {
            try await withCheckedThrowingContinuation { continuation in
                transferLock.lock()
                guard !cancelled,!token.isCancelled else {
                    transferLock.unlock();continuation.resume(throwing:NashiError.cancelled);return
                }
                let task=session.downloadTask(with:url)
                self.continuation=continuation;self.task=task
                transferLock.unlock()
                task.resume()
            }
        }
        func cancel() {
            transferLock.lock();cancelled=true;let active=task;transferLock.unlock()
            active?.cancel()
        }
        func urlSession(_ session:URLSession,downloadTask:URLSessionDownloadTask,didFinishDownloadingTo location:URL) {
            // URLSession removes its temporary file when this callback returns.
            // Retain only our own copy until didComplete validates the transfer.
            let copy=FileManager.default.temporaryDirectory.appendingPathComponent("Nashi-Download-\(UUID().uuidString).tmp")
            do {
                guard let response=downloadTask.response else { throw NashiError.failed("The server did not return a response.") }
                try FileManager.default.moveItem(at:location,to:copy)
                downloaded=(copy,response)
            } catch { downloadError=error }
        }
        func urlSession(_ session:URLSession,task:URLSessionTask,didCompleteWithError error:Error?) {
            transferLock.lock();let continuation=self.continuation;self.continuation=nil;self.task=nil;transferLock.unlock()
            guard let continuation else { return }
            if let error=error ?? downloadError {
                if let downloaded { try? FileManager.default.removeItem(at:downloaded.0) }
                continuation.resume(throwing:error)
            } else if let downloaded { continuation.resume(returning:downloaded) }
            else { continuation.resume(throwing:NashiError.failed("The server returned no downloadable file.")) }
            downloaded=nil
        }
        func urlSession(_ session:URLSession,downloadTask:URLSessionDownloadTask,didWriteData bytesWritten:Int64,totalBytesWritten:Int64,totalBytesExpectedToWrite:Int64) {
            if token.isCancelled { downloadTask.cancel();return }
            let fraction=totalBytesExpectedToWrite>0 ? min(1,Double(totalBytesWritten)/Double(totalBytesExpectedToWrite)):0
            progress(totalBytesExpectedToWrite>0 ? fraction:OperationProgress.indeterminate,"Downloading · \(ByteCountFormatter.string(fromByteCount:totalBytesWritten,countStyle:.file))")
        }
        func urlSession(_ session:URLSession,task:URLSessionTask,willPerformHTTPRedirection response:HTTPURLResponse,newRequest request:URLRequest,completionHandler:@escaping(URLRequest?)->Void) {
            completionHandler(request.url.flatMap{try? WebDownloadEngine.link($0.absoluteString)} == nil ? nil:request)
        }
    }
}

/// A failed web download that keeps the friendly sentence first and yt-dlp's own words as details.
public struct WebDownloadFailure:LocalizedError,Sendable {
    public let message:String
    public let details:String?
    public var errorDescription:String? { message }
}
