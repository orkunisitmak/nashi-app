import Foundation
#if canImport(FoundationXML)
import FoundationXML
#endif
import UniformTypeIdentifiers
import PDFKit
import AppKit

enum EPUBCodec {
    static func createFixed(fromPDF input: URL, title: String, at output: URL, options: OperationOptions, token: CancellationToken) throws {
        let pdf = try PDFEngine.open(input, password: options.password)
        let work = try OutputFiles.temporaryDirectory()
        defer { try? FileManager.default.removeItem(at: work) }
        let fm = FileManager.default
        let meta = work.appendingPathComponent("META-INF", isDirectory: true)
        let pages = work.appendingPathComponent("OEBPS/pages", isDirectory: true)
        try fm.createDirectory(at: meta, withIntermediateDirectories: true)
        try fm.createDirectory(at: pages, withIntermediateDirectories: true)
        try "application/epub+zip".write(to: work.appendingPathComponent("mimetype"), atomically: true, encoding: .utf8)
        try "<?xml version=\"1.0\"?><container version=\"1.0\" xmlns=\"urn:oasis:names:tc:opendocument:xmlns:container\"><rootfiles><rootfile full-path=\"OEBPS/content.opf\" media-type=\"application/oebps-package+xml\"/></rootfiles></container>".write(to: meta.appendingPathComponent("container.xml"), atomically: true, encoding: .utf8)
        var manifest = "", spine = "", navigation = ""
        let escapedTitle = DocumentText.escapeXML(title)
        for index in 0..<pdf.pageCount {
            try token.check()
            guard let page = pdf.page(at: index) else { continue }
            let bounds = PDFOfficeCodec.visualBounds(page)
            let width = max(1, Int(ceil(bounds.width))), height = max(1, Int(ceil(bounds.height)))
            let name = String(format: "page-%04d", index + 1)
            try autoreleasepool {
                let png = try PDFOfficeCodec.rasterPNG(page, dpi: options.dpi, token: token)
                try png.write(to: pages.appendingPathComponent(name + ".png"))
            }
            let alternative = DocumentText.escapeXML(page.string?.trimmingCharacters(in: .whitespacesAndNewlines).prefix(2048).description ?? "Page \(index + 1)")
            let xhtml = """
            <?xml version="1.0" encoding="UTF-8"?><html xmlns="http://www.w3.org/1999/xhtml" lang="und"><head><title>\(escapedTitle) — Page \(index + 1)</title><meta name="viewport" content="width=\(width),height=\(height)"/><style>html,body{margin:0;padding:0;width:\(width)px;height:\(height)px;overflow:hidden;background:white}img{position:absolute;left:0;top:0;width:\(width)px;height:\(height)px}</style></head><body><img src="\(name).png" width="\(width)" height="\(height)" alt="\(alternative)"/></body></html>
            """
            try xhtml.write(to: pages.appendingPathComponent(name + ".xhtml"), atomically: true, encoding: .utf8)
            manifest += "<item id=\"page\(index + 1)\" href=\"pages/\(name).xhtml\" media-type=\"application/xhtml+xml\"/><item id=\"image\(index + 1)\" href=\"pages/\(name).png\" media-type=\"image/png\"/>"
            spine += "<itemref idref=\"page\(index + 1)\"/>"
            navigation += "<li><a href=\"pages/\(name).xhtml\">Page \(index + 1)</a></li>"
        }
        let oebps = pages.deletingLastPathComponent()
        let nav = "<?xml version=\"1.0\"?><html xmlns=\"http://www.w3.org/1999/xhtml\" xmlns:epub=\"http://www.idpf.org/2007/ops\"><head><title>\(escapedTitle)</title></head><body><nav epub:type=\"toc\" id=\"toc\"><h1>\(escapedTitle)</h1><ol>\(navigation)</ol></nav><nav epub:type=\"page-list\"><h2>Pages</h2><ol>\(navigation)</ol></nav></body></html>"
        try nav.write(to: oebps.appendingPathComponent("nav.xhtml"), atomically: true, encoding: .utf8)
        let date = ISO8601DateFormatter().string(from: Date())
        let language = DocumentText.escapeXML(options.metadata["language"] ?? "und")
        let package = """
        <?xml version="1.0" encoding="UTF-8"?><package xmlns="http://www.idpf.org/2007/opf" version="3.0" unique-identifier="id" prefix="rendition: http://www.idpf.org/vocab/rendition/#"><metadata xmlns:dc="http://purl.org/dc/elements/1.1/"><dc:identifier id="id">urn:uuid:\(UUID().uuidString)</dc:identifier><dc:title>\(escapedTitle)</dc:title><dc:language>\(language)</dc:language><meta property="dcterms:modified">\(date)</meta><meta property="rendition:layout">pre-paginated</meta><meta property="rendition:orientation">auto</meta><meta property="rendition:spread">auto</meta></metadata><manifest><item id="nav" href="nav.xhtml" media-type="application/xhtml+xml" properties="nav"/>\(manifest)</manifest><spine>\(spine)</spine></package>
        """
        try package.write(to: oebps.appendingPathComponent("content.opf"), atomically: true, encoding: .utf8)
        try ProcessRunner.run(URL(fileURLWithPath: "/usr/bin/zip"), ["-q", "-0", "-X", output.path, "mimetype"], directory: work, token: token)
        try ProcessRunner.run(URL(fileURLWithPath: "/usr/bin/zip"), ["-q", "-r", "-X", output.path, "META-INF", "OEBPS"], directory: work, token: token)
    }

    static func unpackToHTML(_ input: URL, in work: URL, token: CancellationToken) throws -> URL {
        let root = work.appendingPathComponent("EPUB contents", isDirectory: true)
        try ArchiveCodec.extract(input, into: root, token: token)
        let container = root.appendingPathComponent("META-INF/container.xml")
        try rejectEntityDeclarations(container)
        let xml = try XMLDocument(contentsOf: container, options: [.nodeLoadExternalEntitiesNever])
        guard let opfPath = try xml.nodes(forXPath: "//*[local-name()='rootfile']/@full-path").first?.stringValue else {
            throw NashiError.invalidInput("The EPUB has no package document.")
        }
        let opf = try ArchiveCodec.safeURL(opfPath, inside: root)
        try rejectEntityDeclarations(opf)
        let package = try XMLDocument(contentsOf: opf, options: [.nodeLoadExternalEntitiesNever])
        var manifest: [String: URL] = [:]
        var styles = ""
        for node in try package.nodes(forXPath: "//*[local-name()='manifest']/*[local-name()='item']") {
            guard let element = node as? XMLElement, let id = element.attribute(forName: "id")?.stringValue,
                  let href = element.attribute(forName: "href")?.stringValue?.removingPercentEncoding,
                  !href.contains("://"), !href.hasPrefix("/") else { continue }
            let url = opf.deletingLastPathComponent().appendingPathComponent(href).standardizedFileURL
            guard isInside(url, root: root) else { throw NashiError.invalidInput("An EPUB resource points outside its package.") }
            manifest[id] = url
            if element.attribute(forName: "media-type")?.stringValue == "text/css", let css = try? DocumentText.read(url) { styles += "<style>\(css)</style>" }
        }
        var chapters: [String] = []
        for node in try package.nodes(forXPath: "//*[local-name()='spine']/*[local-name()='itemref']") {
            try token.check()
            guard let element = node as? XMLElement,
                  let id = element.attribute(forName: "idref")?.stringValue, let url = manifest[id] else { continue }
            let chapter = try chapterDocument(url)
            for image in try chapter.nodes(forXPath: "//*[local-name()='img']/@src") {
                guard let href = image.stringValue, !href.hasPrefix("data:") else { continue }
                if href.contains("://") || href.hasPrefix("/") { image.stringValue = ""; continue }
                let target = url.deletingLastPathComponent().appendingPathComponent(href.removingPercentEncoding ?? href).standardizedFileURL
                guard isInside(target, root: root) else { throw NashiError.invalidInput("An EPUB image points outside its package.") }
                image.stringValue = target.absoluteString
            }
            let body = try chapter.nodes(forXPath: "//*[local-name()='body']").first
            if let body { chapters.append((body.children ?? []).map(\.xmlString).joined()) }
        }
        guard !chapters.isEmpty else { throw NashiError.invalidInput("The EPUB contains no readable chapters.") }
        let title = try package.nodes(forXPath: "//*[local-name()='metadata']/*[local-name()='title']").first?.stringValue ?? input.deletingPathExtension().lastPathComponent
        let html = "<!DOCTYPE html><html><head><meta charset=\"UTF-8\"><title>\(DocumentText.escapeXML(title))</title>\(styles)</head><body>" + chapters.enumerated().map { index, chapter in "<section\(index > 0 ? " style=\"page-break-before:always\"" : "")>\(chapter)</section>" }.joined() + "</body></html>"
        let output = work.appendingPathComponent("EPUB document.html")
        try html.write(to: output, atomically: true, encoding: .utf8)
        return output
    }

    static func create(fromHTML input: URL, title: String, at output: URL, token: CancellationToken) throws {
        let work = try OutputFiles.temporaryDirectory()
        defer { try? FileManager.default.removeItem(at: work) }
        let fm = FileManager.default
        let meta = work.appendingPathComponent("META-INF", isDirectory: true)
        let oebps = work.appendingPathComponent("OEBPS", isDirectory: true)
        try fm.createDirectory(at: meta, withIntermediateDirectories: true)
        try fm.createDirectory(at: oebps, withIntermediateDirectories: true)
        try "application/epub+zip".write(to: work.appendingPathComponent("mimetype"), atomically: true, encoding: .utf8)
        try "<?xml version=\"1.0\"?><container version=\"1.0\" xmlns=\"urn:oasis:names:tc:opendocument:xmlns:container\"><rootfiles><rootfile full-path=\"OEBPS/content.opf\" media-type=\"application/oebps-package+xml\"/></rootfiles></container>".write(to: meta.appendingPathComponent("container.xml"), atomically: true, encoding: .utf8)
        let html = try XMLDocument(contentsOf: input, options: [.documentTidyHTML, .nodePreserveWhitespace, .nodeLoadExternalEntitiesNever])
        var manifest = "", imageIndex = 0
        for node in try html.nodes(forXPath: "//*[local-name()='img']/@src") {
            try token.check()
            guard let source = node.stringValue, !source.isEmpty else { continue }
            let data: Data
            let mime: String
            let ext: String
            if source.hasPrefix("data:"), let comma = source.firstIndex(of: ","), let decoded = Data(base64Encoded: String(source[source.index(after: comma)...])) {
                data = decoded
                mime = String(source.dropFirst(5).prefix(while: { $0 != ";" && $0 != "," }))
                ext = UTType(mimeType: mime)?.preferredFilenameExtension ?? "png"
            } else {
                let url: URL
                if source.hasPrefix("file:") { guard let parsed = URL(string: source) else { continue }; url = parsed }
                else if source.contains("://") { node.stringValue = ""; continue }
                else { url = input.deletingLastPathComponent().appendingPathComponent(source.removingPercentEncoding ?? source) }
                guard let loaded = try? Data(contentsOf: url) else { continue }
                data = loaded; ext = url.pathExtension.lowercased(); mime = UTType(filenameExtension: ext)?.preferredMIMEType ?? "image/png"
            }
            imageIndex += 1
            let name = "image-\(imageIndex).\(ext)"
            try data.write(to: oebps.appendingPathComponent(name))
            node.stringValue = name
            manifest += "<item id=\"image\(imageIndex)\" href=\"\(name)\" media-type=\"\(mime)\"/>"
        }
        let body = try html.nodes(forXPath: "//*[local-name()='body']").first
        let content = (body?.children ?? []).map(\.xmlString).joined()
        let style = try html.nodes(forXPath: "//*[local-name()='style']").map(\.xmlString).joined()
        let escapedTitle = DocumentText.escapeXML(title)
        let xhtml = "<?xml version=\"1.0\" encoding=\"UTF-8\"?><html xmlns=\"http://www.w3.org/1999/xhtml\" lang=\"en\"><head><title>\(escapedTitle)</title>\(style)</head><body>\(content)</body></html>"
        try xhtml.write(to: oebps.appendingPathComponent("book.xhtml"), atomically: true, encoding: .utf8)
        let navigation = "<?xml version=\"1.0\"?><html xmlns=\"http://www.w3.org/1999/xhtml\" xmlns:epub=\"http://www.idpf.org/2007/ops\"><head><title>Contents</title></head><body><nav epub:type=\"toc\"><h1>Contents</h1><ol><li><a href=\"book.xhtml\">\(escapedTitle)</a></li></ol></nav></body></html>"
        try navigation.write(to: oebps.appendingPathComponent("nav.xhtml"), atomically: true, encoding: .utf8)
        let date = ISO8601DateFormatter().string(from: Date()).replacingOccurrences(of: #"\.\d+Z$"#, with: "Z", options: .regularExpression)
        let package = "<?xml version=\"1.0\"?><package xmlns=\"http://www.idpf.org/2007/opf\" version=\"3.0\" unique-identifier=\"id\"><metadata xmlns:dc=\"http://purl.org/dc/elements/1.1/\"><dc:identifier id=\"id\">urn:uuid:\(UUID().uuidString)</dc:identifier><dc:title>\(escapedTitle)</dc:title><dc:language>en</dc:language><meta property=\"dcterms:modified\">\(date)</meta></metadata><manifest><item id=\"book\" href=\"book.xhtml\" media-type=\"application/xhtml+xml\"/><item id=\"nav\" href=\"nav.xhtml\" media-type=\"application/xhtml+xml\" properties=\"nav\"/>\(manifest)</manifest><spine><itemref idref=\"book\"/></spine></package>"
        try package.write(to: oebps.appendingPathComponent("content.opf"), atomically: true, encoding: .utf8)
        try ProcessRunner.run(URL(fileURLWithPath: "/usr/bin/zip"), ["-q", "-0", "-X", output.path, "mimetype"], directory: work, token: token)
        try ProcessRunner.run(URL(fileURLWithPath: "/usr/bin/zip"), ["-q", "-r", "-X", output.path, "META-INF", "OEBPS"], directory: work, token: token)
    }

    private static func isInside(_ url: URL, root: URL) -> Bool {
        let path = url.resolvingSymlinksInPath().path, base = root.resolvingSymlinksInPath().path
        return path == base || path.hasPrefix(base + "/")
    }

    /// XMLDocument expands internal entities, so nested declarations in a
    /// tiny EPUB part can amplify into an unbounded document.
    private static func rejectEntityDeclarations(_ url: URL) throws {
        guard DocumentText.declaresXMLEntities(try Data(contentsOf: url)) else { return }
        throw NashiError.invalidInput("The EPUB contains XML entity declarations, which are not supported.")
    }

    private static func chapterDocument(_ url: URL) throws -> XMLDocument {
        let options: XMLNode.Options = [.nodePreserveWhitespace, .nodeLoadExternalEntitiesNever]
        try rejectEntityDeclarations(url)
        // EPUB chapters are XML. Let the XML parser honor their declaration
        // (or XML's UTF-8 default) before tolerating older HTML markup. The
        // URL-based HTML parser can otherwise guess a legacy encoding and
        // silently corrupt ordinary UTF-8 chapter text.
        do { return try XMLDocument(contentsOf: url, options: options) }
        catch {
            return try XMLDocument(xmlString: DocumentText.read(url), options: options.union(.documentTidyHTML))
        }
    }
}
