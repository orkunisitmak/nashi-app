import XCTest
@testable import NashiCore

final class PDFPageRangeSpacingTests:XCTestCase {
    // People type "1 - 3" as often as "1-3"; Windows already trims both sides of the dash.
    func testSpacesAroundTheDashKeepTheRange() throws {
        XCTAssertEqual(try PDFEngine.pageOrder("1 - 3, 5",count:5),[0,1,2,4])
        XCTAssertEqual(try PDFEngine.pageOrder("1 -3",count:5),[0,1,2])
        XCTAssertEqual(try PDFEngine.pageOrder("4- 2",count:5),[3,2,1])
    }
    func testSpacingDoesNotLoosenRangeChecks() {
        XCTAssertThrowsError(try PDFEngine.pageOrder("1 2",count:5))
        XCTAssertThrowsError(try PDFEngine.pageOrder("1 - 6",count:5))
        XCTAssertThrowsError(try PDFEngine.pageOrder(" - ",count:5))
    }
}
