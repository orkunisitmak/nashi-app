import XCTest
@testable import NashiCore

final class TextEncodingDetectionTests: XCTestCase {
    func testUTF32TextWithByteOrderMarkIsReadAsUTF32() throws {
        let root = try OutputFiles.temporaryDirectory()
        defer { try? FileManager.default.removeItem(at: root) }
        let text = "1\n00:00:01,000 --> 00:00:02,000\nŞişli İstanbul 😀\n"
        for (bom, little) in [([UInt8]([0xff, 0xfe, 0, 0]), true), ([0, 0, 0xfe, 0xff], false)] {
            var data = Data(bom)
            for scalar in text.unicodeScalars {
                let bytes = (0..<4).map { UInt8(truncatingIfNeeded: scalar.value >> UInt32($0 * 8)) }
                data += little ? bytes : bytes.reversed()
            }
            let url = root.appendingPathComponent("Captions.srt")
            try data.write(to: url)
            XCTAssertEqual(try DocumentText.read(url), text)
            XCTAssertTrue(try SubtitleCodec.convert(url, to: "vtt", token: .init()).contains("Şişli İstanbul 😀"))
        }
        let utf16 = root.appendingPathComponent("UTF-16.txt")
        try Data([0xff, 0xfe, 0x41, 0]).write(to: utf16)
        XCTAssertEqual(try DocumentText.read(utf16), "A")
    }
}
