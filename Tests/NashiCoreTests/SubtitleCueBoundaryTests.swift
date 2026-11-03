import XCTest
@testable import NashiCore

final class SubtitleCueBoundaryTests: XCTestCase {
    func testPackedWebVTTPreservesNumericPayloadLines() throws {
        for header in ["WEBVTT", "\u{feff}WEBVTT captions"] {
            for newline in ["\n", "\r\n"] {
                for text in ["42", "Year\n1999"] {
                    let source = (header + "\n\n00:01.000 --> 00:02.000\n" + text + "\n00:03.000 --> 00:04.000\nWorld\n").replacingOccurrences(of: "\n", with: newline)
                    XCTAssertEqual(try SubtitleCodec.parse(source).map(\.text), [text, "World"])
                    let root = try OutputFiles.temporaryDirectory()
                    defer { try? FileManager.default.removeItem(at: root) }
                    let input = root.appendingPathComponent("Captions.vtt")
                    try source.write(to: input, atomically: true, encoding: .utf8)
                    XCTAssertTrue(try SubtitleCodec.convert(input, to: "srt", token: .init()).contains(text))
                    XCTAssertEqual(try SubtitleCodec.convert(input, to: "txt", token: .init()), text + "\n\nWorld\n")
                }
            }
        }
    }

    func testCueTextEndsAtTheNextTimingLine() throws {
        let srt = try SubtitleCodec.parse("1\n00:00:01,000 --> 00:00:02,000\nHello\n2\n00:00:03,000 --> 00:00:04,000\nWorld\n")
        XCTAssertEqual(srt.map(\.text), ["Hello", "World"])
        XCTAssertEqual(srt.map(\.start), [1000, 3000])
        let vtt = try SubtitleCodec.parse("WEBVTT\n\n00:01.000 --> 00:02.000\nHello\n00:03.000 --> 00:04.000\nWorld\n")
        XCTAssertEqual(vtt.map(\.text), ["Hello", "World"])
        XCTAssertEqual(try SubtitleCodec.parse("1\n00:00:01,000 --> 00:00:02,000\nA --> B\nYear\n1999\n").map(\.text), ["A --> B\nYear\n1999"])
    }
}
