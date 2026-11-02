import Foundation
import AppKit
import PDFKit

/// Reconstructs document objects from PDF geometry. PDF has no paragraph/table
/// model, so those boundaries are inferred; the resulting objects remain real
/// editable paragraphs and tables rather than a picture of each document page.
enum PDFDocumentReconstruction {
    /// Title of the background picture behind a reconstructed page's text.
    static let artworkTitle = "Original page artwork"
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

    /// Word stores font sizes in half points and rounds to the nearest one:
    /// 8.75pt would become 9pt and push a full line onto two. Rounding down
    /// (allowing for float noise) keeps every line within its source width.
    static func wordSize(_ size: Double) -> Double { max(1, ((size * 2) + 0.1).rounded(.down) / 2) }

    private struct TextStyle: Hashable {
        var family = "Arial"
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
        var text: String { cells.map(\.text).joined(separator: " ") }
    }
    private enum Block {
        case paragraph([Row]), table([Row]), image(CGRect), box(Int, CGRect)
        var bounds: CGRect {
            switch self {
            case .paragraph(let rows), .table(let rows): return rows.dropFirst().reduce(rows.first?.bounds ?? .zero) { $0.union($1.bounds) }
            case .image(let rect), .box(_, let rect): return rect
            }
        }
    }
    /// Editable text recovered from one page, and the ink that text occupies
    /// in the rendered page (lifted out of the artwork layer behind it).
    private struct PageText {
        var rows: [Row]
        var ink: [(rect: CGRect, color: String?)]
        var scanned: Bool
        var photographic: Bool
        /// The page went through `PDFScanRectification`: it is a clean,
        /// axis-aligned image of the paper without the desk, so box/frame
        /// detection and higher artwork resolution are safe.
        var rectified: Bool = false
    }
    /// Header and footer rows repeated across pages. Digit groups that follow
    /// the page number (or equal the page count) become Word fields.
    private struct Furniture {
        enum Token { case literal, pageNumber, pageCount }
        var header: [(row: Row, tokens: [Token])] = []
        var footer: [(row: Row, tokens: [Token])] = []
        var removed: [Set<Int>] = []
        var headerTop: CGFloat = 0, headerBottom: CGFloat = 0
        var footerTop: CGFloat = 0, footerBottom: CGFloat = 0
    }
    private struct SpanKey: Hashable { var style: TextStyle; var scale: Int; var tracking: Int }
    /// Rules and shading of a table read from the page artwork. Positions are
    /// page coordinates: column lines left to right, row lines top to bottom.
    private struct Grid {
        var columns: [CGFloat]
        var rows: [CGFloat]
        var vertical: Bool
        var border: String
        var width: CGFloat
        var fills: [[String?]]
    }
    /// A boxed panel of the source (a framed section, a shaded band) that is
    /// rebuilt as a real table: the frame as its border, bands as row fills.
    private struct Box {
        var rect: CGRect
        var border: String?
        var stroke: CGFloat
        var panels: [(rect: CGRect, color: String)]
        /// Full-width separator rules inside the box (page y, colour, weight).
        var dividers: [(y: CGFloat, color: String, weight: CGFloat)] = []
    }
    private static func tableKey(_ rows: [Row]) -> String { "\(Int(rows[0].bounds.minX))x\(Int(rows[0].bounds.midY))x\(rows.count)" }

    private struct Writer {
        var textStyles: [SpanKey: String] = [:]
        var fonts: Set<String> = []
        /// LibreOffice resolves text fonts through declared font faces only.
        var fontFaces: String { "<office:font-face-decls>" + fonts.sorted().map { "<style:font-face style:name=\"\(DocumentText.escapeXML($0))\" svg:font-family=\"\(DocumentText.escapeXML($0))\"/>" }.joined() + "</office:font-face-decls>" }
        var styles = ""
        var paragraphIndex = 0
        var tableIndex = 0
        var imageIndex = 0
        var artwork = true
        /// Inside form and table cells, lines keep their source breaks.
        var keepLines = false
        /// Ruled tables recognised in the artwork, by `tableKey`.
        var grids: [String: Grid] = [:]
        /// Boxes on the current page and the blocks inside each.
        var boxes: [(box: Box, blocks: [Block])] = []
        /// `tracking`: extra letter spacing in hundredths of a point.
        mutating func span(_ run: Run, scale: Int = 100, tracking: Int = 0) -> String { "<text:span text:style-name=\"\(spanStyle(run.style, scale: scale, tracking: tracking))\">\(DocumentText.escapeXML(run.text))</text:span>" }
        mutating func spanStyle(_ s: TextStyle, scale: Int, tracking: Int = 0) -> String {
            let key = SpanKey(style: s, scale: scale, tracking: tracking)
            if let cached = textStyles[key] { return cached }
            let name = "Text\(textStyles.count)"; textStyles[key] = name
            fonts.insert(s.family)
            let font = DocumentText.escapeXML(s.family)
            styles += "<style:style style:name=\"\(name)\" style:family=\"text\"><style:text-properties style:font-name=\"\(font)\" style:font-name-asian=\"\(font)\" style:font-name-complex=\"\(font)\" fo:font-family=\"\(font)\" fo:font-size=\"\(PDFDocumentReconstruction.wordSize(s.size))pt\" fo:font-weight=\"\(s.bold ? "bold" : "normal")\" fo:font-style=\"\(s.italic ? "italic" : "normal")\" fo:color=\"\(s.color)\"\(scale < 100 ? " style:text-scale=\"\(scale)%\"" : "")\(tracking != 0 ? " fo:letter-spacing=\"\(Double(tracking) / 100)pt\"" : "")\(s.underline ? " style:text-underline-style=\"solid\" style:text-underline-width=\"auto\"" : "")/></style:style>"
            return name
        }
        mutating func paragraphStyle(left: CGFloat, right: CGFloat = 0, before: CGFloat, lineHeight: CGFloat, size: CGFloat, align: String = "start", tabs: [(CGFloat, String)] = []) -> String {
            let name = "Paragraph\(paragraphIndex)"; paragraphIndex += 1
            let stops = tabs.isEmpty ? "" : "<style:tab-stops>" + tabs.map { "<style:tab-stop style:position=\"\(max(0, $0.0))pt\" style:type=\"\($0.1)\"/>" }.joined() + "</style:tab-stops>"
            styles += "<style:style style:name=\"\(name)\" style:family=\"paragraph\"><style:paragraph-properties fo:margin-left=\"\(max(0, left))pt\" fo:margin-right=\"\(max(0, right))pt\" fo:text-indent=\"0pt\" fo:margin-top=\"\(max(0, before))pt\" fo:margin-bottom=\"0pt\" fo:line-height=\"\(max(1, lineHeight))pt\" fo:text-align=\"\(align)\" fo:hyphenation-ladder-count=\"no-limit\">\(stops)</style:paragraph-properties><style:text-properties fo:font-size=\"\(max(1, size))pt\" fo:hyphenate=\"false\"/></style:style>"
            return name
        }
        /// An empty line that opens a vertical gap (Word tables have no space
        /// before). The gap is space-before on a 1pt line: Google Docs ignores
        /// exact line heights on empty paragraphs but keeps paragraph spacing.
        mutating func spacer(_ gap: CGFloat) -> String {
            gap > 0.5 ? "<text:p text:style-name=\"\(paragraphStyle(left: 0, before: gap - 1, lineHeight: 1, size: 1))\"/>" : ""
        }
        mutating func paragraph(_ rows: [Row], page: CGRect, margin: CGFloat, gap: CGFloat, lineHeight: CGFloat) -> String {
            guard let first = rows.first else { return "" }
            let rect = rows.dropFirst().reduce(first.bounds) { $0.union($1.bounds) }
            let contentLeft = page.minX + margin, contentRight = page.maxX - margin
            var align = "start", left = rect.minX - contentLeft
            if rows.count == 1 {
                if abs(rect.midX - page.midX) < 6 && rect.minX - contentLeft > 24 { align = "center"; left = 0 }
                else if contentRight - rect.maxX < 4 && rect.minX - contentLeft > 60 { align = "end"; left = 0 }
            } else {
                let ends = rows.dropLast().map(\.bounds.maxX)
                // Justified: the full lines end together and at least one was
                // stretched beyond its natural width (a ragged two-line
                // paragraph also has one "full" line).
                let stretched = rows.dropLast().contains { row in
                    let natural = row.cells.flatMap(\.runs).map { PDFDocumentReconstruction.width(of: $0.text, style: $0.style) }.reduce(0, +)
                    return natural > 0 && row.cells.map(\.bounds.width).reduce(0, +) > natural * 1.015 + 1
                }
                if let widest = ends.max(), ends.allSatisfy({ widest - $0 < 2.5 }), widest - rect.minX > 120, stretched { align = "justify" }
                // Lines of different lengths sharing one centre line are centred.
                else if rows.allSatisfy({ abs($0.bounds.midX - rect.midX) < 3 }), rows.map(\.bounds.width).max()! - rows.map(\.bounds.width).min()! > 12, rect.minX - contentLeft > 12 { align = "center"; left = 0 }
            }
            // Keep the source line breaks: a substituted font that runs a
            // little wider is condensed instead of wrapping onto extra lines.
            let scale = rows.enumerated().map { index, row -> Int in
                guard index < rows.count - 1 || rows.count == 1 || align != "justify" else { return 100 }
                return condensing(row)
            }.min() ?? 100
            // Letter-spaced (tracked) text: lines wider than their glyphs need,
            // evenly per character. Justified lines are stretched by their
            // alignment instead, so only a justified paragraph's last line counts.
            let tracking: Int = {
                guard scale == 100 else { return 0 }
                let measured = (align == "justify" ? [rows[rows.count - 1]] : rows).compactMap { row -> CGFloat? in
                    let runs = row.cells.flatMap(\.runs)
                    let characters = runs.map(\.text.count).reduce(0, +)
                    guard row.cells.count == 1, characters > 8 else { return nil }
                    let natural = runs.map { PDFDocumentReconstruction.width(of: $0.text, style: $0.style) }.reduce(0, +)
                    return (row.cells[0].bounds.width - natural) / CGFloat(characters - 1)
                }
                guard let least = measured.min(), least > 0.25 else { return 0 }
                return Int((least * 100).rounded(.down))
            }()
            // Cells on one line (label … value) sit on tab stops at their source positions.
            var tabs: [(CGFloat, String)] = []
            if rows.count == 1, first.cells.count > 1 {
                align = "start"; left = first.cells[0].bounds.minX - contentLeft
                for cell in first.cells.dropFirst() {
                    let next = first.cells.firstIndex { $0.bounds.minX > cell.bounds.minX }.map { first.cells[$0] }
                    let rightAligned = contentRight - cell.bounds.maxX < 24 || (next == nil && cell.text.first?.isNumber == true)
                    tabs.append(rightAligned ? (cell.bounds.maxX - contentLeft - left, "right") : (cell.bounds.minX - contentLeft - left, "left"))
                }
            }
            // Wrapped text keeps the source column width, so lines break where they did.
            let blockRight = rows.map(\.bounds.maxX).max() ?? rect.maxX
            // A line ended by the author (the next line's first word would have
            // fitted) keeps its break; a wrapped line flows. The width gets a
            // little room for font metrics, but never enough to pull a word up.
            func firstWord(_ row: Row) -> CGFloat { row.cells.first?.runs.first.map { PDFDocumentReconstruction.width(of: " " + String($0.text.split(separator: " ").first ?? ""), style: $0.style) } ?? 0 }
            let ended = rows.indices.dropFirst().map { rows[$0 - 1].bounds.maxX + firstWord(rows[$0]) < blockRight - 1 }
            let room = rows.indices.dropFirst().filter { !ended[$0 - 1] }.map { firstWord(rows[$0]) - (blockRight - rows[$0 - 1].bounds.maxX) - 0.5 }.min() ?? 0
            let wraps = ended.contains(false)
            var rightIndent = keepLines || rows.count == 1 || !wraps ? 0 : max(0, contentRight - blockRight - 1 - max(0, min(room, (blockRight - rect.minX) * 0.03)))
            // Centred lines keep their breaks and their own column (a little
            // wider than the widest line), so the centre stays where it was.
            let centred = align == "center" && rows.count > 1
            if centred { left = max(0, rect.minX - contentLeft - 2); rightIndent = max(0, contentRight - rect.maxX - 2) }
            // Justified lines keep their breaks and are stretched to the right
            // indent, so it sits at the source's right edge, with 2pt for a
            // renderer's wider metrics (a forced break cannot pull a word up).
            if align == "justify" && !keepLines { rightIndent = max(0, contentRight - blockRight - 2) }
            let style = paragraphStyle(left: left, right: rightIndent, before: gap, lineHeight: lineHeight, size: first.fontSize, align: align, tabs: tabs)
            var content = ""
            for (line, row) in rows.enumerated() {
                // Justified paragraphs keep each source line (Word stretches a
                // line ending in a soft break), so the line count cannot change
                // with a renderer's own spacing. Ragged text flows normally.
                if line > 0 { content += align == "justify" || keepLines || centred || ended[line - 1] ? " <text:line-break/>" : " " }
                for (column, cell) in row.cells.enumerated() {
                    if column > 0 { content += "<text:tab/>" }
                    for run in cell.runs { content += span(run, scale: scale, tracking: tracking) }
                }
            }
            return "<text:p text:style-name=\"\(style)\">\(content)</text:p>"
        }
        func condensing(_ row: Row) -> Int {
            var measured: CGFloat = 0
            for cell in row.cells { for run in cell.runs { measured += PDFDocumentReconstruction.width(of: run.text, style: run.style) } }
            let source = row.cells.map(\.bounds.width).reduce(0, +)
            guard measured > 0, source > 0, source / measured < 0.995 else { return 100 }
            return max(70, Int((source / measured * 100).rounded(.down)))
        }
        /// Header/footer line: cells laid out on tab stops, page numbers as fields.
        mutating func furniture(_ row: Row, tokens: [Furniture.Token], page: CGRect, margin: CGFloat, gap: CGFloat = 0) -> String {
            guard let first = row.cells.first else { return "" }
            let contentLeft = page.minX + margin, contentRight = page.maxX - margin
            let left = max(0, first.bounds.minX - contentLeft)
            var tabs: [(CGFloat, String)] = []
            for cell in row.cells.dropFirst() {
                if contentRight - cell.bounds.maxX < 24 { tabs.append((cell.bounds.maxX - contentLeft - left, "right")) }
                else { tabs.append((cell.bounds.minX - contentLeft - left, "left")) }
            }
            let style = paragraphStyle(left: left, before: gap, lineHeight: PDFDocumentReconstruction.lineSpacing([row]), size: row.fontSize, tabs: tabs)
            var content = "", tokenIndex = 0
            for (column, cell) in row.cells.enumerated() {
                if column > 0 { content += "<text:tab/>" }
                for run in cell.runs {
                    let name = spanStyle(run.style, scale: 100)
                    var text = "", output = ""
                    func flush() { if !text.isEmpty { output += DocumentText.escapeXML(text); text = "" } }
                    var digits = ""
                    func flushDigits() {
                        guard !digits.isEmpty else { return }
                        let kind = tokenIndex < tokens.count ? tokens[tokenIndex] : .literal; tokenIndex += 1
                        switch kind {
                        case .pageNumber: flush(); output += "<text:page-number text:select-page=\"current\">\(digits)</text:page-number>"
                        case .pageCount: flush(); output += "<text:page-count>\(digits)</text:page-count>"
                        case .literal: text += digits
                        }
                        digits = ""
                    }
                    for character in run.text {
                        if character.isASCII && character.isNumber { digits.append(character) } else { flushDigits(); text.append(character) }
                    }
                    flushDigits(); flush()
                    content += "<text:span text:style-name=\"\(name)\">\(output)</text:span>"
                }
            }
            return "<text:p text:style-name=\"\(style)\">\(content)</text:p>"
        }
        mutating func table(_ rows: [Row], page: CGRect, margin: CGFloat, gap: CGFloat) -> String {
            guard let first = rows.first, first.cells.count > 1 else { return "" }
            if let grid = grids[PDFDocumentReconstruction.tableKey(rows)] { return ruled(rows, grid: grid, page: page, margin: margin) }
            let name = "Table\(tableIndex)"; tableIndex += 1
            let count = first.cells.count
            let left = max(0, rows.map(\.bounds.minX).min()! - page.minX - margin - 4)
            var starts = (0..<count).map { column in rows.map { $0.cells[column].bounds.minX }.min() ?? first.cells[column].bounds.minX }
            starts.sort()
            var widths = (0..<(count - 1)).map { max(24, starts[$0 + 1] - starts[$0]) }
            widths.append(max(48, widths.last ?? 100, rows.map { $0.cells[count - 1].bounds.width + 12 }.max() ?? 48, (rows.map(\.bounds.maxX).max() ?? 0) - starts[count - 1] + 8))
            let available = max(1, page.width - margin * 2 - left)
            let scale = min(1, available / max(1, widths.reduce(0, +)))
            widths = widths.map { $0 * scale }
            styles += "<style:style style:name=\"\(name)\" style:family=\"table\"><style:table-properties table:align=\"left\" style:width=\"\(widths.reduce(0, +))pt\" fo:margin-left=\"\(left)pt\" fo:margin-right=\"0pt\" fo:margin-top=\"\(max(0, gap))pt\" fo:margin-bottom=\"0pt\"/></style:style>"
            // With the page artwork behind the text, the source's own rules and
            // shading show through; the cells carry no second set of borders.
            styles += "<style:style style:name=\"\(name)Cell\" style:family=\"table-cell\"><style:table-cell-properties fo:border=\"\(artwork ? "none" : "0.5pt solid #999999")\" fo:padding-left=\"4pt\" fo:padding-right=\"4pt\" fo:padding-top=\"0pt\" fo:padding-bottom=\"0pt\" style:vertical-align=\"middle\"/></style:style>"
            var columns = ""
            for (index, width) in widths.enumerated() {
                let column = "\(name)Column\(index)"
                styles += "<style:style style:name=\"\(column)\" style:family=\"table-column\"><style:table-column-properties style:column-width=\"\(width)pt\"/></style:style>"
                columns += "<table:table-column table:style-name=\"\(column)\"/>"
            }
            var contents = ""
            for (index, row) in rows.enumerated() {
                let height = rowHeight(rows, index)
                let rowStyle = "\(name)Row\(index)"
                styles += "<style:style style:name=\"\(rowStyle)\" style:family=\"table-row\"><style:table-row-properties style:row-height=\"\(height)pt\"/></style:style>"
                contents += "<table:table-row table:style-name=\"\(rowStyle)\">"
                for (column, cell) in row.cells.enumerated() {
                    let columnStart = starts[column] - 4, columnEnd = columnStart + widths[column]
                    let align = columnEnd - cell.bounds.maxX < 10 && cell.bounds.minX - columnStart > 14 ? "end" : abs(cell.bounds.midX - (columnStart + columnEnd) / 2) < 3 && cell.bounds.minX - columnStart > 10 ? "center" : "start"
                    let paragraph = paragraphStyle(left: 0, before: 0, lineHeight: max(cell.fontSize * 1.2, cell.bounds.height), size: cell.fontSize, align: align)
                    var value = ""
                    for run in cell.runs { value += span(run) }
                    contents += "<table:table-cell table:style-name=\"\(name)Cell\" office:value-type=\"string\"><text:p text:style-name=\"\(paragraph)\">\(value)</text:p></table:table-cell>"
                }
                contents += "</table:table-row>"
            }
            return "<table:table table:name=\"\(name)\" table:style-name=\"\(name)\">\(columns)\(contents)</table:table>"
        }
        /// A table whose rules and shading were read from the source: real
        /// Word borders and cell fills at the source's column and row lines.
        mutating func ruled(_ rows: [Row], grid: Grid, page: CGRect, margin: CGFloat) -> String {
            let name = "Table\(tableIndex)"; tableIndex += 1
            let count = rows[0].cells.count
            let contentLeft = page.minX + margin
            let xs = grid.columns
            let widths = (0..<count).map { max(6, xs[$0 + 1] - xs[$0]) }
            styles += "<style:style style:name=\"\(name)\" style:family=\"table\"><style:table-properties table:align=\"left\" style:width=\"\(widths.reduce(0, +))pt\" fo:margin-left=\"\(max(0, xs[0] - contentLeft))pt\" fo:margin-right=\"0pt\" fo:margin-top=\"0pt\" fo:margin-bottom=\"0pt\"/></style:style>"
            var columns = ""
            for (index, width) in widths.enumerated() {
                let column = "\(name)Column\(index)"
                styles += "<style:style style:name=\"\(column)\" style:family=\"table-column\"><style:table-column-properties style:column-width=\"\(width)pt\"/></style:style>"
                columns += "<table:table-column table:style-name=\"\(column)\"/>"
            }
            let rule = "\(String(format: "%.2f", max(0.25, grid.width)))pt solid \(grid.border)"
            var cellStyles: [String: String] = [:], contents = ""
            for (index, row) in rows.enumerated() {
                let rowStyle = "\(name)Row\(index)"
                styles += "<style:style style:name=\"\(rowStyle)\" style:family=\"table-row\"><style:table-row-properties style:row-height=\"\(max(4, grid.rows[index] - grid.rows[index + 1]))pt\"/></style:style>"
                contents += "<table:table-row table:style-name=\"\(rowStyle)\">"
                for (column, cell) in row.cells.enumerated() {
                    let fill = grid.fills[index][column]
                    let start = xs[column], end = xs[column + 1]
                    let padding = max(1, min(12, cell.bounds.minX - start))
                    let align = end - cell.bounds.maxX < padding + 3 && cell.bounds.minX - start > padding + 6 ? "end" : abs(cell.bounds.midX - (start + end) / 2) < 3 && cell.bounds.minX - start > padding + 4 ? "center" : "start"
                    // A right-aligned value keeps its own gap to the right rule.
                    let (padLeft, padRight) = align == "end" ? (CGFloat(1), max(1, min(12, end - cell.bounds.maxX))) : (padding, padding)
                    let key = "\(fill ?? "")|\(Int(padLeft * 4))|\(Int(padRight * 4))"
                    let cellStyle: String
                    if let cached = cellStyles[key] { cellStyle = cached } else {
                        cellStyle = "\(name)Cell\(cellStyles.count)"; cellStyles[key] = cellStyle
                        let borders = grid.vertical ? "fo:border=\"\(rule)\"" : "fo:border-top=\"\(rule)\" fo:border-bottom=\"\(rule)\" fo:border-left=\"none\" fo:border-right=\"none\""
                        styles += "<style:style style:name=\"\(cellStyle)\" style:family=\"table-cell\"><style:table-cell-properties \(borders) fo:padding-left=\"\(padLeft)pt\" fo:padding-right=\"\(padRight)pt\" fo:padding-top=\"0pt\" fo:padding-bottom=\"0pt\" style:vertical-align=\"middle\"\(fill.map { " fo:background-color=\"\($0)\"" } ?? "")/></style:style>"
                    }
                    let paragraph = paragraphStyle(left: 0, before: 0, lineHeight: max(cell.fontSize * 1.2, cell.bounds.height), size: cell.fontSize, align: align)
                    var value = ""
                    for run in cell.runs { value += span(run) }
                    contents += "<table:table-cell table:style-name=\"\(cellStyle)\" office:value-type=\"string\"><text:p text:style-name=\"\(paragraph)\">\(value)</text:p></table:table-cell>"
                }
                contents += "</table:table-row>"
            }
            return "<table:table table:name=\"\(name)\" table:style-name=\"\(name)\">\(columns)\(contents)</table:table>"
        }
        func rowHeight(_ rows: [Row], _ index: Int) -> CGFloat {
            let row = rows[index], minimum = row.fontSize * 1.25
            if rows.count == 1 { return max(minimum, row.bounds.height + 6) }
            let neighbour = index + 1 < rows.count ? rows[index + 1] : rows[index - 1]
            return max(minimum, abs(row.bounds.midY - neighbour.bounds.midY))
        }
        mutating func image(_ rect: CGRect, png: Data, page: CGRect, margin: CGFloat, gap: CGFloat) -> String {
            imageIndex += 1
            let left = max(0, rect.minX - page.minX - margin)
            let scale = min(1, max(1, page.width - margin * 2 - left) / max(1, rect.width))
            let style = paragraphStyle(left: left, before: gap, lineHeight: rect.height * scale, size: 1)
            return "<text:p text:style-name=\"\(style)\"><draw:frame draw:name=\"PDF image \(imageIndex)\" text:anchor-type=\"as-char\" svg:width=\"\(rect.width * scale)pt\" svg:height=\"\(rect.height * scale)pt\"><draw:image><office:binary-data>\(png.base64EncodedString())</office:binary-data></draw:image></draw:frame></text:p>"
        }
    }

    /// `artwork`: lay the original page picture (minus the editable text)
    /// behind the text. Word renders it faithfully; Pages output keeps a plain
    /// white page with readability-adjusted text colours instead.
    static func document(_ pdf: PDFDocument, at output: URL, options: OperationOptions, token: CancellationToken, artwork withArtwork: Bool = true) throws -> URL {
        var pages: [(page: PDFPage, box: CGRect, text: PageText)] = []
        for index in 0..<pdf.pageCount {
            try token.check()
            guard let page = pdf.page(at: index) else { continue }
            // Only what the crop box shows is on the page: text outside it is
            // hidden in every viewer and would otherwise spill onto new pages.
            let visible = page.bounds(for: .cropBox)
            var text = try pageText(page, readableOnWhite: !withArtwork, token: token)
            text.rows = text.rows.compactMap { row in
                let cells = row.cells.filter { visible.contains(CGPoint(x: $0.bounds.midX, y: $0.bounds.midY)) }
                return cells.isEmpty ? nil : Row(cells: cells)
            }
            text.ink = text.ink.filter { $0.rect.intersects(visible) }
            pages.append((page, visible, text))
        }
        let furniture = furniture(pages.map { ($0.box, $0.text.rows) })
        // Content set close to the paper edge (scans, full-bleed letterheads)
        // gets a narrower page margin rather than spilling onto a new page.
        var reach: [String: (top: CGFloat, bottom: CGFloat)] = [:], side: [String: CGFloat] = [:]
        for (index, entry) in pages.enumerated() {
            let removed = index < furniture.removed.count ? furniture.removed[index] : []
            let extents = entry.text.rows.enumerated().filter { !removed.contains($0.offset) }.map { extent(.paragraph([$0.element])) }
            guard let highest = extents.map(\.top).max(), let lowest = extents.map(\.bottom).min() else { continue }
            let key = "\(entry.box.width)x\(entry.box.height)", current = reach[key] ?? (.greatestFiniteMagnitude, .greatestFiniteMagnitude)
            reach[key] = (min(current.top, entry.box.maxY - highest), min(current.bottom, lowest - entry.box.minY))
            // The same for the sides: text near the left or right edge narrows
            // the side margins instead of being pushed inwards.
            let kept = entry.text.rows.enumerated().filter { !removed.contains($0.offset) }.map(\.element.bounds)
            if let leftmost = kept.map(\.minX).min(), let rightmost = kept.map(\.maxX).max() {
                side[key] = min(side[key] ?? .greatestFiniteMagnitude, leftmost - entry.box.minX, entry.box.maxX - rightmost)
            }
        }
        var writer = Writer(), body = "", pageLayouts = "", masters = ""
        // One page style per page size: consecutive pages share it, so Word
        // sees a single section with one header and footer.
        var pageStyles: [String: String] = [:], previousStyle = ""
        writer.styles += "<style:style style:name=\"ArtworkAnchor\" style:family=\"paragraph\"><style:paragraph-properties fo:line-height=\"0.1pt\" fo:margin-top=\"0pt\" fo:margin-bottom=\"0pt\"/><style:text-properties fo:font-size=\"0.1pt\"/></style:style>"
        writer.styles += "<style:style style:name=\"PageArtwork\" style:family=\"graphic\"><style:graphic-properties style:wrap=\"run-through\" style:run-through=\"background\" style:vertical-pos=\"from-top\" style:vertical-rel=\"page\" style:horizontal-pos=\"from-left\" style:horizontal-rel=\"page\" fo:margin-left=\"0pt\" fo:margin-right=\"0pt\" fo:margin-top=\"0pt\" fo:margin-bottom=\"0pt\" style:flow-with-text=\"false\" draw:stroke=\"none\" draw:fill=\"none\"/></style:style>"
        for (index, entry) in pages.enumerated() {
            try token.check()
            let (page, box, text) = entry
            // A fixed half-inch margin consumes most of a small PDF page.
            // Cap the ordinary margin while reserving at least 90% of a small
            // page's narrow dimension for editable content.
            let margin = max(0, min(36, min(box.width, box.height) * 0.05, (side["\(box.width)x\(box.height)"] ?? .greatestFiniteMagnitude) - 1))
            let removed = index < furniture.removed.count ? furniture.removed[index] : []
            let rows = text.rows.enumerated().filter { !removed.contains($0.offset) }.map(\.element)
            // Side-by-side items are separate blocks so the layout pass can
            // find columns; tables stay whole.
            var blocks: [Block] = textBlocks(rows).flatMap { block -> [Block] in
                if case .paragraph(let lines) = block, lines.count == 1, lines[0].cells.count > 1 { return lines[0].cells.map { .paragraph([Row(cells: [$0])]) } }
                return [block]
            }
            // Placed photos become their own pictures in Word, cut from the
            // artwork (so text recognised inside them is already lifted out).
            // A rectified scan is clean enough for placed-image detection too.
            let skipImages = (text.scanned && !text.rectified) || !withArtwork
            let pictures = skipImages ? [] : try PDFPlacedImages.regions(page, token: token).filter { $0.width * $0.height >= 300 && $0.width * $0.height < box.width * box.height * 0.85 }
            let tables = blocks.compactMap { block -> [Row]? in if case .table(let rows) = block { return rows }; return nil }
            // Every placed image, however large or clipped, keeps boxes off it.
            // Boxes (like page furniture) are only looked for on document-sized
            // pages. Raw scans have noise that makes bands unreliable, but a
            // rectified scan (straightened photo) is clean enough for box and
            // grid detection — it gives the utility bill its framed sections.
            let placedAll = skipImages ? [] : try PDFPlacedImages.regions(page, token: token)
            let allowBoxes = !text.scanned || text.rectified
            let art = withArtwork ? try artwork(page, ink: text.ink, tables: tables, pictures: pictures, placed: placedAll, contents: !allowBoxes || box.height < 300 ? [] : blocks.map(\.bounds), photographic: text.photographic, token: token) : nil
            // Blocks inside a recognised box are laid out by that box's table.
            writer.boxes = []
            for found in art?.boxes ?? [] {
                let inside = blocks.filter { found.rect.insetBy(dx: -1, dy: -1).contains(CGPoint(x: $0.bounds.midX, y: $0.bounds.midY)) }
                guard !inside.isEmpty else { continue }
                blocks.removeAll { found.rect.insetBy(dx: -1, dy: -1).contains(CGPoint(x: $0.bounds.midX, y: $0.bounds.midY)) }
                blocks.append(.box(writer.boxes.count, found.rect))
                // An unruled table inside a box is laid out by the box itself
                // (one line per cell), so the box's shading and dividers fall
                // between its rows and its columns line up with the rest.
                let split = inside.flatMap { block -> [Block] in
                    if case .table(let rows) = block, art?.grids[tableKey(rows)] == nil { return rows.flatMap { $0.cells.map { Block.paragraph([Row(cells: [$0])]) } } }
                    return [block]
                }
                writer.boxes.append((found, split))
            }
            writer.artwork = art != nil
            writer.grids = art?.grids ?? [:]
            if art == nil {
                for rect in try PDFPlacedImages.regions(page, token: token) where text.scanned == false { blocks.append(.image(rect)) }
            }
            let hasHeader = !furniture.header.isEmpty, hasFooter = !furniture.footer.isEmpty
            let edges = reach["\(box.width)x\(box.height)"]
            // Every page starts with its own break, so extra room at the foot
            // never moves content; it only stops a tall scan from spilling.
            let topMargin = max(0, min(margin, (edges?.top ?? margin) - 2)), bottomMargin = max(0, min(margin, (edges?.bottom ?? margin) - 14))
            let top = hasHeader ? furniture.headerTop : topMargin, bottom = hasFooter ? furniture.footerBottom : bottomMargin
            let bodyTop = hasHeader ? max(furniture.headerBottom + 4, top) : topMargin
            let bodyBottom = hasFooter ? max(furniture.footerTop + 4, bottom) : bottomMargin
            let pageSize = PDFOfficeCodec.visualBounds(page).size
            // Word paints the header and footer beneath the body, so their
            // band of the artwork (logo, rules) lives in the header/footer
            // itself, behind its text; the body artwork covers the rest.
            let bodyArtTop = hasHeader ? bodyTop : 0, bodyArtBottom = hasFooter ? pageSize.height - bodyBottom : pageSize.height
            func frame(_ name: String, _ band: (data: Data, jpeg: Bool), y: CGFloat, height: CGFloat) -> String {
                "<draw:frame draw:style-name=\"PageArtwork\" draw:name=\"\(name)\" text:anchor-type=\"paragraph\" svg:x=\"0pt\" svg:y=\"\(y)pt\" svg:width=\"\(pageSize.width)pt\" svg:height=\"\(height)pt\" draw:z-index=\"0\"><draw:image draw:mime-type=\"\(band.jpeg ? "image/jpeg" : "image/png")\"><office:binary-data>\(band.data.base64EncodedString())</office:binary-data></draw:image><svg:title>\(artworkTitle)</svg:title></draw:frame>"
            }
            let sizeKey = "\(box.width)x\(box.height)"
            let masterName: String
            if let existing = pageStyles[sizeKey] { masterName = existing } else {
            masterName = "PageMaster\(pageStyles.count)"; pageStyles[sizeKey] = masterName
            var layout = "<style:page-layout style:name=\"Layout\(masterName)\"><style:page-layout-properties fo:page-width=\"\(box.width)pt\" fo:page-height=\"\(box.height)pt\" fo:margin-left=\"\(margin)pt\" fo:margin-right=\"\(margin)pt\" fo:margin-top=\"\(top)pt\" fo:margin-bottom=\"\(bottom)pt\"\(art?.paper.map { " fo:background-color=\"\($0)\"" } ?? "")/>"
            if hasHeader { layout += "<style:header-style><style:header-footer-properties fo:min-height=\"\(bodyTop - top)pt\" fo:margin-bottom=\"0pt\" fo:margin-left=\"0pt\" fo:margin-right=\"0pt\" style:dynamic-spacing=\"false\"/></style:header-style>" }
            if hasFooter { layout += "<style:footer-style><style:header-footer-properties fo:min-height=\"\(bodyBottom - bottom)pt\" fo:margin-top=\"0pt\" fo:margin-left=\"0pt\" fo:margin-right=\"0pt\" style:dynamic-spacing=\"false\"/></style:footer-style>" }
            pageLayouts += layout + "</style:page-layout>"
            var master = "<style:master-page style:name=\"\(masterName)\" style:page-layout-name=\"Layout\(masterName)\">"
            let headerArt = hasHeader ? art?.band(0, bodyTop, always: art?.paper != nil).map { "<text:p text:style-name=\"ArtworkAnchor\">" + frame("Header artwork", $0, y: 0, height: bodyTop) + "</text:p>" } ?? "" : ""
            let footerArt = hasFooter ? art?.band(pageSize.height - bodyBottom, pageSize.height, always: art?.paper != nil).map { "<text:p text:style-name=\"ArtworkAnchor\">" + frame("Footer artwork", $0, y: pageSize.height - bodyBottom, height: bodyBottom) + "</text:p>" } ?? "" : ""
            // Header and footer lines keep their spacing from the area's top.
            func lines(_ rows: [(row: Row, tokens: [Furniture.Token])], from start: CGFloat) -> String {
                var previous = start, xml = ""
                for item in rows {
                    let (rowTop, rowBottom) = extent(.paragraph([item.row]))
                    xml += writer.furniture(item.row, tokens: item.tokens, page: box, margin: margin, gap: max(0, previous - rowTop))
                    previous = rowBottom
                }
                return xml
            }
            if hasHeader { master += "<style:header>" + headerArt + lines(furniture.header, from: box.maxY - top) + "</style:header>" }
            if hasFooter { master += "<style:footer>" + footerArt + lines(furniture.footer, from: box.minY + bodyBottom) + "</style:footer>" }
            masters += master + "</style:master-page>"
            }
            let masterAttribute = masterName == previousStyle ? "" : " style:master-page-name=\"\(masterName)\""
            previousStyle = masterName
            writer.styles += "<style:style style:name=\"PageStart\(index)\" style:family=\"paragraph\"\(masterAttribute)><style:paragraph-properties\(index > 0 ? " fo:break-before=\"page\"" : "") fo:line-height=\"0.1pt\" fo:margin-top=\"0pt\" fo:margin-bottom=\"0pt\"/><style:text-properties fo:font-size=\"0.1pt\"/></style:style>"
            // The artwork is anchored to its own paragraph after the page
            // break; anchored in the breaking paragraph, Word would place it
            // on the previous page.
            var start = "<text:p text:style-name=\"PageStart\(index)\"/><text:p text:style-name=\"ArtworkAnchor\">"
            if let band = art?.band(bodyArtTop, bodyArtBottom, always: art?.paper != nil) { start += frame("Page \(index + 1) artwork", band, y: bodyArtTop, height: bodyArtBottom - bodyArtTop) }
            for (number, picture) in (art?.pictures ?? []).enumerated() {
                let representation = NSBitmapImageRep(cgImage: picture.image)
                let jpeg = picture.image.width * picture.image.height > 250_000
                guard let data = jpeg ? representation.representation(using: .jpeg, properties: [.compressionFactor: 0.9]) : representation.representation(using: .png, properties: [:]) else { continue }
                start += "<draw:frame draw:style-name=\"PageArtwork\" draw:name=\"Picture \(index + 1).\(number + 1)\" text:anchor-type=\"paragraph\" svg:x=\"\(picture.rect.minX - box.minX)pt\" svg:y=\"\(box.maxY - picture.rect.maxY)pt\" svg:width=\"\(picture.rect.width)pt\" svg:height=\"\(picture.rect.height)pt\" draw:z-index=\"1\"><draw:image draw:mime-type=\"\(jpeg ? "image/jpeg" : "image/png")\"><office:binary-data>\(data.base64EncodedString())</office:binary-data></draw:image><svg:title>Picture</svg:title></draw:frame>"
            }
            body += start + "</text:p>"
            var previousBottom = box.maxY - bodyTop
            let region = layout(blocks, left: box.minX + margin, right: box.maxX - margin, depth: 0)
            body += try emit(region, left: box.minX + margin, right: box.maxX - margin, page: page, box: box, margin: margin, previousBottom: &previousBottom, writer: &writer, options: options, token: token)
        }
        // Word requires a paragraph after a final table. Supplying a tiny one
        // prevents LibreOffice from appending a normal-height paragraph that
        // spills an otherwise complete small document onto a blank extra page.
        writer.styles += "<style:style style:name=\"DocumentEnd\" style:family=\"paragraph\"><style:paragraph-properties fo:line-height=\"0.1pt\" fo:margin-top=\"0pt\" fo:margin-bottom=\"0pt\"/><style:text-properties fo:font-size=\"0.1pt\"/></style:style>"
        body += "<text:p text:style-name=\"DocumentEnd\"/>"
        let xml = """
        <?xml version="1.0" encoding="UTF-8"?><office:document xmlns:office="urn:oasis:names:tc:opendocument:xmlns:office:1.0" xmlns:style="urn:oasis:names:tc:opendocument:xmlns:style:1.0" xmlns:text="urn:oasis:names:tc:opendocument:xmlns:text:1.0" xmlns:table="urn:oasis:names:tc:opendocument:xmlns:table:1.0" xmlns:draw="urn:oasis:names:tc:opendocument:xmlns:drawing:1.0" xmlns:svg="urn:oasis:names:tc:opendocument:xmlns:svg-compatible:1.0" xmlns:fo="urn:oasis:names:tc:opendocument:xmlns:xsl-fo-compatible:1.0" office:version="1.3" office:mimetype="application/vnd.oasis.opendocument.text">\(writer.fontFaces)<office:automatic-styles>\(writer.styles)\(pageLayouts)</office:automatic-styles><office:master-styles>\(masters)</office:master-styles><office:body><office:text>\(body)</office:text></office:body></office:document>
        """
        try xml.write(to: output, atomically: true, encoding: .utf8)
        return output
    }

    /// PDFKit glyph boxes run from the font's descender to its ascender
    /// (about 19% / 81% of the box for common text faces).
    private static func baseline(_ row: Row) -> CGFloat {
        let lead = row.cells.max { $0.fontSize < $1.fontSize } ?? row.cells[0]
        return lead.bounds.minY + lead.bounds.height * 0.19
    }

    // MARK: Layout

    /// A page region: blocks in reading order, regions stacked top to bottom,
    /// or regions side by side (columns with their x-range).
    private indirect enum Region {
        case flow([Block])
        case stack([Region])
        case columns([(region: Region, left: CGFloat, right: CGFloat)])
        var blocks: [Block] {
            switch self {
            case .flow(let blocks): return blocks
            case .stack(let parts): return parts.flatMap(\.blocks)
            case .columns(let parts): return parts.flatMap(\.region.blocks)
            }
        }
    }

    /// Recursive XY-cut: split at horizontal gaps first (sections one above
    /// another), then at vertical gutters (columns side by side).
    private static func layout(_ blocks: [Block], left: CGFloat, right: CGFloat, depth: Int) -> Region {
        guard blocks.count > 1, depth < 5 else { return .flow(blocks) }
        let byTop = blocks.sorted { $0.bounds.maxY > $1.bounds.maxY }
        var bands: [[Block]] = [[byTop[0]]], bottom = byTop[0].bounds.minY
        for block in byTop.dropFirst() {
            if bottom - block.bounds.maxY >= 4 { bands.append([block]) } else { bands[bands.count - 1].append(block) }
            bottom = min(bottom, block.bounds.minY)
        }
        if bands.count > 1 { return .stack(bands.map { layout($0, left: left, right: right, depth: depth) }) }
        let byLeft = blocks.sorted { $0.bounds.minX < $1.bounds.minX }
        var columns: [[Block]] = [[byLeft[0]]], edge = byLeft[0].bounds.maxX
        for block in byLeft.dropFirst() {
            if block.bounds.minX - edge >= 10 { columns.append([block]) } else { columns[columns.count - 1].append(block) }
            edge = max(edge, block.bounds.maxX)
        }
        guard columns.count > 1 else { return .flow(blocks) }
        var parts: [(region: Region, left: CGFloat, right: CGFloat)] = []
        for (index, column) in columns.enumerated() {
            let l = index == 0 ? left : (columns[index - 1].map(\.bounds.maxX).max()! + column.map(\.bounds.minX).min()!) / 2
            let r = index == columns.count - 1 ? right : (column.map(\.bounds.maxX).max()! + columns[index + 1].map(\.bounds.minX).min()!) / 2
            parts.append((layout(column, left: l, right: r, depth: depth + 1), l, r))
        }
        return .columns(parts)
    }

    /// Top and bottom of a block's line boxes as Word lays them out.
    private static func extent(_ block: Block, grids: [String: Grid] = [:]) -> (top: CGFloat, bottom: CGFloat) {
        if case .table(let rows) = block, let grid = grids[tableKey(rows)] { return (grid.rows[0], grid.rows[grid.rows.count - 1]) }
        switch block {
        case .paragraph(let rows):
            let lineHeight = lineSpacing(rows), last = rows[rows.count - 1]
            return (baseline(rows[0]) - rows[0].fontSize * 0.212 + lineHeight, baseline(last) - last.fontSize * 0.212)
        case .table(let rows):
            let first = Writer().rowHeight(rows, 0), last = Writer().rowHeight(rows, rows.count - 1)
            return (rows[0].bounds.midY + first / 2, rows[rows.count - 1].bounds.midY - last / 2)
        case .image(let rect), .box(_, let rect): return (rect.maxY, rect.minY)
        }
    }

    /// A box as one table: its frame is the table border, its bands become
    /// rows with their own fill, side-by-side content shares columns across
    /// rows and full-width content spans them. Rows grow with their content,
    /// so text and shading stay together in any word processor.
    private static func boxed(_ index: Int, left: CGFloat, right: CGFloat, page: PDFPage, box: CGRect, margin: CGFloat, previousBottom: inout CGFloat, writer: inout Writer, options: OperationOptions, token: CancellationToken) throws -> String {
        let (shape, blocks) = writer.boxes[index]
        let rect = shape.rect, grids = writer.grids
        let innerLeft = rect.minX + shape.stroke, innerRight = rect.maxX - shape.stroke
        // Horizontal cut lines: the frame, every band edge, and the middle of
        // each gap between content that no band edge already separates.
        let extents = blocks.map { extent($0, grids: grids) }.sorted { $0.top > $1.top }
        var groups: [(top: CGFloat, bottom: CGFloat)] = []
        for item in extents {
            if let last = groups.last, item.top > last.bottom + 1.5 { groups[groups.count - 1].bottom = min(last.bottom, item.bottom) } else { groups.append(item) }
        }
        var cuts: [CGFloat] = [rect.maxY, rect.minY]
        for panel in shape.panels {
            // Text boxes are estimates: an edge within 3pt of content still cuts.
            for edge in [panel.rect.maxY, panel.rect.minY] where edge < rect.maxY - 1.5 && edge > rect.minY + 1.5 && !groups.contains(where: { edge < $0.top - 3 && edge > $0.bottom + 3 }) { cuts.append(edge) }
        }
        for divider in shape.dividers where !groups.contains(where: { divider.y < $0.top - 0.5 && divider.y > $0.bottom + 0.5 }) { cuts.append(divider.y) }
        for (upper, lower) in zip(groups, groups.dropFirst()) where !cuts.contains(where: { $0 <= upper.bottom + 3 && $0 >= lower.top - 3 }) { cuts.append((upper.bottom + lower.top) / 2) }
        cuts.sort(by: >)
        var lines: [CGFloat] = []
        for cut in cuts where lines.last.map({ $0 - cut > 1 }) ?? true { lines.append(cut) }
        guard lines.count >= 2 else { return "" }
        // Each row's content, fill and layout.
        var rowsContent: [(top: CGFloat, bottom: CGFloat, blocks: [Block], fill: String?, region: Region?)] = []
        for (top, bottom) in zip(lines, lines.dropFirst()) {
            let inside = blocks.filter { let e = extent($0, grids: grids); let middle = (e.top + e.bottom) / 2; return middle <= top && middle > bottom }
            func overlap(_ panel: CGRect) -> CGFloat { max(0, min(panel.maxY, top) - max(panel.minY, bottom)) / max(0.1, top - bottom) }
            let fill = shape.panels.max { overlap($0.rect) < overlap($1.rect) }.flatMap { overlap($0.rect) >= 0.45 ? $0.color : nil }
            rowsContent.append((top, bottom, inside, fill, inside.isEmpty ? nil : layout(inside, left: innerLeft, right: innerRight, depth: 1)))
        }
        // Shared columns from every row laid out side by side.
        let columnRows = rowsContent.compactMap { row -> [(region: Region, left: CGFloat, right: CGFloat)]? in if case .columns(let parts)? = row.region { return parts }; return nil }
        var edges: [CGFloat] = []
        var placement: [[Int: Region]?] = Array(repeating: nil, count: rowsContent.count)
        if let guide = columnRows.max(by: { $0.count < $1.count }) {
            let guideEdges = guide.dropLast().map(\.right)
            var spans = [(minX: CGFloat, maxX: CGFloat)](repeating: (.greatestFiniteMagnitude, -.greatestFiniteMagnitude), count: guide.count)
            var valid = true
            for (index, row) in rowsContent.enumerated() {
                guard case .columns(let parts)? = row.region else { continue }
                var cells: [Int: Region] = [:]
                for part in parts {
                    let b = part.region.blocks.map(\.bounds), minX = b.map(\.minX).min() ?? 0, maxX = b.map(\.maxX).max() ?? 0
                    let column = guideEdges.filter { $0 < (minX + maxX) / 2 }.count
                    if cells[column] != nil { valid = false }
                    cells[column] = part.region
                    spans[column] = (min(spans[column].minX, minX), max(spans[column].maxX, maxX))
                }
                placement[index] = cells
            }
            if valid, (0..<(spans.count - 1)).allSatisfy({ spans[$0].maxX < spans[$0 + 1].minX - 2 && spans[$0].minX <= spans[$0].maxX }) {
                edges = (0..<(spans.count - 1)).map { (spans[$0].maxX + spans[$0 + 1].minX) / 2 }
            } else { placement = Array(repeating: nil, count: rowsContent.count) }
        }
        // Bands set inside the frame (a white margin between frame and band)
        // keep that margin as an unshaded column on each side.
        let insets = shape.panels.flatMap { [$0.rect.minX - rect.minX, rect.maxX - $0.rect.maxX] }
        let inset = shape.border != nil && !insets.isEmpty ? max(0, insets.min()!) : 0
        let gutter = inset >= 1.5 && inset < 20 && rowsContent.allSatisfy { row in row.blocks.allSatisfy { $0.bounds.minX >= rect.minX + inset - 0.5 && $0.bounds.maxX <= rect.maxX - inset + 0.5 } }
        let bounds = gutter ? [rect.minX, rect.minX + inset] + edges + [rect.maxX - inset, rect.maxX] : [rect.minX] + edges + [rect.maxX]
        let count = bounds.count - 1, first = gutter ? 1 : 0, last = gutter ? count - 2 : count - 1
        var xml = ""
        xml += writer.spacer(previousBottom - rect.maxY)
        let name = "Box\(writer.tableIndex)"; writer.tableIndex += 1
        writer.styles += "<style:style style:name=\"\(name)\" style:family=\"table\"><style:table-properties table:align=\"left\" style:width=\"\(rect.width)pt\" fo:margin-left=\"\(max(0, rect.minX - left))pt\" fo:margin-top=\"0pt\" fo:margin-bottom=\"0pt\"/></style:style>"
        var columns = ""
        for index in 0..<count {
            let column = "\(name)Column\(index)"
            writer.styles += "<style:style style:name=\"\(column)\" style:family=\"table-column\"><style:table-column-properties style:column-width=\"\(bounds[index + 1] - bounds[index])pt\"/></style:style>"
            columns += "<table:table-column table:style-name=\"\(column)\"/>"
        }
        let rule = shape.border.map { "\(String(format: "%.2f", shape.stroke))pt solid \($0)" } ?? "none"
        var cellStyles: [String: String] = [:]
        func cellStyle(fill: String?, top: Bool, bottom: Bool, first: Bool, last: Bool, divider: String? = nil) -> String {
            let key = "\(fill ?? "")|\(top)\(bottom)\(first)\(last)|\(divider ?? "")"
            if let cached = cellStyles[key] { return cached }
            let style = "\(name)Cell\(cellStyles.count)"; cellStyles[key] = style
            let sides = "fo:border-top=\"\(top ? rule : "none")\" fo:border-bottom=\"\(bottom ? rule : divider ?? "none")\" fo:border-left=\"\(first ? rule : "none")\" fo:border-right=\"\(last ? rule : "none")\""
            writer.styles += "<style:style style:name=\"\(style)\" style:family=\"table-cell\"><style:table-cell-properties \(sides) fo:padding=\"0pt\" style:vertical-align=\"top\"\(fill.map { " fo:background-color=\"\($0)\"" } ?? "")/></style:style>"
            return style
        }
        var body = ""
        let outerKeep = writer.keepLines; writer.keepLines = true; defer { writer.keepLines = outerKeep }
        for (index, row) in rowsContent.enumerated() {
            try token.check()
            let isTop = index == 0, isBottom = index == rowsContent.count - 1
            // A separator rule at this row's lower edge becomes its bottom border.
            let divider = shape.dividers.first { abs($0.y - row.bottom) < 1.5 }.map { "\(String(format: "%.2f", $0.weight))pt solid \($0.color)" }
            let rowStyle = "\(name)Row\(index)"
            // Borders add to a row's height in Word: the frame's rules and a
            // divider are taken out of the row they sit on.
            let borders = 0.75 * ((isTop && shape.border != nil ? shape.stroke : 0) + (isBottom && shape.border != nil ? shape.stroke : 0)) + (divider == nil || isBottom ? 0 : shape.dividers.first { abs($0.y - row.bottom) < 1.5 }!.weight)
            writer.styles += "<style:style style:name=\"\(rowStyle)\" style:family=\"table-row\"><style:table-row-properties style:min-row-height=\"\(max(1, row.top - row.bottom - borders))pt\"/></style:style>"
            body += "<table:table-row table:style-name=\"\(rowStyle)\">"
            func content(_ region: Region?, _ l: CGFloat, _ r: CGFloat) throws -> String {
                var cellTop = row.top
                var xml = try region.map { try emit($0, left: l, right: r, page: page, box: box, margin: margin, previousBottom: &cellTop, writer: &writer, options: options, token: token) } ?? ""
                if xml.isEmpty || xml.hasSuffix("</table:table>") { xml += "<text:p text:style-name=\"DocumentEnd\"/>" }
                return xml
            }
            let margin = "<table:table-cell table:style-name=\"\(cellStyle(fill: nil, top: isTop, bottom: isBottom, first: true, last: false))\" office:value-type=\"string\"><text:p text:style-name=\"DocumentEnd\"/></table:table-cell>"
            if gutter { body += margin }
            let contentLeft = gutter ? bounds[1] : innerLeft, contentRight = gutter ? bounds[count - 1] : innerRight
            if let cells = placement[index] {
                for column in first...last {
                    let l = column == first ? contentLeft : bounds[column], r = column == last ? contentRight : bounds[column + 1]
                    body += "<table:table-cell table:style-name=\"\(cellStyle(fill: row.fill, top: isTop, bottom: isBottom, first: !gutter && column == first, last: !gutter && column == last, divider: divider))\" office:value-type=\"string\">\(try content(cells[column - first], l, r))</table:table-cell>"
                }
            } else {
                let width = last - first + 1
                let span = width > 1 ? " table:number-columns-spanned=\"\(width)\"" : ""
                body += "<table:table-cell table:style-name=\"\(cellStyle(fill: row.fill, top: isTop, bottom: isBottom, first: !gutter, last: !gutter, divider: divider))\"\(span) office:value-type=\"string\">\(try content(row.region, contentLeft, contentRight))</table:table-cell>"
                body += String(repeating: "<table:covered-table-cell/>", count: width - 1)
            }
            if gutter { body += "<table:table-cell table:style-name=\"\(cellStyle(fill: nil, top: isTop, bottom: isBottom, first: false, last: true))\" office:value-type=\"string\"><text:p text:style-name=\"DocumentEnd\"/></table:table-cell>" }
            body += "</table:table-row>"
        }
        previousBottom = min(previousBottom, rect.minY)
        return xml + "<table:table table:name=\"\(name)\" table:style-name=\"\(name)\">\(columns)\(body)</table:table>"
    }

    /// Several column bands as one table. Columns come from the band with
    /// the most columns; every other band's columns must each fall in one of
    /// them (missing ones become empty cells), or nil keeps them separate.
    private static func rows(_ bands: [Region], left: CGFloat, right: CGFloat, page: PDFPage, box: CGRect, margin: CGFloat, previousBottom: inout CGFloat, writer: inout Writer, options: OperationOptions, token: CancellationToken) throws -> String? {
        let parts = bands.compactMap { band -> [(region: Region, left: CGFloat, right: CGFloat)]? in if case .columns(let parts) = band { return parts }; return nil }
        guard parts.count == bands.count, let guide = parts.max(by: { $0.count < $1.count }) else { return nil }
        let guideEdges = guide.dropLast().map(\.right)
        func content(_ region: Region) -> (minX: CGFloat, maxX: CGFloat) { let b = region.blocks.map(\.bounds); return (b.map(\.minX).min() ?? 0, b.map(\.maxX).max() ?? 0) }
        var layout: [[Int: Region]] = []
        var spans = [(minX: CGFloat, maxX: CGFloat)](repeating: (.greatestFiniteMagnitude, -.greatestFiniteMagnitude), count: guide.count)
        for band in parts {
            var cells: [Int: Region] = [:]
            for part in band {
                let extent = content(part.region), middle = (extent.minX + extent.maxX) / 2
                let column = guideEdges.filter { $0 < middle }.count
                guard cells[column] == nil else { return nil }
                cells[column] = part.region
                spans[column] = (min(spans[column].minX, extent.minX), max(spans[column].maxX, extent.maxX))
            }
            layout.append(cells)
        }
        // Column lines from every band's content: halfway across each gutter.
        guard (0..<(spans.count - 1)).allSatisfy({ spans[$0].maxX < spans[$0 + 1].minX - 2 }) else { return nil }
        let edges = (0..<(spans.count - 1)).map { (spans[$0].maxX + spans[$0 + 1].minX) / 2 }
        let grids = writer.grids
        let extents = bands.map { band -> (top: CGFloat, bottom: CGFloat) in let e = band.blocks.map { extent($0, grids: grids) }; return (e.map(\.top).max()!, e.map(\.bottom).min()!) }
        // Row lines halfway between bands, where the source's row shading changes.
        var lines = [extents[0].top]
        for index in 1..<extents.count { lines.append((extents[index - 1].bottom + extents[index].top) / 2) }
        lines.append(extents[extents.count - 1].bottom)
        var xml = ""
        if previousBottom - lines[0] > 0.5 { xml += "<text:p text:style-name=\"\(writer.paragraphStyle(left: 0, before: 0, lineHeight: previousBottom - lines[0], size: 1))\"/>" }
        let name = "Layout\(writer.tableIndex)"; writer.tableIndex += 1
        let bounds = [left] + edges + [right]
        writer.styles += "<style:style style:name=\"\(name)\" style:family=\"table\"><style:table-properties table:align=\"left\" style:width=\"\(right - left)pt\" fo:margin-left=\"0pt\" fo:margin-top=\"0pt\" fo:margin-bottom=\"0pt\"/></style:style>"
        writer.styles += "<style:style style:name=\"\(name)Cell\" style:family=\"table-cell\"><style:table-cell-properties fo:border=\"none\" fo:padding=\"0pt\" style:vertical-align=\"top\"/></style:style>"
        var columns = ""
        for index in 0..<(bounds.count - 1) {
            let column = "\(name)Column\(index)"
            writer.styles += "<style:style style:name=\"\(column)\" style:family=\"table-column\"><style:table-column-properties style:column-width=\"\(bounds[index + 1] - bounds[index])pt\"/></style:style>"
            columns += "<table:table-column table:style-name=\"\(column)\"/>"
        }
        var body = ""
        let outerKeep = writer.keepLines; writer.keepLines = true; defer { writer.keepLines = outerKeep }
        for (row, cells) in layout.enumerated() {
            try token.check()
            let rowStyle = "\(name)Row\(row)"
            writer.styles += "<style:style style:name=\"\(rowStyle)\" style:family=\"table-row\"><style:table-row-properties style:min-row-height=\"\(max(1, lines[row] - lines[row + 1]))pt\"/></style:style>"
            body += "<table:table-row table:style-name=\"\(rowStyle)\">"
            for column in 0..<(bounds.count - 1) {
                var cellTop = lines[row]
                var content = try cells[column].map { try emit($0, left: bounds[column], right: bounds[column + 1], page: page, box: box, margin: margin, previousBottom: &cellTop, writer: &writer, options: options, token: token) } ?? ""
                if content.isEmpty || content.hasSuffix("</table:table>") { content += "<text:p text:style-name=\"DocumentEnd\"/>" }
                body += "<table:table-cell table:style-name=\"\(name)Cell\" office:value-type=\"string\">\(content)</table:table-cell>"
            }
            body += "</table:table-row>"
        }
        previousBottom = min(previousBottom, lines[lines.count - 1])
        return xml + "<table:table table:name=\"\(name)\" table:style-name=\"\(name)\">\(columns)\(body)</table:table>"
    }

    private static func emit(_ region: Region, left: CGFloat, right: CGFloat, page: PDFPage, box: CGRect, margin: CGFloat, previousBottom: inout CGFloat, writer: inout Writer, options: OperationOptions, token: CancellationToken) throws -> String {
        // Writer positions content inside `frame`'s margins; a column is a
        // narrower frame with the same margin arithmetic.
        let frame = CGRect(x: left - margin, y: box.minY, width: right - left + margin * 2, height: box.height)
        switch region {
        case .stack(let parts):
            var xml = ""
            var index = 0
            while index < parts.count {
                // Consecutive bands split into compatible columns are one table:
                // one row per band, columns shared, so values line up like the
                // source's rows and its row shading.
                if case .columns = parts[index] {
                    var end = index + 1
                    while end < parts.count, case .columns = parts[end] { end += 1 }
                    if end - index >= 2, let merged = try rows(Array(parts[index..<end]), left: left, right: right, page: page, box: box, margin: margin, previousBottom: &previousBottom, writer: &writer, options: options, token: token) {
                        xml += merged; index = end; continue
                    }
                }
                xml += try emit(parts[index], left: left, right: right, page: page, box: box, margin: margin, previousBottom: &previousBottom, writer: &writer, options: options, token: token)
                index += 1
            }
            return xml
        case .flow(let blocks):
            // Lines of one column re-join into paragraphs once their
            // neighbours from other columns are out of the way.
            let tables = blocks.filter { if case .paragraph = $0 { return false }; return true }
            let lines = blocks.flatMap { block -> [Row] in if case .paragraph(let rows) = block { return rows }; return [] }.sorted { $0.bounds.midY > $1.bounds.midY }
            let grids = writer.grids
            let ordered = (textBlocks(lines) + tables).sorted { extent($0, grids: grids).top > extent($1, grids: grids).top }
            var xml = ""
            for block in ordered {
                try token.check()
                let (top, bottom) = extent(block, grids: grids)
                switch block {
                case .paragraph(let rows):
                    xml += writer.paragraph(rows, page: frame, margin: margin, gap: max(0, previousBottom - top), lineHeight: lineSpacing(rows))
                case .table(let rows):
                    // Word tables have no space-before; a spacer line keeps the gap.
                    let gap = previousBottom - top
                    xml += writer.spacer(gap)
                    xml += writer.table(rows, page: frame, margin: margin, gap: 0)
                case .image(let rect):
                    let png = try PDFOfficeCodec.rasterPNG(page, rect: rect, dpi: options.dpi, token: token)
                    xml += writer.image(rect, png: png, page: frame, margin: margin, gap: max(0, previousBottom - top))
                case .box(let index, _):
                    xml += try boxed(index, left: left, right: right, page: page, box: box, margin: margin, previousBottom: &previousBottom, writer: &writer, options: options, token: token)
                }
                previousBottom = min(previousBottom, bottom)
            }
            return xml
        case .columns(let parts):
            // Columns become one borderless table row; each cell holds its
            // column's own flow, so neither column pushes the other down.
            let grids = writer.grids
            let extents = region.blocks.map { extent($0, grids: grids) }
            let top = extents.map(\.top).max()!, bottom = extents.map(\.bottom).min()!
            var xml = ""
            xml += writer.spacer(previousBottom - top)
            let name = "Layout\(writer.tableIndex)"; writer.tableIndex += 1
            writer.styles += "<style:style style:name=\"\(name)\" style:family=\"table\"><style:table-properties table:align=\"left\" style:width=\"\(right - left)pt\" fo:margin-left=\"0pt\" fo:margin-top=\"0pt\" fo:margin-bottom=\"0pt\"/></style:style>"
            writer.styles += "<style:style style:name=\"\(name)Cell\" style:family=\"table-cell\"><style:table-cell-properties fo:border=\"none\" fo:padding=\"0pt\" style:vertical-align=\"top\"/></style:style>"
            writer.styles += "<style:style style:name=\"\(name)Row\" style:family=\"table-row\"><style:table-row-properties style:min-row-height=\"\(max(1, top - bottom))pt\"/></style:style>"
            var columns = "", cells = ""
            for (index, part) in parts.enumerated() {
                let column = "\(name)Column\(index)"
                writer.styles += "<style:style style:name=\"\(column)\" style:family=\"table-column\"><style:table-column-properties style:column-width=\"\(part.right - part.left)pt\"/></style:style>"
                columns += "<table:table-column table:style-name=\"\(column)\"/>"
                var cellBottom = top
                let outerKeep = writer.keepLines; writer.keepLines = true; defer { writer.keepLines = outerKeep }
                var content = try emit(part.region, left: part.left, right: part.right, page: page, box: box, margin: margin, previousBottom: &cellBottom, writer: &writer, options: options, token: token)
                // A cell must end in a paragraph (and may not be empty).
                if content.isEmpty || content.hasSuffix("</table:table>") { content += "<text:p text:style-name=\"DocumentEnd\"/>" }
                cells += "<table:table-cell table:style-name=\"\(name)Cell\" office:value-type=\"string\">\(content)</table:table-cell>"
            }
            xml += "<table:table table:name=\"\(name)\" table:style-name=\"\(name)\">\(columns)<table:table-row table:style-name=\"\(name)Row\">\(cells)</table:table-row></table:table>"
            previousBottom = min(previousBottom, bottom)
            return xml
        }
    }

    private static func lineSpacing(_ rows: [Row]) -> CGFloat {
        let size = rows.map(\.fontSize).max() ?? 12
        guard rows.count > 1 else { return max(size * 1.2, rows[0].bounds.height) }
        return max(size * 1.0, (rows[0].bounds.midY - rows[rows.count - 1].bounds.midY) / CGFloat(rows.count - 1))
    }

    // MARK: Text

    private static func pageText(_ page: PDFPage, readableOnWhite: Bool, token: CancellationToken) throws -> PageText {
        let box = page.bounds(for: .cropBox)
        let placed = try PDFPlacedImages.regions(page, token: token)
        let photographic = placed.contains { $0.width * $0.height > box.width * box.height * 0.04 }
        // A rectified page is a single image filling the entire crop box
        // (output of PDFScanRectification). It is clean enough for box/frame
        // detection, so it gets higher artwork resolution and frame analysis.
        let rectified = placed.count == 1 && {
            let area = placed[0].width * placed[0].height
            return area > box.width * box.height * 0.9
        }()
        guard let attributed = page.attributedString, attributed.length > 0,
              !attributed.string.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty else {
            let cells = try PDFOfficeCodec.recognizedCells(page, token: token)
            return PageText(rows: ocrRows(cells), ink: cells.map { ($0.rect, $0.color) }, scanned: true, photographic: true, rectified: rectified)
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
            glyphs.append(Cell(runs: [.init(text: value, style: style(attributes, readableOnWhite: readableOnWhite))], bounds: rect))
        }
        glyphs = settle(glyphs, page: page)
        glyphs = horizontal(glyphs, page: box)
        var rows = rows(glyphs)
        var ink = glyphs.filter { !$0.text.trimmingCharacters(in: .whitespaces).isEmpty }.map { ($0.bounds, Optional($0.runs[0].style.color)) }
        // Pictures of text (screenshots, pasted scans) inside a text PDF.
        let pictured = placed.filter { $0.width * $0.height > box.width * box.height * 0.04 }
        if !pictured.isEmpty {
            let native = glyphs.map(\.bounds)
            let recognized = try PDFOfficeCodec.recognizedCells(page, token: token).filter { cell in
                pictured.contains { $0.insetBy(dx: -2, dy: -2).contains(CGPoint(x: cell.rect.midX, y: cell.rect.midY)) } && !native.contains { $0.intersects(cell.rect.insetBy(dx: 2, dy: 2)) }
            }
            rows += ocrRows(recognized)
            ink += recognized.map { ($0.rect, $0.color) }
            rows.sort { $0.bounds.midY > $1.bounds.midY }
        }
        return PageText(rows: rows, ink: ink, scanned: false, photographic: photographic)
    }

    /// Places each glyph on its real baseline. A selection's bounds are the
    /// box of the whole line PDFKit assembled around it, and PDFKit can join
    /// lines from neighbouring table cells (one cell's second line with the
    /// next cell's centred label), giving both a merged, misplaced box. Exact
    /// per-glyph boxes come from characterBounds(at:), whose indices drift
    /// from the string's by a few places; they are matched back by x position
    /// within a small window, in reading order.
    private static func settle(_ glyphs: [Cell], page: PDFPage) -> [Cell] {
        let count = page.numberOfCharacters
        guard count > 0, !glyphs.isEmpty else { return glyphs }
        let exact = (0..<count).map { page.characterBounds(at: $0) }
        // Candidates by x (half-point buckets); the drift between the two
        // index spaces varies along the page, so the whole page is searched.
        var byX: [Int: [Int]] = [:]
        for (index, rect) in exact.enumerated() where rect.width > 0 { byX[Int((rect.minX * 2).rounded()), default: []].append(index) }
        var cursor = 0
        var baselines = [CGFloat?](repeating: nil, count: glyphs.count)
        for (index, glyph) in glyphs.enumerated() where !glyph.text.trimmingCharacters(in: .whitespaces).isEmpty {
            let x = glyph.bounds.minX, tolerance = max(1.0, glyph.fontSize * 0.15)
            let key = Int((x * 2).rounded()), reach = Int((tolerance * 2).rounded(.up))
            let widthTolerance: CGFloat = max(1.2, glyph.fontSize * 0.2)
            let lowest: CGFloat = glyph.bounds.minY - 1, highest: CGFloat = glyph.bounds.maxY - glyph.fontSize * 0.3
            var candidates: [Int] = []
            for bucket in (key - reach)...(key + reach) {
                for candidate in byX[bucket] ?? [] {
                    let r = exact[candidate]
                    guard abs(r.minX - x) < tolerance, abs(r.maxX - glyph.bounds.maxX) < widthTolerance, r.minY >= lowest, r.minY <= highest else { continue }
                    candidates.append(candidate)
                }
            }
            guard let match = candidates.min(by: { abs($0 - cursor) < abs($1 - cursor) }) else { continue }
            cursor = match + 1
            // PDFKit's (line) box always contains the glyph itself; a match
            // outside it is a different glyph that happens to share an x.
            if exact[match].height > 0.5, exact[match].minY >= glyph.bounds.minY - 1, exact[match].minY <= glyph.bounds.maxY - glyph.fontSize * 0.3 { baselines[index] = exact[match].minY }
        }
        // One baseline per run of adjacent glyphs from the same PDFKit line:
        // the median of its matches, so a stray match (an apostrophe, another
        // glyph at the same x) cannot pull a letter off its line, and letters
        // with descenders sit on the run's baseline. A PDFKit line that joined
        // two table cells splits at the gap between them.
        var runStart = 0
        for index in 1...glyphs.count {
            let ends = index == glyphs.count || {
                let a = glyphs[index - 1], b = glyphs[index], size = max(a.fontSize, b.fontSize)
                // A wide space glyph bridges two cells without leaving a gap.
                func gap(_ c: Cell) -> Bool { c.text.trimmingCharacters(in: .whitespaces).isEmpty && c.bounds.width > c.fontSize * 0.8 }
                return abs(a.bounds.minY - b.bounds.minY) > 0.1 || abs(a.bounds.height - b.bounds.height) > 0.1 || b.bounds.minX - a.bounds.maxX > size * 1.2 || b.bounds.maxX < a.bounds.minX || gap(a) || gap(b)
            }()
            guard ends else { continue }
            let values = baselines[runStart..<index].compactMap { $0 }.sorted()
            let median = values.isEmpty ? nil : values[values.count / 2]
            for i in runStart..<index { baselines[i] = median }
            runStart = index
        }
        return glyphs.enumerated().map { index, glyph in
            guard let base = baselines[index] else { return glyph }
            var settled = glyph
            // A text-face box (descender ≈ 19%, ascender ≈ 81% of 1.15 em),
            // the same proportions baseline(_:) reads back.
            let height = glyph.fontSize * 1.15
            settled.bounds = CGRect(x: glyph.bounds.minX, y: base - height * 0.19, width: glyph.bounds.width, height: height)
            return settled
        }
    }

    private static func ocrRows(_ cells: [PDFOfficeCodec.TextCell]) -> [Row] {
        // The recognised text is known, so the size that makes it span its
        // source width is exact; box height (which varies with ascenders,
        // descenders and nearby rules) only bounds that estimate.
        let raw = cells.map { cell -> Double in
            let byHeight = max(6, Double(cell.rect.height) / 1.3)
            let unit = Double(width(of: cell.text, style: TextStyle(family: "Arial", size: 10))) / 10
            guard unit > 0 else { return byHeight }
            let byWidth = Double(cell.rect.width) / unit
            return byWidth > byHeight * 0.4 && byWidth < byHeight * 1.6 ? max(5, byWidth) : byHeight
        }
        // Box heights jitter from line to line; lines of one size class share one size.
        var classes: [[Double]] = []
        for value in raw.sorted() { if let last = classes.last?.first, value <= last * 1.18 { classes[classes.count - 1].append(value) } else { classes.append([value]) } }
        func size(_ value: Double) -> Double {
            let group = classes.first { $0.contains(value) } ?? [value]
            return (group.sorted()[group.count / 2] * 2).rounded() / 2
        }
        // Split long OCR lines into separate cells when a wide gap between
        // words likely marks separate table columns. This lets textBlocks()
        // find column structure in the recognised text the same way it does
        // for native text, giving utility bills their columnar layout.
        var rows: [Row] = []
        for (cell, value) in zip(cells, raw) {
            let fontSize = size(value)
            let style = TextStyle(family: "Arial", size: fontSize, color: cell.color ?? "#1A1A1A")
            let words = cell.text.split(separator: " ", omittingEmptySubsequences: true)
            // A line with a single word or a very short line stays whole.
            if words.count <= 1 || cell.rect.width < fontSize * 4 {
                rows.append(Row(cells: [Cell(runs: [.init(text: cell.text, style: style)], bounds: cell.rect)]))
                continue
            }
            // Distribute the line's width proportionally to each word's
            // measured width, then split at gaps wider than 1.5x the font size
            // (the typical space between table columns in a bill).
            let measured = words.map { width(of: String($0), style: style) }
            let totalMeasured = measured.reduce(0, +)
            let spaceWidth = max(1, (cell.rect.width - totalMeasured) / CGFloat(words.count - 1))
            let columnGap = fontSize * 1.5
            var cells_: [Cell] = []
            var x = cell.rect.minX
            var group: [(text: String, x: CGFloat, w: CGFloat)] = []
            for (index, word) in words.enumerated() {
                let w = measured[index]
                if !group.isEmpty && spaceWidth > columnGap {
                    // Flush the accumulated group as one cell.
                    let text = group.map(\.text).joined(separator: " ")
                    let gx = group.first!.x, gw = x - gx - spaceWidth
                    cells_.append(Cell(runs: [.init(text: text, style: style)], bounds: CGRect(x: gx, y: cell.rect.minY, width: max(1, gw), height: cell.rect.height)))
                    group = []
                }
                group.append((String(word), x, w))
                x += w + spaceWidth
            }
            if !group.isEmpty {
                let text = group.map(\.text).joined(separator: " ")
                let gx = group.first!.x
                cells_.append(Cell(runs: [.init(text: text, style: style)], bounds: CGRect(x: gx, y: cell.rect.minY, width: max(1, cell.rect.maxX - gx), height: cell.rect.height)))
            }
            rows.append(Row(cells: cells_.isEmpty ? [Cell(runs: [.init(text: cell.text, style: style)], bounds: cell.rect)] : cells_))
        }
        return rows.sorted { $0.bounds.midY > $1.bounds.midY }
    }

    /// Drops glyphs drawn at an angle (stamps, seals, watermarks, rotated
    /// labels). They stay in the page artwork instead of becoming stray text.
    private static func horizontal(_ glyphs: [Cell], page: CGRect) -> [Cell] {
        guard glyphs.count > 1 else { return glyphs }
        var rotated = [Bool](repeating: false, count: glyphs.count)
        func breaks(_ a: Cell, _ b: Cell) -> Bool {
            let size = max(a.fontSize, b.fontSize)
            return b.bounds.midX < a.bounds.minX - 1 || abs(b.bounds.midY - a.bounds.midY) > size * 1.2 || b.bounds.minX - a.bounds.maxX > size * 3
        }
        func examine(_ range: Range<Int>) {
            let indices = range.filter { !glyphs[$0].text.trimmingCharacters(in: .whitespaces).isEmpty }
            guard indices.count >= 2 else { return }
            let size = indices.map { glyphs[$0].fontSize }.max() ?? 12
            let xs = indices.map { glyphs[$0].bounds.midX }, ys = indices.map { glyphs[$0].bounds.midY }
            let n = CGFloat(indices.count), mx = xs.reduce(0, +) / n, my = ys.reduce(0, +) / n
            var sxx: CGFloat = 0, sxy: CGFloat = 0
            for (x, y) in zip(xs, ys) { sxx += (x - mx) * (x - mx); sxy += (x - mx) * (y - my) }
            let slope = sxx > 1 ? sxy / sxx : 0
            let median = ys.sorted()[ys.count / 2]
            let off = ys.filter { abs($0 - median) > size * 0.3 }.count
            let tilted = abs(slope) > 0.07 || (indices.count >= 3 && CGFloat(off) > n * 0.5) || (indices.count == 2 && abs(ys[0] - ys[1]) > size * 0.5)
            if tilted { for i in range { rotated[i] = true } }
        }
        var start = 0, segments: [(Range<Int>, Bool)] = []
        for i in 1...glyphs.count where i == glyphs.count || breaks(glyphs[i - 1], glyphs[i]) {
            examine(start..<i)
            segments.append((start..<i, (start..<i).filter { !glyphs[$0].text.trimmingCharacters(in: .whitespaces).isEmpty }.count <= 1))
            start = i
        }
        // Characters set one by one around a seal (often upside down at the
        // bottom) form one-glyph segments; they belong with the rotated text
        // around them.
        var changed = true
        while changed {
            changed = false
            for (range, isolated) in segments where isolated && !rotated[range.lowerBound] {
                let near = (max(0, range.lowerBound - 2)..<min(glyphs.count, range.upperBound + 2)).contains { !range.contains($0) && rotated[$0] }
                if near { for i in range { rotated[i] = true }; changed = true }
            }
        }
        // A seal is a compact cluster of rotated characters. Short runs lying
        // wholly inside it (upside-down letters at the bottom of the ring)
        // are part of it; text lines crossing it are not. Page-wide clusters
        // such as diagonal watermarks are never used this way.
        var clusters: [(rect: CGRect, count: Int)] = []
        for i in glyphs.indices where rotated[i] && !glyphs[i].text.trimmingCharacters(in: .whitespaces).isEmpty {
            let rect = glyphs[i].bounds, reach = glyphs[i].fontSize * 2.5
            if let c = clusters.firstIndex(where: { $0.rect.insetBy(dx: -reach, dy: -reach).intersects(rect) }) { clusters[c].rect = clusters[c].rect.union(rect); clusters[c].count += 1 }
            else { clusters.append((rect, 1)) }
        }
        let seals = clusters.filter { $0.count >= 6 && $0.rect.width * $0.rect.height < page.width * page.height * 0.06 && max($0.rect.width, $0.rect.height) < min($0.rect.width, $0.rect.height) * 4 }.map { $0.rect.insetBy(dx: -2, dy: -2) }
        if !seals.isEmpty {
            for (range, _) in segments where !rotated[range.lowerBound] {
                let centres = range.filter { !glyphs[$0].text.trimmingCharacters(in: .whitespaces).isEmpty }.map { CGPoint(x: glyphs[$0].bounds.midX, y: glyphs[$0].bounds.midY) }
                if !centres.isEmpty, seals.contains(where: { seal in centres.allSatisfy { seal.contains($0) } }) { for i in range { rotated[i] = true } }
            }
        }
        return zip(glyphs, rotated).filter { !$0.1 }.map(\.0)
    }

    private static func rows(_ glyphs: [Cell]) -> [Row] {
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

    private static let listMarker = try! NSRegularExpression(pattern: "^\\s*(?:[•●○▪■◦\\-–*]|\\(?[0-9]{1,3}[.)]|[a-zA-Z][.)])\\s")

    private static func textBlocks(_ rows: [Row]) -> [Block] {
        func overlaps(_ a: Cell, _ b: Cell) -> Bool { a.bounds.minX < b.bounds.maxX - 2 && b.bounds.minX < a.bounds.maxX - 2 }
        func aligned(_ a: Row, _ b: Row) -> Bool {
            guard a.cells.count > 1, a.cells.count == b.cells.count,
                  a.bounds.midY - b.bounds.midY < max(a.fontSize, b.fontSize) * 2.5 + 12 else { return false }
            // Same column structure: each cell sits before the other row's next cell.
            let ordered = (0..<(a.cells.count - 1)).allSatisfy { max(a.cells[$0].bounds.minX, b.cells[$0].bounds.minX) < min(a.cells[$0 + 1].bounds.minX, b.cells[$0 + 1].bounds.minX) - 2 }
            return ordered && zip(a.cells, b.cells).allSatisfy { abs($0.bounds.minX - $1.bounds.minX) < 18 || abs($0.bounds.maxX - $1.bounds.maxX) < 12 || overlaps($0, $1) || $0.bounds.width < 40 || $1.bounds.width < 40 }
        }
        func bold(_ row: Row) -> Bool {
            let runs = row.cells.flatMap(\.runs), total = runs.map(\.text.count).reduce(0, +)
            return total > 0 && runs.filter(\.style.bold).map(\.text.count).reduce(0, +) * 2 > total
        }
        func listItem(_ row: Row) -> Bool {
            let text = row.cells.first?.text ?? ""
            return listMarker.firstMatch(in: text, range: NSRange(text.startIndex..., in: text)) != nil
        }
        var blocks: [Block] = [], index = 0
        while index < rows.count {
            if index + 1 < rows.count, aligned(rows[index], rows[index + 1]) {
                var table = [rows[index]]; index += 1
                while index < rows.count && aligned(table.last!, rows[index]) { table.append(rows[index]); index += 1 }
                // Long runs of wide prose cells are newspaper columns: they go to
                // the layout pass as lines, which rebuilds them as columns.
                let cells = table.flatMap(\.cells)
                let prose = table.count >= 3 && table[0].cells.count <= 3 && cells.filter { $0.bounds.width > 150 && $0.text.count > 30 }.count * 10 >= cells.count * 6
                if prose { blocks += table.flatMap { row in row.cells.map { .paragraph([Row(cells: [$0])]) } } }
                else { blocks.append(.table(table)) }
                continue
            }
            var paragraph = [rows[index]]; index += 1
            while index < rows.count, let previous = paragraph.last, previous.cells.count == 1, rows[index].cells.count == 1,
                  !listItem(rows[index]), bold(previous) == bold(rows[index]),
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

    /// Rows repeated near the top or bottom of most pages become the Word
    /// header and footer. A single-page document keeps everything in the body.
    private static func furniture(_ pages: [(box: CGRect, rows: [Row])]) -> Furniture {
        var result = Furniture()
        result.removed = Array(repeating: [], count: pages.count)
        // Labels, cards and other small pages have no running header/footer.
        guard pages.count >= 2, pages.allSatisfy({ $0.box.height >= 300 }) else { return result }
        func key(_ row: Row) -> String { row.text.replacingOccurrences(of: "[0-9]+", with: "#", options: .regularExpression).trimmingCharacters(in: .whitespaces) }
        let needed = max(2, Int((Double(pages.count) * 0.6).rounded(.up)))
        for band in [true, false] {
            var occurrences: [String: [(page: Int, row: Int, offset: CGFloat)]] = [:]
            for (pageIndex, page) in pages.enumerated() {
                for (rowIndex, row) in page.rows.enumerated() {
                    let offset = band ? page.box.maxY - row.bounds.maxY : row.bounds.minY - page.box.minY
                    guard offset >= 0, offset < page.box.height * (band ? 0.12 : 0.1) else { continue }
                    occurrences["\(key(row))|\(Int((offset / 6).rounded()))", default: []].append((pageIndex, rowIndex, offset))
                }
            }
            var chosen: [(row: Row, tokens: [Furniture.Token])] = []
            for (_, hits) in occurrences.sorted(by: { ($0.value.first?.offset ?? 0) < ($1.value.first?.offset ?? 0) }) where Set(hits.map(\.page)).count >= needed {
                let template = pages[hits[0].page].rows[hits[0].row]
                let samples = hits.map { (page: $0.page, numbers: digitGroups(pages[$0.page].rows[$0.row].text)) }
                let count = samples[0].numbers.count
                var tokens: [Furniture.Token] = []
                for position in 0..<count {
                    let values = samples.compactMap { position < $0.numbers.count ? ($0.page, $0.numbers[position]) : nil }
                    if values.count > 1 && values.allSatisfy({ $0.1 == $0.0 + 1 }) { tokens.append(.pageNumber) }
                    else if values.allSatisfy({ $0.1 == pages.count }) && tokens.contains(.pageNumber) { tokens.append(.pageCount) }
                    else { tokens.append(.literal) }
                }
                chosen.append((template, tokens))
                for hit in hits { result.removed[hit.page].insert(hit.row) }
            }
            chosen.sort { $0.row.bounds.midY > $1.row.bounds.midY }
            guard !chosen.isEmpty else { continue }
            let box = pages[0].box
            if band {
                result.header = chosen
                result.headerTop = max(0, chosen.map { box.maxY - $0.row.bounds.maxY }.min()! - 2)
                result.headerBottom = chosen.map { box.maxY - $0.row.bounds.minY }.max()!
            } else {
                result.footer = chosen
                result.footerBottom = max(0, chosen.map { $0.row.bounds.minY - box.minY }.min()! - 2)
                result.footerTop = chosen.map { $0.row.bounds.maxY - box.minY }.max()!
            }
        }
        return result
    }

    private static func digitGroups(_ text: String) -> [Int] {
        text.split(whereSeparator: { !($0.isASCII && $0.isNumber) }).compactMap { Int($0) }
    }

    private static func style(_ attributes: [NSAttributedString.Key: Any], readableOnWhite: Bool) -> TextStyle {
        let font = attributes[.font] as? NSFont ?? NSFont.systemFont(ofSize: 12)
        let traits = font.fontDescriptor.symbolicTraits
        let family = wordFamily((font.familyName ?? font.fontName).replacingOccurrences(of: "^[A-Z]{6}\\+", with: "", options: .regularExpression))
        let color = (attributes[.foregroundColor] as? NSColor)?.usingColorSpace(.deviceRGB)
        let hex = color.map { color -> String in
            let components = [color.redComponent, color.greenComponent, color.blueComponent].map { max(0, min(1, Double($0))) }
            if readableOnWhite {
                // Without the source's background behind it, light text would
                // vanish on the white output page.
                let linear = components.map { $0 <= 0.04045 ? $0 / 12.92 : pow(($0 + 0.055) / 1.055, 2.4) }
                let luminance = 0.2126 * linear[0] + 0.7152 * linear[1] + 0.0722 * linear[2]
                if 1.05 / (luminance + 0.05) < 3 { return "#202020" }
            }
            return String(format: "#%02X%02X%02X", Int(components[0] * 255), Int(components[1] * 255), Int(components[2] * 255))
        } ?? "#000000"
        let name = font.fontName.lowercased()
        let bold = traits.contains(.bold) || name.contains("bold") || name.contains("black") || name.contains("heavy") || name.contains("semibold")
        let italic = traits.contains(.italic) || name.contains("italic") || name.contains("oblique")
        return TextStyle(family: family, size: (Double(font.pointSize) * 10).rounded() / 10, bold: bold, italic: italic, underline: (attributes[.underlineStyle] as? NSNumber)?.intValue ?? 0 != 0, color: hex)
    }

    /// Apple's private UI font names (".SFNS…", "SF Pro") do not exist in Word.
    /// Arial keeps Helvetica-class metrics on both macOS and Windows.
    static func wordFamily(_ family: String) -> String {
        let name = family.trimmingCharacters(in: .whitespaces)
        if name.isEmpty || name.hasPrefix(".") || name.hasPrefix("SF ") || name.hasPrefix("SFPro") || name.hasPrefix("SFNS") || name.hasPrefix("System") || name.contains("AppleSystemUI") { return "Arial" }
        return name
    }

    private static func width(of text: String, style: TextStyle) -> CGFloat {
        var traits: NSFontTraitMask = []
        if style.bold { traits.insert(.boldFontMask) }
        if style.italic { traits.insert(.italicFontMask) }
        let font = NSFontManager.shared.font(withFamily: style.family, traits: traits, weight: 5, size: CGFloat(style.size)) ?? NSFont(name: "Helvetica", size: CGFloat(style.size)) ?? .systemFont(ofSize: CGFloat(style.size))
        return NSAttributedString(string: text, attributes: [.font: font]).size().width
    }

    // MARK: Artwork

    /// The page rendered as a picture with the editable text lifted out. Each
    /// glyph is removed only where a pixel matches that glyph's ink blended
    /// with the local background, so stamps, signatures and rules drawn over
    /// or under the text survive intact.
    private struct Artwork {
        var image: CGImage
        var scale: CGFloat
        var paper: String?
        var photographic: Bool
        /// Marked (non-paper) pixels per pixel row, to skip blank crops.
        var ink: [Int]
        var grids: [String: Grid] = [:]
        var pictures: [(rect: CGRect, image: CGImage)] = []
        var boxes: [Box] = []
        /// A horizontal band of the page, `top`/`bottom` measured in points from the page top.
        /// A horizontal slice of the artwork; an empty slice is skipped unless
        /// `always` (a coloured page, whose colour Word does not keep as a page
        /// background everywhere).
        func band(_ top: CGFloat, _ bottom: CGFloat, always: Bool = false) -> (data: Data, jpeg: Bool)? {
            let y0 = max(0, Int((top * scale).rounded(.down))), y1 = min(image.height, Int((bottom * scale).rounded(.up)))
            guard y1 - y0 > 2, always || ink[y0..<y1].reduce(0, +) * 2000 > (y1 - y0) * image.width / 7,
                  let crop = image.cropping(to: CGRect(x: 0, y: y0, width: image.width, height: y1 - y0)) else { return nil }
            let representation = NSBitmapImageRep(cgImage: crop)
            if photographic, let jpeg = representation.representation(using: .jpeg, properties: [.compressionFactor: 0.88]) { return (jpeg, true) }
            return representation.representation(using: .png, properties: [:]).map { ($0, false) }
        }
    }

    private static func artwork(_ page: PDFPage, ink: [(rect: CGRect, color: String?)], tables: [[Row]] = [], pictures: [CGRect] = [], placed: [CGRect] = [], contents: [CGRect] = [], photographic: Bool, token: CancellationToken) throws -> Artwork? {
        try token.check()
        guard let cgPage = page.pageRef else { return nil }
        let bounds = PDFOfficeCodec.visualBounds(page)
        // Photographic pages (scans, photos) get a higher render so lines,
        // QR codes and fine print stay sharp in the Word artwork.
        let scale: CGFloat = photographic ? 3.0 : 2.5
        let width = Int(ceil(bounds.width * scale)), height = Int(ceil(bounds.height * scale))
        guard width > 0, height > 0, width <= 12000, height <= 12000 else { return nil }
        var pixels = [UInt8](repeating: 255, count: width * height * 4)
        var paper: String?, rows = [Int](repeating: 0, count: height)
        var grids: [String: Grid] = [:], cut: [(rect: CGRect, image: CGImage)] = [], found: [Box] = []
        let image: CGImage? = pixels.withUnsafeMutableBytes { raw -> CGImage? in
            guard let context = CGContext(data: raw.baseAddress, width: width, height: height, bitsPerComponent: 8, bytesPerRow: width * 4, space: CGColorSpaceCreateDeviceRGB(), bitmapInfo: CGImageAlphaInfo.noneSkipLast.rawValue) else { return nil }
            context.setFillColor(CGColor(gray: 1, alpha: 1)); context.fill(CGRect(x: 0, y: 0, width: width, height: height))
            let transform = cgPage.getDrawingTransform(.cropBox, rect: CGRect(origin: .zero, size: bounds.size), rotate: 0, preserveAspectRatio: true)
            context.scaleBy(x: scale, y: scale); context.concatenate(transform); context.drawPDFPage(cgPage)
            let toPixels = transform.concatenating(CGAffineTransform(scaleX: scale, y: scale))
            let buffer = raw.bindMemory(to: UInt8.self)
            for item in ink { erase(buffer, width: width, height: height, rect: item.rect.applying(toPixels), color: item.color) }
            paper = paperColour(buffer, width: width, height: height)
            for table in tables {
                if let grid = grid(buffer, width: width, height: height, toPixels: toPixels, rows: table) { grids[tableKey(table)] = grid }
            }
            found = boxes(buffer, width: width, height: height, toPixels: toPixels, contents: contents, pictures: pictures + placed)
            if !pictures.isEmpty, let snapshot = context.makeImage() {
                for rect in pictures {
                    let r = rect.applying(toPixels).integral
                    guard let image = snapshot.cropping(to: CGRect(x: r.minX, y: CGFloat(height) - r.maxY, width: r.width, height: r.height)) else { continue }
                    cut.append((rect, image))
                    fill(buffer, width: width, height: height, rect: r.insetBy(dx: -1, dy: -1))
                }
            }
            for y in 0..<height {
                var count = 0
                for x in stride(from: 0, to: width, by: 7) { let i = (y * width + x) * 4; if Int(buffer[i]) + Int(buffer[i + 1]) + Int(buffer[i + 2]) < 3 * 243 { count += 1 } }
                rows[y] = count
            }
            return context.makeImage()
        }
        guard let image else { return nil }
        return Artwork(image: image, scale: scale, paper: paper, photographic: photographic, ink: rows, grids: grids, pictures: cut, boxes: found)
    }

    /// Square-cornered framed sections and full-width shaded bands that hold
    /// text. Each is found as runs of one colour across a pixel row: thin
    /// stacks are rules (a frame is a top and bottom rule joined by vertical
    /// rules at both ends), tall stacks are fills. A box is used only when it
    /// contains text and nothing else is drawn inside it (a logo, a stamp);
    /// used boxes are cleared from the artwork.
    private static func boxes(_ pixels: UnsafeMutableBufferPointer<UInt8>, width: Int, height: Int, toPixels: CGAffineTransform, contents: [CGRect], pictures: [CGRect]) -> [Box] {
        guard !contents.isEmpty else { return [] }
        func at(_ x: Int, _ y: Int) -> (Int, Int, Int) { let i = (y * width + x) * 4; return (Int(pixels[i]), Int(pixels[i + 1]), Int(pixels[i + 2])) }
        func distance(_ a: (Int, Int, Int), _ b: (Int, Int, Int)) -> Int { abs(a.0 - b.0) + abs(a.1 - b.1) + abs(a.2 - b.2) }
        let corners = [at(1, 1), at(width - 2, 1), at(1, height - 2), at(width - 2, height - 2)]
        let paper = (corners.map(\.0).sorted()[1], corners.map(\.1).sorted()[1], corners.map(\.2).sorted()[1])
        struct Run { var x0: Int, x1: Int, y0: Int, y1: Int, colour: (Int, Int, Int) }
        var open: [Run] = [], closed: [Run] = []
        let shortest = width / 5
        for y in 0..<height {
            var found: [(Int, Int, (Int, Int, Int))] = []
            var x = 0
            while x < width {
                let c = at(x, y)
                if distance(c, paper) <= 12 { x += 1; continue }
                var end = x
                while end + 1 < width && distance(at(end + 1, y), c) <= 24 { end += 1 }
                if end - x + 1 >= shortest { found.append((x, end, c)) }
                x = end + 1
            }
            var next: [Run] = []
            for run in found {
                if let i = open.firstIndex(where: { abs($0.x0 - run.0) <= 3 && abs($0.x1 - run.1) <= 3 && distance($0.colour, run.2) <= 24 }) {
                    var extended = open.remove(at: i); extended.y1 = y; next.append(extended)
                } else { next.append(Run(x0: run.0, x1: run.1, y0: y, y1: y, colour: run.2)) }
            }
            closed += open; open = next
        }
        closed += open
        // An anti-aliased rule spans two pixel rows of different tone: merge
        // touching thin runs with the same extent into one rule.
        var rules: [Run] = []
        // Near-paper thin runs are anti-aliasing and scanner noise, not rules
        // (light fills are tall and stay below).
        for run in closed.filter({ $0.y1 - $0.y0 + 1 <= 5 && distance($0.colour, paper) > 30 }).sorted(by: { $0.y0 < $1.y0 }) {
            if let i = rules.lastIndex(where: { run.y0 - $0.y1 <= 1 && abs($0.x0 - run.x0) <= 4 && abs($0.x1 - run.x1) <= 4 }), run.y1 - rules[i].y0 + 1 <= 6 {
                rules[i].y1 = max(rules[i].y1, run.y1); rules[i].x0 = min(rules[i].x0, run.x0); rules[i].x1 = max(rules[i].x1, run.x1)
                if run.colour.0 + run.colour.1 + run.colour.2 < rules[i].colour.0 + rules[i].colour.1 + rules[i].colour.2 { rules[i].colour = run.colour }
            } else { rules.append(run) }
        }
        // Rounded corners (and anything else not a plain rectangle) are left as artwork.
        // A rounded fill's straight middle stack has the same colour continuing
        // a few rows above or below it (the shorter corner rows); a rule or
        // anti-aliased edge right against a square fill does not.
        func continues(_ run: Run, _ y: Int) -> Bool { y >= 0 && y < height && distance(at((run.x0 + run.x1) / 2, y), run.colour) <= 24 }
        let fills = closed.filter { $0.y1 - $0.y0 + 1 >= 8 && distance(at($0.x0, $0.y0), $0.colour) <= 30 && distance(at($0.x1, $0.y1), $0.colour) <= 30 && !(continues($0, $0.y0 - 3) && continues($0, $0.y0 - 5)) && !(continues($0, $0.y1 + 3) && continues($0, $0.y1 + 5)) }
        func vertical(_ x: Int, _ y0: Int, _ y1: Int, _ colour: (Int, Int, Int)) -> Bool {
            for cx in max(0, x - 4)...min(width - 1, x + 4) {
                var hits = 0, total = 0
                for y in stride(from: y0, through: y1, by: 2) { total += 1; let c = at(cx, y); if distance(c, colour) <= 40 && distance(c, paper) > 30 { hits += 1 } }
                if hits * 100 >= total * 85 { return true }
            }
            return false
        }
        var frames: [(x0: Int, x1: Int, y0: Int, y1: Int, colour: (Int, Int, Int), stroke: Int)] = []
        var usedRules = Set<Int>()
        for (t, top) in rules.enumerated() where !usedRules.contains(t) {
            guard let b = rules.indices.first(where: { $0 > t && !usedRules.contains($0) && rules[$0].y0 > top.y1 + 8 && abs(rules[$0].x0 - top.x0) <= 4 && abs(rules[$0].x1 - top.x1) <= 4 && distance(rules[$0].colour, top.colour) <= 40 }) else { continue }
            let bottom = rules[b]
            // The inside must differ from the rule: otherwise these "rules" are
            // the rounded ends of a fill.
            guard vertical(top.x0, top.y1 + 1, bottom.y0 - 1, top.colour), vertical(top.x1, top.y1 + 1, bottom.y0 - 1, top.colour),
                  distance(at((top.x0 + top.x1) / 2, (top.y1 + bottom.y0) / 2), top.colour) > 40 || distance(at(top.x0 + 8, (top.y1 + bottom.y0) / 2), top.colour) > 40 else { continue }
            usedRules.insert(t); usedRules.insert(b)
            frames.append((min(top.x0, bottom.x0), max(top.x1, bottom.x1), top.y0, bottom.y1, top.colour, top.y1 - top.y0 + 1))
        }
        let inverse = toPixels.inverted()
        func page(_ x0: Int, _ y0: Int, _ x1: Int, _ y1: Int) -> CGRect {
            let a = CGPoint(x: CGFloat(x0), y: CGFloat(height - y0)).applying(inverse), b = CGPoint(x: CGFloat(x1 + 1), y: CGFloat(height - y1 - 1)).applying(inverse)
            return CGRect(x: min(a.x, b.x), y: min(a.y, b.y), width: abs(b.x - a.x), height: abs(b.y - a.y))
        }
        func hex(_ c: (Int, Int, Int)) -> String { String(format: "#%02X%02X%02X", c.0, c.1, c.2) }
        let scale = abs(toPixels.a) > 0 ? abs(toPixels.a) : 1
        var candidates: [(box: Box, pixels: (x0: Int, x1: Int, y0: Int, y1: Int), fills: [Run], dividers: [Run])] = []
        var claimed = Set<Int>()
        for frame in frames {
            let inner = fills.indices.filter { fills[$0].x0 >= frame.x0 - 2 && fills[$0].x1 <= frame.x1 + 2 && fills[$0].y0 >= frame.y0 - 2 && fills[$0].y1 <= frame.y1 + 2 }
            claimed.formUnion(inner)
            let panels = inner.map { (rect: page(fills[$0].x0, fills[$0].y0, fills[$0].x1, fills[$0].y1), color: hex(fills[$0].colour)) }
            // Separator rules spanning most of the box, away from its edges.
            let dividers = rules.filter { $0.y0 > frame.y0 + 6 && $0.y1 < frame.y1 - 6 && $0.x0 >= frame.x0 - 2 && $0.x1 <= frame.x1 + 2 && ($0.x1 - $0.x0) * 10 >= (frame.x1 - frame.x0) * 8 }
            var box = Box(rect: page(frame.x0, frame.y0, frame.x1, frame.y1), border: hex(frame.colour), stroke: max(0.25, CGFloat(frame.stroke) / scale), panels: panels)
            box.dividers = dividers.map { (y: page(0, $0.y0, 0, $0.y1).midY, color: hex($0.colour), weight: max(0.25, CGFloat($0.y1 - $0.y0 + 1) / scale)) }
            candidates.append((box, (frame.x0, frame.x1, frame.y0, frame.y1), inner.map { fills[$0] }, dividers))
        }
        // Shaded full-width bands without a frame.
        for (index, fill) in fills.enumerated() where !claimed.contains(index) && fill.x1 - fill.x0 >= width * 2 / 5 {
            let rect = page(fill.x0, fill.y0, fill.x1, fill.y1)
            candidates.append((Box(rect: rect, border: nil, stroke: 0, panels: [(rect, hex(fill.colour))]), (fill.x0, fill.x1, fill.y0, fill.y1), [fill], []))
        }
        var result: [Box] = []
        for candidate in candidates {
            // A frame around a photo or screenshot stays with the picture.
            guard contents.contains(where: { candidate.box.rect.insetBy(dx: -1, dy: -1).contains(CGPoint(x: $0.midX, y: $0.midY)) }),
                  !pictures.contains(where: { $0.intersects(candidate.box.rect.insetBy(dx: 2, dy: 2)) }) else { continue }
            let (x0, x1, y0, y1) = candidate.pixels
            var stray = 0, checked = 0
            // Anti-aliased fill edges and the box's own dividers are accounted for.
            var edgeRows = Set<Int>()
            for band in candidate.fills { for y in (band.y0 - 2)...(band.y0 + 1) { edgeRows.insert(y) }; for y in (band.y1 - 1)...(band.y1 + 2) { edgeRows.insert(y) } }
            for divider in candidate.dividers { for y in (divider.y0 - 2)...(divider.y1 + 2) { edgeRows.insert(y) } }
            for y in stride(from: y0 + 4, to: y1 - 3, by: 2) where !edgeRows.contains(y) {
                for x in stride(from: x0 + 4, to: x1 - 3, by: 2) {
                    let expected = candidate.fills.first { x >= $0.x0 && x <= $0.x1 && y >= $0.y0 && y <= $0.y1 }?.colour ?? paper
                    checked += 1
                    if distance(at(x, y), expected) > 60 { stray += 1 }
                }
            }
            guard checked > 0, stray * 300 < checked else { continue }
            fill(pixels, width: width, height: height, rect: CGRect(x: CGFloat(x0) - 2, y: CGFloat(height - 1 - y1) - 2, width: CGFloat(x1 - x0) + 4, height: CGFloat(y1 - y0) + 4))
            result.append(candidate.box)
        }
        return result
    }

    /// Paints a pixel rectangle (y-up pixel coordinates) with the paper
    /// colour found just outside it.
    private static func fill(_ pixels: UnsafeMutableBufferPointer<UInt8>, width: Int, height: Int, rect: CGRect) {
        let x0 = max(0, Int(rect.minX)), x1 = min(width - 1, Int(rect.maxX))
        let top = max(0, height - 1 - Int(rect.maxY)), bottom = min(height - 1, height - 1 - Int(rect.minY))
        guard x0 < x1, top < bottom else { return }
        var ring: [(Int, Int, Int)] = []
        func at(_ x: Int, _ y: Int) -> (Int, Int, Int) { let i = (y * width + x) * 4; return (Int(pixels[i]), Int(pixels[i + 1]), Int(pixels[i + 2])) }
        for x in stride(from: x0, through: x1, by: 3) { if top >= 3 { ring.append(at(x, top - 3)) }; if bottom + 3 < height { ring.append(at(x, bottom + 3)) } }
        for y in stride(from: top, through: bottom, by: 3) { if x0 >= 3 { ring.append(at(x0 - 3, y)) }; if x1 + 3 < width { ring.append(at(x1 + 3, y)) } }
        let paper = ring.isEmpty ? (255, 255, 255) : (ring.map(\.0).sorted()[ring.count / 2], ring.map(\.1).sorted()[ring.count / 2], ring.map(\.2).sorted()[ring.count / 2])
        for y in top...bottom { for x in x0...x1 { let i = (y * width + x) * 4; pixels[i] = UInt8(paper.0); pixels[i + 1] = UInt8(paper.1); pixels[i + 2] = UInt8(paper.2) } }
    }

    /// Finds the ruled grid of a table (text already lifted out): thin lines
    /// that differ from the pixels on both sides of them. Accepted only when
    /// the lines match the table's rows (and columns, when vertical rules
    /// exist) and nothing else is drawn inside; the table area is then
    /// cleared from the artwork and becomes real Word borders and fills.
    private static func grid(_ pixels: UnsafeMutableBufferPointer<UInt8>, width: Int, height: Int, toPixels: CGAffineTransform, rows: [Row]) -> Grid? {
        let cells = rows.flatMap(\.cells)
        guard let minX = cells.map(\.bounds.minX).min(), let maxX = cells.map(\.bounds.maxX).max() else { return nil }
        let pitch = Writer().rowHeight(rows, 0)
        let area = CGRect(x: minX - 16, y: rows[rows.count - 1].bounds.minY - pitch * 0.8, width: maxX - minX + 32, height: rows[0].bounds.maxY - rows[rows.count - 1].bounds.minY + pitch * 1.6).applying(toPixels)
        let x0 = max(5, Int(area.minX)), x1 = min(width - 6, Int(area.maxX))
        let top = max(5, height - 1 - Int(area.maxY)), bottom = min(height - 6, height - 1 - Int(area.minY))
        guard x1 - x0 > 20, bottom - top > 10 else { return nil }
        func at(_ x: Int, _ y: Int) -> (Int, Int, Int) { let i = (y * width + x) * 4; return (Int(pixels[i]), Int(pixels[i + 1]), Int(pixels[i + 2])) }
        func distance(_ a: (Int, Int, Int), _ b: (Int, Int, Int)) -> Int { abs(a.0 - b.0) + abs(a.1 - b.1) + abs(a.2 - b.2) }
        func lines(_ count: Int, along: Int, _ sample: (Int, Int) -> Bool) -> [(Int, Int)] {
            var hits: [Int] = []
            for position in 0..<count {
                var n = 0, total = 0
                for t in stride(from: 0, to: along, by: 2) { total += 1; if sample(position, t) { n += 1 } }
                if n * 10 >= total * 6 { hits.append(position) }
            }
            var groups: [(Int, Int)] = []
            for hit in hits { if let last = groups.last, hit - (last.0 + last.1 - 1) <= 1 { groups[groups.count - 1].1 += 1 } else { groups.append((hit, 1)) } }
            return groups
        }
        let horizontal = lines(bottom - top + 1, along: x1 - x0) { dy, dx in
            let y = top + dy, x = x0 + dx, p = at(x, y)
            return distance(p, at(x, y - 4)) > 30 && distance(p, at(x, y + 4)) > 30
        }
        let columnCount = rows[0].cells.count
        guard horizontal.count == rows.count + 1, let firstRule = horizontal.first, let lastRule = horizontal.last else { return nil }
        // The rules can run well past the text (a short last column): follow
        // each rule to its ends to find the table's real left and right edges.
        func ruled(_ x: Int, _ y: Int) -> Bool { let p = at(x, y); return distance(p, at(x, y - 4)) > 30 && distance(p, at(x, y + 4)) > 30 }
        func reach(_ line: (Int, Int), _ step: Int) -> Int {
            let y = top + line.0 + line.1 / 2
            var x = step < 0 ? x0 : x1, miss = 0
            while x + step > 4 && x + step < width - 5 && miss <= 3 { x += step; miss = ruled(x, y) ? 0 : miss + 1 }
            return x - step * miss
        }
        let tableLeft = horizontal.map { reach($0, -1) }.min()!, tableRight = horizontal.map { reach($0, 1) }.max()!
        // Vertical rules run between the top and bottom rules.
        let ruleTop = top + firstRule.0 + firstRule.1, ruleBottom = top + lastRule.0
        let scanLeft = max(5, tableLeft - 4), scanRight = min(width - 6, tableRight + 4)
        let vertical = ruleBottom - ruleTop < 6 ? [] : lines(scanRight - scanLeft + 1, along: ruleBottom - ruleTop) { dx, dy in
            let y = ruleTop + dy, x = scanLeft + dx, p = at(x, y)
            return distance(p, at(x - 4, y)) > 30 && distance(p, at(x + 4, y)) > 30
        }
        guard horizontal.allSatisfy({ $0.1 <= 8 }),
              vertical.isEmpty || vertical.count == columnCount + 1 || vertical.count == 2, vertical.allSatisfy({ $0.1 <= 8 }) else { return nil }
        let inverse = toPixels.inverted()
        func pageY(_ memoryRow: CGFloat) -> CGFloat { CGPoint(x: 0, y: CGFloat(height) - memoryRow).applying(inverse).y }
        func pageX(_ column: CGFloat) -> CGFloat { CGPoint(x: column, y: 0).applying(inverse).x }
        let rowLines = horizontal.map { pageY(CGFloat(top + $0.0) + CGFloat($0.1) / 2) }
        var columnLines: [CGFloat]
        if vertical.count == columnCount + 1 { columnLines = vertical.map { pageX(CGFloat(scanLeft + $0.0) + CGFloat($0.1) / 2) } }
        else {
            // No (or only outer) vertical rules: columns from the text, framed by
            // the outer rules when present, else by the horizontal rules' ends.
            let starts = (0..<columnCount).map { c in rows.map { $0.cells[c].bounds.minX }.min()! }
            let outerLeft = vertical.first.map { pageX(CGFloat(scanLeft + $0.0) + CGFloat($0.1) / 2) } ?? pageX(CGFloat(tableLeft))
            let outerRight = vertical.last.map { pageX(CGFloat(scanLeft + $0.0) + CGFloat($0.1) / 2) } ?? pageX(CGFloat(tableRight))
            columnLines = [outerLeft] + starts.dropFirst().map { $0 - 6 } + [outerRight]
        }
        // Rule colour and weight.
        var samples: [(Int, Int, Int)] = []
        for line in horizontal { for x in stride(from: tableLeft + 6, to: tableRight - 6, by: 5) { samples.append(at(x, top + line.0 + line.1 / 2)) } }
        guard !samples.isEmpty else { return nil }
        let rule = (samples.map(\.0).sorted()[samples.count / 2], samples.map(\.1).sorted()[samples.count / 2], samples.map(\.2).sorted()[samples.count / 2])
        let scale = toPixels.a == 0 ? 1 : abs(toPixels.a)
        let weight = CGFloat(horizontal.map(\.1).sorted()[horizontal.count / 2]) / scale
        // Cell shading: median of a few interior points per cell.
        let toMemory = { (point: CGPoint) -> (Int, Int) in let p = point.applying(toPixels); return (Int(p.x), height - 1 - Int(p.y)) }
        var fills: [[String?]] = []
        let paper = at(max(0, tableLeft - 6), max(0, top - 4))
        for r in 0..<rows.count {
            var row: [String?] = []
            for c in 0..<columnCount {
                var colours: [(Int, Int, Int)] = []
                for fy in [0.25, 0.5, 0.75] { for fx in [0.15, 0.5, 0.85] {
                    let point = CGPoint(x: columnLines[c] + (columnLines[c + 1] - columnLines[c]) * fx, y: rowLines[r] - (rowLines[r] - rowLines[r + 1]) * fy)
                    let (mx, my) = toMemory(point)
                    if mx > 0, mx < width, my > 0, my < height { colours.append(at(mx, my)) }
                } }
                guard !colours.isEmpty else { row.append(nil); continue }
                let median = (colours.map(\.0).sorted()[colours.count / 2], colours.map(\.1).sorted()[colours.count / 2], colours.map(\.2).sorted()[colours.count / 2])
                row.append(distance(median, (255, 255, 255)) > 12 && distance(median, paper) > 6 ? String(format: "#%02X%02X%02X", median.0, median.1, median.2) : nil)
            }
            fills.append(row)
        }
        // Anything else inside the table (a stamp, a logo) keeps the table in the artwork.
        let (left, upper) = toMemory(CGPoint(x: columnLines[0], y: rowLines[0])), (right, lower) = toMemory(CGPoint(x: columnLines[columnCount], y: rowLines[rows.count]))
        guard left < right, upper < lower else { return nil }
        var stray = 0, checked = 0
        let lineRows = Set(horizontal.flatMap { (top + $0.0 - 3)...(top + $0.0 + $0.1 + 2) })
        let lineColumns = Set(vertical.flatMap { (scanLeft + $0.0 - 3)...(scanLeft + $0.0 + $0.1 + 2) })
        for y in stride(from: upper + 3, to: lower - 3, by: 2) where !lineRows.contains(y) {
            let rowIndex = rowLines.lastIndex { pageY(CGFloat(y)) <= $0 } ?? 0
            for x in stride(from: left + 3, to: right - 3, by: 2) where !lineColumns.contains(x) {
                let columnIndex = max(0, min(columnCount - 1, (columnLines.lastIndex { pageX(CGFloat(x)) >= $0 } ?? 0)))
                let expected = fills[min(rowIndex, rows.count - 1)][columnIndex].flatMap { hex -> (Int, Int, Int)? in Int(hex.dropFirst(), radix: 16).map { (($0 >> 16) & 255, ($0 >> 8) & 255, $0 & 255) } } ?? paper
                checked += 1
                if distance(at(x, y), expected) > 60 { stray += 1 }
            }
        }
        guard checked > 0, stray * 400 < checked else { return nil }
        fill(pixels, width: width, height: height, rect: CGRect(x: CGFloat(left) - 4, y: CGFloat(height - 1 - lower) - 4, width: CGFloat(right - left) + 8, height: CGFloat(lower - upper) + 8))
        return Grid(columns: columnLines, rows: rowLines, vertical: vertical.count == columnCount + 1, border: String(format: "#%02X%02X%02X", rule.0, rule.1, rule.2), width: max(0.25, weight), fills: fills)
    }

    /// A tinted page (letterhead paper, coloured report pages) becomes the
    /// Word page colour; white pages leave it unset.
    private static func paperColour(_ pixels: UnsafeMutableBufferPointer<UInt8>, width: Int, height: Int) -> String? {
        var samples: [(Int, Int, Int)] = []
        for (fx, fy) in [(0.02, 0.02), (0.98, 0.02), (0.02, 0.98), (0.98, 0.98), (0.5, 0.01), (0.5, 0.99), (0.01, 0.5), (0.99, 0.5)] {
            let i = (Int(Double(height - 1) * fy) * width + Int(Double(width - 1) * fx)) * 4
            samples.append((Int(pixels[i]), Int(pixels[i + 1]), Int(pixels[i + 2])))
        }
        let first = samples[0]
        guard samples.allSatisfy({ abs($0.0 - first.0) + abs($0.1 - first.1) + abs($0.2 - first.2) < 12 }),
              first.0 + first.1 + first.2 < 3 * 250 else { return nil }
        return String(format: "#%02X%02X%02X", first.0, first.1, first.2)
    }

    private static func erase(_ pixels: UnsafeMutableBufferPointer<UInt8>, width: Int, height: Int, rect: CGRect, color: String?) {
        let x0 = max(0, Int(floor(rect.minX)) - 1), x1 = min(width - 1, Int(ceil(rect.maxX)) + 1)
        let top = max(0, height - 1 - Int(ceil(rect.maxY)) - 1), bottom = min(height - 1, height - 1 - Int(floor(rect.minY)) + 1)
        guard x0 < x1, top < bottom else { return }
        func pixel(_ x: Int, _ y: Int) -> (Int, Int, Int) { let i = (y * width + x) * 4; return (Int(pixels[i]), Int(pixels[i + 1]), Int(pixels[i + 2])) }
        // Local background: median of the ring just outside the glyph box.
        var ring: [(Int, Int, Int)] = []
        for x in stride(from: x0, through: x1, by: 2) {
            if top >= 2 { ring.append(pixel(x, top - 2)) }
            if bottom + 2 < height { ring.append(pixel(x, bottom + 2)) }
        }
        for y in stride(from: top, through: bottom, by: 2) {
            if x0 >= 2 { ring.append(pixel(x0 - 2, y)) }
            if x1 + 2 < width { ring.append(pixel(x1 + 2, y)) }
        }
        guard !ring.isEmpty else { return }
        func median(_ values: [Int]) -> Int { values.sorted()[values.count / 2] }
        let background = (median(ring.map(\.0)), median(ring.map(\.1)), median(ring.map(\.2)))
        var ink: (Int, Int, Int)
        if let color, color.count == 7, let value = Int(color.dropFirst(), radix: 16) {
            ink = ((value >> 16) & 255, (value >> 8) & 255, value & 255)
        } else {
            // Scanned text: the darkest tenth of the box is the ink colour.
            // Strongly coloured pixels (a stamp crossing the line) are left out.
            var box: [(Int, Int, Int)] = []
            for y in top...bottom { for x in x0...x1 { let c = pixel(x, y); if max(c.0, c.1, c.2) - min(c.0, c.1, c.2) < 60 { box.append(c) } } }
            if box.isEmpty { for y in top...bottom { for x in x0...x1 { box.append(pixel(x, y)) } } }
            box.sort { $0.0 + $0.1 + $0.2 < $1.0 + $1.1 + $1.2 }
            let dark = box.prefix(max(1, box.count / 10))
            ink = (dark.map(\.0).reduce(0, +) / dark.count, dark.map(\.1).reduce(0, +) / dark.count, dark.map(\.2).reduce(0, +) / dark.count)
        }
        let d = (ink.0 - background.0, ink.1 - background.1, ink.2 - background.2)
        let dd = d.0 * d.0 + d.1 * d.1 + d.2 * d.2
        guard dd > 900 else { return }
        // Scanned text (color == nil) has softer edges and anti-aliasing from
        // the camera; a wider tolerance erases the ink cleanly, leaving a
        // smooth background for the artwork layer behind the editable text.
        let tolerance = color == nil ? 85 * 85 : 48 * 48
        for y in top...bottom {
            var mask = [Bool](repeating: false, count: x1 - x0 + 1)
            for x in x0...x1 {
                let i = (y * width + x) * 4
                let v = (Int(pixels[i]) - background.0, Int(pixels[i + 1]) - background.1, Int(pixels[i + 2]) - background.2)
                let t = max(0, min(dd * 6 / 5, v.0 * d.0 + v.1 * d.1 + v.2 * d.2))
                let r = (v.0 * dd - t * d.0, v.1 * dd - t * d.1, v.2 * dd - t * d.2)
                // |v − (t/dd)·d|² < tolerance, kept in integers.
                mask[x - x0] = (r.0 * r.0 + r.1 * r.1 + r.2 * r.2) / dd < tolerance * dd
            }
            // Refill each run of ink from the clean pixels either side of it,
            // so a watermark, gradient or tint under the text continues.
            var x = 0
            while x < mask.count {
                guard mask[x] else { x += 1; continue }
                var end = x
                while end + 1 < mask.count && mask[end + 1] { end += 1 }
                // An edge pixel unlike the paper (a logo, a stamp) is not carried into the gap.
                func clean(_ c: (Int, Int, Int)) -> (Int, Int, Int) { abs(c.0 - background.0) + abs(c.1 - background.1) + abs(c.2 - background.2) > 90 ? background : c }
                let leftColour = x > 0 ? clean(pixel(x0 + x - 1, y)) : background, rightColour = end + 1 < mask.count ? clean(pixel(x0 + end + 1, y)) : background
                for k in x...end {
                    let f = Double(k - x + 1) / Double(end - x + 2), i = (y * width + x0 + k) * 4
                    pixels[i] = UInt8(Double(leftColour.0) * (1 - f) + Double(rightColour.0) * f)
                    pixels[i + 1] = UInt8(Double(leftColour.1) * (1 - f) + Double(rightColour.1) * f)
                    pixels[i + 2] = UInt8(Double(leftColour.2) * (1 - f) + Double(rightColour.2) * f)
                }
                x = end + 1
            }
        }
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
