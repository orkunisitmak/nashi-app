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
    func testTextInsideAnotherAttributeValueIsNotReadAsTheLinkOrImage() throws {
        let other="aqz-KE-bpKQ",real="https://cdn.example.org/real.png"
        for quote in ["\"","'"] {
            let html="<a title=\(quote)x href=https://www.youtube.com/watch?v=\(other) \(quote) href=\(quote)https://www.youtube.com/watch?v=\(id)\(quote)>t</a>"
            XCTAssertEqual(WebMediaDragPayload.parse(urls:[],html:[html])?.videoURL?.absoluteString,"https://www.youtube.com/watch?v=\(id)",html)
        }
        let alt=try XCTUnwrap(WebMediaDragPayload.parse(urls:[],html:["<img alt=\"a > b src=https://evil.example/x.jpg \" src=\"\(real)\">"]))
        XCTAssertEqual(alt.primaryURL.absoluteString,real)
        let unquoted=try XCTUnwrap(WebMediaDragPayload.parse(urls:[],html:["<IMG ALT=photo SRC=\(real)>"]))
        XCTAssertEqual(unquoted.primaryURL.absoluteString,real)
        XCTAssertNil(WebMediaDragPayload.parse(urls:[],html:["<a title=' href=https://www.youtube.com/watch?v=\(other) '>t</a>"]))
        XCTAssertNil(WebMediaDragPayload.parse(urls:[],html:["<img alt=\" src=https://evil.example/x.jpg \">"]))
    }
    func testTagTextInsideAnEarlierQuotedValueIsNotReadAsATag() throws {
        let other="aqz-KE-bpKQ",real="https://cdn.example.org/real.png"
        let image=try XCTUnwrap(WebMediaDragPayload.parse(urls:[],html:["<div title=\"<img src='https://evil.example/x.jpg'>\"><img src='\(real)'></div>"]))
        XCTAssertEqual(image.primaryURL.absoluteString,real)
        let link=try XCTUnwrap(WebMediaDragPayload.parse(urls:[],html:["<div title='<a href=\"https://youtu.be/\(other)\">'><a href=\"https://youtu.be/\(id)\">t</a></div>"]))
        XCTAssertEqual(link.videoURL?.absoluteString,"https://www.youtube.com/watch?v=\(id)")
        XCTAssertNil(WebMediaDragPayload.parse(urls:[],html:["<div title=\"<img src='https://evil.example/x.jpg'>\">t</div>"]))
        XCTAssertNil(WebMediaDragPayload.parse(urls:[],html:["<div title='<video src=https://evil.example/x.mp4 >'>t</div>"]))
        XCTAssertNil(WebMediaDragPayload.parse(urls:[],html:["<div title=\"<img src='https://evil.example/x.jpg'>"]))
    }
}
