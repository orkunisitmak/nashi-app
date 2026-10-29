import Foundation
import ImageIO
import AppKit
import PDFKit
import UniformTypeIdentifiers

public enum DocumentEngine {
    public static func convert(_ input: URL, to requestedFormat: String, options: OperationOptions, token: CancellationToken) throws -> [URL] {
        try token.check()
        guard FileManager.default.fileExists(atPath: input.path) else { throw NashiError.invalidInput("The input file does not exist.") }
        let format = normalized(requestedFormat)
        let category = FileCategory.detect(input)
        if category == .archive { return try convertArchive(input, to: format, options: options, token: token) }
        if category == .subtitle {
            let text = try SubtitleCodec.convert(input, to: format, token: token)
            let output = try destination(input, format: format, options: options)
            try token.check()
            try text.write(to: output, atomically: true, encoding: .utf8)
            return [output]
        }
        let work = try OutputFiles.temporaryDirectory()
        defer { try? FileManager.default.removeItem(at: work) }
        let output = try destination(input, format: format, options: options)
        let staging = work.appendingPathComponent("Converted", isDirectory: true)
        try FileManager.default.createDirectory(at: staging, withIntermediateDirectories: true)

        if category == .pdf {
            if ["png", "jpg"].contains(format) { return try PDFEngine.render(input, to: format, options: options, token: token) }
            if format == "epub" && options.epubLayout == "fixed" {
                let epub = work.appendingPathComponent("Fixed book.epub")
                try EPUBCodec.createFixed(fromPDF: input, title: input.deletingPathExtension().lastPathComponent, at: epub, options: options, token: token)
                return try publish(epub, to: output, token: token)
            }
            let intermediate = try PDFOfficeCodec.prepare(input, to: format, at: work, options: options, token: token)
            if format == "txt" || format == "md" { return try publish(intermediate, to: output, token: token) }
            if format == "pages" {
                let docx = try office(intermediate, to: "docx", in: staging, token: token)
                try PagesCodec.create(fromDOCX: docx, at: output, token: token)
                return try checked([output])
            }
            if format == "epub" {
                let html = try office(intermediate, to: "html", in: staging, token: token)
                let epub = work.appendingPathComponent("PDF book.epub")
                try EPUBCodec.create(fromHTML: html, title: input.deletingPathExtension().lastPathComponent, at: epub, token: token)
                return try publish(epub, to: output, token: token)
            }
            return try publish(office(intermediate, to: format, in: staging, token: token), to: output, token: token)
        }

        var source = input
        let ext = normalized(input.pathExtension)
        if ext == "html" && format == "pdf" {
            // The office importer drops most CSS layout; WebKit prints the page as a browser does.
            let pdf = work.appendingPathComponent(input.deletingPathExtension().lastPathComponent).appendingPathExtension("pdf")
            try HTMLPDFRenderer.render(input, to: pdf, token: token)
            return try publish(pdf, to: output, token: token)
        }
        if ext == "pages" {
            // Modern IWA Pages files are parsed locally; the original is
            // never opened by or handed to an installed Apple office app.
            source = try PagesCodec.materializeHTML(input, in: work, token: token)
        } else if ext == "epub" {
            source = try EPUBCodec.unpackToHTML(input, in: work, token: token)
        } else if ext == "textclipping" {
            source = try TextClippingCodec.materialize(input, in: work)
        } else if ext == "rtfd" {
            source = try RichTextCodec.materializeHTML(input, in: work)
        } else if ext == "txt" || ext == "md" {
            let data = try Data(contentsOf: input)
            if String(data: data, encoding: .utf8) == nil || data.contains(0) {
                source = work.appendingPathComponent(input.lastPathComponent)
                try DocumentText.read(input).write(to: source, atomically: true, encoding: .utf8)
            }
        }

        if format == "epub" && options.epubLayout == "fixed" {
            let pdf = try office(source, to: "pdf", in: staging, token: token)
            let epub = work.appendingPathComponent("Fixed book.epub")
            try EPUBCodec.createFixed(fromPDF: pdf, title: input.deletingPathExtension().lastPathComponent, at: epub, options: options, token: token)
            return try publish(epub, to: output, token: token)
        }

        if ["png", "jpg"].contains(format) {
            let pdf = try office(source, to: "pdf", in: staging, token: token)
            // Give rendered pages the input's name and the requested destination.
            let named = work.appendingPathComponent(input.deletingPathExtension().lastPathComponent).appendingPathExtension("pdf")
            if pdf != named { try FileManager.default.copyItem(at: pdf, to: named) }
            var renderOptions = options
            renderOptions.outputDirectory = options.outputDirectory ?? input.deletingLastPathComponent()
            return try PDFEngine.render(named, to: format, options: renderOptions, token: token)
        }
        if format == "pages" {
            let docx: URL
            if source.pathExtension.lowercased() == "docx" { docx = source }
            else { docx = try office(source, to: "docx", in: staging, token: token) }
            try PagesCodec.create(fromDOCX: docx, at: output, token: token)
            return try checked([output])
        }
        let converted: URL
        do { converted = try office(source, to: format, in: staging, token: token) }
        catch {
            try token.check()
            if format == "epub" {
                let htmlFolder = work.appendingPathComponent("EPUB source", isDirectory: true)
                try FileManager.default.createDirectory(at: htmlFolder, withIntermediateDirectories: true)
                let html = try office(source, to: "html", in: htmlFolder, token: token)
                let epub = work.appendingPathComponent("Book.epub")
                try EPUBCodec.create(fromHTML: html, title: input.deletingPathExtension().lastPathComponent, at: epub, token: token)
                converted = epub
            } else { throw error }
        }
        return try publish(converted, to: output, token: token)
    }

    public static func extractArchive(_ input: URL, options: OperationOptions, token: CancellationToken) throws -> [URL] {
        let parent = options.outputDirectory ?? input.deletingLastPathComponent()
        try FileManager.default.createDirectory(at: parent, withIntermediateDirectories: true)
        let name = archiveNamed(input).deletingPathExtension().lastPathComponent + " Extracted"
        let output = uniqueFolder(name, in: parent)
        let work = try OutputFiles.temporaryDirectory()
        defer { try? FileManager.default.removeItem(at: work) }
        let extracted = work.appendingPathComponent(name, isDirectory: true)
        try ArchiveCodec.extract(input, into: extracted, password: options.password, token: token)
        try token.check()
        try FileManager.default.moveItem(at: extracted, to: output)
        return [output]
    }

    private static func convertArchive(_ input: URL, to format: String, options: OperationOptions, token: CancellationToken) throws -> [URL] {
        guard ["zip", "tar", "gz"].contains(format) else { throw NashiError.unsupported("Archives can be converted to ZIP, TAR, or GZIP.") }
        let work = try OutputFiles.temporaryDirectory()
        defer { try? FileManager.default.removeItem(at: work) }
        let extracted = work.appendingPathComponent("Contents", isDirectory: true)
        try ArchiveCodec.extract(input, into: extracted, password: options.password, token: token)
        let archive = work.appendingPathComponent("Archive").appendingPathExtension(format)
        let level = options.compressionPreset == "fast" ? 0 : options.compressionPreset == "maximum" ? 9 : 6
        try ArchiveCodec.create(from: extracted, at: archive, format: format, compressionLevel: level, token: token)
        return try publish(archive, to: destination(archiveNamed(input), format: format, options: options), token: token)
    }

    /// Names outputs of "Name.tar.gz" after "Name", as is already done for "Name.tgz".
    private static func archiveNamed(_ input: URL) -> URL {
        let inner = input.deletingPathExtension()
        return ["gz", "gzip"].contains(input.pathExtension.lowercased()) && inner.pathExtension.lowercased() == "tar" ? inner : input
    }

    /// Each invocation owns an isolated LibreOffice profile. Headless conversion
    /// therefore cannot reuse or alter a user's open LibreOffice documents.
    static func office(_ input: URL, to format: String, in directory: URL, token: CancellationToken) throws -> URL {
        try token.check()
        let filter = try officeFilter(format, category: FileCategory.detect(input))
        let fm = FileManager.default
        try fm.createDirectory(at: directory, withIntermediateDirectories: true)
        let profile = try OutputFiles.temporaryDirectory()
        defer { try? fm.removeItem(at: profile) }
        let profileUser = profile.appendingPathComponent("user", isDirectory: true)
        try fm.createDirectory(at: profileUser, withIntermediateDirectories: true)
        let config = """
        <?xml version="1.0" encoding="UTF-8"?><oor:items xmlns:oor="http://openoffice.org/2001/registry"><item oor:path="/org.openoffice.Office.Common/Security/Scripting"><prop oor:name="MacroSecurityLevel" oor:op="fuse"><value>3</value></prop></item><item oor:path="/org.openoffice.Office.Common/Load"><prop oor:name="UpdateLinkMode" oor:op="fuse"><value>0</value></prop></item></oor:items>
        """
        try config.write(to: profileUser.appendingPathComponent("registrymodifications.xcu"), atomically: true, encoding: .utf8)
        let executable = try Dependencies.executable("soffice")
        var arguments = ["-env:UserInstallation=\(profile.absoluteString)", "--headless", "--nologo", "--nodefault", "--nolockcheck", "--nofirststartwizard", "--norestore"]
        let sourceFormat = normalized(input.pathExtension)
        var source = input
        if sourceFormat == "html" {
            let prepared = profile.appendingPathComponent(input.lastPathComponent)
            try embedHTMLImages(input, output: prepared, token: token)
            source = prepared
        }
        if ["csv", "tsv"].contains(sourceFormat) {
            let delimiter = sourceFormat == "tsv" ? 9 : 44
            arguments.append("--infilter=Text - txt - csv (StarCalc):\(delimiter),34,76,1,,1033,true,true")
        } else if sourceFormat == "txt" {
            arguments.append("--infilter=Text (encoded):UTF8,LF")
        } else if sourceFormat == "md" {
            arguments.append("--infilter=Markdown")
        }
        arguments += ["--convert-to", filter, "--outdir", directory.path, source.path]
        // LibreOffice imports linked HTML resources. Denying IP networking at
        // the process boundary keeps conversions local. Unix-domain IPC remains
        // available because the upstream macOS build requires it to start.
        let result: ProcessResult
        let sandbox = URL(fileURLWithPath: "/usr/bin/sandbox-exec")
        if fm.isExecutableFile(atPath: sandbox.path) {
            result = try ProcessRunner.run(sandbox, ["-p", "(version 1)(allow default)(deny network* (remote ip))", executable.path] + arguments, token: token, environment: ["SAL_DISABLE_OPENCL": "1", "LC_ALL": "en_US.UTF-8"])
        } else {
            result = try ProcessRunner.run(executable, arguments, token: token, environment: ["SAL_DISABLE_OPENCL": "1", "LC_ALL": "en_US.UTF-8"])
        }
        let output = directory.appendingPathComponent(input.deletingPathExtension().lastPathComponent).appendingPathExtension(format)
        guard fm.fileExists(atPath: output.path) else {
            let diagnostic = result.text + String(decoding: result.errorOutput, as: UTF8.self)
            throw NashiError.failed("The local document converter did not produce a \(format.uppercased()) file. \(diagnostic.trimmingCharacters(in: .whitespacesAndNewlines))")
        }
        try OutputFiles.validate([output])
        if format == "html" { try embedHTMLImages(output, token: token) }
        return output
    }

    private static func officeFilter(_ format: String, category: FileCategory) throws -> String {
        switch format {
        case "docx": return "docx:Office Open XML Text"
        case "doc": return "doc:MS Word 97"
        case "odt": return "odt:writer8"
        case "rtf": return "rtf:Rich Text Format"
        case "txt": return "txt:Text (encoded):UTF8"
        case "md": return "md:Markdown"
        case "html": return "html:HTML (StarWriter)"
        case "epub": return "epub:EPUB"
        case "xlsx": return "xlsx:Calc MS Excel 2007 XML"
        case "xls": return "xls:MS Excel 97"
        case "ods": return "ods:calc8"
        case "csv", "tsv": return "\(format):Text - txt - csv (StarCalc):\(format == "csv" ? 44 : 9),34,76,1,,0,false,true,false,false,false,0"
        case "pptx": return "pptx:Impress MS PowerPoint 2007 XML"
        case "ppt": return "ppt:MS PowerPoint 97"
        case "pdf":
            if category == .spreadsheet { return "pdf:calc_pdf_Export" }
            if category == .presentation { return "pdf:impress_pdf_Export" }
            return "pdf:writer_pdf_Export"
        default: throw NashiError.unsupported("Cannot convert this document to \(format.uppercased()).")
        }
    }

    static func embedHTMLImages(_ html: URL, output: URL? = nil, token: CancellationToken) throws {
        var source = try DocumentText.read(html)
        let root=html.deletingLastPathComponent().resolvingSymlinksInPath().standardizedFileURL.path
        func imageData(_ value:String)->String {
            let data:Data
            if value.lowercased().hasPrefix("data:") {
                guard value.utf8.count<70_000_000,let comma=value.firstIndex(of:","),value[..<comma].lowercased().hasSuffix(";base64"),let decoded=Data(base64Encoded:String(value[value.index(after:comma)...])) else{return ""}
                data=decoded
            } else {
                guard let decoded=value.removingPercentEncoding else{return ""}
                let asset:URL
                if let address=URL(string:value),address.scheme != nil {
                    guard address.isFileURL else{return ""};asset=address
                }else{asset=html.deletingLastPathComponent().appendingPathComponent(decoded)}
                let resolved=asset.resolvingSymlinksInPath().standardizedFileURL
                guard resolved.path.hasPrefix(root+"/"),let size=try? resolved.resourceValues(forKeys:[.fileSizeKey,.isRegularFileKey]),size.isRegularFile==true,(size.fileSize ?? Int.max)<=50_000_000,let bytes=try? Data(contentsOf:resolved) else{return ""}
                data=bytes
            }
            guard let raster=CGImageSourceCreateWithData(data as CFData,nil),let type=CGImageSourceGetType(raster),
                  let properties=CGImageSourceCopyPropertiesAtIndex(raster,0,nil) as? [CFString:Any],
                  let width=properties[kCGImagePropertyPixelWidth] as? Int,let height=properties[kCGImagePropertyPixelHeight] as? Int,
                  width>0,height>0,Double(width)*Double(height)<=100_000_000,
                  let mime=UTType(type as String)?.preferredMIMEType,mime.hasPrefix("image/"),!mime.contains("svg") else{return ""}
            return "data:\(mime);base64,\(data.base64EncodedString())"
        }
        // Linked resources must be local raster images beside the selected HTML.
        // External CSS and SVG may load arbitrary additional resources, so they
        // are excluded instead of handing those references to the importer.
        for pattern in [#"(?is)<link\b[^>]*>"#,#"(?is)@import\s+[^;]+;"#] {
            let expression=try NSRegularExpression(pattern:pattern)
            source=expression.stringByReplacingMatches(in:source,range:NSRange(source.startIndex...,in:source),withTemplate:"")
        }
        let attributes=try NSRegularExpression(pattern:#"(?i)\bsrc\s*=\s*(?:"([^"]*)"|'([^']*)'|([^\s>]+))"#)
        for match in attributes.matches(in:source,range:NSRange(source.startIndex...,in:source)).reversed() {
            try token.check();let index=(1...3).first{match.range(at:$0).location != NSNotFound}!
            let value=(source as NSString).substring(with:match.range(at:index))
            if let range=Range(match.range,in:source){source.replaceSubrange(range,with:"src=\"\(imageData(value))\"")}
        }
        let css=try NSRegularExpression(pattern:#"(?is)url\(\s*(['"]?)(.*?)\1\s*\)"#)
        for match in css.matches(in:source,range:NSRange(source.startIndex...,in:source)).reversed() {
            try token.check();let value=(source as NSString).substring(with:match.range(at:2))
            if let range=Range(match.range,in:source){source.replaceSubrange(range,with:"url('\(imageData(value))')")}
        }
        try source.write(to:output ?? html,atomically:true,encoding:.utf8)
    }

    private static func normalized(_ format: String) -> String {
        let value = format.lowercased().trimmingCharacters(in: CharacterSet(charactersIn: ". "))
        return ["jpeg": "jpg", "markdown": "md", "htm": "html", "gzip": "gz", "tgz": "gz"][value] ?? value
    }
    private static func destination(_ input: URL, format: String, options: OperationOptions) throws -> URL {
        let directory = options.outputDirectory ?? input.deletingLastPathComponent()
        try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
        return OutputFiles.unique(for: input, extension: format, directory: directory)
    }
    private static func uniqueFolder(_ name: String, in parent: URL) -> URL {
        var candidate = parent.appendingPathComponent(name, isDirectory: true), index = 2
        // lstat-based, so a dangling link with this name is not chosen for moveItem.
        while (try? FileManager.default.attributesOfItem(atPath: candidate.path)) != nil {
            candidate = parent.appendingPathComponent("\(name) \(index)", isDirectory: true); index += 1
        }
        return candidate
    }
    private static func publish(_ source: URL, to output: URL, token: CancellationToken) throws -> [URL] {
        try token.check()
        try OutputFiles.validate([source])
        try FileManager.default.moveItem(at: source, to: output)
        return try checked([output])
    }
    private static func checked(_ urls: [URL]) throws -> [URL] { try OutputFiles.validate(urls); return urls }
}

enum RichTextCodec {
    static func materializeHTML(_ input: URL, in work: URL) throws -> URL {
        let text = try NSAttributedString(url: input, options: [.documentType: NSAttributedString.DocumentType.rtfd], documentAttributes: nil)
        let wrapper = try text.fileWrapper(from: NSRange(location: 0, length: text.length), documentAttributes: [.documentType: NSAttributedString.DocumentType.html, .characterEncoding: String.Encoding.utf8.rawValue])
        if wrapper.isDirectory {
            let directory = work.appendingPathComponent("Rich text resources", isDirectory: true)
            try wrapper.write(to: directory, options: .atomic, originalContentsURL: nil)
            let output = directory.appendingPathComponent("index.html")
            // Cocoa writes RTFD attachment URLs as file:///image.png even when
            // its HTML file wrapper placed image.png beside index.html.
            var html = try DocumentText.read(output)
            let pattern = try NSRegularExpression(pattern: #"(?i)\bsrc\s*=\s*(["'])(file:[^"']+)\1"#)
            for match in pattern.matches(in: html, range: NSRange(html.startIndex..., in: html)).reversed() {
                let source = (html as NSString).substring(with: match.range(at: 2))
                guard let original = URL(string: source) else { continue }
                let candidate = directory.appendingPathComponent(original.lastPathComponent)
                if FileManager.default.fileExists(atPath: candidate.path), let range = Range(match.range(at: 2), in: html) {
                    html.replaceSubrange(range, with: candidate.absoluteString)
                }
            }
            try html.write(to: output, atomically: true, encoding: .utf8)
            return output
        }
        let output = work.appendingPathComponent("Rich text document.html")
        try wrapper.write(to: output, options: .atomic, originalContentsURL: nil)
        return output
    }
}

enum TextClippingCodec {
    static func materialize(_ input: URL, in work: URL) throws -> URL {
        let fm = FileManager.default
        let regular = (try? Data(contentsOf: input)) ?? Data()
        if !regular.isEmpty {
            if let plist = try? PropertyListSerialization.propertyList(from: regular, format: nil), let payload = plistText(plist) {
                return try save(payload.0, extension: payload.1, in: work)
            }
            if String(data: regular, encoding: .utf8) != nil { return try save(regular, extension: "txt", in: work) }
        }
        let fork = input.path + "/..namedfork/rsrc"
        guard fm.fileExists(atPath: fork), let data = try? Data(contentsOf: URL(fileURLWithPath: fork)), data.count >= 16 else {
            throw NashiError.invalidInput("No readable text was found in this text clipping.")
        }
        func u16(_ offset: Int) -> Int? { guard offset >= 0, offset + 2 <= data.count else { return nil }; return Int(data[offset]) << 8 | Int(data[offset + 1]) }
        func u32(_ offset: Int) -> Int? { guard offset >= 0, offset + 4 <= data.count else { return nil }; return (0..<4).reduce(0) { ($0 << 8) | Int(data[offset + $1]) } }
        guard let dataOffset = u32(0), let mapOffset = u32(4), let typeOffset = u16(mapOffset + 24) else { throw NashiError.invalidInput("The text clipping's resource data is damaged.") }
        let typeBase = mapOffset + typeOffset
        guard let countMinusOne = u16(typeBase), countMinusOne < 4096 else { throw NashiError.invalidInput("Invalid text clipping resources.") }
        var candidates: [(String, Data)] = []
        for index in 0...countMinusOne {
            let offset = typeBase + 2 + index * 8
            guard offset + 8 <= data.count, let resourceCount = u16(offset + 4), resourceCount < 4096, let references = u16(offset + 6) else { continue }
            let type = String(decoding: data[offset..<(offset + 4)], as: UTF8.self)
            for resource in 0...resourceCount {
                let reference = typeBase + references + resource * 12
                guard reference + 8 <= data.count else { continue }
                let location = dataOffset + (Int(data[reference + 5]) << 16 | Int(data[reference + 6]) << 8 | Int(data[reference + 7]))
                guard let length = u32(location), length <= data.count, location + 4 <= data.count - length else { continue }
                candidates.append((type, data[(location + 4)..<(location + 4 + length)]))
            }
        }
        for type in ["RTF ", "utf8", "utxt", "TEXT"] {
            guard let payload = candidates.first(where: { $0.0 == type })?.1 else { continue }
            if type == "RTF " { return try save(payload, extension: "rtf", in: work) }
            let encoding: String.Encoding = type == "utxt" ? .utf16BigEndian : type == "TEXT" ? .macOSRoman : .utf8
            if let text = String(data: payload, encoding: encoding) { return try save(Data(text.utf8), extension: "txt", in: work) }
        }
        throw NashiError.invalidInput("This text clipping contains no supported text resource.")
    }
    private static func plistText(_ plist: Any) -> (Data, String)? {
        guard let dictionary = plist as? [String: Any] else { return nil }
        for (key, ext) in [("public.rtf", "rtf"), ("public.utf8-plain-text", "txt"), ("public.plain-text", "txt")] {
            if let data = dictionary[key] as? Data { return (data, ext) }
            if let value = dictionary[key] as? String { return (Data(value.utf8), ext) }
        }
        for value in dictionary.values { if let found = plistText(value) { return found } }
        return nil
    }
    private static func save(_ data: Data, extension ext: String, in work: URL) throws -> URL {
        let output = work.appendingPathComponent("Text clipping").appendingPathExtension(ext)
        try data.write(to: output); return output
    }
}
