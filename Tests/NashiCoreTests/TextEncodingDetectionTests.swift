import XCTest
import CoreFoundation
@testable import NashiCore

final class TextEncodingDetectionTests: XCTestCase {
    func testAutomaticEncodingRetainsExistingWesternAndUnicodeDetection() throws {
        let root = try OutputFiles.temporaryDirectory()
        defer { try? FileManager.default.removeItem(at: root) }
        let input = root.appendingPathComponent("Text.txt")
        for (encoding, text) in [(String.Encoding.windowsCP1252, "Café déjà vu"), (.utf8, "Şişli İstanbul 😀"), (.utf16, "Şişli İstanbul 😀"), (.utf32, "Şişli İstanbul 😀")] {
            try XCTUnwrap(text.data(using: encoding)).write(to: input)
            XCTAssertEqual(try DocumentText.read(input), text)
            XCTAssertEqual(try DocumentText.read(input, encoding: "auto"), text)
        }
    }

    func testExplicitCLIEncodingPreservesTurkishAndWesternSubtitles() throws {
        let root = try OutputFiles.temporaryDirectory()
        defer { try? FileManager.default.removeItem(at: root) }
        let turkish = String.Encoding(rawValue: CFStringConvertEncodingToNSStringEncoding(CFStringEncoding(CFStringEncodings.windowsLatin5.rawValue)))
        for (name, encoding, text) in [("windows-1254", turkish, "İstanbul, ışık, şeker, öğün"), ("windows-1252", .windowsCP1252, "Café déjà vu"), ("utf-8", .utf8, "Şişli İstanbul 😀")] {
            let source = "1\n00:00:01,000 --> 00:00:02,000\n" + text + "\n"
            let data = try XCTUnwrap(source.data(using: encoding))
            let input = root.appendingPathComponent(name + ".srt")
            try data.write(to: input)
            let directory = root.appendingPathComponent(name)
            let options = String(decoding: try JSONSerialization.data(withJSONObject: ["textEncoding": name, "outputDirectory": directory.path]), as: UTF8.self)
            XCTAssertEqual(NashiCommandLine.run(arguments: ["nashi-cli", "convert", "vtt", input.path, "--options", options]), 0)
            XCTAssertTrue(try String(contentsOf: directory.appendingPathComponent(name + ".vtt"), encoding: .utf8).contains(text))
            XCTAssertEqual(try Data(contentsOf: input), data)
        }
    }

    func testUnknownOrInvalidExplicitEncodingPublishesNothing() throws {
        let root = try OutputFiles.temporaryDirectory()
        defer { try? FileManager.default.removeItem(at: root) }
        let input = root.appendingPathComponent("Captions.srt")
        try Data([0xff, 0xff]).write(to: input)
        for encoding in ["unsupported", "utf-8"] {
            let directory = root.appendingPathComponent(encoding)
            let options = String(decoding: try JSONSerialization.data(withJSONObject: ["textEncoding": encoding, "outputDirectory": directory.path]), as: UTF8.self)
            XCTAssertEqual(NashiCommandLine.run(arguments: ["nashi-cli", "convert", "vtt", input.path, "--options", options]), 1)
            XCTAssertFalse(FileManager.default.fileExists(atPath: directory.path))
        }
    }

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
