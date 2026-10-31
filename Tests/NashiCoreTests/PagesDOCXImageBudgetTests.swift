import XCTest
@testable import NashiCore

final class PagesDOCXImageBudgetTests: XCTestCase {
    func testAggregateImageBytesAreBoundedWithoutChangingEarlierAccounting() throws {
        var budget = PagesDOCXImageBudget(maximumBytes: 8, maximumImages: 5)
        try budget.accountForImage(bytes: 5)
        try budget.accountForImage(bytes: 3)
        XCTAssertThrowsError(try budget.accountForImage(bytes: 1))
        XCTAssertEqual(budget.imageCount, 2)
        XCTAssertEqual(budget.totalBytes, 8)
    }

    func testImageCountIsBoundedEvenForTinyRepeatedEmbeds() throws {
        var budget = PagesDOCXImageBudget(maximumBytes: 100, maximumImages: 2)
        try budget.accountForImage(bytes: 1)
        try budget.accountForImage(bytes: 1)
        XCTAssertThrowsError(try budget.accountForImage(bytes: 1))
        XCTAssertEqual(budget.imageCount, 2)
        XCTAssertEqual(budget.totalBytes, 2)
    }

    func testInvalidAndOversizedImageLengthsFailClosed() throws {
        var budget = PagesDOCXImageBudget(maximumBytes: 8, maximumImages: 2)
        XCTAssertThrowsError(try budget.accountForImage(bytes: -1))
        XCTAssertThrowsError(try budget.accountForImage(bytes: Int64.max))
        XCTAssertEqual(budget.imageCount, 0)
        XCTAssertEqual(budget.totalBytes, 0)
        try budget.accountForImage(bytes: 8)
    }
}
