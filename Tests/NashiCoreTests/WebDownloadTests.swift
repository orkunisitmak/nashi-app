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
        let base=try WebDownloadEngine.baseArguments()
        for flag in ["--ignore-config","--no-plugin-dirs","--no-remote-components","--no-playlist","--extractor-args","youtube:player_client=default,-android_vr,android"] { XCTAssertTrue(base.contains(flag)) }
        let info=try WebDownloadEngine.parseMetadata(Data(#"{"formats":[{"height":1080}]}"#.utf8),url:url)
        let args=try WebDownloadEngine.downloadArguments(info,mode:.video,maximumHeight:720,directory:URL(fileURLWithPath:"/tmp/stage"))
        for flag in ["--ignore-config","--no-plugin-dirs","--no-remote-components","--no-playlist","--no-overwrites","--extractor-args","youtube:player_client=default,-android_vr,android"] { XCTAssertTrue(args.contains(flag)) }
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
}
