import XCTest
@testable import NashiCore

final class WebDownloadTests:XCTestCase {
    func testOnlySinglePublicWebAddressSyntax() throws {
        for value in ["file:///tmp/a.mp4","javascript:alert(1)","blob:https://example.org/id","https://u:p@example.org/a","https://a.org/a https://b.org/b","https://a.org/a\nb","https://a.org/\tb",""] { XCTAssertThrowsError(try WebDownloadEngine.link(value),value) }
        XCTAssertEqual(try WebDownloadEngine.link("  https://example.org/a?q=a%20b  ").host,"example.org")
    }
    func testManifestsAndHTMLAreNotDirectMedia() {
        XCTAssertNil(WebDownloadEngine.directKind(mime:"application/vnd.apple.mpegurl",extension:"m3u8"))
        XCTAssertNil(WebDownloadEngine.directKind(mime:"text/html",extension:"mp4"))
        XCTAssertEqual(WebDownloadEngine.directKind(mime:"application/octet-stream",extension:"PNG"),.image)
    }
    func testNamesCannotEscapeDestinationOrMasqueradeAsApplications() {
        XCTAssertEqual(WebDownloadEngine.cleanFilename("../../escape.app",mime:"video/mp4"),"escape.mp4")
        XCTAssertEqual(WebDownloadEngine.cleanFilename(".hidden.jpg",mime:"image/jpeg"),"hidden.jpg")
        XCTAssertFalse(WebDownloadEngine.cleanFilename("a\\b:c\n.jpg",mime:"image/jpeg").contains(":"))
        XCTAssertEqual(WebDownloadEngine.cleanFilename("Türkçe 🥭.png",mime:"image/png"),"Türkçe 🥭.png")
        XCTAssertEqual(WebDownloadEngine.cleanFilename("clip.avi",mime:"video/x-msvideo"),"clip.avi")
        XCTAssertEqual(WebDownloadEngine.cleanFilename("movie.wmv",mime:"video/x-ms-wmv"),"movie.wmv")
        XCTAssertEqual(WebDownloadEngine.cleanFilename("song.wma",mime:"audio/x-ms-wma"),"song.wma")
        XCTAssertEqual(WebDownloadEngine.cleanFilename("clip.avi",mime:nil),"clip.avi")
        XCTAssertEqual(WebDownloadEngine.cleanFilename("clip.3gp",mime:"video/3gpp"),"clip.3gp")
        XCTAssertEqual(WebDownloadEngine.cleanFilename("clip.mpg",mime:"video/mpeg"),"clip.mpg")
        XCTAssertEqual(WebDownloadEngine.cleanFilename("clip.flv",mime:"video/x-flv"),"clip.flv")
        XCTAssertEqual(WebDownloadEngine.cleanFilename("clip.exe",mime:"video/mp4"),"clip.mp4")
        XCTAssertEqual(WebDownloadEngine.cleanFilename("notes.ts",mime:"text/plain"),"notes")
    }
    func testMetadataModesQualityAndDuration() throws {
        let json=Data(#"{"title":"A test","duration":12.5,"extractor_key":"YouTube","formats":[{"height":1080,"vcodec":"avc1"},{"height":720},{"height":1080},{"height":-1},{"height":999999}]}"#.utf8)
        let info=try WebDownloadEngine.parseMetadata(json,url:URL(string:"https://example.org/watch")!)
        XCTAssertEqual(info.kind,.video);XCTAssertEqual(info.heights,[720,1080]);XCTAssertEqual(info.duration,12.5);XCTAssertEqual(info.modes,[.video,.audioMP3])
        let audio=try WebDownloadEngine.parseMetadata(Data(#"{"title":"Sound","url":"https://example.org/a","vcodec":"none","formats":[{"acodec":"aac"}]}"#.utf8),url:info.url)
        XCTAssertEqual(audio.kind,.audio);XCTAssertEqual(audio.modes,[.original,.audioMP3])
    }
    func testGenericHTML5VideoWithoutCodecStillOffersVideo() throws {
        let info=try WebDownloadEngine.parseMetadata(Data(#"{"formats":[{"ext":"mp4","url":"https://example.org/a.mp4"}]}"#.utf8),url:URL(string:"https://example.org/page")!)
        XCTAssertEqual(info.kind,.video);XCTAssertEqual(info.modes,[.video,.audioMP3])
    }
    func testLiveAndProtectedMediaRejected() {
        for json in [#"{"is_live":true,"url":"https://a.org/a"}"#,#"{"live_status":"is_upcoming","url":"https://a.org/a"}"#,#"{"has_drm":true,"url":"https://a.org/a"}"#,#"{"url":"https://a.org/a","formats":[{"height":1080,"has_drm":true}]}"#,#"{"formats":[]}"#] {
            XCTAssertThrowsError(try WebDownloadEngine.parseMetadata(Data(json.utf8),url:URL(string:"https://example.org/watch")!))
        }
    }
    func testExtractorArgumentsIgnoreUserConfigurationAndKeepURLAsData() throws {
        let url=try WebDownloadEngine.link("https://example.org/watch?q=%24%28touch%20x%29&list=playlist")
        let info=try WebDownloadEngine.parseMetadata(Data(#"{"formats":[{"height":1080}]}"#.utf8),url:url)
        let args=try WebDownloadEngine.downloadArguments(info,mode:.video,maximumHeight:720,directory:URL(fileURLWithPath:"/tmp/stage"))
        for flag in ["--ignore-config","--no-plugin-dirs","--no-remote-components","--no-playlist","--no-overwrites"] { XCTAssertTrue(args.contains(flag)) }
        XCTAssertEqual(Array(args.suffix(2)),["--",url.absoluteString]);XCTAssertTrue(args.contains(where:{$0.contains("height<=?720")}));XCTAssertFalse(args.contains(where:{$0.contains("cookies")}))
    }
    func testProcessTimeoutAndCancellation() throws {
        let start=Date()
        XCTAssertThrowsError(try WebDownloadProcess.run(URL(fileURLWithPath:"/bin/sleep"),["30"],token:.init(),timeout:0.1))
        XCTAssertLessThan(Date().timeIntervalSince(start),3)
        let token=CancellationToken();token.cancel()
        XCTAssertThrowsError(try WebDownloadProcess.run(URL(fileURLWithPath:"/bin/sleep"),["30"],token:token,timeout:30))
    }
    func testDownloadChildReceivesOnlyStandardStreams() throws {
        let directory=try OutputFiles.temporaryDirectory();defer { try? FileManager.default.removeItem(at:directory) }
        let probe=directory.appendingPathComponent("probe")
        try "#!/bin/sh\ns=\"\";for fd in $1; do if { true >&\"$fd\"; } 2>/dev/null; then s=\"$s $fd:open\"; else s=\"$s $fd:closed\"; fi; done\necho \"NASHI_STAGE:fds=$s\"\n".write(to:probe,atomically:true,encoding:.utf8)
        try FileManager.default.setAttributes([.posixPermissions:0o755],ofItemAtPath:probe.path)
        // Another run's pipe and an open file: a child holding them would keep that pipe from reaching EOF.
        let other=Pipe(),file=try XCTUnwrap(FileHandle(forWritingAtPath:probe.path));defer { try? file.close() }
        let fds="1 2 \(other.fileHandleForWriting.fileDescriptor) \(file.fileDescriptor)",label=NSMutableString()
        _=try WebDownloadProcess.run(probe,[fds],token:.init(),timeout:30) { _,text in label.setString(text) }
        XCTAssertEqual(label as String,"fds= 1:open 2:open \(other.fileHandleForWriting.fileDescriptor):closed \(file.fileDescriptor):closed")
    }
    func testDownloadKeepsTemporaryFilesInTheStageInsteadOfTheWorkingDirectory() throws {
        let info=try WebDownloadEngine.parseMetadata(Data(#"{"formats":[{"height":1080}]}"#.utf8),url:URL(string:"https://example.org/watch")!)
        let args=try WebDownloadEngine.downloadArguments(info,mode:.video,maximumHeight:nil,directory:URL(fileURLWithPath:"/tmp/stage"))
        let paths=args.indices.filter{args[$0]=="--paths"}.map{args[$0+1]}
        XCTAssertEqual(Set(paths),["home:/tmp/stage","temp:/tmp/stage"])
    }
    func testDiagnosticDetailsPutErrorsFirstAndDropLinksAndHome() throws {
        let stderr="""
        [youtube] abc: Downloading webpage
        WARNING: [youtube] Falling back to https://www.youtube.com/watch?v=abc&token=secret
        NASHI_PROGRESS: 12.0%
        [download]  12.0% of 10MiB
        ERROR: [Errno 30] Read-only file system: '/Volumes/Home/tester/Downloads/.Nashi-1/tmpz7.tmp'

        """
        let details=try XCTUnwrap(WebDownloadEngine.diagnosticDetails(stderr,home:"/Volumes/Home/tester"))
        let lines=details.components(separatedBy:"\n")
        XCTAssertEqual(lines.first,"ERROR: [Errno 30] Read-only file system: '~/Downloads/.Nashi-1/tmpz7.tmp'")
        XCTAssertEqual(lines.dropFirst(),["[youtube] abc: Downloading webpage","WARNING: [youtube] Falling back to <link>"])
        XCTAssertFalse(details.contains("secret"));XCTAssertFalse(details.contains("NASHI_"));XCTAssertFalse(details.contains("[download]"))
        XCTAssertNil(WebDownloadEngine.diagnosticDetails("\n  \nNASHI_STAGE:Preparing media…\n"))
        let flood=String(repeating:"ERROR: "+String(repeating:"x",count:1_000)+"\n",count:50)
        let capped=try XCTUnwrap(WebDownloadEngine.diagnosticDetails(flood))
        XCTAssertLessThanOrEqual(capped.count,1_500);XCTAssertEqual(capped.components(separatedBy:"\n").count,4)
    }
    func testFailedProcessKeepsFriendlyMessageAndDetails() throws {
        XCTAssertThrowsError(try WebDownloadProcess.run(URL(fileURLWithPath:"/bin/sh"),["-c","echo 'ERROR: Something specific broke' >&2;exit 1"],token:.init(),timeout:10)) { error in
            let failure=error as? WebDownloadFailure
            XCTAssertEqual(failure?.localizedDescription,WebDownloadEngine.friendlyError("ERROR: Something specific broke"))
            XCTAssertEqual(failure?.details,"ERROR: Something specific broke")
        }
    }
    func testTrimmedDiagnosticsKeepOnlyWholeLines() throws {
        func progress(_ count:Int)->String { "i=0;while [ $i -lt \(count) ];do echo 'NASHI_PROGRESS:  12.3%' >&2;i=$((i+1));done;" }
        let link="https://media.example.org/video?"+String(repeating:"p",count:1_450)+"&signature=secret"
        // Only the last 16,000 bytes are kept; this trim lands near the end of the warning's link.
        let script=progress(1_000)+"echo 'WARNING: Retrying \(link)' >&2;"+progress(690)+"echo 'ERROR: HTTP Error 403: Forbidden' >&2;exit 1"
        XCTAssertThrowsError(try WebDownloadProcess.run(URL(fileURLWithPath:"/bin/sh"),["-c",script],token:.init(),timeout:30)) { error in
            XCTAssertEqual((error as? WebDownloadFailure)?.details,"ERROR: HTTP Error 403: Forbidden")
        }
    }
    func testErrorsDoNotExposeSourceSecrets() {
        let message=WebDownloadEngine.friendlyError("ERROR 403: https://example.org/private?token=secret")
        XCTAssertFalse(message.contains("secret"));XCTAssertFalse(message.contains("https://"))
    }
    // F1920: the size fit replaced a reported file outside staging before record rejected it.
    func testSizeFitNeverReplacesAReportedFileOutsideStaging() async throws {
        let fm=FileManager.default,root=fm.temporaryDirectory.appendingPathComponent("nashi-web-confine-\(UUID().uuidString)",isDirectory:true)
        try fm.createDirectory(at:root,withIntermediateDirectories:true)
        addTeardownBlock { try? fm.removeItem(at:root) }
        let plan=root.appendingPathComponent("plan.sh")
        let tools=["yt-dlp":"out=\"\";prev=\"\"\nfor a in \"$@\"; do [ \"$prev\" = --output ] && out=\"$a\";prev=\"$a\"; done\nstage=$(dirname \"$out\")\n. '\(plan.path)'\n",
                   "ffprobe":"for f; do :; done\nif grep -q H1080 \"$f\"; then h=1080; else h=480; fi\nprintf '{\"format\":{\"duration\":\"1\"},\"streams\":[{\"index\":0,\"codec_type\":\"video\",\"codec_name\":\"h264\",\"width\":640,\"height\":%s}]}' $h\n",
                   "ffmpeg":"for o; do :; done\nprintf H480 > \"$o\"\n","deno":"exit 0\n"]
        for (name,body) in tools {
            let tool=root.appendingPathComponent(name);try Data(("#!/bin/sh\n"+body).utf8).write(to:tool)
            try fm.setAttributes([.posixPermissions:0o755],ofItemAtPath:tool.path)
            let variable="NASHI_"+name.uppercased(),previous=ProcessInfo.processInfo.environment[variable]
            setenv(variable,tool.path,1)
            addTeardownBlock { if let previous { setenv(variable,previous,1) } else { unsetenv(variable) } }
        }
        let info=try WebDownloadEngine.parseMetadata(Data(#"{"formats":[{"height":1080}]}"#.utf8),url:URL(string:"https://example.org/watch")!)
        let outside=root.appendingPathComponent("Outside/secret.mp4");try fm.createDirectory(at:outside.deletingLastPathComponent(),withIntermediateDirectories:true)
        for (index,report) in [outside.path,"$stage/../../Outside/secret.mp4"].enumerated() {
            try Data("H1080 original".utf8).write(to:outside)
            try Data("printf H1080 > \"$stage/a.mp4\"\necho NASHI_FILE:\\\"$stage/a.mp4\\\"\necho NASHI_FILE:\\\"\(report)\\\"\n".utf8).write(to:plan)
            let destination=root.appendingPathComponent("Downloads \(index)",isDirectory:true)
            do { _=try await WebDownloadEngine.download(info,mode:.video,maximumHeight:720,destination:destination) { _,_ in };XCTFail("outside path accepted: \(report)") } catch {}
            XCTAssertEqual(try String(contentsOf:outside,encoding:.utf8),"H1080 original",report)
            XCTAssertEqual(try fm.contentsOfDirectory(atPath:destination.path),[],report)
        }
        // Control: a staged file over the limit is still fitted and published.
        try Data("printf H1080 > \"$stage/a.mp4\"\necho NASHI_FILE:\\\"$stage/a.mp4\\\"\n".utf8).write(to:plan)
        let files=try await WebDownloadEngine.download(info,mode:.video,maximumHeight:720,destination:root.appendingPathComponent("Downloads",isDirectory:true)) { _,_ in }
        XCTAssertEqual(files.map(\.lastPathComponent),["a.mp4"]);XCTAssertEqual(try String(contentsOf:files[0],encoding:.utf8),"H480")
    }
    // F3510: presigned storage links are signed for GET only and some servers refuse HEAD; the photo went to yt-dlp.
    func testDirectImageWhoseServerRefusesHEADStaysAPhoto() async throws {
        let fm=FileManager.default,root=try OutputFiles.temporaryDirectory();addTeardownBlock { try? FileManager.default.removeItem(at:root) }
        let argv=root.appendingPathComponent("yt-dlp.argv")
        for (name,body) in ["yt-dlp":"echo \"$@\" > '\(argv.path)'\necho 'ERROR: Unsupported URL' >&2\nexit 1\n","deno":"exit 0\n","ffmpeg":"exit 0\n"] {
            let tool=root.appendingPathComponent(name);try Data(("#!/bin/sh\n"+body).utf8).write(to:tool)
            try fm.setAttributes([.posixPermissions:0o755],ofItemAtPath:tool.path)
            let variable="NASHI_"+name.uppercased(),previous=ProcessInfo.processInfo.environment[variable]
            setenv(variable,tool.path,1)
            addTeardownBlock { if let previous { setenv(variable,previous,1) } else { unsetenv(variable) } }
        }
        let png=try XCTUnwrap(Data(base64Encoded:"iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg=="))
        for head in [200,403,405] {
            let server=try LoopbackServer { method in
                let ok=method=="GET" || head==200
                return Data("HTTP/1.1 \(ok ? 200:head) X\r\nContent-Type: \(ok ? "image/png":"application/xml")\r\nContent-Length: \(png.count)\r\nConnection: close\r\n\r\n".utf8)+(method=="GET" ? png:Data())
            }
            defer { server.stop() }
            guard let info=try? await WebDownloadEngine.inspect(URL(string:"http://127.0.0.1:\(server.port)/photo.png?X-Amz-Signature=abc")!) else {
                XCTFail("HEAD \(head): the photo link was handed to yt-dlp (\(server.methods))");continue
            }
            XCTAssertEqual(info.kind,.image,"HEAD \(head)");XCTAssertTrue(info.direct,"HEAD \(head)");XCTAssertFalse(fm.fileExists(atPath:argv.path),"HEAD \(head)")
            XCTAssertEqual(server.methods,head==200 ? ["HEAD"]:["HEAD","GET"])
            let files=try await WebDownloadEngine.download(info,mode:.original,maximumHeight:nil,destination:root.appendingPathComponent("Downloads \(head)")) { _,_ in }
            XCTAssertEqual(files.map(\.lastPathComponent),["photo.png"]);XCTAssertEqual(try Data(contentsOf:files[0]),png)
        }
    }
}

/// HTTP on 127.0.0.1 with an ephemeral port, one response per connection; no network.
private final class LoopbackServer:@unchecked Sendable {
    let port:UInt16
    private let socketFD:Int32,lock=NSLock(),reply:(String)->Data
    private var seen:[String]=[]
    var methods:[String] { lock.lock();defer{lock.unlock()};return seen }
    init(_ reply:@escaping(String)->Data) throws {
        self.reply=reply;socketFD=socket(AF_INET,SOCK_STREAM,0)
        var address=sockaddr_in();address.sin_family=sa_family_t(AF_INET);address.sin_addr.s_addr=inet_addr("127.0.0.1")
        var length=socklen_t(MemoryLayout<sockaddr_in>.size)
        let fd=socketFD
        guard withUnsafeMutablePointer(to:&address,{ $0.withMemoryRebound(to:sockaddr.self,capacity:1) { bind(fd,$0,length)==0 && listen(fd,8)==0 && getsockname(fd,$0,&length)==0 } }) else {
            close(fd);throw POSIXError(POSIXErrorCode(rawValue:errno) ?? .EIO)
        }
        port=UInt16(bigEndian:address.sin_port)
        Thread.detachNewThread { [self] in
            while case let client=accept(socketFD,nil,nil),client>=0 {
                var noSignal:Int32=1;setsockopt(client,SOL_SOCKET,SO_NOSIGPIPE,&noSignal,socklen_t(MemoryLayout<Int32>.size))
                var request=Data(),buffer=[UInt8](repeating:0,count:4_096)
                while request.range(of:Data("\r\n\r\n".utf8))==nil { let count=read(client,&buffer,buffer.count);if count<=0 { break };request.append(buffer,count:count) }
                let method=String(decoding:request.prefix{$0 != 32},as:UTF8.self)
                lock.lock();seen.append(method);lock.unlock()
                _=reply(method).withUnsafeBytes { write(client,$0.baseAddress,$0.count) }
                close(client)
            }
        }
    }
    func stop() { shutdown(socketFD,SHUT_RDWR);close(socketFD) }
}
