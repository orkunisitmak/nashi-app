import XCTest
@testable import NashiCore

/// Amazon S3 labels objects uploaded without a type as binary/octet-stream. Browsers
/// still show such a photo, so its copied address must stay a direct image download.
final class WebDirectBinaryOctetStreamTests:XCTestCase {
    func testS3DefaultTypeFallsBackToTheFileExtensionLikeOctetStream() {
        let image:WebMediaKind?=WebDownloadEngine.directKind(mime:"binary/octet-stream",extension:"jpg")
        let video:WebMediaKind?=WebDownloadEngine.directKind(mime:"Binary/Octet-Stream",extension:"MP4")
        let audio:WebMediaKind?=WebDownloadEngine.directKind(mime:"binary/octet-stream",extension:"m4a")
        XCTAssertEqual(image,.image);XCTAssertEqual(video,.video);XCTAssertEqual(audio,.audio)
    }
    func testBinaryTypeStillNeedsAMediaExtension() {
        for ext in ["","html","zip","m3u8","mpd","exe"] {
            XCTAssertNil(WebDownloadEngine.directKind(mime:"binary/octet-stream",extension:ext),ext)
        }
        XCTAssertNil(WebDownloadEngine.directKind(mime:"binary/x-unknown",extension:"jpg"))
    }
}
