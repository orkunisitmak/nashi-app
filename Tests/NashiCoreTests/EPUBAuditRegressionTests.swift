import XCTest
import AppKit
@testable import NashiCore

final class EPUBAuditRegressionTests: XCTestCase {
    private var root: URL!

    override func setUpWithError() throws {
        root = try OutputFiles.temporaryDirectory()
    }

    override func tearDownWithError() throws {
        try? FileManager.default.removeItem(at: root)
    }

    func testUTF8ChaptersWithoutXMLDeclarationPreserveSpineTextAndImages() throws {
        let epub = try fixture(name: "UTF8 book", encoding: .utf8, declaration: "", meta: "", tolerantHTML: false)
        let text = try convertedText(epub)
        XCTAssertTrue(text.contains("First chapter Şişli café"), text)
        XCTAssertTrue(text.contains("Second chapter Şişli café"), text)
        XCTAssertLessThan(try XCTUnwrap(text.range(of: "First chapter")?.lowerBound), try XCTUnwrap(text.range(of: "Second chapter")?.lowerBound))
        let docx = try XCTUnwrap(DocumentEngine.convert(epub, to: "docx", options: .init(), token: .init()).first)
        let package = root.appendingPathComponent("DOCX contents", isDirectory: true)
        try ArchiveCodec.extract(docx, into: package, token: .init())
        let xml = try XMLDocument(contentsOf: package.appendingPathComponent("word/document.xml"), options: .nodeLoadExternalEntitiesNever)
        XCTAssertEqual(try xml.nodes(forXPath: "//*[local-name()='drawing']").count, 2)
    }

    func testUnpackedChaptersKeepTheirOwnLanguageAndDirection() throws {
        let epub = try fixture(name: "Bilingual", encoding: .utf8, declaration: "", meta: "", tolerantHTML: false,
                               htmlAttributes: " xml:lang=\"he-IL\" dir=\"rtl\"", secondBodyAttributes: " lang=\"en-GB\" dir=\"ltr\"")
        let html = try EPUBCodec.unpackToHTML(epub, in: root, token: .init())
        let document = try XMLDocument(contentsOf: html, options: [.documentTidyHTML, .nodeLoadExternalEntitiesNever])
        for (chapter, language, direction) in [("first", "he-IL", "rtl"), ("second", "en-GB", "ltr")] {
            let paragraph = try XCTUnwrap(document.nodes(forXPath: "//*[local-name()='p' and normalize-space(.)='Body marker \(chapter)']").first)
            XCTAssertEqual(try paragraph.nodes(forXPath: "ancestor::*[@lang][1]/@lang").first?.stringValue, language)
            XCTAssertEqual(try paragraph.nodes(forXPath: "ancestor::*[@dir][1]/@dir").first?.stringValue, direction)
        }
    }

    func testHTML5CharsetUTF8KeepsAccentsWithoutXMLDeclaration() throws {
        let epub = try fixture(name: "HTML5 book", encoding: .utf8, declaration: "", meta: "<meta charset=\"UTF-8\"/>", tolerantHTML: false)
        XCTAssertTrue(try convertedText(epub).contains("First chapter Şişli café"))
    }

    func testTolerantHTMLFallbackDecodesUTF8BeforeParsing() throws {
        let epub = try fixture(name: "Older UTF8 HTML", encoding: .utf8, declaration: "", meta: "<meta charset=\"UTF-8\">", tolerantHTML: true)
        XCTAssertTrue(try convertedText(epub).contains("First chapter Şişli café"))
    }

    func testTolerantHTMLFallbackPreservesDeclaredCP1252() throws {
        let epub = try fixture(name: "Legacy HTML", encoding: .windowsCP1252, declaration: "", meta: "<meta http-equiv=\"Content-Type\" content=\"text/html; charset=windows-1252\">", tolerantHTML: true, marker: "Café déjà vu — résumé")
        XCTAssertTrue(try convertedText(epub).contains("First chapter Café déjà vu — résumé"))
    }

    func testXMLDeclarationHonorsNonUTF8ChapterEncoding() throws {
        let epub = try fixture(name: "Declared encoding", encoding: .windowsCP1252, declaration: "<?xml version=\"1.0\" encoding=\"windows-1252\"?>", meta: "", tolerantHTML: false, marker: "Café déjà vu — résumé")
        XCTAssertTrue(try convertedText(epub).contains("Second chapter Café déjà vu — résumé"))
    }

    func testCreatedBookKeepsUTF8TextWithoutMetaCharset() throws {
        let html = root.appendingPathComponent("Plain UTF8.html")
        try "<!DOCTYPE html><html><head><title>Türkçe</title></head><body><p>Şişli café — Türkçe</p></body></html>".write(to: html, atomically: true, encoding: .utf8)
        let epub = root.appendingPathComponent("Plain UTF8.epub")
        try EPUBCodec.create(fromHTML: html, title: "Book", at: epub, token: .init())
        let package = root.appendingPathComponent("Created UTF8", isDirectory: true)
        try ArchiveCodec.extract(epub, into: package, token: .init())
        let xml = try XMLDocument(contentsOf: package.appendingPathComponent("OEBPS/book.xhtml"), options: .nodeLoadExternalEntitiesNever)
        XCTAssertEqual(try xml.nodes(forXPath: "//*[local-name()='p']").first?.stringValue?.trimmingCharacters(in: .whitespacesAndNewlines), "Şişli café — Türkçe")
    }

    func testCreatedBookKeepsDeclaredLegacyCharset() throws {
        let html = root.appendingPathComponent("Legacy Windows1254.html")
        let source = "<html><head><meta http-equiv=\"Content-Type\" content=\"text/html; charset=windows-1254\"></head><body><p>Şişli İğne</p></body></html>"
        let encoding = String.Encoding(rawValue: CFStringConvertEncodingToNSStringEncoding(CFStringEncoding(CFStringEncodings.windowsLatin5.rawValue)))
        try XCTUnwrap(source.data(using: encoding)).write(to: html)
        let epub = root.appendingPathComponent("Legacy Windows1254.epub")
        try EPUBCodec.create(fromHTML: html, title: "Book", at: epub, token: .init())
        let package = root.appendingPathComponent("Created Windows1254", isDirectory: true)
        try ArchiveCodec.extract(epub, into: package, token: .init())
        let xml = try XMLDocument(contentsOf: package.appendingPathComponent("OEBPS/book.xhtml"), options: .nodeLoadExternalEntitiesNever)
        XCTAssertEqual(try xml.nodes(forXPath: "//*[local-name()='p']").first?.stringValue?.trimmingCharacters(in: .whitespacesAndNewlines), "Şişli İğne")
    }

    func testCreatedBookStaysWellFormedWithForbiddenCharacterReferences() throws {
        let html = root.appendingPathComponent("Office export.html")
        try "<!DOCTYPE HTML PUBLIC \"-//W3C//DTD HTML 4.0 Transitional//EN\"><html><head><meta http-equiv=\"content-type\" content=\"text/html; charset=utf-8\"/><title>x</title></head><body><p title=\"x&#11;y\">a&#12;b&#x1F; Şişli café</p></body></html>".write(to: html, atomically: true, encoding: .utf8)
        let epub = root.appendingPathComponent("Office export.epub")
        try EPUBCodec.create(fromHTML: html, title: "Book", at: epub, token: .init())
        let package = root.appendingPathComponent("Created book", isDirectory: true)
        try ArchiveCodec.extract(epub, into: package, token: .init())
        let xml = try XMLDocument(contentsOf: package.appendingPathComponent("OEBPS/book.xhtml"), options: .nodeLoadExternalEntitiesNever)
        XCTAssertEqual(try xml.nodes(forXPath: "//*[local-name()='p']").first?.stringValue?.trimmingCharacters(in: .whitespacesAndNewlines), "ab Şişli café")
    }

    func testCreatedBookKeepsSourceLanguageAndDirection() throws {
        // LibreOffice's HTML export puts the document language and direction on <body>.
        for (attributes, language, direction) in [(" lang=\"ar-SA\" dir=\"rtl\"", "ar-SA", "rtl"), (" lang=\"tr-TR\" dir=\"ltr\"", "tr-TR", "ltr"), ("", "en", nil)] {
            let html = root.appendingPathComponent("Office \(language).html")
            try "<!DOCTYPE HTML PUBLIC \"-//W3C//DTD HTML 4.0 Transitional//EN\"><html><head><meta http-equiv=\"content-type\" content=\"text/html; charset=utf-8\"/><title></title></head><body\(attributes)><p>مرحبا Şişli</p></body></html>".write(to: html, atomically: true, encoding: .utf8)
            let epub = root.appendingPathComponent("Office \(language).epub")
            try EPUBCodec.create(fromHTML: html, title: "Book", at: epub, token: .init())
            let package = root.appendingPathComponent("Created \(language)", isDirectory: true)
            try ArchiveCodec.extract(epub, into: package, token: .init())
            let xhtml = try XCTUnwrap(XMLDocument(contentsOf: package.appendingPathComponent("OEBPS/book.xhtml"), options: .nodeLoadExternalEntitiesNever).rootElement())
            let opf = try XMLDocument(contentsOf: package.appendingPathComponent("OEBPS/content.opf"), options: .nodeLoadExternalEntitiesNever)
            XCTAssertEqual(xhtml.attribute(forName: "lang")?.stringValue, language)
            XCTAssertEqual(xhtml.attribute(forName: "dir")?.stringValue, direction)
            XCTAssertEqual(try opf.nodes(forXPath: "//*[local-name()='language']").first?.stringValue, language)
        }
    }

    private func convertedText(_ epub: URL) throws -> String {
        let output = try XCTUnwrap(DocumentEngine.convert(epub, to: "txt", options: .init(), token: .init()).first)
        return try DocumentText.read(output)
    }

    private func fixture(name: String, encoding: String.Encoding, declaration: String, meta: String, tolerantHTML: Bool, marker: String = "Şişli café",
                         htmlAttributes: String = "", secondBodyAttributes: String = "") throws -> URL {
        let package = root.appendingPathComponent(name, isDirectory: true)
        let metadata = package.appendingPathComponent("META-INF", isDirectory: true)
        let chapters = package.appendingPathComponent("OEBPS/chapters", isDirectory: true)
        let images = package.appendingPathComponent("OEBPS/images", isDirectory: true)
        for directory in [metadata, chapters, images] {
            try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
        }
        try "application/epub+zip".write(to: package.appendingPathComponent("mimetype"), atomically: true, encoding: .utf8)
        try "<?xml version=\"1.0\"?><container xmlns=\"urn:oasis:names:tc:opendocument:xmlns:container\" version=\"1.0\"><rootfiles><rootfile full-path=\"OEBPS/content.opf\" media-type=\"application/oebps-package+xml\"/></rootfiles></container>".write(to: metadata.appendingPathComponent("container.xml"), atomically: true, encoding: .utf8)
        let opf = """
        <package xmlns="http://www.idpf.org/2007/opf" version="3.0" unique-identifier="id"><metadata xmlns:dc="http://purl.org/dc/elements/1.1/"><dc:identifier id="id">urn:uuid:audit</dc:identifier><dc:title>Audit book</dc:title><dc:language>tr</dc:language></metadata><manifest><item id="second" href="chapters/second.xhtml" media-type="application/xhtml+xml"/><item id="first" href="chapters/first.xhtml" media-type="application/xhtml+xml"/><item id="image" href="images/orange.png" media-type="image/png"/></manifest><spine><itemref idref="first"/><itemref idref="second"/></spine></package>
        """
        try opf.write(to: package.appendingPathComponent("OEBPS/content.opf"), atomically: true, encoding: .utf8)
        let bitmap = try XCTUnwrap(NSBitmapImageRep(bitmapDataPlanes: nil, pixelsWide: 16, pixelsHigh: 16, bitsPerSample: 8, samplesPerPixel: 4, hasAlpha: true, isPlanar: false, colorSpaceName: .deviceRGB, bytesPerRow: 0, bitsPerPixel: 0))
        for y in 0..<16 { for x in 0..<16 { bitmap.setColor(.orange, atX: x, y: y) } }
        try XCTUnwrap(bitmap.representation(using: .png, properties: [:])).write(to: images.appendingPathComponent("orange.png"))
        for (file, title) in [("first", "First"), ("second", "Second")] {
            let image = "<img src=\"../images/orange.png\" alt=\"Orange\"\(tolerantHTML ? ">" : "/>")"
            let bodyAttributes = file == "second" ? secondBodyAttributes : ""
            let html = "\(declaration)<html xmlns=\"http://www.w3.org/1999/xhtml\"\(htmlAttributes)><head><title>\(title)</title>\(meta)</head><body\(bodyAttributes)><h1>\(title) chapter \(marker)</h1><p>Body marker \(file)</p>\(image)</body></html>"
            try XCTUnwrap(html.data(using: encoding)).write(to: chapters.appendingPathComponent(file + ".xhtml"))
        }
        let epub = root.appendingPathComponent(name).appendingPathExtension("epub")
        try ArchiveCodec.create(from: package, at: epub, format: "zip", token: .init())
        return epub
    }
}
