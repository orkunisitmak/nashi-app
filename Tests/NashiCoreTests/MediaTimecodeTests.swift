import XCTest
@testable import NashiCore

/// Trim start/end used to accept only raw seconds, so 12:33.900 had to be typed as 753.9.
final class MediaTimecodeTests: XCTestCase {
    func testReadsTimelineStyleTimes() {
        XCTAssertEqual(MediaTimecode.parse("12:33.900")!, 753.9, accuracy: 1e-9)
        XCTAssertEqual(MediaTimecode.parse("12:33,9")!, 753.9, accuracy: 1e-9)
        XCTAssertEqual(MediaTimecode.parse("0:05")!, 5, accuracy: 1e-9)
        XCTAssertEqual(MediaTimecode.parse("1:02:33.5")!, 3753.5, accuracy: 1e-9)
        XCTAssertEqual(MediaTimecode.parse(" 90:00 ")!, 5400, accuracy: 1e-9)
    }

    func testStillReadsPlainSeconds() {
        XCTAssertEqual(MediaTimecode.parse("753.9")!, 753.9, accuracy: 1e-9)
        XCTAssertEqual(MediaTimecode.parse("753,9")!, 753.9, accuracy: 1e-9)
        XCTAssertEqual(MediaTimecode.parse("0")!, 0, accuracy: 1e-9)
    }

    func testRejectsMalformedTimes() {
        for text in ["", ":", "12:", ":30", "12:60", "1:60:00", "1:2:3:4", "-5", "12:3a", "1.5:30", "12:33.9.1", ".", "inf", "nan"] {
            XCTAssertNil(MediaTimecode.parse(text), text)
        }
    }

    func testFormatsLikeThePlayheadAndRoundTrips() {
        XCTAssertEqual(MediaTimecode.format(753.9), "12:33.900")
        XCTAssertEqual(MediaTimecode.format(59.9996), "01:00.000")
        XCTAssertEqual(MediaTimecode.format(3753.5), "1:02:33.500")
        XCTAssertEqual(MediaTimecode.format(.nan), "00:00.000")
        for value in [0, 0.001, 59.999, 753.9, 3599.999, 3753.5] {
            XCTAssertEqual(MediaTimecode.parse(MediaTimecode.format(value))!, value, accuracy: 0.0005)
        }
    }
}
