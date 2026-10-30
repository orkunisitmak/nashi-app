import Foundation
import AppKit
import PDFKit
import Vision

/// Converts the fixed PDF page model into local office intermediates. Text
/// documents and sheets contain editable extracted/OCR text; slides preserve
/// the page appearance as full-resolution page images.
enum PDFOfficeCodec {
    struct TextCell { var text: String; var rect: CGRect }

    static func visualBounds(_ page: PDFPage) -> CGRect {
        let box = page.bounds(for: .cropBox)
        let rotated = abs(page.rotation) % 180 == 90
        return CGRect(origin: .zero, size: rotated ? CGSize(width: box.height, height: box.width) : box.size)
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
        guard let pdf = PDFDocument(url: input) else { throw NashiError.invalidInput("This PDF could not be opened.") }
        if pdf.isLocked && !pdf.unlock(withPassword: options.password) { throw NashiError.invalidInput("The PDF password is incorrect or missing.") }
        guard pdf.pageCount > 0 else { throw NashiError.invalidInput("This PDF contains no pages.") }
        if format == "pptx" { return try presentation(pdf, at: work, token: token) }
        if format == "xlsx" { return try spreadsheet(pdf, at: work, token: token) }
        if ["docx", "pages"].contains(format) && options.pdfOfficeLayout == "preservePages" {
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
        return try PDFDocumentReconstruction.document(pdf, at: work.appendingPathComponent("PDF document.fodt"), options: options, token: token)
    }

    static func text(_ page: PDFPage, token: CancellationToken) throws -> String {
        if let text = page.string, !text.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty { return text }
        return try recognizedCells(page, token: token).sorted { $0.rect.midY > $1.rect.midY }.map(\.text).joined(separator: "\n")
    }

    static func recognizedCells(_ page: PDFPage, token: CancellationToken) throws -> [TextCell] {
        try token.check()
        let bounds = page.bounds(for: .cropBox)
        let size = NSSize(width: max(1, bounds.width * 2), height: max(1, bounds.height * 2))
        let image = page.thumbnail(of: size, for: .cropBox)
        var rect = CGRect(origin: .zero, size: size)
        guard let cgImage = image.cgImage(forProposedRect: &rect, context: nil, hints: nil) else { return [] }
        let request = VNRecognizeTextRequest()
        request.recognitionLevel = .accurate
        request.usesLanguageCorrection = true
        request.automaticallyDetectsLanguage = true
        try VNImageRequestHandler(cgImage: cgImage).perform([request])
        try token.check()
        return (request.results ?? []).compactMap { observation in
            guard let candidate = observation.topCandidates(1).first else { return nil }
            let r = observation.boundingBox
            return TextCell(text: candidate.string, rect: CGRect(x: bounds.minX + r.minX * bounds.width, y: bounds.minY + r.minY * bounds.height, width: r.width * bounds.width, height: r.height * bounds.height))
        }
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
            guard let cg = image.cgImage(forProposedRect: &rect, context: nil, hints: nil), let png = NSBitmapImageRep(cgImage: cg).representation(using: .png, properties: [:]) else {
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
