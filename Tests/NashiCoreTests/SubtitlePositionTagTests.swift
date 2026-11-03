import XCTest
@testable import NashiCore

final class SubtitlePositionTagTests: XCTestCase {
    func testSSAPositionTagIsNotWrittenAsCaptionText() throws {
        let root = try OutputFiles.temporaryDirectory()
        defer { try? FileManager.default.removeItem(at: root) }
        let input = root.appendingPathComponent("Movie.srt")
        try "1\n00:00:01,000 --> 00:00:03,000\n{\\an8}JOHN (on the radio)\n\n2\n00:00:04,000 --> 00:00:06,000\nHello {not a tag}.\n".write(to: input, atomically: true, encoding: .utf8)

        let vtt = try SubtitleCodec.convert(input, to: "vtt", token: .init())
        XCTAssertEqual(vtt, "WEBVTT\n\n00:00:01.000 --> 00:00:03.000\nJOHN (on the radio)\n\n00:00:04.000 --> 00:00:06.000\nHello {not a tag}.\n")
        XCTAssertEqual(try SubtitleCodec.convert(input, to: "txt", token: .init()), "JOHN (on the radio)\n\nHello {not a tag}.\n")
    }

    func testPositionTagAloneOnItsLineLeavesNoBlankLineInTheCue() throws {
        let root = try OutputFiles.temporaryDirectory()
        defer { try? FileManager.default.removeItem(at: root) }
        let input = root.appendingPathComponent("Movie.srt")
        try "1\n00:00:01,000 --> 00:00:03,000\n{\\an8}\nHello\n".write(to: input, atomically: true, encoding: .utf8)

        XCTAssertEqual(try SubtitleCodec.convert(input, to: "vtt", token: .init()), "WEBVTT\n\n00:00:01.000 --> 00:00:03.000\nHello\n")
        XCTAssertEqual(try SubtitleCodec.convert(input, to: "txt", token: .init()), "Hello\n")
    }

    func testSeveralPositionTagsAloneOnTheirLineLeaveNoBlankLineInTheCue() throws {
        let root = try OutputFiles.temporaryDirectory()
        defer { try? FileManager.default.removeItem(at: root) }
        let input = root.appendingPathComponent("Movie.srt")
        try "1\n00:00:01,000 --> 00:00:03,000\n{\\an1}{\\an8}\nHello\n".write(to: input, atomically: true, encoding: .utf8)

        XCTAssertEqual(try SubtitleCodec.convert(input, to: "vtt", token: .init()), "WEBVTT\n\n00:00:01.000 --> 00:00:03.000\nHello\n")
        XCTAssertEqual(try SubtitleCodec.convert(input, to: "txt", token: .init()), "Hello\n")
    }

    func testPositionTagBesideANoBreakSpaceLeavesNoBlankLineInTheCue() throws {
        let root = try OutputFiles.temporaryDirectory()
        defer { try? FileManager.default.removeItem(at: root) }
        let input = root.appendingPathComponent("Movie.srt")
        try "1\n00:00:01,000 --> 00:00:03,000\nHello\n\u{00A0}{\\an8}\nWorld\n".write(to: input, atomically: true, encoding: .utf8)

        XCTAssertEqual(try SubtitleCodec.convert(input, to: "vtt", token: .init()), "WEBVTT\n\n00:00:01.000 --> 00:00:03.000\nHello\nWorld\n")
    }

    func testPositionTagLineAtTheEndOfTheFileTakesItsLineBreakWithIt() throws {
        let root = try OutputFiles.temporaryDirectory()
        defer { try? FileManager.default.removeItem(at: root) }
        let input = root.appendingPathComponent("Movie.srt")
        try "1\n00:00:01,000 --> 00:00:03,000\nHello\n\n2\n00:00:04,000 --> 00:00:06,000\nWorld\n{\\an8}".write(to: input, atomically: true, encoding: .utf8)

        XCTAssertEqual(try SubtitleCodec.convert(input, to: "vtt", token: .init()), "WEBVTT\n\n00:00:01.000 --> 00:00:03.000\nHello\n\n00:00:04.000 --> 00:00:06.000\nWorld\n")
        XCTAssertEqual(try SubtitleCodec.convert(input, to: "txt", token: .init()), "Hello\n\nWorld\n")
    }
}
