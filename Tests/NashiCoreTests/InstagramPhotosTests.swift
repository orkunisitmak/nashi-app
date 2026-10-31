import XCTest
import ImageIO
@testable import NashiCore

final class InstagramPhotosTests:XCTestCase {
    private let post=URL(string:"https://www.instagram.com/p/Dax2EpciOrn/")!
    private func photo(_ suffix:String)->[String:Any] {
        ["formats":[],"thumbnails":[["url":"https://scontent.cdninstagram.com/\(suffix)-small.jpg","width":320,"height":400],
                                     ["url":"https://scontent.cdninstagram.com/\(suffix).jpg","width":1080,"height":1350]]]
    }
    private var video:[String:Any] {
        ["formats":[["url":"https://scontent.cdninstagram.com/video.mp4"]],
         "thumbnails":[["url":"https://scontent.cdninstagram.com/video-cover.jpg","width":1080,"height":1920]]]
    }
    private func parse(_ value:[String:Any]) throws -> WebMediaInfo {
        try InstagramPhotos.parse(JSONSerialization.data(withJSONObject:value),url:post)
    }
    func testMixedCarouselKeepsPhotosInOrderWithoutVideoCovers() throws {
        let info=try parse(["entries":[photo("first"),video,photo("third")]])
        XCTAssertEqual(info.kind,.image);XCTAssertEqual(info.modes,[.original])
        XCTAssertEqual(info.photos.map(\.id),[1,3]);XCTAssertEqual(info.skippedVideoCount,1)
        XCTAssertEqual(info.photos.map{$0.url.lastPathComponent},["first.jpg","third.jpg"])
        XCTAssertEqual(info.photos.first?.width,1080);XCTAssertEqual(info.photos.first?.height,1350)
    }
    func testSinglePhotoDoesNotBecomeVideoOrThumbnailMode() throws {
        let info=try parse(photo("single"))
        XCTAssertEqual(info.photos.count,1);XCTAssertEqual(info.modes,[.original]);XCTAssertFalse(info.direct)
        XCTAssertEqual(info.url,post);XCTAssertEqual(info.skippedVideoCount,0)
    }
    func testMissingDimensionsPreferFullOriginalOverSquareGridCrop() throws {
        let root:[String:Any]=["thumbnails":[
            ["url":"https://scontent.cdninstagram.com/photo.jpg?stp=c0.386.2316.2316a_dst-jpg_s1080x1080_tt6"],
            ["url":"https://scontent.cdninstagram.com/photo.jpg?stp=dst-jpg_p1080x1080_tt6"],
            ["url":"https://scontent.cdninstagram.com/photo.jpg?stp=dst-jpg_e35_tt6"]]]
        let info=try parse(root)
        XCTAssertEqual(URLComponents(url:info.photos[0].url,resolvingAgainstBaseURL:false)?.queryItems?.first?.value,"dst-jpg_e35_tt6")
    }
    func testVideoOnlyPostCannotBeDownloadedAsPhotoCovers() throws {
        XCTAssertThrowsError(try parse(["entries":[video,video]]))
    }
    func testMissingOrUnsafePhotoDoesNotSilentlyReturnPartialGallery() throws {
        for url in ["file:///etc/passwd","https://fbcdn.net.evil.example/a.jpg","https://user:secret@scontent.cdninstagram.com/a.jpg"] {
            let missing:[String:Any]=["thumbnails":[["url":url]]]
            XCTAssertThrowsError(try parse(["entries":[photo("valid"),missing]]))
        }
        XCTAssertThrowsError(try parse(["entries":Array(repeating:photo("photo"),count:51)]))
    }
    func testEmptyAndUnknownSelectionLeaveNoPartialAlbum() async throws {
        let info=try parse(["entries":[photo("first"),photo("second")]])
        let destination=FileManager.default.temporaryDirectory.appendingPathComponent("Nashi-Gallery-\(UUID().uuidString)")
        defer{try? FileManager.default.removeItem(at:destination)}
        for selection:Set<Int> in [[],[999]] {
            do {
                _=try await WebDownloadEngine.download(info,mode:.original,maximumHeight:nil,destination:destination,selectedPhotos:selection,progress:{_,_ in})
                XCTFail("An invalid photo selection must not start a download")
            } catch {}
            // Not even the destination folder the failed download created is left.
            XCTAssertFalse(FileManager.default.fileExists(atPath:destination.path))
        }
    }
    func testOptInRealCarouselDownloadsEveryPhotoInOrder() async throws {
        guard let text=ProcessInfo.processInfo.environment["NASHI_TEST_INSTAGRAM_PHOTOS"] else { throw XCTSkip("Live public Instagram check is opt-in") }
        let info=try await WebDownloadEngine.inspect(try WebDownloadEngine.link(text))
        XCTAssertGreaterThan(info.photos.count,1);XCTAssertEqual(info.modes,[.original])
        let destination=FileManager.default.temporaryDirectory.appendingPathComponent("Nashi-Instagram-Live-\(UUID().uuidString)")
        defer{try? FileManager.default.removeItem(at:destination)}
        let files=try await WebDownloadEngine.download(info,mode:.original,maximumHeight:nil,destination:destination,progress:{_,_ in})
        XCTAssertEqual(files.count,info.photos.count)
        for (photo,file) in zip(info.photos,files) {
            XCTAssertEqual(file.deletingPathExtension().lastPathComponent,String(format:"%02d",photo.id))
            XCTAssertTrue(file.deletingLastPathComponent().lastPathComponent.hasPrefix("Instagram "))
            let image=try XCTUnwrap(CGImageSourceCreateWithURL(file as CFURL,nil))
            let properties=try XCTUnwrap(CGImageSourceCopyPropertiesAtIndex(image,0,nil) as? [CFString:Any])
            if let width=photo.width { XCTAssertEqual(properties[kCGImagePropertyPixelWidth] as? Int,width) }
            if let height=photo.height { XCTAssertEqual(properties[kCGImagePropertyPixelHeight] as? Int,height) }
        }
    }
}
