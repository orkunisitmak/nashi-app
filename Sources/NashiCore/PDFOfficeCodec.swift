import Foundation
import AppKit
import PDFKit
import Vision

/// Converts the fixed PDF page model into local office intermediates. Text
/// documents and sheets contain editable extracted/OCR text; slides preserve
/// the page appearance as full-resolution page images.
enum PDFOfficeCodec {
    struct TextCell { var text: String; var rect: CGRect; var color: String? = nil }

    static func visualBounds(_ page: PDFPage) -> CGRect {
        let box = page.bounds(for: .cropBox)
        let rotated = abs(page.rotation) % 180 == 90
        return CGRect(origin: .zero, size: rotated ? CGSize(width: box.height, height: box.width) : box.size)
    }

    /// PDFKit renders thumbnails in the display's colour space (Display P3 on
    /// most Macs). PowerPoint, Keynote exports and the web read untagged or
    /// ignore embedded profiles, so office images are converted to sRGB.
    static func sRGB(_ image: CGImage) -> CGImage? {
        guard let space = CGColorSpace(name: CGColorSpace.sRGB),
              let context = CGContext(data: nil, width: image.width, height: image.height, bitsPerComponent: 8, bytesPerRow: 0, space: space, bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue) else { return nil }
        context.draw(image, in: CGRect(x: 0, y: 0, width: image.width, height: image.height))
        return context.makeImage()
    }

    static func rasterPNG(_ page: PDFPage, rect crop: CGRect? = nil, dpi: Int = 144, token: CancellationToken) throws -> Data {
        try token.check()
        let bounds = crop ?? visualBounds(page)
        let scale = CGFloat(max(36, min(600, dpi))) / 72
        let width = max(1, Int(ceil(bounds.width * scale))), height = max(1, Int(ceil(bounds.height * scale)))
        guard width <= 32000, height <= 32000, let cgPage = page.pageRef,
              let context = CGContext(data: nil, width: width, height: height, bitsPerComponent: 8, bytesPerRow: 0, space: CGColorSpaceCreateDeviceRGB(), bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue) else {
            throw NashiError.failed("The PDF page is too large to render at this resolution.")
        }
        context.setFillColor(CGColor(gray: 1, alpha: 1)); context.fill(CGRect(x: 0, y: 0, width: width, height: height))
        context.scaleBy(x: scale, y: scale)
        if crop != nil { context.translateBy(x: -bounds.minX, y: -bounds.minY) }
        else { context.concatenate(cgPage.getDrawingTransform(.cropBox, rect: bounds, rotate: 0, preserveAspectRatio: true)) }
        context.drawPDFPage(cgPage)
        try token.check()
        guard let image = context.makeImage(), let data = NSBitmapImageRep(cgImage: image).representation(using: .png, properties: [:]) else { throw NashiError.failed("The PDF page image could not be written.") }
        return data
    }

    static func prepare(_ input: URL, to format: String, at work: URL, options: OperationOptions, token: CancellationToken) throws -> URL {
        guard let source = PDFDocument(url: input) else { throw NashiError.invalidInput("This PDF could not be opened.") }
        if source.isLocked && !source.unlock(withPassword: options.password) { throw NashiError.invalidInput("The PDF password is incorrect or missing.") }
        guard source.pageCount > 0 else { throw NashiError.invalidInput("This PDF contains no pages.") }
        if format == "pptx" { return try presentation(source, at: work, token: token) }
        // Photographed documents are cut to the paper and straightened first,
        // so OCR reads horizontal lines and the desk stays out of the result.
        // Slides and "keep pages as pictures" show the page exactly as it is.
        let keepsPages = ["docx", "pages"].contains(format) && options.pdfOfficeLayout == "preservePages"
        var pdf = source
        if !keepsPages, let flattened = try PDFScanRectification.rectified(source, token: token) { pdf = flattened }
        if format == "xlsx" { return try spreadsheet(pdf, at: work, token: token) }
        if keepsPages {
            return try PDFDocumentReconstruction.preservePages(pdf, at: work.appendingPathComponent("PDF pages.fodt"), options: options, token: token)
        }
        if format == "txt" || format == "md" {
            var pages: [String] = []
            for index in 0..<pdf.pageCount {
                try token.check()
                guard let page = pdf.page(at: index) else { continue }
                pages.append(try text(page, token: token))
            }
            let output = work.appendingPathComponent("Extracted text").appendingPathExtension(format)
            let content = pages.joined(separator: format == "md" ? "\n\n---\n\n" : "\n\n\u{0c}\n\n")
            guard !content.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty else { throw NashiError.failed("No text could be extracted or recognized in this PDF.") }
            try content.write(to: output, atomically: true, encoding: .utf8)
            return output
        }
        return try PDFDocumentReconstruction.document(pdf, at: work.appendingPathComponent("PDF document.fodt"), options: options, token: token, artwork: format == "docx")
    }

    static func text(_ page: PDFPage, token: CancellationToken) throws -> String {
        if let text = page.string, !text.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty { return text }
        return try recognizedCells(page, token: token).sorted { $0.rect.midY > $1.rect.midY }.map(\.text).joined(separator: "\n")
    }

    static func recognizedCells(_ page: PDFPage, token: CancellationToken) throws -> [TextCell] {
        try token.check()
        let bounds = page.bounds(for: .cropBox)
        let size = NSSize(width: max(1, bounds.width * 3), height: max(1, bounds.height * 3))
        let image = page.thumbnail(of: size, for: .cropBox)
        var rect = CGRect(origin: .zero, size: size)
        guard let cgImage = image.cgImage(forProposedRect: &rect, context: nil, hints: nil) else { return [] }
        var observations = try recognize(cgImage, languages: ocrLanguages, detect: true)
        try token.check()
        // Vision reads a page best with its script's languages first. A page
        // that turns out to be mostly Cyrillic is read again that way, with
        // Russian/Ukrainian spelling correction instead of Turkish/English.
        let first = observations.compactMap { $0.topCandidates(1).first?.string }
        if dominantScript(first) == .cyrillic {
            let again = try recognize(cgImage, languages: cyrillicLanguages, detect: false)
            try token.check()
            let letters = { (items: [VNRecognizedTextObservation]) in items.compactMap { $0.topCandidates(1).first?.string }.joined().filter(\.isLetter).count }
            if letters(again) >= letters(observations) * 8 / 10 { observations = again }
        }
        let width = cgImage.width, height = cgImage.height
        var pixels = [UInt8](repeating: 255, count: width * height * 4)
        pixels.withUnsafeMutableBytes { raw in
            CGContext(data: raw.baseAddress, width: width, height: height, bitsPerComponent: 8, bytesPerRow: width * 4, space: CGColorSpaceCreateDeviceRGB(), bitmapInfo: CGImageAlphaInfo.noneSkipLast.rawValue)?.draw(cgImage, in: CGRect(x: 0, y: 0, width: width, height: height))
        }
        /// Ink of a recognised line: the near-neutral pixels farthest from the
        /// line's surrounding paper (dark text on light paper, white on a band).
        func ink(_ box: CGRect) -> String? {
            let x0 = max(0, Int(box.minX * CGFloat(width))), x1 = min(width - 1, Int(box.maxX * CGFloat(width)))
            let y0 = max(0, Int((1 - box.maxY) * CGFloat(height))), y1 = min(height - 1, Int((1 - box.minY) * CGFloat(height)))
            guard x1 > x0 + 2, y1 > y0 + 2 else { return nil }
            func at(_ x: Int, _ y: Int) -> (Int, Int, Int) { let i = (y * width + x) * 4; return (Int(pixels[i]), Int(pixels[i + 1]), Int(pixels[i + 2])) }
            var ring: [(Int, Int, Int)] = []
            for x in stride(from: x0, through: x1, by: 3) { if y0 >= 3 { ring.append(at(x, y0 - 3)) }; if y1 + 3 < height { ring.append(at(x, y1 + 3)) } }
            guard !ring.isEmpty else { return nil }
            let paper = (ring.map(\.0).sorted()[ring.count / 2], ring.map(\.1).sorted()[ring.count / 2], ring.map(\.2).sorted()[ring.count / 2])
            var candidates: [(Int, Int, Int)] = []
            for y in stride(from: y0, through: y1, by: 2) { for x in stride(from: x0, through: x1, by: 2) { let c = at(x, y); if max(c.0, c.1, c.2) - min(c.0, c.1, c.2) < 60 { candidates.append(c) } } }
            guard !candidates.isEmpty else { return nil }
            candidates.sort { abs($0.0 - paper.0) + abs($0.1 - paper.1) + abs($0.2 - paper.2) > abs($1.0 - paper.0) + abs($1.1 - paper.1) + abs($1.2 - paper.2) }
            let top = candidates.prefix(max(1, candidates.count / 10))
            let c = (top.map(\.0).reduce(0, +) / top.count, top.map(\.1).reduce(0, +) / top.count, top.map(\.2).reduce(0, +) / top.count)
            guard abs(c.0 - paper.0) + abs(c.1 - paper.1) + abs(c.2 - paper.2) > 90 else { return nil }
            return String(format: "#%02X%02X%02X", c.0, c.1, c.2)
        }
        return observations.compactMap { observation in
            // A lone recognised character is usually a logo, bullet or mark, not text.
            guard let candidate = observation.topCandidates(1).first, candidate.confidence >= 0.45,
                  candidate.string.filter({ $0.isLetter || $0.isNumber }).count >= 2 else { return nil }
            // Stamps, seals and watermarks are set at an angle. Their text is
            // kept as part of the page artwork rather than as body text.
            let dx = (observation.topRight.x - observation.topLeft.x) * size.width, dy = (observation.topRight.y - observation.topLeft.y) * size.height
            guard abs(atan2(dy, dx)) < 3 * .pi / 180 else { return nil }
            let r = observation.boundingBox
            return TextCell(text: candidate.string, rect: CGRect(x: bounds.minX + r.minX * bounds.width, y: bounds.minY + r.minY * bounds.height, width: r.width * bounds.width, height: r.height * bounds.height), color: ink(r))
        }
    }

    /// OCR languages in priority order. Vision also detects the language of
    /// each page, so Turkish and English documents read as before while
    /// Russian, Ukrainian and the main Western European languages are read
    /// with their own alphabets and spelling instead of Latin look-alikes.
    static let ocrLanguages = ["tr-TR", "en-US", "ru-RU", "uk-UA", "de-DE", "fr-FR", "es-ES", "it-IT", "pt-BR"]
    static let cyrillicLanguages = ["ru-RU", "uk-UA", "en-US"]

    enum Script { case latin, cyrillic, other }

    /// The alphabet most letters of the recognised lines are written in.
    static func dominantScript(_ lines: [String]) -> Script {
        var latin = 0, cyrillic = 0, other = 0
        for scalar in lines.joined().unicodeScalars where scalar.properties.isAlphabetic {
            switch scalar.value {
            case 0x0041...0x024F: latin += 1
            case 0x0400...0x052F: cyrillic += 1
            default: other += 1
            }
        }
        if cyrillic > 0, cyrillic * 4 >= (latin + cyrillic + other) { return .cyrillic }
        return latin >= other ? .latin : .other
    }

    private static func recognize(_ image: CGImage, languages: [String], detect: Bool) throws -> [VNRecognizedTextObservation] {
        let request = VNRecognizeTextRequest()
        request.recognitionLevel = .accurate
        request.usesLanguageCorrection = true
        let supported = Set((try? request.supportedRecognitionLanguages()) ?? ["en-US"])
        let usable = languages.filter(supported.contains)
        request.recognitionLanguages = usable.isEmpty ? ["en-US"] : usable
        request.automaticallyDetectsLanguage = detect
        try VNImageRequestHandler(cgImage: image).perform([request])
        return request.results ?? []
    }

    private static func presentation(_ pdf: PDFDocument, at work: URL, token: CancellationToken) throws -> URL {
        guard let first = pdf.page(at: 0) else { throw NashiError.invalidInput("This PDF contains no pages.") }
        let canvas = visualBounds(first)
        guard canvas.width.isFinite, canvas.height.isFinite, canvas.width > 0, canvas.height > 0 else {
            throw NashiError.invalidInput("The PDF has an invalid page size.")
        }
        let width = canvas.width / 72 * 2.54, height = canvas.height / 72 * 2.54
        let orientation = width > height ? "landscape" : "portrait"
        let layout = "<style:page-layout style:name=\"PDFCanvas\"><style:page-layout-properties fo:page-width=\"\(width)cm\" fo:page-height=\"\(height)cm\" fo:margin=\"0cm\" style:print-orientation=\"\(orientation)\"/></style:page-layout>"
        let master = "<style:master-page style:name=\"PDFMaster\" style:page-layout-name=\"PDFCanvas\"/>"
        var slides = ""
        for index in 0..<pdf.pageCount {
            try token.check()
            guard let page = pdf.page(at: index) else { continue }
            let bounds = visualBounds(page)
            guard bounds.width.isFinite, bounds.height.isFinite, bounds.width > 0, bounds.height > 0 else {
                throw NashiError.invalidInput("PDF page \(index + 1) has an invalid size.")
            }
            let size = NSSize(width: max(1, bounds.width * 2), height: max(1, bounds.height * 2))
            let image = page.thumbnail(of: size, for: .cropBox)
            var rect = CGRect(origin: .zero, size: size)
            guard let rendered = image.cgImage(forProposedRect: &rect, context: nil, hints: nil), let cg = sRGB(rendered), let png = NSBitmapImageRep(cgImage: cg).representation(using: .png, properties: [:]) else {
                throw NashiError.failed("PDF page \(index + 1) could not be rendered.")
            }
            // A presentation has one shared canvas. Match the first displayed
            // PDF page, then fit and center later pages without distorting them.
            let scale = min(canvas.width / bounds.width, canvas.height / bounds.height)
            let w = bounds.width * scale / 72 * 2.54, h = bounds.height * scale / 72 * 2.54
            let x = (width - w) / 2, y = (height - h) / 2
            slides += "<draw:page draw:name=\"Page \(index + 1)\" draw:master-page-name=\"PDFMaster\"><draw:frame draw:name=\"PDF Page \(index + 1)\" svg:x=\"\(x)cm\" svg:y=\"\(y)cm\" svg:width=\"\(w)cm\" svg:height=\"\(h)cm\"><draw:image><office:binary-data>\(png.base64EncodedString())</office:binary-data></draw:image></draw:frame></draw:page>"
        }
        let source = """
        <?xml version="1.0" encoding="UTF-8"?><office:document xmlns:office="urn:oasis:names:tc:opendocument:xmlns:office:1.0" xmlns:style="urn:oasis:names:tc:opendocument:xmlns:style:1.0" xmlns:draw="urn:oasis:names:tc:opendocument:xmlns:drawing:1.0" xmlns:svg="urn:oasis:names:tc:opendocument:xmlns:svg-compatible:1.0" xmlns:fo="urn:oasis:names:tc:opendocument:xmlns:xsl-fo-compatible:1.0" office:version="1.3" office:mimetype="application/vnd.oasis.opendocument.presentation"><office:styles/><office:automatic-styles>\(layout)</office:automatic-styles><office:master-styles>\(master)</office:master-styles><office:body><office:presentation>\(slides)</office:presentation></office:body></office:document>
        """
        let output = work.appendingPathComponent("PDF slides.fodp")
        try source.write(to: output, atomically: true, encoding: .utf8)
        return output
    }

    private static func spreadsheet(_ pdf: PDFDocument, at work: URL, token: CancellationToken) throws -> URL {
        var sheets = ""
        for index in 0..<pdf.pageCount {
            try token.check()
            guard let page = pdf.page(at: index) else { continue }
            let cells = try positionedCells(page, token: token)
            var rows: [[TextCell]] = []
            for cell in cells.sorted(by: { abs($0.rect.midY - $1.rect.midY) > 3 ? $0.rect.midY > $1.rect.midY : $0.rect.minX < $1.rect.minX }) {
                if let row = rows.indices.last, let y = rows[row].first?.rect.midY, abs(y - cell.rect.midY) < max(4, cell.rect.height * 0.5) { rows[row].append(cell) }
                else { rows.append([cell]) }
            }
            var anchors: [CGFloat] = []
            for cell in cells.sorted(by: { $0.rect.minX < $1.rect.minX }) {
                if !anchors.contains(where: { abs($0 - cell.rect.minX) < 15 }) { anchors.append(cell.rect.minX) }
            }
            anchors.sort()
            var table = ""
            for row in rows {
                var values = [String](repeating: "", count: max(1, anchors.count))
                for cell in row {
                    let column = anchors.indices.min(by: { abs(anchors[$0] - cell.rect.minX) < abs(anchors[$1] - cell.rect.minX) }) ?? 0
                    values[column] += (values[column].isEmpty ? "" : " ") + cell.text
                }
                table += "<table:table-row>" + values.map { value in
                    if value.range(of: #"^-?(?:0|[1-9]\d*)(?:\.\d+)?$"#, options: .regularExpression) != nil, Double(value) != nil {
                        return "<table:table-cell office:value-type=\"float\" office:value=\"\(value)\"><text:p>\(value)</text:p></table:table-cell>"
                    }
                    return "<table:table-cell office:value-type=\"string\"><text:p>\(DocumentText.escapeXML(value))</text:p></table:table-cell>"
                }.joined() + "</table:table-row>"
            }
            sheets += "<table:table table:name=\"Page \(index + 1)\">\(table)</table:table>"
        }
        let document = """
        <?xml version="1.0" encoding="UTF-8"?><office:document xmlns:office="urn:oasis:names:tc:opendocument:xmlns:office:1.0" xmlns:table="urn:oasis:names:tc:opendocument:xmlns:table:1.0" xmlns:text="urn:oasis:names:tc:opendocument:xmlns:text:1.0" office:version="1.3" office:mimetype="application/vnd.oasis.opendocument.spreadsheet"><office:body><office:spreadsheet>\(sheets)</office:spreadsheet></office:body></office:document>
        """
        let output = work.appendingPathComponent("PDF tables.fods")
        try document.write(to: output, atomically: true, encoding: .utf8)
        return output
    }

    private static func positionedCells(_ page: PDFPage, token: CancellationToken) throws -> [TextCell] {
        guard let string = page.string, !string.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty else { return try recognizedCells(page, token: token) }
        let ns = string as NSString
        var characters: [TextCell] = []
        var index = 0
        while index < ns.length {
            if index % 256 == 0 { try token.check() }
            let range = ns.rangeOfComposedCharacterSequence(at: index)
            let value = ns.substring(with: range)
            index = NSMaxRange(range)
            if value == "\n" || value == "\r" { continue }
            guard let selection = page.selection(for: range) else { continue }
            let bounds = selection.bounds(for: page)
            guard bounds.width > 0, bounds.height > 0, !bounds.isInfinite, !bounds.isNull else { continue }
            characters.append(.init(text: value, rect: bounds))
        }
        var lines: [[TextCell]] = []
        for character in characters.sorted(by: { abs($0.rect.midY - $1.rect.midY) > 3 ? $0.rect.midY > $1.rect.midY : $0.rect.minX < $1.rect.minX }) {
            if let index = lines.indices.last, let previous = lines[index].first, abs(previous.rect.midY - character.rect.midY) < max(3, character.rect.height * 0.35) { lines[index].append(character) }
            else { lines.append([character]) }
        }
        var cells: [TextCell] = []
        for line in lines {
            var current: TextCell?
            var previous: TextCell?
            for character in line.sorted(by: { $0.rect.minX < $1.rect.minX }) {
                if character.text.trimmingCharacters(in: .whitespaces).isEmpty && character.rect.width > max(7, character.rect.height * 0.7) {
                    if let cell = current, !cell.text.trimmingCharacters(in: .whitespaces).isEmpty { cells.append(.init(text: cell.text.trimmingCharacters(in: .whitespaces), rect: cell.rect)) }
                    current = nil; previous = nil; continue
                }
                if let prev = previous, character.rect.minX - prev.rect.maxX > max(7, character.rect.height * 0.7) {
                    if let cell = current, !cell.text.trimmingCharacters(in: .whitespaces).isEmpty { cells.append(.init(text: cell.text.trimmingCharacters(in: .whitespaces), rect: cell.rect)) }
                    current = nil
                }
                if current == nil { current = character }
                else { current!.text += character.text; current!.rect = current!.rect.union(character.rect) }
                previous = character
            }
            if let cell = current, !cell.text.trimmingCharacters(in: .whitespaces).isEmpty { cells.append(.init(text: cell.text.trimmingCharacters(in: .whitespaces), rect: cell.rect)) }
        }
        return cells
    }
}
