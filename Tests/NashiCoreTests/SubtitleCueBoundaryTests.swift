import XCTest
@testable import NashiCore

final class SubtitleCueBoundaryTests: XCTestCase {
    func testCueTextEndsAtTheNextTimingLine() throws {
        let srt = try SubtitleCodec.parse("1\n00:00:01,000 --> 00:00:02,000\nHello\n2\n00:00:03,000 --> 00:00:04,000\nWorld\n")
        XCTAssertEqual(srt.map(\.text), ["Hello", "World"])
        XCTAssertEqual(srt.map(\.start), [1000, 3000])
        let vtt = try SubtitleCodec.parse("WEBVTT\n\n00:01.000 --> 00:02.000\nHello\n00:03.000 --> 00:04.000\nWorld\n")
        XCTAssertEqual(vtt.map(\.text), ["Hello", "World"])
        XCTAssertEqual(try SubtitleCodec.parse("1\n00:00:01,000 --> 00:00:02,000\nA --> B\nYear\n1999\n").map(\.text), ["A --> B\nYear\n1999"])
    }
}
