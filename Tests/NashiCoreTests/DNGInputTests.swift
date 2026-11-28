import XCTest
@testable import NashiCore

final class DNGInputTests: XCTestCase {
    func testDNGIsAnInputOnlyImageFormat() {
        let input=URL(fileURLWithPath:"/tmp/RAW.DNG")
        XCTAssertEqual(FileCategory.detect(input),.image)
        XCTAssertEqual(FeatureCatalog.formats(for:[input]),["jpg","png","webp","heic","tiff","avif","bmp","pdf"])
        XCTAssertFalse(FeatureCatalog.conversionMatrix.values.contains { $0.contains("dng") })
        // DNG cannot be written back, so in-place editors that keep the input extension stay hidden.
        XCTAssertEqual(FeatureCatalog.singleTools["dng"],[])
        XCTAssertEqual(FeatureCatalog.multipleTools["dng"],[.createPDF,.createCollage])
    }

    func testDNGMixesWithOtherPhotosForSharedDestinations() {
        let inputs=[URL(fileURLWithPath:"/tmp/a.dng"),URL(fileURLWithPath:"/tmp/b.jpg")]
        XCTAssertEqual(FeatureCatalog.formats(for:inputs),["png","webp","heic","tiff","avif","bmp","pdf"])
        XCTAssertEqual(FeatureCatalog.tools(for:inputs).filter { $0 != .readQRCode },[.createPDF,.createCollage])
    }
}
