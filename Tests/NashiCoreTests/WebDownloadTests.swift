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
    func testErrorsDoNotExposeSourceSecrets() {
        let message=WebDownloadEngine.friendlyError("ERROR 403: https://example.org/private?token=secret")
        XCTAssertFalse(message.contains("secret"));XCTAssertFalse(message.contains("https://"))
    }
    func testYouTubePlaylistLinksAreRecognizedButMixesAndOtherSitesAreNot() {
        XCTAssertEqual(WebDownloadEngine.youTubePlaylistID(URL(string:"https://www.youtube.com/playlist?list=PLabc_-123")!),"PLabc_-123")
        XCTAssertEqual(WebDownloadEngine.youTubePlaylistID(URL(string:"https://youtu.be/dQw4w9WgXcQ?list=PLabc")!),"PLabc")
        XCTAssertEqual(WebDownloadEngine.youTubePlaylistID(URL(string:"https://music.youtube.com/watch?v=dQw4w9WgXcQ&list=OLAK5uy")!),"OLAK5uy")
        for value in ["https://www.youtube.com/watch?v=dQw4w9WgXcQ","https://www.youtube.com/watch?v=dQw4w9WgXcQ&list=RDdQw4w9WgXcQ",
                      "https://example.org/playlist?list=PLabc","https://www.youtube.com/playlist?list=PL%3Bx","https://www.youtube.com/playlist?list="] {
            XCTAssertNil(WebDownloadEngine.youTubePlaylistID(URL(string:value)!),value)
        }
    }
    func testFlatPlaylistKeepsOrderMarksTheLinkedVideoAndDropsUnsafeEntries() throws {
        let json=Data(#"{"_type":"playlist","title":"Road trip","entries":[{"id":"aaaaaaaaaaa","title":"First","duration":61.0},{"id":"bad","url":"file:///etc/passwd","title":"Unsafe"},{"id":"bbbbbbbbbbb","url":"https://www.youtube.com/watch?v=bbbbbbbbbbb","title":"Second"}]}"#.utf8)
        let info=try WebDownloadEngine.parsePlaylist(json,url:URL(string:"https://www.youtube.com/watch?v=bbbbbbbbbbb&list=PLabc")!)
        XCTAssertEqual(info.title,"Road trip");XCTAssertEqual(info.kind,.video);XCTAssertEqual(info.modes,[.video,.audioMP3])
        XCTAssertEqual(info.playlist.map(\.id),[1,2]);XCTAssertEqual(info.playlist.map(\.title),["First","Second"])
        XCTAssertEqual(info.playlist[0].url.absoluteString,"https://www.youtube.com/watch?v=aaaaaaaaaaa");XCTAssertEqual(info.playlist[0].duration,61)
        XCTAssertEqual(info.playlistStart,2)
        XCTAssertNil(try WebDownloadEngine.parsePlaylist(json,url:URL(string:"https://www.youtube.com/playlist?list=PLabc")!).playlistStart)
        XCTAssertThrowsError(try WebDownloadEngine.parsePlaylist(Data(#"{"_type":"playlist","entries":[]}"#.utf8),url:info.url))
        XCTAssertThrowsError(try WebDownloadEngine.parsePlaylist(Data(#"{"title":"A video","formats":[]}"#.utf8),url:info.url))
    }
    func testNullPlaylistEntriesAreSkippedAndAnUnlistedWatchVideoFallsBackToSingleVideo() throws {
        let json=Data(#"{"_type":"playlist","entries":[null,{"id":"aaaaaaaaaaa","title":"First"},"junk",{"id":"bbbbbbbbbbb","title":"Second"}]}"#.utf8)
        let info=try WebDownloadEngine.parsePlaylist(json,url:URL(string:"https://www.youtube.com/playlist?list=PLabc")!)
        XCTAssertEqual(info.playlist.map(\.title),["First","Second"]);XCTAssertEqual(info.playlist.map(\.id),[1,2])
        XCTAssertThrowsError(try WebDownloadEngine.parsePlaylist(json,url:URL(string:"https://www.youtube.com/watch?v=ccccccccccc&list=PLabc")!))
    }
    func testPlaylistDownloadRejectsSelectionsOutsideThePlaylist() async throws {
        let info=try WebDownloadEngine.parsePlaylist(Data(#"{"_type":"playlist","entries":[{"id":"aaaaaaaaaaa"}]}"#.utf8),url:URL(string:"https://www.youtube.com/playlist?list=PLabc")!)
        let folder=FileManager.default.temporaryDirectory.appendingPathComponent("Nashi-Playlist-\(UUID().uuidString)")
        defer { try? FileManager.default.removeItem(at:folder) }
        for items:Set<Int> in [[],[2]] {
            do { _=try await WebDownloadEngine.download(info,mode:.video,maximumHeight:nil,destination:folder,playlistItems:items) { _,_ in };XCTFail("\(items)") }
            catch NashiError.invalidInput {}
        }
        XCTAssertEqual(try FileManager.default.contentsOfDirectory(atPath:folder.path),[])
    }
    func testFailedPlaylistVideoLeavesNoPartialFilesBesideFinishedOnes() async throws {
        let root=FileManager.default.temporaryDirectory.appendingPathComponent("Nashi-Playlist-\(UUID().uuidString)")
        let tools=root.appendingPathComponent("tools"),destination=root.appendingPathComponent("out")
        try FileManager.default.createDirectory(at:tools,withIntermediateDirectories:true)
        defer { try? FileManager.default.removeItem(at:root) }
        // The first video writes a partial download and unmerged streams, then fails like a dropped connection.
        let script=tools.appendingPathComponent("yt-dlp")
        try """
        #!/bin/sh
        for a; do last=$a; done
        while [ $# -gt 0 ]; do [ "$1" = --output ] && out=$2; shift; done
        dir=$(dirname "$out")
        case "$last" in *aaaaaaaaaaa) echo x > "$dir/First.mp4.part"; echo x > "$dir/First.f137.mp4"; echo x > "$dir/First.f140.m4a"; exit 1;; esac
        echo x > "$dir/Second.mp3"; printf 'NASHI_FILE:"%s"\\n' "$dir/Second.mp3"
        """.write(to:script,atomically:true,encoding:.utf8)
        try FileManager.default.setAttributes([.posixPermissions:0o755],ofItemAtPath:script.path)
        let overrides=["NASHI_YT-DLP":script.path,"NASHI_DENO":"/bin/echo","NASHI_FFMPEG":"/bin/echo"]
        for (key,value) in overrides { setenv(key,value,1) }
        defer { for key in overrides.keys { unsetenv(key) } }
        let info=try WebDownloadEngine.parsePlaylist(Data(#"{"_type":"playlist","title":"Road trip","entries":[{"id":"aaaaaaaaaaa"},{"id":"bbbbbbbbbbb"}]}"#.utf8),url:URL(string:"https://www.youtube.com/playlist?list=PLabc")!)
        let files=try await WebDownloadEngine.download(info,mode:.audioMP3,maximumHeight:nil,destination:destination) { _,_ in }
        XCTAssertEqual(files.map(\.lastPathComponent),["002 - Second.mp3"])
        XCTAssertEqual(try FileManager.default.contentsOfDirectory(atPath:destination.path),["Road trip"])
        XCTAssertEqual(try FileManager.default.contentsOfDirectory(atPath:destination.appendingPathComponent("Road trip").path),["002 - Second.mp3"])
    }
}
