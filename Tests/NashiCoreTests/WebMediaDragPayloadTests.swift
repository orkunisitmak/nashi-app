import XCTest
@testable import NashiCore

final class WebMediaDragPayloadTests:XCTestCase {
    private let id="BaW_jenozKc"
    func testYouTubeHomeLinkedCardKeepsVideoAndThumbnailSeparate() throws {
        let image="https://i.ytimg.com/vi/\(id)/hq720.jpg?sqp=preview"
        let html="<a href='https://www.youtube.com/watch?v=\(id)&amp;list=feed'><img src='\(image)'></a>"
        let media=try XCTUnwrap(WebMediaDragPayload.parse(urls:[image],html:[html]))
        XCTAssertEqual(media.videoURL?.absoluteString,"https://www.youtube.com/watch?v=\(id)")
        XCTAssertEqual(media.imageURL?.absoluteString,image)
        XCTAssertEqual(media.urls.count,2);XCTAssertEqual(media.urls.first,media.videoURL)
        XCTAssertEqual(WebMediaDragPayload.parse(urls:media.urls.map(\.absoluteString)),media)
    }
    func testImageOnlyYouTubeThumbnailRecoversVideoIncludingAnimatedPreviews() throws {
        for path in ["vi/\(id)/mqdefault.jpg","vi_webp/\(id)/hqdefault.webp","an_webp/\(id)/mqdefault_6s.webp"] {
            let url="https://i.ytimg.com/"+path
            let media=try XCTUnwrap(WebMediaDragPayload.parse(urls:[url]))
            XCTAssertEqual(media.videoURL?.absoluteString,"https://www.youtube.com/watch?v=\(id)");XCTAssertEqual(media.imageURL?.absoluteString,url)
        }
    }
    func testYouTubeShareShortsAndWatchURLsHaveAThumbnailChoice() throws {
        for url in ["https://youtu.be/\(id)?si=share","https://m.youtube.com/watch?v=\(id)&list=feed","https://www.youtube.com/shorts/\(id)","https://www.youtube.com/embed/\(id)"] {
            let media=try XCTUnwrap(WebMediaDragPayload.parse(urls:[url]))
            XCTAssertEqual(media.primaryURL.absoluteString,"https://www.youtube.com/watch?v=\(id)")
            XCTAssertEqual(media.imageURL?.absoluteString,"https://i.ytimg.com/vi/\(id)/hqdefault.jpg")
        }
    }
    func testInstagramGridReelWinsOverCoverAndDropsTrackingParameters() throws {
        let html="<a href='https://www.instagram.com/orkunisitmak/reel/DemKc58oBP4/?igsh=share'><img alt='Video' src='https://scontent.cdninstagram.com/v/test.jpg?x=1&amp;y=2'></a>"
        let media=try XCTUnwrap(WebMediaDragPayload.parse(urls:["https://www.instagram.com/orkunisitmak/reel/DemKc58oBP4/"],html:[html]))
        XCTAssertEqual(media.videoURL?.absoluteString,"https://www.instagram.com/reel/DemKc58oBP4/")
        XCTAssertNil(media.imageURL)
        XCTAssertEqual(media.urls,[media.primaryURL])
    }
    func testRelativeInstagramAnchorUsesOnlyTheProvidedSourceAsBase() throws {
        let html="<A HREF=/orkunisitmak/reel/DemKc58oBP4/><IMG SRC='//cdn.example.org/cover.jpg?a=1&#x26;b=2'></A>"
        let media=try XCTUnwrap(WebMediaDragPayload.parse(urls:[],html:[html],baseURLs:["https://www.instagram.com"]))
        XCTAssertEqual(media.videoURL?.absoluteString,"https://www.instagram.com/reel/DemKc58oBP4/")
        XCTAssertNil(media.imageURL)
        XCTAssertNil(WebMediaDragPayload.parse(urls:[],baseURLs:["https://www.instagram.com/orkunisitmak/"]))
    }
    func testOrdinaryLinkedImageStillSavesTheImageRatherThanItsPage() throws {
        let media=try XCTUnwrap(WebMediaDragPayload.parse(urls:["https://example.org/article"],html:["<a href='https://example.org/article'><img src='https://cdn.example.org/photo.png'></a>"]))
        XCTAssertNil(media.videoURL);XCTAssertEqual(media.primaryURL.absoluteString,"https://cdn.example.org/photo.png");XCTAssertEqual(media.urls.count,1)
    }
    func testInstagramPhotoAndCarouselCardsPreserveThePostWithoutVideoChoices() throws {
        for link in ["https://www.instagram.com/orkunisitmak/p/Dax2EpciOrn/?igsh=share","https://www.instagram.com/p/Dax2EpciOrn/"] {
            let html="<a href='\(link)'><img src='https://scontent.cdninstagram.com/cover.jpg'></a>"
            let media=try XCTUnwrap(WebMediaDragPayload.parse(urls:[link],html:[html]))
            XCTAssertTrue(media.isInstagramPhotoPost);XCTAssertNil(media.videoURL)
            XCTAssertEqual(media.primaryURL.absoluteString,"https://www.instagram.com/p/Dax2EpciOrn/")
            XCTAssertEqual(WebMediaDragPayload.parse(urls:media.urls.map(\.absoluteString)),media)
        }
        let linkOnly=try XCTUnwrap(WebMediaDragPayload.parse(urls:["https://www.instagram.com/p/Dax2EpciOrn/"]))
        XCTAssertTrue(linkOnly.isInstagramPhotoPost);XCTAssertNil(linkOnly.videoURL)
        XCTAssertNil(WebMediaDragPayload.parse(urls:["https://www.instagram.com/p/Dax2EpciOrn/","https://www.instagram.com/reel/DemKc58oBP4/"]))
    }
    func testDirectVideoAndPosterStaySeparate() throws {
        let media=try XCTUnwrap(WebMediaDragPayload.parse(urls:["https://example.org/clip.mp4"],html:["<video src='https://example.org/clip.mp4' poster='https://example.org/cover.png'></video>"]))
        XCTAssertEqual(media.videoURL?.lastPathComponent,"clip.mp4");XCTAssertEqual(media.imageURL?.lastPathComponent,"cover.png")
    }
    func testLookalikeHostsAndMalformedIDsCannotCreateAVideoIdentity() {
        for url in ["https://i.ytimg.com.evil.example/vi/\(id)/hq.jpg","https://i.ytimg.com/vi/short/hq.jpg","https://www.youtube.com.evil.example/watch?v=\(id)","https://www.instagram.com.evil.example/reel/DemKc58oBP4/"] {
            XCTAssertNil(WebMediaDragPayload.parse(urls:[url])?.videoURL,url)
        }
    }
    func testMultipleCardsAreRejectedInsteadOfChoosingAnUnrelatedVideo() {
        XCTAssertNil(WebMediaDragPayload.parse(urls:["https://youtu.be/BaW_jenozKc","https://youtu.be/aqz-KE-bpKQ"]))
        XCTAssertNil(WebMediaDragPayload.parse(urls:["https://example.org/a","https://example.org/b"]))
    }
    func testHTMLCannotFetchSubresourcesOrPromoteNonWebSchemes() {
        XCTAssertNil(WebMediaDragPayload.parse(urls:[],html:["<img src='file:///etc/passwd'><a href='javascript:alert(1)'>x</a>"]))
        XCTAssertNil(WebMediaDragPayload.parse(urls:[],html:["<img src='https://example.org/image.jpg'>"+String(repeating:"x",count:131_072)]))
        XCTAssertNil(WebMediaDragPayload.parse(urls:["blob:https://www.youtube.com/id"]))
    }
    func testFirefoxThumbnailDragRecoversTheVideoFromItsClipdata() throws {
        // Layout captured from a Firefox YouTube thumbnail drag on macOS.
        func entry(_ text:String)->Data {
            let utf16=text.data(using:.utf16LittleEndian)!
            return withUnsafeBytes(of:UInt32(utf16.count).bigEndian){Data($0)}+utf16
        }
        let link="https://www.youtube.com/watch?v=\(id)&pp=ugUEEgJlbg%3D%3D"
        let clip=withUnsafeBytes(of:UInt32(1).bigEndian){Data($0)}+entry("text/uri-list")+entry(link)+Data(count:4)
        XCTAssertEqual(WebMediaDragPayload.mozillaClipdataURLs(clip),[link])
        let media=try XCTUnwrap(WebMediaDragPayload.parse(urls:WebMediaDragPayload.mozillaClipdataURLs(clip)))
        XCTAssertEqual(media.videoURL?.absoluteString,"https://www.youtube.com/watch?v=\(id)")
        XCTAssertTrue(WebMediaDragPayload.isBrowserDragCopy(URL(fileURLWithPath:"/var/folders/x/T/mozDraggedFiles/hq720.webp")))
        XCTAssertFalse(WebMediaDragPayload.isBrowserDragCopy(URL(fileURLWithPath:"/Users/me/Pictures/hq720.webp")))
        XCTAssertEqual(WebMediaDragPayload.mozillaClipdataURLs(Data([0,0,0,1,0,0,0,9])),[])
        XCTAssertEqual(WebMediaDragPayload.mozillaClipdataURLs(Data()),[])
    }
}
