import XCTest
@testable import NashiCore

final class DocumentAuditRegressionTests: XCTestCase {
    private var root: URL!

    override func setUpWithError() throws {
        root = try OutputFiles.temporaryDirectory()
    }

    override func tearDownWithError() throws {
        try? FileManager.default.removeItem(at: root)
    }

    private func fixture(_ name: String, data: Data) throws -> URL {
        let url = root.appendingPathComponent(name)
        try data.write(to: url)
        return url
    }

    func testCP1252TextPreservesAccentsAndPunctuationThroughOfficeConversion() throws {
        let text = "Café déjà vu — résumé.\n"
        let input = try fixture("Legacy.txt", data: XCTUnwrap(text.data(using: .windowsCP1252)))
        let source = try Data(contentsOf: input)
        XCTAssertEqual(try DocumentText.read(input), text)
        let document = try XCTUnwrap(DocumentEngine.convert(input, to: "docx", options: .init(), token: .init()).first)
        let restored = try XCTUnwrap(DocumentEngine.convert(document, to: "txt", options: .init(), token: .init()).first)
        XCTAssertTrue(try DocumentText.read(restored).contains("Café déjà vu — résumé."))
        XCTAssertEqual(try Data(contentsOf: input), source)
    }

    func testCP1252SubtitlesKeepTimedCuesAndAccentedText() throws {
        let text = "1\r\n00:00:01,250 --> 00:00:02,500\r\nCafé — résumé.\r\n\r\n"
        let input = try fixture("Legacy.srt", data: XCTUnwrap(text.data(using: .windowsCP1252)))
        let result = try SubtitleCodec.convert(input, to: "vtt", token: .init())
        XCTAssertTrue(result.contains("00:00:01.250 --> 00:00:02.500\nCafé — résumé."))
    }

    func testWebVTTEntitiesRenderAsVisibleTextInSRT() throws {
        let source = "WEBVTT\n\n00:00:01.000 --> 00:00:02.000\nTom &amp; Jerry &nbsp;A&lrm;B&rlm;C &lt;end&gt;\n\n"
        let input = try fixture("Entities.vtt", data: Data(source.utf8))
        let srt = try SubtitleCodec.convert(input, to: "srt", token: .init())
        XCTAssertEqual(srt, "1\n00:00:01,000 --> 00:00:02,000\nTom & Jerry \u{00A0}A\u{200E}B\u{200F}C <end>\n")
    }

    func testWebVTTNamedCharacterReferencesRenderInSRTAndText() throws {
        let source = "WEBVTT\n\n00:00:01.000 --> 00:00:02.000\nCaf&eacute; &hellip; &copy; &Omega; &bogus; &amp;eacute;\n\n"
        let input = try fixture("Named.vtt", data: Data(source.utf8))
        XCTAssertEqual(try SubtitleCodec.convert(input, to: "srt", token: .init()), "1\n00:00:01,000 --> 00:00:02,000\nCafé … © Ω &bogus; &eacute;\n")
        XCTAssertEqual(try SubtitleCodec.convert(input, to: "txt", token: .init()), "Café … © Ω &bogus; &eacute;\n")
        XCTAssertEqual(DocumentText.decodeEntities("&eacute; &amp;"), "&eacute; &")
    }

    func testWebVTTNumericCharacterReferencesFollowHTMLRules() throws {
        let source = "WEBVTT\n\n00:00:01.000 --> 00:00:02.000\nIt&#146;s 9&#150;5 &#X41; a&#0;b&#xD800;c&#99999999999;d\n\n"
        let input = try fixture("Numeric.vtt", data: Data(source.utf8))
        XCTAssertEqual(try SubtitleCodec.convert(input, to: "srt", token: .init()), "1\n00:00:01,000 --> 00:00:02,000\nIt’s 9–5 A a\u{FFFD}b\u{FFFD}c\u{FFFD}d\n")
        XCTAssertEqual(try SubtitleCodec.convert(input, to: "txt", token: .init()), "It’s 9–5 A a\u{FFFD}b\u{FFFD}c\u{FFFD}d\n")
        XCTAssertEqual(DocumentText.decodeEntities("&#146;&#X41;"), "\u{92}&#X41;")
    }

    func testWebVTTDirectionMarksSurviveTextExport() throws {
        let source = "WEBVTT\n\n00:00:01.000 --> 00:00:02.000\n<v Bob>&rlm;שלום</v> A&lrm;B &amp;lrm;\n\n"
        let input = try fixture("Marks.vtt", data: Data(source.utf8))
        XCTAssertEqual(try SubtitleCodec.convert(input, to: "txt", token: .init()), "\u{200F}שלום A\u{200E}B &lrm;\n")
    }

    func testUTF16BOMAndBOMLessTextRetainUnicode() throws {
        let text = "Nashi Şişli — café\n"
        let encodings: [(String, Data)] = [
            ("Little BOM.txt", Data([0xff, 0xfe]) + (try XCTUnwrap(text.data(using: .utf16LittleEndian)))),
            ("Big BOM.txt", Data([0xfe, 0xff]) + (try XCTUnwrap(text.data(using: .utf16BigEndian)))),
            ("Little no BOM.txt", try XCTUnwrap(text.data(using: .utf16LittleEndian))),
            ("Big no BOM.txt", try XCTUnwrap(text.data(using: .utf16BigEndian)))
        ]
        for (name, data) in encodings {
            XCTAssertEqual(try DocumentText.read(fixture(name, data: data)), text, name)
        }
        let source = try fixture("BOM less document.txt", data: XCTUnwrap(text.data(using: .utf16LittleEndian)))
        let docx = try XCTUnwrap(DocumentEngine.convert(source, to: "docx", options: .init(), token: .init()).first)
        let restored = try XCTUnwrap(DocumentEngine.convert(docx, to: "txt", options: .init(), token: .init()).first)
        XCTAssertTrue(try DocumentText.read(restored).contains("Nashi Şişli — café"))
        let subtitle = "1\n00:00:00,100 --> 00:00:01,100\nNashi Şişli\n\n"
        let input = try fixture("BOM less.srt", data: XCTUnwrap(subtitle.data(using: .utf16LittleEndian)))
        XCTAssertTrue(try SubtitleCodec.convert(input, to: "vtt", token: .init()).contains("Nashi Şişli"))
    }

    func testOversizedSubtitleTimesThrowWithoutPublishingOutput() throws {
        for hour in ["3000000000000", "9223372036854775807", "99999999999999999999"] {
            let text = "1\n\(hour):00:00,000 --> \(hour):00:01,000\nExample\n\n"
            let input = try fixture("Overflow \(hour).srt", data: Data(text.utf8))
            XCTAssertThrowsError(try DocumentEngine.convert(input, to: "vtt", options: .init(), token: .init())) { error in
                XCTAssertTrue(error is NashiError)
                XCTAssertTrue(error.localizedDescription.lowercased().contains("timestamp"))
            }
            XCTAssertFalse(FileManager.default.fileExists(atPath: input.deletingPathExtension().appendingPathExtension("vtt").path))
            XCTAssertEqual(try String(contentsOf: input, encoding: .utf8), text)
        }
    }

    func testLargeRepresentableSubtitleTimesAndMillisecondPrecisionRemainValid() throws {
        let source = "WEBVTT\n\n2562047788:00:00.001 --> 2562047788:00:00.999\nA long running recording\n\n"
        let input = try fixture("Long recording.vtt", data: Data(source.utf8))
        let result = try SubtitleCodec.convert(input, to: "srt", token: .init())
        XCTAssertTrue(result.contains("2562047788:00:00,001 --> 2562047788:00:00,999"))
    }

    func testSRTTextThatWebVTTReservesIsEscapedInVTT() throws {
        let source = "1\n00:00:01,000 --> 00:00:02,000\n<i>Keep</i> <font color=\"#FF0000\">x</font> < y, I <3 you\nNext --> stop\n\n"
        let input = try fixture("Reserved.srt", data: Data(source.utf8))
        let vtt = try SubtitleCodec.convert(input, to: "vtt", token: .init())
        XCTAssertEqual(vtt, "WEBVTT\n\n00:00:01.000 --> 00:00:02.000\n<i>Keep</i> <font color=\"#FF0000\">x</font> &lt; y, I &lt;3 you\nNext --&gt; stop\n")
        let back = try fixture("Reserved.vtt", data: Data(vtt.utf8))
        XCTAssertTrue(try SubtitleCodec.convert(back, to: "srt", token: .init()).hasSuffix("< y, I <3 you\nNext --> stop\n"))
    }
}
