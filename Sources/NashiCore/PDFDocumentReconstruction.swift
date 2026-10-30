import Foundation
import AppKit
import PDFKit

/// Reconstructs document objects from PDF geometry. PDF has no paragraph/table
/// model, so those boundaries are inferred; the resulting objects remain real
/// editable paragraphs and tables rather than a picture of each document page.
enum PDFDocumentReconstruction {
    /// Deliberate appearance-preserving mode: one raster per PDF page. The
    /// editable reconstruction remains the default and uses the separate path.
    static func preservePages(_ pdf: PDFDocument, at output: URL, options: OperationOptions, token: CancellationToken) throws -> URL {
        var layouts = "", masters = "", styles = "", body = ""
        for index in 0..<pdf.pageCount {
            try token.check()
            guard let page = pdf.page(at:index) else {continue}
            let box = PDFOfficeCodec.visualBounds(page)
            let png = try PDFOfficeCodec.rasterPNG(page,dpi:options.dpi,token:token)
            layouts += "<style:page-layout style:name=\"Layout\(index)\"><style:page-layout-properties fo:page-width=\"\(box.width)pt\" fo:page-height=\"\(box.height)pt\" fo:margin=\"0pt\"/></style:page-layout>"
            masters += "<style:master-page style:name=\"Master\(index)\" style:page-layout-name=\"Layout\(index)\"/>"
            styles += "<style:style style:name=\"Page\(index)\" style:family=\"paragraph\" style:master-page-name=\"Master\(index)\"><style:paragraph-properties\(index > 0 ? " fo:break-before=\"page\"" : "") fo:margin=\"0pt\" fo:line-height=\"115%\"/><style:text-properties fo:font-size=\"0.1pt\"/></style:style>"
            body += "<text:p text:style-name=\"Page\(index)\"><draw:frame draw:name=\"Original PDF page \(index+1)\" text:anchor-type=\"as-char\" svg:width=\"\(box.width)pt\" svg:height=\"\(max(1,box.height-1))pt\"><draw:image><office:binary-data>\(png.base64EncodedString())</office:binary-data></draw:image></draw:frame></text:p>"
        }
        let xml = """
        <?xml version="1.0" encoding="UTF-8"?><office:document xmlns:office="urn:oasis:names:tc:opendocument:xmlns:office:1.0" xmlns:style="urn:oasis:names:tc:opendocument:xmlns:style:1.0" xmlns:text="urn:oasis:names:tc:opendocument:xmlns:text:1.0" xmlns:draw="urn:oasis:names:tc:opendocument:xmlns:drawing:1.0" xmlns:svg="urn:oasis:names:tc:opendocument:xmlns:svg-compatible:1.0" xmlns:fo="urn:oasis:names:tc:opendocument:xmlns:xsl-fo-compatible:1.0" office:version="1.3" office:mimetype="application/vnd.oasis.opendocument.text"><office:automatic-styles>\(layouts)\(styles)</office:automatic-styles><office:master-styles>\(masters)</office:master-styles><office:body><office:text>\(body)</office:text></office:body></office:document>
        """
        try xml.write(to:output,atomically:true,encoding:.utf8)
        return output
    }

    private struct TextStyle: Hashable {
        var family = "Helvetica"
        var size: Double = 12
        var bold = false
        var italic = false
        var underline = false
        var color = "#000000"
    }
    private struct Run { var text: String; var style: TextStyle }
    private struct Cell {
        var runs: [Run]
        var bounds: CGRect
        var text: String { runs.map(\.text).joined() }
        var fontSize: CGFloat { CGFloat(runs.map { $0.style.size }.max() ?? 12) }
    }
    private struct Row {
        var cells: [Cell]
        var bounds: CGRect { cells.dropFirst().reduce(cells.first?.bounds ?? .zero) { $0.union($1.bounds) } }
        var fontSize: CGFloat { cells.map(\.fontSize).max() ?? 12 }
    }
    private enum Block {
        case paragraph([Row]), table([Row]), image(CGRect)
        var bounds: CGRect {
            switch self {
            case .paragraph(let rows), .table(let rows): return rows.dropFirst().reduce(rows.first?.bounds ?? .zero) { $0.union($1.bounds) }
            case .image(let rect): return rect
            }
        }
    }
    private struct Writer {
        var textStyles: [TextStyle: String] = [:]
        var styles = ""
        var paragraphIndex = 0
        var tableIndex = 0
        var imageIndex = 0
        mutating func span(_ run: Run) -> String {
            let name: String
            if let cached = textStyles[run.style] { name = cached }
            else {
                name = "Text\(textStyles.count)"; textStyles[run.style] = name
                let s = run.style
                styles += "<style:style style:name=\"\(name)\" style:family=\"text\"><style:text-properties fo:font-family=\"\(DocumentText.escapeXML(s.family))\" fo:font-size=\"\(s.size)pt\" fo:font-weight=\"\(s.bold ? "bold" : "normal")\" fo:font-style=\"\(s.italic ? "italic" : "normal")\" fo:color=\"\(s.color)\"\(s.underline ? " style:text-underline-style=\"solid\" style:text-underline-width=\"auto\"" : "")/></style:style>"
            }
            return "<text:span text:style-name=\"\(name)\">\(DocumentText.escapeXML(run.text))</text:span>"
        }
        mutating func paragraphStyle(left: CGFloat, before: CGFloat, size: CGFloat, centered: Bool = false) -> String {
            let name = "Paragraph\(paragraphIndex)"; paragraphIndex += 1
            styles += "<style:style style:name=\"\(name)\" style:family=\"paragraph\"><style:paragraph-properties fo:margin-left=\"\(max(0, left))pt\" fo:margin-top=\"\(max(0, before))pt\" fo:margin-bottom=\"0pt\" fo:line-height=\"115%\" fo:text-align=\"\(centered ? "center" : "start")\"/><style:text-properties fo:font-size=\"\(max(1, size))pt\"/></style:style>"
            return name
        }
        mutating func paragraph(_ rows: [Row], page: CGRect, margin: CGFloat, gap: CGFloat) -> String {
            guard let first = rows.first else { return "" }
            let rect = rows.dropFirst().reduce(first.bounds) { $0.union($1.bounds) }
            let center = rows.count == 1 && first.fontSize >= 18 && abs(rect.midX - page.midX) < 18
            let style = paragraphStyle(left: center ? 0 : rect.minX - page.minX - margin, before: gap, size: first.fontSize, centered: center)
            var content = ""
            for (line, row) in rows.enumerated() {
                if line > 0 { content += " " }
                for (column, cell) in row.cells.enumerated() {
                    if column > 0 { content += "<text:tab/>" }
                    for run in cell.runs { content += span(run) }
                }
            }
            return "<text:p text:style-name=\"\(style)\">\(content)</text:p>"
        }
        mutating func table(_ rows: [Row], page: CGRect, margin: CGFloat, gap: CGFloat) -> String {
            guard let first = rows.first, first.cells.count > 1 else { return "" }
            let name = "Table\(tableIndex)"; tableIndex += 1
            let count = first.cells.count
            let left = max(0, first.bounds.minX - page.minX - margin)
            var starts = (0..<count).map { column in rows.map { $0.cells[column].bounds.minX }.min() ?? first.cells[column].bounds.minX }
            starts.sort()
            var widths = (0..<(count - 1)).map { max(24, starts[$0 + 1] - starts[$0]) }
            widths.append(max(48, widths.last ?? 100, rows.map { $0.cells[count - 1].bounds.width + 12 }.max() ?? 48))
            let available = max(1, page.width - margin * 2 - left)
            let scale = min(1, available / max(1, widths.reduce(0, +)))
            widths = widths.map { $0 * scale }
            styles += "<style:style style:name=\"\(name)\" style:family=\"table\"><style:table-properties table:align=\"margins\" style:width=\"\(widths.reduce(0, +))pt\" fo:margin-left=\"\(left)pt\" fo:margin-right=\"0pt\" fo:margin-top=\"\(gap)pt\" fo:margin-bottom=\"0pt\"/></style:style>"
            styles += "<style:style style:name=\"\(name)Cell\" style:family=\"table-cell\"><style:table-cell-properties fo:border=\"0.5pt solid #999999\" fo:padding=\"4pt\" style:vertical-align=\"middle\"/></style:style>"
            var columns = ""
            for (index, width) in widths.enumerated() {
                let column = "\(name)Column\(index)"
                styles += "<style:style style:name=\"\(column)\" style:family=\"table-column\"><style:table-column-properties style:column-width=\"\(width)pt\"/></style:style>"
                columns += "<table:table-column table:style-name=\"\(column)\"/>"
            }
            var contents = ""
            for (index, row) in rows.enumerated() {
                let height = index + 1 < rows.count ? max(row.fontSize + 8, abs(row.bounds.midY - rows[index + 1].bounds.midY)) : max(row.fontSize + 8, rows.count > 1 ? abs(rows[index - 1].bounds.midY - row.bounds.midY) : row.fontSize + 8)
                let rowStyle = "\(name)Row\(index)"
                styles += "<style:style style:name=\"\(rowStyle)\" style:family=\"table-row\"><style:table-row-properties style:min-row-height=\"\(height)pt\"/></style:style>"
                contents += "<table:table-row table:style-name=\"\(rowStyle)\">"
                for cell in row.cells {
                    var value = ""
                    for run in cell.runs { value += span(run) }
                    contents += "<table:table-cell table:style-name=\"\(name)Cell\" office:value-type=\"string\"><text:p>\(value)</text:p></table:table-cell>"
                }
                contents += "</table:table-row>"
            }
            return "<table:table table:name=\"\(name)\" table:style-name=\"\(name)\">\(columns)\(contents)</table:table>"
        }
        mutating func image(_ rect: CGRect, png: Data, page: CGRect, margin: CGFloat, gap: CGFloat) -> String {
            imageIndex += 1
            let left = max(0, rect.minX - page.minX - margin)
            let scale = min(1, max(1, page.width - margin * 2 - left) / max(1, rect.width))
            let style = paragraphStyle(left: left, before: gap, size: 1)
            return "<text:p text:style-name=\"\(style)\"><draw:frame draw:name=\"PDF image \(imageIndex)\" text:anchor-type=\"as-char\" svg:width=\"\(rect.width * scale)pt\" svg:height=\"\(rect.height * scale)pt\"><draw:image><office:binary-data>\(png.base64EncodedString())</office:binary-data></draw:image></draw:frame></text:p>"
        }
    }

    static func document(_ pdf: PDFDocument, at output: URL, options: OperationOptions, token: CancellationToken) throws -> URL {
        var writer = Writer(), body = "", pageLayouts = "", masters = ""
        for index in 0..<pdf.pageCount {
            try token.check()
            guard let page = pdf.page(at: index) else { continue }
            let box = page.bounds(for: .cropBox)
            // A fixed half-inch margin consumes most of a small PDF page.
            // Cap the ordinary margin while reserving at least 90% of a small
            // page's narrow dimension for editable content.
            let margin = min(36, min(box.width, box.height) * 0.05)
            let rows = try textRows(page, token: token)
            var blocks = textBlocks(rows)
            let nativeText = !(page.string ?? "").trimmingCharacters(in: .whitespacesAndNewlines).isEmpty
            for rect in try PDFPlacedImages.regions(page, token: token) {
                if !nativeText && !rows.isEmpty && rect.width * rect.height > box.width * box.height * 0.8 { continue }
                blocks.append(.image(rect))
            }
            blocks.sort { abs($0.bounds.maxY - $1.bounds.maxY) > 2 ? $0.bounds.maxY > $1.bounds.maxY : $0.bounds.minX < $1.bounds.minX }
            pageLayouts += "<style:page-layout style:name=\"PageLayout\(index)\"><style:page-layout-properties fo:page-width=\"\(box.width)pt\" fo:page-height=\"\(box.height)pt\" fo:margin=\"\(margin)pt\"/></style:page-layout>"
            masters += "<style:master-page style:name=\"PageMaster\(index)\" style:page-layout-name=\"PageLayout\(index)\"/>"
            writer.styles += "<style:style style:name=\"PageStart\(index)\" style:family=\"paragraph\" style:master-page-name=\"PageMaster\(index)\"><style:paragraph-properties\(index > 0 ? " fo:break-before=\"page\"" : "") fo:line-height=\"0.1pt\" fo:margin-top=\"0pt\" fo:margin-bottom=\"0pt\"/><style:text-properties fo:font-size=\"0.1pt\"/></style:style>"
            body += "<text:p text:style-name=\"PageStart\(index)\"/>"
            var previousBottom = box.maxY - margin
            for block in blocks {
                try token.check()
                let gap = max(0, previousBottom - block.bounds.maxY)
                switch block {
                case .paragraph(let rows): body += writer.paragraph(rows, page: box, margin: margin, gap: gap)
                case .table(let rows): body += writer.table(rows, page: box, margin: margin, gap: gap)
                case .image(let rect):
                    let png = try PDFOfficeCodec.rasterPNG(page, rect: rect, dpi: options.dpi, token: token)
                    body += writer.image(rect, png: png, page: box, margin: margin, gap: gap)
                }
                previousBottom = min(previousBottom, block.bounds.minY)
            }
        }
        // Word requires a paragraph after a final table. Supplying a tiny one
        // prevents LibreOffice from appending a normal-height paragraph that
        // spills an otherwise complete small document onto a blank extra page.
        writer.styles += "<style:style style:name=\"DocumentEnd\" style:family=\"paragraph\"><style:paragraph-properties fo:line-height=\"0.1pt\" fo:margin-top=\"0pt\" fo:margin-bottom=\"0pt\"/><style:text-properties fo:font-size=\"0.1pt\"/></style:style>"
        body += "<text:p text:style-name=\"DocumentEnd\"/>"
        let xml = """
        <?xml version="1.0" encoding="UTF-8"?><office:document xmlns:office="urn:oasis:names:tc:opendocument:xmlns:office:1.0" xmlns:style="urn:oasis:names:tc:opendocument:xmlns:style:1.0" xmlns:text="urn:oasis:names:tc:opendocument:xmlns:text:1.0" xmlns:table="urn:oasis:names:tc:opendocument:xmlns:table:1.0" xmlns:draw="urn:oasis:names:tc:opendocument:xmlns:drawing:1.0" xmlns:svg="urn:oasis:names:tc:opendocument:xmlns:svg-compatible:1.0" xmlns:fo="urn:oasis:names:tc:opendocument:xmlns:xsl-fo-compatible:1.0" office:version="1.3" office:mimetype="application/vnd.oasis.opendocument.text"><office:automatic-styles>\(writer.styles)\(pageLayouts)</office:automatic-styles><office:master-styles>\(masters)</office:master-styles><office:body><office:text>\(body)</office:text></office:body></office:document>
        """
        try xml.write(to: output, atomically: true, encoding: .utf8)
        return output
    }

    private static func textRows(_ page: PDFPage, token: CancellationToken) throws -> [Row] {
        guard let attributed = page.attributedString, attributed.length > 0,
              !attributed.string.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty else {
            return try PDFOfficeCodec.recognizedCells(page, token: token).map { Row(cells: [Cell(runs: [.init(text: $0.text, style: TextStyle(size: max(8, Double($0.rect.height) * 0.85)))], bounds: $0.rect)]) }.sorted { $0.bounds.midY > $1.bounds.midY }
        }
        let ns = attributed.string as NSString
        var glyphs: [Cell] = []
        var index = 0
        while index < ns.length {
            if index % 256 == 0 { try token.check() }
            let range = ns.rangeOfComposedCharacterSequence(at: index)
            let value = ns.substring(with: range)
            let attributes = attributed.attributes(at: index, effectiveRange: nil)
            index = NSMaxRange(range)
            if value == "\n" || value == "\r" || value == "\u{0c}" { continue }
            // characterBounds(at:) indexes PDF glyphs, while PDFKit inserts
            // line separators in string/attributedString. Selection ranges use
            // the normalized string indices and avoid losing a character at
            // every line boundary (also preserving composed Unicode glyphs).
            guard let selection = page.selection(for: range) else { continue }
            let rect = selection.bounds(for: page)
            if rect.isNull || rect.isInfinite || rect.width <= 0 || rect.height <= 0 { continue }
            glyphs.append(Cell(runs: [.init(text: value, style: style(attributes))], bounds: rect))
        }
        var groups: [[Cell]] = []
        for glyph in glyphs.sorted(by: { $0.bounds.midY > $1.bounds.midY }) {
            if let row = groups.indices.reversed().first(where: { index in
                guard let first = groups[index].first else { return false }
                return abs(first.bounds.midY - glyph.bounds.midY) < max(3, min(first.bounds.height, glyph.bounds.height) * 0.45)
            }) { groups[row].append(glyph) }
            else { groups.append([glyph]) }
        }
        var result: [Row] = []
        for group in groups {
            var cells: [Cell] = [], current: Cell?, previous: Cell?
            for glyph in group.sorted(by: { $0.bounds.minX < $1.bounds.minX }) {
                if glyph.text.trimmingCharacters(in: .whitespaces).isEmpty && glyph.bounds.width > max(12, glyph.fontSize * 0.8) {
                    if let current, !current.text.trimmingCharacters(in: .whitespaces).isEmpty { cells.append(current) }
                    current = nil; previous = nil; continue
                }
                if let previous {
                    let gap = glyph.bounds.minX - previous.bounds.maxX
                    if gap > max(12, glyph.fontSize * 0.8) {
                        if let current, !current.text.trimmingCharacters(in: .whitespaces).isEmpty { cells.append(current) }
                        current = nil
                    } else if gap > glyph.fontSize * 0.18, let previousText = current?.runs.last?.text, !previousText.hasSuffix(" "), glyph.text != " " {
                        current?.runs.append(Run(text: " ", style: previous.runs[0].style))
                    }
                }
                if current == nil { current = glyph }
                else {
                    current!.bounds = current!.bounds.union(glyph.bounds)
                    let run = glyph.runs[0]
                    if let last = current!.runs.indices.last, current!.runs[last].style == run.style { current!.runs[last].text += run.text }
                    else { current!.runs.append(run) }
                }
                previous = glyph
            }
            if let current, !current.text.trimmingCharacters(in: .whitespaces).isEmpty { cells.append(current) }
            if !cells.isEmpty { result.append(Row(cells: cells)) }
        }
        return result.sorted { $0.bounds.midY > $1.bounds.midY }
    }

    private static func textBlocks(_ rows: [Row]) -> [Block] {
        func aligned(_ a: Row, _ b: Row) -> Bool {
            guard a.cells.count > 1, a.cells.count == b.cells.count,
                  a.bounds.midY - b.bounds.midY < max(a.fontSize, b.fontSize) * 2.5 + 12 else { return false }
            return zip(a.cells, b.cells).allSatisfy { abs($0.bounds.minX - $1.bounds.minX) < 18 || abs($0.bounds.maxX - $1.bounds.maxX) < 12 }
        }
        var blocks: [Block] = [], index = 0
        while index < rows.count {
            if index + 1 < rows.count, aligned(rows[index], rows[index + 1]) {
                var table = [rows[index]]; index += 1
                while index < rows.count && aligned(table.last!, rows[index]) { table.append(rows[index]); index += 1 }
                blocks.append(.table(table)); continue
            }
            var paragraph = [rows[index]]; index += 1
            while index < rows.count, let previous = paragraph.last, previous.cells.count == 1, rows[index].cells.count == 1,
                  abs(previous.bounds.minX - rows[index].bounds.minX) < 8,
                  abs(previous.fontSize - rows[index].fontSize) < 1,
                  previous.bounds.midY - rows[index].bounds.midY < previous.fontSize * 1.65,
                  previous.bounds.midY - rows[index].bounds.midY > previous.fontSize * 0.55 {
                paragraph.append(rows[index]); index += 1
            }
            blocks.append(.paragraph(paragraph))
        }
        return blocks
    }

    private static func style(_ attributes: [NSAttributedString.Key: Any]) -> TextStyle {
        let font = attributes[.font] as? NSFont ?? NSFont.systemFont(ofSize: 12)
        let traits = font.fontDescriptor.symbolicTraits
        let family = (font.familyName ?? font.fontName).replacingOccurrences(of: "^[A-Z]{6}\\+", with: "", options: .regularExpression)
        let color = (attributes[.foregroundColor] as? NSColor)?.usingColorSpace(.deviceRGB)
        let hex = color.map { color -> String in
            let components = [color.redComponent, color.greenComponent, color.blueComponent].map { max(0, min(1, Double($0))) }
            let linear = components.map { $0 <= 0.04045 ? $0 / 12.92 : pow(($0 + 0.055) / 1.055, 2.4) }
            let luminance = 0.2126 * linear[0] + 0.7152 * linear[1] + 0.0722 * linear[2]
            // Editable output has a white page, without the source's vector
            // background. Light PDF text must not become invisible on that page.
            if 1.05 / (luminance + 0.05) < 3 { return "#202020" }
            return String(format: "#%02X%02X%02X", Int(components[0] * 255), Int(components[1] * 255), Int(components[2] * 255))
        } ?? "#000000"
        return TextStyle(family: family, size: (Double(font.pointSize) * 10).rounded() / 10, bold: traits.contains(.bold), italic: traits.contains(.italic), underline: (attributes[.underlineStyle] as? NSNumber)?.intValue ?? 0 != 0, color: hex)
    }
}

/// Finds actual placed PDF Image XObjects, including images nested inside Form
/// XObjects. Cropping those placed regions preserves masks and image orientation
/// without depending on PDF compression/color-space implementations.
enum PDFPlacedImages {
    static func regions(_ page: PDFPage, token: CancellationToken) throws -> [CGRect] {
        guard let cgPage = page.pageRef, let dictionary = cgPage.dictionary else { return [] }
        var resources: CGPDFDictionaryRef?
        guard CGPDFDictionaryGetDictionary(dictionary, "Resources", &resources), let resources else { return [] }
        let content = CGPDFContentStreamCreateWithPage(cgPage)
        defer { CGPDFContentStreamRelease(content) }
        let scanner = Scanner(content: content, resources: resources, transform: .identity, token: token, depth: 0)
        scanner.scan()
        try token.check()
        let pageBounds = page.bounds(for: .cropBox)
        var result: [CGRect] = []
        for rect in scanner.images {
            let clipped = rect.intersection(pageBounds)
            guard !clipped.isNull, clipped.width > 1, clipped.height > 1 else { continue }
            if !result.contains(where: { abs($0.minX - clipped.minX) < 0.5 && abs($0.minY - clipped.minY) < 0.5 && abs($0.width - clipped.width) < 0.5 && abs($0.height - clipped.height) < 0.5 }) { result.append(clipped) }
        }
        return result
    }

    private final class Scanner {
        let content: CGPDFContentStreamRef
        let resources: CGPDFDictionaryRef
        let token: CancellationToken
        let depth: Int
        var transform: CGAffineTransform
        var stack: [CGAffineTransform] = []
        var images: [CGRect] = []
        init(content: CGPDFContentStreamRef, resources: CGPDFDictionaryRef, transform: CGAffineTransform, token: CancellationToken, depth: Int) {
            self.content = content; self.resources = resources; self.transform = transform; self.token = token; self.depth = depth
        }
        func scan() {
            let table = CGPDFOperatorTableCreate()!
            defer { CGPDFOperatorTableRelease(table) }
            CGPDFOperatorTableSetCallback(table, "q") { _, info in
                guard let info else { return }; let state = Unmanaged<Scanner>.fromOpaque(info).takeUnretainedValue(); state.stack.append(state.transform)
            }
            CGPDFOperatorTableSetCallback(table, "Q") { _, info in
                guard let info else { return }; let state = Unmanaged<Scanner>.fromOpaque(info).takeUnretainedValue(); if let previous = state.stack.popLast() { state.transform = previous }
            }
            CGPDFOperatorTableSetCallback(table, "cm") { scanner, info in
                guard let info else { return }; let state = Unmanaged<Scanner>.fromOpaque(info).takeUnretainedValue()
                var values = [CGPDFReal](repeating: 0, count: 6)
                for index in (0..<6).reversed() { guard CGPDFScannerPopNumber(scanner, &values[index]) else { return } }
                state.transform = CGAffineTransform(a: values[0], b: values[1], c: values[2], d: values[3], tx: values[4], ty: values[5]).concatenating(state.transform)
            }
            CGPDFOperatorTableSetCallback(table, "Do") { scanner, info in
                guard let info else { return }; let state = Unmanaged<Scanner>.fromOpaque(info).takeUnretainedValue()
                if state.token.isCancelled { CGPDFScannerStop(scanner); return }
                var name: UnsafePointer<CChar>?
                guard CGPDFScannerPopName(scanner, &name), let name,
                      let object = CGPDFContentStreamGetResource(state.content, "XObject", name) else { return }
                var stream: CGPDFStreamRef?
                guard CGPDFObjectGetValue(object, .stream, &stream), let stream else { return }
                guard let dictionary = CGPDFStreamGetDictionary(stream) else { return }
                var subtype: UnsafePointer<CChar>?
                guard CGPDFDictionaryGetName(dictionary, "Subtype", &subtype), let subtype else { return }
                if String(cString: subtype) == "Image" {
                    state.images.append(CGRect(x: 0, y: 0, width: 1, height: 1).applying(state.transform).standardized)
                } else if String(cString: subtype) == "Form", state.depth < 16 {
                    var local: CGPDFDictionaryRef?
                    _ = CGPDFDictionaryGetDictionary(dictionary, "Resources", &local)
                    var matrix = CGAffineTransform.identity, array: CGPDFArrayRef?
                    if CGPDFDictionaryGetArray(dictionary, "Matrix", &array), let array, CGPDFArrayGetCount(array) == 6 {
                        var values = [CGPDFReal](repeating: 0, count: 6)
                        if (0..<6).allSatisfy({ CGPDFArrayGetNumber(array, $0, &values[$0]) }) { matrix = CGAffineTransform(a: values[0], b: values[1], c: values[2], d: values[3], tx: values[4], ty: values[5]) }
                    }
                    let childContent = CGPDFContentStreamCreateWithStream(stream, local ?? state.resources, state.content)
                    defer { CGPDFContentStreamRelease(childContent) }
                    let child = Scanner(content: childContent, resources: local ?? state.resources, transform: matrix.concatenating(state.transform), token: state.token, depth: state.depth + 1)
                    child.scan(); state.images += child.images
                }
            }
            let scanner = CGPDFScannerCreate(content, table, Unmanaged.passUnretained(self).toOpaque())
            defer { CGPDFScannerRelease(scanner) }
            _ = CGPDFScannerScan(scanner)
        }
    }
}
