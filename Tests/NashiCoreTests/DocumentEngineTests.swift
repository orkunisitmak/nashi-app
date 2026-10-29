import XCTest
import Foundation
import AppKit
import PDFKit
import CoreText
import CryptoKit
@testable import NashiCore

final class DocumentEngineTests: XCTestCase {
    private var root: URL!
    override func setUpWithError() throws { root = try OutputFiles.temporaryDirectory() }
    override func tearDownWithError() throws { if let root { try? FileManager.default.removeItem(at: root) } }

    func testSubtitleRoundTripKeepsTimingUnicodeAndCueText() throws {
        let input = try file("Captions.srt", "1\n00:00:01,250 --> 00:00:03,875\n<b>Merhaba Şişli</b>\nSecond line\n\n2\n01:02:03,004 --> 01:02:04,999\nA &amp; B\n")
        let vtt = try convert(input, "vtt")
        XCTAssertTrue(try DocumentText.read(vtt).hasPrefix("WEBVTT\n"))
        let restored = try convert(vtt, "srt")
        let cues = try SubtitleCodec.parse(DocumentText.read(restored))
        XCTAssertEqual(cues.count, 2)
        XCTAssertEqual(cues[0].start, 1250)
        XCTAssertEqual(cues[0].end, 3875)
        XCTAssertEqual(cues[1].start, 3_723_004)
        XCTAssertTrue(cues[0].text.contains("Şişli"))
        XCTAssertNotEqual(restored, input)
        let txt = try convert(vtt, "txt")
        let plain = try DocumentText.read(txt)
        XCTAssertTrue(plain.contains("Merhaba Şişli"))
        XCTAssertTrue(plain.contains("A & B"))
        XCTAssertFalse(plain.contains("<b>"))
    }

    func testWebVTTSkipsNotesAndStylesAndUnderstandsMinuteTimestamps() throws {
        let source = """
        WEBVTT

        NOTE ignored note
        00:00.000 --> 00:01.000

        STYLE
        ::cue { color: white; }

        caption-1
        00:01.250 --> 00:02.500 align:start position:10%
        <v Orkun><c.yellow>Hello</c></v>

        """
        let cues = try SubtitleCodec.parse(source)
        XCTAssertEqual(cues.count, 1)
        XCTAssertEqual(cues[0].start, 1250)
        XCTAssertEqual(cues[0].settings, "align:start position:10%")
        let input = try file("Cue.vtt", source)
        let srt = try DocumentText.read(convert(input, "srt"))
        XCTAssertTrue(srt.contains("Hello"))
        XCTAssertFalse(srt.contains("<v "))
        XCTAssertThrowsError(try SubtitleCodec.parse("1\n00:00:03,000 --> 00:00:01,000\nBad\n"))
        XCTAssertThrowsError(try SubtitleCodec.parse("not subtitles"))
    }

    func testZIPTARGZIPRoundTripsNestedFilesAndUnicodeWithoutOverwritingOriginals() throws {
        let contents = root.appendingPathComponent("Payload", isDirectory: true)
        try FileManager.default.createDirectory(at: contents.appendingPathComponent("empty", isDirectory: true), withIntermediateDirectories: true)
        try FileManager.default.createDirectory(at: contents.appendingPathComponent("nested", isDirectory: true), withIntermediateDirectories: true)
        try Data("Merhaba Şişli\n".utf8).write(to: contents.appendingPathComponent("nested/şeker.txt"))
        try Data().write(to: contents.appendingPathComponent("empty.txt"))
        try Data([0, 1, 2, 0, 255]).write(to: contents.appendingPathComponent("binary.dat"))
        try FileManager.default.createSymbolicLink(atPath: contents.appendingPathComponent("safe-link").path, withDestinationPath: "nested/şeker.txt")
        for sourceFormat in ["zip", "tar", "gz"] {
            let source = root.appendingPathComponent("Original \(sourceFormat)").appendingPathExtension(sourceFormat)
            try ArchiveCodec.create(from: contents, at: source, format: sourceFormat, token: .init())
            let original = try Data(contentsOf: source)
            for target in ["zip", "tar", "gz"] where sourceFormat != target {
                let result = try convert(source, target)
                XCTAssertTrue(FileManager.default.fileExists(atPath: result.path))
                let folder = try XCTUnwrap(DocumentEngine.extractArchive(result, options: .init(), token: .init()).first)
                XCTAssertEqual(try String(contentsOf: folder.appendingPathComponent("nested/şeker.txt"), encoding: .utf8), "Merhaba Şişli\n")
                XCTAssertEqual(try Data(contentsOf: folder.appendingPathComponent("binary.dat")), Data([0, 1, 2, 0, 255]))
                XCTAssertEqual(try FileManager.default.destinationOfSymbolicLink(atPath: folder.appendingPathComponent("safe-link").path), "nested/şeker.txt")
                var isDirectory: ObjCBool = false
                XCTAssertTrue(FileManager.default.fileExists(atPath: folder.appendingPathComponent("empty").path, isDirectory: &isDirectory))
                XCTAssertTrue(isDirectory.boolValue)
            }
            XCTAssertEqual(try Data(contentsOf: source), original)
        }
    }

    func testArchiveCompressionProfilesAffectZIPAndGZIPAndRemainExtractable() throws {
        let payload=root.appendingPathComponent("Compression payload",isDirectory:true)
        try FileManager.default.createDirectory(at:payload,withIntermediateDirectories:true)
        let bytes=Data(String(repeating:"Nashi compression fixture 0123456789\n",count:12000).utf8)
        try bytes.write(to:payload.appendingPathComponent("payload.txt"))
        let source=root.appendingPathComponent("Compression source.tar")
        try ArchiveCodec.create(from:payload,at:source,format:"tar",token:.init())
        for format in ["zip","gz"] {
            var sizes:[Int]=[]
            for preset in ["fast","balanced","maximum"] {
                var options=OperationOptions();options.compressionPreset=preset
                let output=try XCTUnwrap(DocumentEngine.convert(source,to:format,options:options,token:.init()).first)
                sizes.append(try Data(contentsOf:output).count)
                let extracted=root.appendingPathComponent("\(format) \(preset)",isDirectory:true)
                try ArchiveCodec.extract(output,into:extracted,token:.init())
                XCTAssertEqual(try Data(contentsOf:extracted.appendingPathComponent("payload.txt")),bytes)
            }
            XCTAssertLessThan(sizes[1],sizes[0]/4,format)
            XCTAssertLessThanOrEqual(sizes[2],sizes[1],format)
        }
    }

    func testStoredRARAndOrdinaryGZIPAreReadable() throws {
        let rar = root.appendingPathComponent("Stored.rar")
        try Self.storedRAR(name: "hello.txt", payload: Data("RAR round trip\n".utf8)).write(to: rar)
        for format in ["zip", "tar", "gz"] {
            let converted = try convert(rar, format)
            let extracted = try XCTUnwrap(DocumentEngine.extractArchive(converted, options: .init(), token: .init()).first)
            XCTAssertEqual(try DocumentText.read(extracted.appendingPathComponent("hello.txt")), "RAR round trip\n")
        }
        let txt = try file("Single.txt", "one gzip stream\n")
        let gzip = try ProcessRunner.run(URL(fileURLWithPath: "/usr/bin/gzip"), ["-c", txt.path])
        let gz = root.appendingPathComponent("Single.txt.gz")
        try gzip.output.write(to: gz)
        let folder = try XCTUnwrap(DocumentEngine.extractArchive(gz, options: .init(), token: .init()).first)
        XCTAssertEqual(try DocumentText.read(folder.appendingPathComponent("Single.txt")), "one gzip stream\n")
    }

    func testArchiveRejectsTraversalAbsolutePathsEscapingLinksAndInvalidContainers() throws {
        let outside = root.appendingPathComponent("outside.txt")
        for (index, name) in ["../outside.txt", "/tmp/nashi-escape.txt", "C:\\outside.txt", "folder/../../outside.txt"].enumerated() {
            let source = root.appendingPathComponent("Traversal \(index).tar")
            try Self.tar(name: name, payload: Data("escape".utf8)).write(to: source)
            XCTAssertThrowsError(try DocumentEngine.extractArchive(source, options: .init(), token: .init()))
            XCTAssertFalse(FileManager.default.fileExists(atPath: outside.path))
        }
        let link = root.appendingPathComponent("Escape link.tar")
        try Self.tar(name: "link", payload: Data(), type: 50, link: "../../outside").write(to: link)
        XCTAssertThrowsError(try DocumentEngine.extractArchive(link, options: .init(), token: .init()))
        for ext in ["zip", "rar", "gz"] {
            let invalid = try file("Invalid.\(ext)", "plain text")
            XCTAssertThrowsError(try DocumentEngine.extractArchive(invalid, options: .init(), token: .init()))
        }
    }

    func testUpstreamRAR4AndCompressedRAR5Interoperability() throws {
        let fixtures = URL(fileURLWithPath: #filePath).deletingLastPathComponent().deletingLastPathComponent().deletingLastPathComponent().appendingPathComponent("Reference/archive-fixtures")
        for name in ["test_read_format_rar.rar", "test_read_format_rar5_stored.rar", "test_read_format_rar5_compressed.rar"] {
            let source = root.appendingPathComponent(name)
            try FileManager.default.copyItem(at: fixtures.appendingPathComponent(name), to: source)
            for format in ["zip", "tar", "gz"] {
                let archive = try convert(source, format)
                let output = try XCTUnwrap(DocumentEngine.extractArchive(archive, options: .init(), token: .init()).first)
                if name.contains("compressed") {
                    let bytes = try Data(contentsOf: output.appendingPathComponent("test.bin"))
                    XCTAssertEqual(bytes.count, 1200)
                    XCTAssertEqual(SHA256.hash(data: bytes).map { String(format: "%02x", $0) }.joined(), "588870a2dade35c2650fbb7898c9a9c7f21fce7c281198604e8d0c9737f2c375")
                } else if name.contains("rar5") {
                    XCTAssertTrue(try DocumentText.read(output.appendingPathComponent("helloworld.txt")).contains("hello libarchive"))
                } else {
                    XCTAssertTrue(try DocumentText.read(output.appendingPathComponent("test.txt")).contains("test text document"))
                    XCTAssertEqual(try FileManager.default.destinationOfSymbolicLink(atPath: output.appendingPathComponent("testlink").path), "test.txt")
                }
            }
        }
    }

    func testDanglingLinkNamedLikeTheExtractedFolderDoesNotBlockExtraction() throws {
        let fm = FileManager.default
        let link = root.appendingPathComponent("Bundle Extracted")
        try fm.createSymbolicLink(atPath: link.path, withDestinationPath: root.appendingPathComponent("Missing folder").path)
        let payload = root.appendingPathComponent("Payload", isDirectory: true)
        try fm.createDirectory(at: payload, withIntermediateDirectories: true)
        try Data("data\n".utf8).write(to: payload.appendingPathComponent("a.txt"))
        let archive = root.appendingPathComponent("Bundle.zip")
        try ArchiveCodec.create(from: payload, at: archive, format: "zip", token: .init())
        XCTAssertEqual(try DocumentEngine.extractArchive(archive, options: .init(), token: .init()).first?.lastPathComponent, "Bundle Extracted 2")
        XCTAssertEqual(try fm.destinationOfSymbolicLink(atPath: link.path), root.appendingPathComponent("Missing folder").path)
    }

    func testDanglingLinksBesideTheInputKeepTheirNamesAndAreNotReplaced() throws {
        let fm = FileManager.default
        func danglingLink(_ name: String) throws -> URL {
            let link = root.appendingPathComponent(name)
            try fm.createSymbolicLink(atPath: link.path, withDestinationPath: root.appendingPathComponent("Missing \(name)").path)
            return link
        }
        let captionsLink = try danglingLink("Captions.vtt")
        let tarLink = try danglingLink("Bundle.tar")
        let subtitle = try file("Captions.srt", "1\n00:00:01,000 --> 00:00:02,000\nHello\n")
        XCTAssertEqual(try convert(subtitle, "vtt").lastPathComponent, "Captions 2.vtt")
        let payload = root.appendingPathComponent("Payload", isDirectory: true)
        try fm.createDirectory(at: payload, withIntermediateDirectories: true)
        try Data("data\n".utf8).write(to: payload.appendingPathComponent("a.txt"))
        let archive = root.appendingPathComponent("Bundle.zip")
        try ArchiveCodec.create(from: payload, at: archive, format: "zip", token: .init())
        XCTAssertEqual(try convert(archive, "tar").lastPathComponent, "Bundle 2.tar")
        for link in [captionsLink, tarLink] {
            XCTAssertEqual(try fm.destinationOfSymbolicLink(atPath: link.path), root.appendingPathComponent("Missing \(link.lastPathComponent)").path)
        }
    }

    func testCancellationProducesNoPublishedDocumentOrExtractedFolder() throws {
        let input = try file("Cancelled.txt", "content")
        let token = CancellationToken(); token.cancel()
        XCTAssertThrowsError(try DocumentEngine.convert(input, to: "docx", options: .init(), token: token))
        XCTAssertFalse(FileManager.default.fileExists(atPath: root.appendingPathComponent("Cancelled.docx").path))
    }

    func testOfficeTextMarkdownAndEPUBRoutesPreserveContent() throws {
        let markdown = try file("Document.md", "# Nashi fixture\n\nMerhaba Şişli & café.\n\n- Apple\n- Orange\n\n**Bold text** and *italic text*.\n")
        let original = try Data(contentsOf: markdown)
        let docx = try convert(markdown, "docx")
        let txt = try convert(docx, "txt")
        XCTAssertTrue(try DocumentText.read(txt).contains("Merhaba Şişli"))
        let html = try convert(docx, "html")
        XCTAssertTrue(try DocumentText.read(html).contains("Nashi fixture"))
        let md = try convert(docx, "md")
        XCTAssertTrue(try DocumentText.read(md).contains("Nashi fixture"))
        let epub = try convert(docx, "epub")
        let restored = try convert(epub, "docx")
        let restoredText = try convert(restored, "txt")
        XCTAssertTrue(try DocumentText.read(restoredText).contains("Merhaba Şişli"))
        let pdf = try convert(markdown, "pdf")
        XCTAssertGreaterThan(try XCTUnwrap(PDFDocument(url: pdf)).pageCount, 0)
        var options = OperationOptions(); options.dpi = 72
        let png = try DocumentEngine.convert(docx, to: "png", options: options, token: .init())
        XCTAssertFalse(png.isEmpty)
        for image in png {
            XCTAssertTrue(FileManager.default.fileExists(atPath: image.path))
            XCTAssertTrue(image.path.hasPrefix(root.path + "/"))
            XCTAssertNotNil(NSImage(contentsOf: image))
        }
        XCTAssertEqual(try Data(contentsOf: markdown), original)
    }

    func testRTFRTFDAndLegacyResourceForkClippingToDOCX() throws {
        let attributed = NSAttributedString(string: "Legacy fixture Şişli\n", attributes: [.font: NSFont.systemFont(ofSize: 14)])
        let rtfData = try attributed.data(from: NSRange(location: 0, length: attributed.length), documentAttributes: [.documentType: NSAttributedString.DocumentType.rtf])
        let rtf = root.appendingPathComponent("Rich.rtf"); try rtfData.write(to: rtf)
        let rtfd = root.appendingPathComponent("Rich.rtfd", isDirectory: true)
        let wrapper = try attributed.fileWrapper(from: NSRange(location: 0, length: attributed.length), documentAttributes: [.documentType: NSAttributedString.DocumentType.rtfd])
        try wrapper.write(to: rtfd, options: .atomic, originalContentsURL: nil)
        let clipping = root.appendingPathComponent("Legacy.textClipping")
        try Data().write(to: clipping)
        try Self.resourceFork(type: "utf8", payload: Data("Legacy fixture Şişli\n".utf8)).write(to: URL(fileURLWithPath: clipping.path + "/..namedfork/rsrc"))
        for source in [rtf, rtfd, clipping] {
            let output = try convert(source, "docx")
            let txt = try convert(output, "txt")
            XCTAssertTrue(try DocumentText.read(txt).contains("Legacy fixture Şişli"), source.lastPathComponent)
        }
    }

    func testUTF16TextInputIsNormalizedBeforeOfficeImport() throws {
        let input = root.appendingPathComponent("UTF16.txt")
        try "UTF16 fixture Şişli\n".data(using: .utf16)!.write(to: input)
        XCTAssertTrue(try DocumentText.read(convert(convert(input, "docx"), "txt")).contains("UTF16 fixture Şişli"))
    }

    func testRTFDAttachmentSurvivesDOCXAndSelfContainedHTML() throws {
        let bitmap = try XCTUnwrap(NSBitmapImageRep(bitmapDataPlanes: nil, pixelsWide: 32, pixelsHigh: 24, bitsPerSample: 8, samplesPerPixel: 4, hasAlpha: true, isPlanar: false, colorSpaceName: .deviceRGB, bytesPerRow: 0, bitsPerPixel: 0))
        for y in 0..<24 { for x in 0..<32 { bitmap.setColor(NSColor(deviceRed: 1, green: 0.5, blue: 0, alpha: 1), atX: x, y: y) } }
        let image = try XCTUnwrap(bitmap.representation(using: .png, properties: [:]))
        let wrapper = FileWrapper(regularFileWithContents: image); wrapper.preferredFilename = "orange.png"
        let text = NSMutableAttributedString(string: "Image fixture\n")
        text.append(NSAttributedString(attachment: NSTextAttachment(fileWrapper: wrapper)))
        let rtfd = root.appendingPathComponent("With image.rtfd", isDirectory: true)
        try text.fileWrapper(from: NSRange(location: 0, length: text.length), documentAttributes: [.documentType: NSAttributedString.DocumentType.rtfd]).write(to: rtfd, options: .atomic, originalContentsURL: nil)
        let docx = try convert(rtfd, "docx")
        let package = root.appendingPathComponent("DOCX package", isDirectory: true)
        try ArchiveCodec.extract(docx, into: package, token: .init())
        let media = try FileManager.default.contentsOfDirectory(at: package.appendingPathComponent("word/media"), includingPropertiesForKeys: nil)
        XCTAssertFalse(media.isEmpty)
        let html = try DocumentText.read(convert(docx, "html"))
        XCTAssertTrue(html.contains("data:image/"))
        XCTAssertFalse(html.contains("src=\"file:"))
    }

    func testSpreadsheetFormulaSurvivesXLSXODSRoundTrip() throws {
        let source = try file("Formula.fods", """
        <?xml version="1.0"?><office:document xmlns:office="urn:oasis:names:tc:opendocument:xmlns:office:1.0" xmlns:table="urn:oasis:names:tc:opendocument:xmlns:table:1.0" xmlns:text="urn:oasis:names:tc:opendocument:xmlns:text:1.0" xmlns:of="urn:oasis:names:tc:opendocument:xmlns:of:1.2" office:version="1.3" office:mimetype="application/vnd.oasis.opendocument.spreadsheet"><office:body><office:spreadsheet><table:table table:name="Formula"><table:table-row><table:table-cell office:value-type="float" office:value="3"><text:p>3</text:p></table:table-cell><table:table-cell office:value-type="float" office:value="4"><text:p>4</text:p></table:table-cell><table:table-cell table:formula="of:=SUM([.A1:.B1])" office:value-type="float" office:value="7"><text:p>7</text:p></table:table-cell></table:table-row></table:table></office:spreadsheet></office:body></office:document>
        """)
        let xlsx = try DocumentEngine.office(source, to: "xlsx", in: root.appendingPathComponent("Initial workbook", isDirectory: true), token: .init())
        let restored = try convert(convert(xlsx, "ods"), "xlsx")
        let package = root.appendingPathComponent("XLSX package", isDirectory: true)
        try ArchiveCodec.extract(restored, into: package, token: .init())
        let xml = try DocumentText.read(package.appendingPathComponent("xl/worksheets/sheet1.xml"))
        XCTAssertTrue(xml.contains("SUM(A1:B1)"))
        XCTAssertTrue(try DocumentText.read(convert(restored, "csv")).contains("3,4,7"))
    }

    func testScannedPDFUsesLocalOCRForTextConversion() throws {
        let width = 1000, height = 220
        let context = try XCTUnwrap(CGContext(data: nil, width: width, height: height, bitsPerComponent: 8, bytesPerRow: 0, space: CGColorSpaceCreateDeviceRGB(), bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue))
        context.setFillColor(CGColor(gray: 1, alpha: 1)); context.fill(CGRect(x: 0, y: 0, width: width, height: height))
        let font = CTFontCreateWithName("Helvetica" as CFString, 48, nil)
        let value = NSAttributedString(string: "Offline OCR fixture 12345", attributes: [NSAttributedString.Key(kCTFontAttributeName as String): font])
        context.textPosition = CGPoint(x: 30, y: 90); CTLineDraw(CTLineCreateWithAttributedString(value), context)
        let image = try XCTUnwrap(context.makeImage())
        let page = try XCTUnwrap(PDFPage(image: NSImage(cgImage: image, size: NSSize(width: width, height: height))))
        let pdf = PDFDocument(); pdf.insert(page, at: 0)
        let source = root.appendingPathComponent("Scanned.pdf"); XCTAssertTrue(pdf.write(to: source))
        XCTAssertTrue(try DocumentText.read(convert(source, "txt")).contains("Offline OCR fixture 12345"))
    }

    func testEncryptedPDFRequiresAndAcceptsTheSuppliedPassword() throws {
        let document = try XCTUnwrap(PDFDocument(url: makePDF()))
        let source = root.appendingPathComponent("Encrypted.pdf")
        XCTAssertTrue(document.write(to: source, withOptions: [.ownerPasswordOption: "owner", .userPasswordOption: "fixture password"]))
        XCTAssertThrowsError(try DocumentEngine.convert(source, to: "txt", options: .init(), token: .init()))
        var options = OperationOptions(); options.password = "fixture password"
        let text = try XCTUnwrap(DocumentEngine.convert(source, to: "txt", options: options, token: .init()).first)
        XCTAssertTrue(try DocumentText.read(text).contains("Nashi fixture"))
    }

    func testSpreadsheetConversionsPreserveQuotedCellsUnicodeAndNumbers() throws {
        let csv = try file("Sheet.csv", "Name,Amount,Notes\r\n\"Şişli, İstanbul\",12.5,\"Line one\nLine two\"\r\nOrange,7,\"A \"\"quote\"\"\"\r\n")
        let xlsx = try convert(csv, "xlsx")
        let ods = try convert(xlsx, "ods")
        let tsv = try convert(ods, "tsv")
        let text = try DocumentText.read(tsv)
        XCTAssertTrue(text.contains("Şişli, İstanbul"))
        XCTAssertTrue(text.contains("12.5"))
        XCTAssertTrue(text.contains("\t"))
        let restoredXLSX = try convert(tsv, "xlsx")
        let restoredCSV = try convert(restoredXLSX, "csv")
        XCTAssertTrue(try DocumentText.read(restoredCSV).contains("Line one\nLine two"))
        let pdf = try convert(xlsx, "pdf")
        XCTAssertGreaterThan(try XCTUnwrap(PDFDocument(url: pdf)).pageCount, 0)
        let xlsFolder = root.appendingPathComponent("XLS", isDirectory: true)
        let xls = try DocumentEngine.office(xlsx, to: "xls", in: xlsFolder, token: .init())
        let fromXLS = try convert(xls, "xlsx")
        XCTAssertTrue(try DocumentText.read(convert(fromXLS, "csv")).contains("Şişli, İstanbul"))
    }

    func testPDFToEditableTextDOCXXLSXEPUBAndPageBasedPPTX() throws {
        let pdf = try makePDF()
        let txt = try convert(pdf, "txt")
        XCTAssertTrue(try DocumentText.read(txt).contains("Nashi fixture"))
        XCTAssertTrue(try DocumentText.read(convert(pdf, "md")).contains("Page two"))
        let docx = try convert(pdf, "docx")
        XCTAssertTrue(try DocumentText.read(convert(docx, "txt")).contains("Nashi fixture"))
        let epub = try convert(pdf, "epub")
        XCTAssertTrue(try DocumentText.read(convert(epub, "txt")).contains("Page two"))
        let xlsx = try convert(pdf, "xlsx")
        let csv = try convert(xlsx, "csv")
        XCTAssertTrue(try DocumentText.read(csv).contains("Apple"))
        let pptx = try convert(pdf, "pptx")
        let archive = root.appendingPathComponent("PPTX package", isDirectory: true)
        try ArchiveCodec.extract(pptx, into: archive, token: .init())
        XCTAssertTrue(FileManager.default.fileExists(atPath: archive.appendingPathComponent("ppt/slides/slide1.xml").path))
        XCTAssertTrue(FileManager.default.fileExists(atPath: archive.appendingPathComponent("ppt/slides/slide2.xml").path))
        let slidesPDF = try convert(pptx, "pdf")
        let renderedSlides = try XCTUnwrap(PDFDocument(url: slidesPDF))
        XCTAssertEqual(renderedSlides.pageCount, 2)
        XCTAssertGreaterThan(try withExtendedLifetime(renderedSlides) { try darkPixels(renderedSlides.page(at: 0)!) }, 100)
        var options = OperationOptions(); options.dpi = 72
        for format in ["jpg", "png"] {
            let images = try DocumentEngine.convert(pptx, to: format, options: options, token: .init())
            XCTAssertEqual(images.count, 2)
            for image in images { XCTAssertNotNil(NSImage(contentsOf: image)) }
        }
        let pptFolder = root.appendingPathComponent("PPT", isDirectory: true)
        let ppt = try DocumentEngine.office(pptx, to: "ppt", in: pptFolder, token: .init())
        XCTAssertEqual(try XCTUnwrap(PDFDocument(url: convert(ppt, "pdf"))).pageCount, 2)
    }

    func testNativePagesRoundTripUsesCopiedInputs() throws {
        let txt = try file("Native.txt", "Native Pages fixture Şişli\n")
        let docx = try convert(txt, "docx")
        let original = try Data(contentsOf: docx)
        let pages = try convert(docx, "pages")
        XCTAssertTrue(try DocumentText.read(convert(pages, "txt")).contains("Native Pages fixture Şişli"))
        let restored = try convert(pages, "docx")
        XCTAssertTrue(try DocumentText.read(convert(restored, "txt")).contains("Native Pages fixture Şişli"))
        XCTAssertGreaterThan(try XCTUnwrap(PDFDocument(url: convert(pages, "pdf"))).pageCount, 0)
        XCTAssertEqual(try Data(contentsOf: docx), original)
    }

    func testOfflinePagesPreservesEditableParagraphsTablesAndEmbeddedImages() throws {
        let pdf = try richFixture()
        let docx = try convert(pdf,"docx"), original = try Data(contentsOf:pdf)
        let pages = try convert(docx,"pages")
        let model = try PagesCodec.model(pages,token:.init())
        let blocks = try XCTUnwrap(model["blocks"] as? [[String:Any]])
        let paragraphs = blocks.filter { $0["type"] as? String == "paragraph" }
        // The pre-existing binary PDF fixture retains its original document title.
        let runs = paragraphs.flatMap { $0["runs"] as? [[String:Any]] ?? [] }
        XCTAssertTrue(runs.compactMap { $0["text"] as? String }.joined().contains("Quarterly Tangerine Report"))
        XCTAssertTrue(runs.contains { ($0["character"] as? [String:Any])?["bold"] as? Bool == true })
        let table = try XCTUnwrap(blocks.first { $0["type"] as? String == "table" })
        let rows = try XCTUnwrap(table["rows"] as? [[[String:Any]]])
        XCTAssertEqual(rows.count,3);XCTAssertEqual(rows[0].count,2)
        XCTAssertEqual(rows[1][0]["text"] as? String,"Apple")
        XCTAssertEqual(rows[1][1]["text"] as? String,"12")
        let image = try XCTUnwrap(runs.first { $0["type"] as? String == "image" })
        let imageData = try XCTUnwrap(Data(base64Encoded:try XCTUnwrap(image["data"] as? String)))
        XCTAssertNotNil(NSImage(data:imageData))
        let restored = try convert(pages,"docx")
        let package = root.appendingPathComponent("Restored Pages DOCX",isDirectory:true)
        try ArchiveCodec.extract(restored,into:package,token:.init())
        let xml = try XMLDocument(contentsOf:package.appendingPathComponent("word/document.xml"),options:[.nodeLoadExternalEntitiesNever])
        XCTAssertEqual(try xml.nodes(forXPath:"//*[local-name()='tbl']").count,1)
        XCTAssertEqual(try FileManager.default.contentsOfDirectory(at:package.appendingPathComponent("word/media"),includingPropertiesForKeys:nil).count,1)
        XCTAssertTrue(try DocumentText.read(convert(restored,"txt")).contains("Second page remains editable."))
        XCTAssertEqual(try Data(contentsOf:pdf),original)
    }

    func testAllSixteenAdvertisedPagesRoutesRunOfflineWithoutApplePages() throws {
        let text = "Offline Pages route fixture Şişli\n"
        let txt = try file("Pages routes.txt",text)
        let docx = try convert(txt,"docx"), md = try file("Pages routes.md",text)
        let rtf = try DocumentEngine.office(docx,to:"rtf",in:root.appendingPathComponent("RTF"),token:.init())
        let attributed=NSAttributedString(string:text)
        let rtfd=root.appendingPathComponent("Pages routes.rtfd",isDirectory:true)
        try attributed.fileWrapper(from:NSRange(location:0,length:attributed.length),documentAttributes:[.documentType:NSAttributedString.DocumentType.rtfd]).write(to:rtfd,options:.atomic,originalContentsURL:nil)
        let clipping=root.appendingPathComponent("Pages routes.textClipping")
        try PropertyListSerialization.data(fromPropertyList:["public.utf8-plain-text":text],format:.binary,options:0).write(to:clipping)
        let epub=try convert(docx,"epub"),pdf=try convert(docx,"pdf")
        var generated:[URL]=[]
        for source in [pdf,docx,md,rtf,rtfd,clipping,epub,txt] {
            let pages=try convert(source,"pages");generated.append(pages)
            XCTAssertTrue(try DocumentText.read(convert(pages,"txt")).contains("Offline Pages route fixture"),source.pathExtension)
        }
        let pages=try XCTUnwrap(generated.first)
        var options=OperationOptions();options.dpi=72
        for format in FeatureCatalog.conversionMatrix["pages"] ?? [] {
            let outputs=try DocumentEngine.convert(pages,to:format,options:options,token:.init())
            XCTAssertFalse(outputs.isEmpty,format)
            try OutputFiles.validate(outputs)
            if ["jpg","png"].contains(format){XCTAssertNotNil(NSImage(contentsOf:outputs[0]))}
            if format == "pdf"{XCTAssertGreaterThan(try XCTUnwrap(PDFDocument(url:outputs[0])).pageCount,0)}
        }
    }

    func testPagesRejectsMalformedInputAndCancellationPreservesSource() throws {
        let bad=try file("Invalid.pages","PK this is not an iWork archive")
        let original=try Data(contentsOf:bad)
        XCTAssertThrowsError(try DocumentEngine.convert(bad,to:"docx",options:.init(),token:.init()))
        XCTAssertEqual(try Data(contentsOf:bad),original)
        let source=try file("Cancel Pages.txt","Cancellation fixture\n")
        let token=CancellationToken();token.cancel()
        XCTAssertThrowsError(try DocumentEngine.convert(source,to:"pages",options:.init(),token:token))
        XCTAssertFalse(FileManager.default.fileExists(atPath:source.deletingPathExtension().appendingPathExtension("pages").path))
        XCTAssertEqual(try DocumentText.read(source),"Cancellation fixture\n")
    }

    func testPDFPreservePagesModeEmbedsOneFullPageImagePerPage() throws {
        let pdf=try richFixture(), original=try Data(contentsOf:pdf)
        var options=OperationOptions();options.pdfOfficeLayout="preservePages";options.dpi=72
        for format in ["docx","pages"] {
            let result=try XCTUnwrap(DocumentEngine.convert(pdf,to:format,options:options,token:.init()).first)
            if format == "docx" {
                let package=root.appendingPathComponent("Preserved DOCX",isDirectory:true)
                try ArchiveCodec.extract(result,into:package,token:.init())
                XCTAssertEqual(try FileManager.default.contentsOfDirectory(at:package.appendingPathComponent("word/media"),includingPropertiesForKeys:nil).count,2)
                let xml=try XMLDocument(contentsOf:package.appendingPathComponent("word/document.xml"),options:[.nodeLoadExternalEntitiesNever])
                XCTAssertEqual(try xml.nodes(forXPath:"//*[local-name()='blip']").count,2)
            } else {
                let model=try PagesCodec.model(result,token:.init())
                let runs=(model["blocks"] as? [[String:Any]] ?? []).flatMap{$0["runs"] as? [[String:Any]] ?? []}
                XCTAssertEqual(runs.filter{$0["type"] as? String == "image"}.count,2)
            }
            let restored=try convert(result,"pdf")
            let document=try XCTUnwrap(PDFDocument(url:restored))
            XCTAssertEqual(document.pageCount,2,format)
            XCTAssertGreaterThan(try withExtendedLifetime(document){try darkPixels(document.page(at:0)!)},100,format)
        }
        XCTAssertEqual(try Data(contentsOf:pdf),original)
    }

    func testRichPDFCreatesEditableParagraphsTablesAndEmbeddedPicturesInDOCX() throws {
        let source = try richFixture()
        let docx = try convert(source, "docx")
        let package = root.appendingPathComponent("Rich DOCX package", isDirectory: true)
        try ArchiveCodec.extract(docx, into: package, token: .init())
        let xml = try XMLDocument(contentsOf: package.appendingPathComponent("word/document.xml"), options: [.nodePreserveWhitespace, .nodeLoadExternalEntitiesNever])
        // NSXMLDocument drops whitespace-only w:t nodes even with the preserve
        // option on this macOS version. SAX keeps the exact OpenXML text Word
        // displays, including standalone xml:space="preserve" separator runs.
        let paragraphReader = WordParagraphReader()
        let parser = XMLParser(data: try Data(contentsOf: package.appendingPathComponent("word/document.xml")))
        parser.delegate = paragraphReader
        XCTAssertTrue(parser.parse())
        let paragraphs = paragraphReader.paragraphs
        XCTAssertTrue(paragraphs.contains(where: { $0.contains("This editable paragraph starts on its first line and continues naturally on the second line.") }))
        XCTAssertTrue(paragraphs.contains(where: { $0.contains("Second page remains editable.") }))
        let tables = try xml.nodes(forXPath: "//*[local-name()='tbl']")
        XCTAssertEqual(tables.count, 1)
        let table = try XCTUnwrap(tables.first)
        XCTAssertEqual(try table.nodes(forXPath: "./*[local-name()='tr']").count, 3)
        XCTAssertEqual(try table.nodes(forXPath: ".//*[local-name()='tc']").count, 6)
        for text in ["Product", "Units", "Apple", "12", "Orange", "7"] { XCTAssertTrue((table.stringValue ?? "").contains(text)) }
        XCTAssertFalse(try xml.nodes(forXPath: "//*[local-name()='b']").isEmpty)
        let images = try FileManager.default.contentsOfDirectory(at: package.appendingPathComponent("word/media"), includingPropertiesForKeys: nil)
        XCTAssertEqual(images.count, 1)
        let bitmap = try XCTUnwrap(NSBitmapImageRep(data: Data(contentsOf: images[0])))
        XCTAssertGreaterThan(bitmap.pixelsWide, 150)
        XCTAssertGreaterThan(bitmap.pixelsHigh, 90)
        let orange = try XCTUnwrap(bitmap.colorAt(x: bitmap.pixelsWide / 2, y: bitmap.pixelsHigh / 2)?.usingColorSpace(.deviceRGB))
        XCTAssertGreaterThan(orange.redComponent, 0.85)
        XCTAssertLessThan(orange.blueComponent, 0.15)
        let relationships = try XMLDocument(contentsOf: package.appendingPathComponent("word/_rels/document.xml.rels"), options: [.nodeLoadExternalEntitiesNever])
        XCTAssertTrue(try relationships.nodes(forXPath: "//*[contains(@Type,'/image') and @TargetMode='External']").isEmpty)
        let restored = try convert(docx, "pdf")
        XCTAssertEqual(try XCTUnwrap(PDFDocument(url: restored)).pageCount, 2)
        XCTAssertTrue(try DocumentText.read(convert(docx, "txt")).contains("Quarterly Tangerine Report"))
    }

    func testFixedEPUBKeepsEveryPDFPageViewportAndRenderedImage() throws {
        let source = try richFixture()
        let pdf = try XCTUnwrap(PDFDocument(url: source))
        pdf.page(at: 1)!.rotation = 90
        let rotated = root.appendingPathComponent("Rotated rich.pdf")
        XCTAssertTrue(pdf.write(to: rotated))
        let original = try Data(contentsOf: rotated)
        var options = OperationOptions(); options.epubLayout = "fixed"; options.dpi = 72
        let epub = try XCTUnwrap(DocumentEngine.convert(rotated, to: "epub", options: options, token: .init()).first)
        let bytes = try Data(contentsOf: epub)
        XCTAssertEqual(Array(bytes.prefix(4)), [0x50, 0x4b, 0x03, 0x04])
        XCTAssertEqual(bytes[8], 0); XCTAssertEqual(bytes[9], 0) // The first mimetype entry must be uncompressed.
        let package = root.appendingPathComponent("Fixed EPUB package", isDirectory: true)
        try ArchiveCodec.extract(epub, into: package, token: .init())
        let opf = try XMLDocument(contentsOf: package.appendingPathComponent("OEBPS/content.opf"), options: [.nodeLoadExternalEntitiesNever])
        XCTAssertEqual(try opf.nodes(forXPath: "//*[local-name()='meta' and @property='rendition:layout']").first?.stringValue, "pre-paginated")
        XCTAssertEqual(try opf.nodes(forXPath: "//*[local-name()='spine']/*[local-name()='itemref']").count, 2)
        for (number, expected) in [(1, CGSize(width: 612, height: 792)), (2, CGSize(width: 792, height: 612))] {
            let name = String(format: "page-%04d", number)
            let page = try XMLDocument(contentsOf: package.appendingPathComponent("OEBPS/pages/\(name).xhtml"), options: [.nodeLoadExternalEntitiesNever])
            XCTAssertEqual(try page.nodes(forXPath: "//*[local-name()='meta' and @name='viewport']/@content").first?.stringValue, "width=\(Int(expected.width)),height=\(Int(expected.height))")
            let png = try XCTUnwrap(NSBitmapImageRep(data: Data(contentsOf: package.appendingPathComponent("OEBPS/pages/\(name).png"))))
            XCTAssertEqual(png.pixelsWide, Int(expected.width)); XCTAssertEqual(png.pixelsHigh, Int(expected.height))
            if number == 1 {
                let photo = try XCTUnwrap(png.colorAt(x: 150, y: 392)?.usingColorSpace(.deviceRGB))
                XCTAssertGreaterThan(photo.redComponent, 0.85); XCTAssertLessThan(photo.blueComponent, 0.15)
            }
        }
        XCTAssertEqual(try Data(contentsOf: rotated), original)
    }

    func testFixedEPUBIsAlsoAvailableForTextDocuments() throws {
        let source = try file("Fixed document.txt", "Fixed document fixture\n")
        var options = OperationOptions(); options.epubLayout = "fixed"; options.dpi = 72
        let epub = try XCTUnwrap(DocumentEngine.convert(source, to: "epub", options: options, token: .init()).first)
        let package = root.appendingPathComponent("Text EPUB package", isDirectory: true)
        try ArchiveCodec.extract(epub, into: package, token: .init())
        let opf = try XMLDocument(contentsOf: package.appendingPathComponent("OEBPS/content.opf"), options: [.nodeLoadExternalEntitiesNever])
        XCTAssertEqual(try opf.nodes(forXPath: "//*[local-name()='spine']/*[local-name()='itemref']").count, 1)
        XCTAssertEqual(try opf.nodes(forXPath: "//*[local-name()='meta' and @property='rendition:layout']").first?.stringValue, "pre-paginated")
    }

    func testPDFImageScannerRespectsNestedTransformsAndFormImages() throws {
        let output = root.appendingPathComponent("Nested image.pdf")
        var box = CGRect(x: 0, y: 0, width: 612, height: 792)
        let context = try XCTUnwrap(CGContext(output as CFURL, mediaBox: &box, nil))
        let image = try XCTUnwrap(CGContext(data: nil, width: 10, height: 10, bitsPerComponent: 8, bytesPerRow: 0, space: CGColorSpaceCreateDeviceRGB(), bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue))
        image.setFillColor(CGColor(red: 1, green: 0.4, blue: 0, alpha: 1)); image.fill(CGRect(x: 0, y: 0, width: 10, height: 10))
        context.beginPDFPage(nil); context.saveGState(); context.translateBy(x: 100, y: 200); context.scaleBy(x: 2, y: 3)
        context.beginTransparencyLayer(auxiliaryInfo: nil); context.draw(image.makeImage()!, in: CGRect(x: 0, y: 0, width: 40, height: 30)); context.endTransparencyLayer()
        context.restoreGState(); context.endPDFPage(); context.closePDF()
        let page = try XCTUnwrap(PDFDocument(url: output)?.page(at: 0))
        let regions = try PDFPlacedImages.regions(page, token: .init())
        XCTAssertEqual(regions.count, 1)
        let rect = try XCTUnwrap(regions.first)
        XCTAssertEqual(rect.minX, 100, accuracy: 0.1); XCTAssertEqual(rect.minY, 200, accuracy: 0.1)
        XCTAssertEqual(rect.width, 80, accuracy: 0.1); XCTAssertEqual(rect.height, 90, accuracy: 0.1)
    }

    private func convert(_ input: URL, _ format: String) throws -> URL {
        try XCTUnwrap(DocumentEngine.convert(input, to: format, options: .init(), token: .init()).first)
    }
    private func file(_ name: String, _ text: String) throws -> URL {
        let url = root.appendingPathComponent(name); try text.write(to: url, atomically: true, encoding: .utf8); return url
    }
    private func richFixture() throws -> URL {
        let fixture = URL(fileURLWithPath: #filePath).deletingLastPathComponent().deletingLastPathComponent().deletingLastPathComponent().appendingPathComponent("Reference/document-fixtures/rich.pdf")
        let copy = root.appendingPathComponent("Rich document.pdf")
        try FileManager.default.copyItem(at: fixture, to: copy)
        return copy
    }
    private func makePDF() throws -> URL {
        let output = root.appendingPathComponent("Two pages.pdf")
        var bounds = CGRect(x: 0, y: 0, width: 612, height: 792)
        guard let context = CGContext(output as CFURL, mediaBox: &bounds, nil) else { throw NashiError.failed("PDF fixture context failed.") }
        for page in 0..<2 {
            context.beginPDFPage(nil)
            func draw(_ value: String, x: CGFloat, y: CGFloat) {
                let font = CTFontCreateWithName("Helvetica" as CFString, 14, nil)
                let text = NSAttributedString(string: value, attributes: [NSAttributedString.Key(kCTFontAttributeName as String): font])
                context.textPosition = CGPoint(x: x, y: y)
                CTLineDraw(CTLineCreateWithAttributedString(text), context)
            }
            draw(page == 0 ? "Nashi fixture" : "Page two", x: 50, y: 730)
            draw("Name", x: 50, y: 650); draw("Amount", x: 250, y: 650)
            draw("Apple", x: 50, y: 620); draw("12.5", x: 250, y: 620)
            context.endPDFPage()
        }
        context.closePDF(); return output
    }
    private func darkPixels(_ page: PDFPage) throws -> Int {
        let context = try XCTUnwrap(CGContext(data: nil, width: 306, height: 396, bitsPerComponent: 8, bytesPerRow: 306 * 4, space: CGColorSpaceCreateDeviceRGB(), bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue))
        context.setFillColor(CGColor(gray: 1, alpha: 1)); context.fill(CGRect(x: 0, y: 0, width: 306, height: 396))
        context.scaleBy(x: 0.5, y: 0.5); page.draw(with: .mediaBox, to: context)
        let bytes = try XCTUnwrap(context.data).assumingMemoryBound(to: UInt8.self)
        return (0..<(306 * 396)).filter { Int(bytes[$0 * 4]) + Int(bytes[$0 * 4 + 1]) + Int(bytes[$0 * 4 + 2]) < 600 }.count
    }

    private final class WordParagraphReader: NSObject, XMLParserDelegate {
        var paragraphs: [String] = []
        private var current: String?
        private var inText = false
        func parser(_ parser: XMLParser, didStartElement name: String, namespaceURI: String?, qualifiedName qName: String?, attributes: [String: String]) {
            if name == "w:p" { current = "" }
            if name == "w:t" { inText = true }
        }
        func parser(_ parser: XMLParser, foundCharacters string: String) { if inText { current? += string } }
        func parser(_ parser: XMLParser, didEndElement name: String, namespaceURI: String?, qualifiedName qName: String?) {
            if name == "w:t" { inText = false }
            if name == "w:p", let value = current { paragraphs.append(value); current = nil }
        }
    }

    private static func little(_ value: UInt64, count: Int) -> Data { Data((0..<count).map { UInt8(truncatingIfNeeded: value >> ($0 * 8)) }) }
    private static func big(_ value: Int, count: Int) -> Data { Data((0..<count).reversed().map { UInt8(truncatingIfNeeded: value >> ($0 * 8)) }) }
    private static func crc32(_ data: Data) -> UInt32 {
        var crc: UInt32 = 0xffff_ffff
        for byte in data { crc ^= UInt32(byte); for _ in 0..<8 { crc = crc & 1 == 1 ? (crc >> 1) ^ 0xedb8_8320 : crc >> 1 } }
        return ~crc
    }
    private static func storedRAR(name: String, payload: Data) -> Data {
        func block(_ body: Data) -> Data { little(UInt64(crc32(body) & 0xffff), count: 2) + body }
        var result = Data([0x52, 0x61, 0x72, 0x21, 0x1a, 0x07, 0x00])
        result += block(Data([0x73]) + little(0, count: 2) + little(13, count: 2) + Data(repeating: 0, count: 6))
        let filename = Data(name.utf8)
        var header = Data([0x74]) + little(0x8000, count: 2) + little(UInt64(32 + filename.count), count: 2)
        header += little(UInt64(payload.count), count: 4) + little(UInt64(payload.count), count: 4) + Data([3])
        header += little(UInt64(crc32(payload)), count: 4) + little(0, count: 4) + Data([20, 0x30])
        header += little(UInt64(filename.count), count: 2) + little(0x81a4, count: 4) + filename
        result += block(header) + payload
        result += block(Data([0x7b]) + little(0, count: 2) + little(7, count: 2))
        return result
    }
    private static func tar(name: String, payload: Data, type: UInt8 = 48, link: String = "") -> Data {
        var header = Data(repeating: 0, count: 512)
        func put(_ value: String, at offset: Int, length: Int) { header.replaceSubrange(offset..<(offset + min(length, value.utf8.count)), with: value.utf8.prefix(length)) }
        put(name, at: 0, length: 100); put("0000644\0", at: 100, length: 8)
        put("0000000\0", at: 108, length: 8); put("0000000\0", at: 116, length: 8)
        put(String(format: "%011o", payload.count) + "\0", at: 124, length: 12)
        put("00000000000\0", at: 136, length: 12)
        put("        ", at: 148, length: 8); header[156] = type
        put(link, at: 157, length: 100); put("ustar\0", at: 257, length: 6); put("00", at: 263, length: 2)
        let sum = header.reduce(0) { $0 + Int($1) }
        put(String(format: "%06o", sum) + "\0 ", at: 148, length: 8)
        return header + payload + Data(repeating: 0, count: (512 - payload.count % 512) % 512 + 1024)
    }
    private static func resourceFork(type: String, payload: Data) -> Data {
        let dataOffset = 256, mapOffset = dataOffset + 4 + payload.count, mapLength = 50
        let header = big(dataOffset, count: 4) + big(mapOffset, count: 4) + big(4 + payload.count, count: 4) + big(mapLength, count: 4)
        var data = header + Data(repeating: 0, count: dataOffset - header.count) + big(payload.count, count: 4) + payload
        var map = header + Data(repeating: 0, count: 8) + big(28, count: 2) + big(50, count: 2)
        map += big(0, count: 2) + Data(type.utf8) + big(0, count: 2) + big(10, count: 2)
        map += big(128, count: 2) + big(0xffff, count: 2) + Data(repeating: 0, count: 8)
        data += map; return data
    }
}
