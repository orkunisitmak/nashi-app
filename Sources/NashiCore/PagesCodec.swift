import Foundation
#if canImport(FoundationXML)
import FoundationXML
#endif
import JavaScriptCore
import Security
import UniformTypeIdentifiers

/// Native iWork interoperability. All parsing and writing happens inside a
/// fresh JavaScriptCore context over the independently acquired MIT library.
/// No installed office app, subprocess JavaScript runtime or network is used.
enum PagesCodec {
    static func create(fromDOCX input: URL, at output: URL, token: CancellationToken) throws {
        try token.check()
        let model = try PagesDOCXReader.read(input, token: token)
        let json = try JSONSerialization.data(withJSONObject: model, options: [.sortedKeys])
        let engine = try Runtime(token: token)
        let encoded = try engine.call("write", String(decoding: json, as: UTF8.self))
        guard let bytes = Data(base64Encoded: encoded), bytes.count > 100 else { throw NashiError.failed("The Pages writer returned an invalid document.") }
        try token.check()
        try bytes.write(to: output, options: .atomic)
    }

    static func model(_ input: URL, token: CancellationToken) throws -> [String: Any] {
        try token.check()
        guard let size = try input.resourceValues(forKeys: [.fileSizeKey]).fileSize, size <= 200_000_000 else { throw NashiError.invalidInput("This Pages file is too large or is not a regular document file.") }
        let data = try Data(contentsOf: input)
        guard data.count > 16, data.starts(with: [0x50, 0x4b]) else { throw NashiError.invalidInput("This is not a modern Pages document.") }
        let engine = try Runtime(token: token)
        let json = try engine.call("read", data.base64EncodedString())
        try token.check()
        guard let bytes = json.data(using: .utf8), let model = try JSONSerialization.jsonObject(with: bytes) as? [String: Any], model["blocks"] is [[String: Any]] else { throw NashiError.invalidInput("This Pages document could not be read.") }
        return model
    }

    static func materializeHTML(_ input: URL, in directory: URL, token: CancellationToken) throws -> URL {
        let value = try model(input, token: token)
        let html = try HTML.render(value, token: token)
        let output = directory.appendingPathComponent("Pages document.html")
        try html.write(to: output, atomically: true, encoding: .utf8)
        return output
    }

    private final class Runtime {
        private let context: JSContext
        private let token: CancellationToken
        private var diagnostic: String?

        init(token: CancellationToken) throws {
            self.token = token
            guard let context = JSContext() else { throw NashiError.failed("The native Pages runtime could not start.") }
            self.context = context
            context.exceptionHandler = { [weak self] _, exception in self?.diagnostic = exception?.toString() }
            let base64Encode: @convention(block) (String) -> String = { value in Data(value.utf16.map { UInt8(truncatingIfNeeded: $0) }).base64EncodedString() }
            let base64Decode: @convention(block) (String) -> String = { value in
                guard let data = Data(base64Encoded: value) else { return "" }
                return String(String.UnicodeScalarView(data.map { UnicodeScalar(UInt32($0))! }))
            }
            let utf8Encode: @convention(block) (String) -> String = { Data($0.utf8).base64EncodedString() }
            let utf8Decode: @convention(block) (String) -> String = { String(decoding: Data(base64Encoded: $0) ?? Data(), as: UTF8.self) }
            let random: @convention(block) (Int) -> String = { count in
                guard count >= 0, count <= 65_536 else { return "" }
                var bytes = [UInt8](repeating: 0, count: count)
                _ = SecRandomCopyBytes(kSecRandomDefault, count, &bytes)
                return Data(bytes).base64EncodedString()
            }
            let cancelled: @convention(block) () -> Bool = { token.isCancelled }
            for (name, block) in [("_nashiBase64Encode", base64Encode as AnyObject), ("_nashiBase64Decode", base64Decode as AnyObject), ("_nashiUTF8Encode", utf8Encode as AnyObject), ("_nashiUTF8Decode", utf8Decode as AnyObject), ("_nashiRandom", random as AnyObject), ("_nashiCheckCancelled", cancelled as AnyObject)] { context.setObject(block, forKeyedSubscript: name as NSString) }
            var candidates: [URL] = [Bundle.main.resourceURL?.appendingPathComponent("PagesInterop.js")].compactMap{$0}
            #if DEBUG
            let source = URL(fileURLWithPath: #filePath).deletingLastPathComponent().deletingLastPathComponent().deletingLastPathComponent().appendingPathComponent("Resources/PagesInterop.js")
            candidates.append(source)
            #endif
            guard let script = candidates.first(where: { FileManager.default.fileExists(atPath: $0.path) }) else { throw NashiError.missingDependency("the bundled native Pages library") }
            context.evaluateScript(try String(contentsOf: script, encoding: .utf8), withSourceURL: script)
            if let diagnostic { throw NashiError.failed("The native Pages runtime could not load: \(diagnostic)") }
        }

        func call(_ method: String, _ argument: String) throws -> String {
            try token.check()
            diagnostic = nil
            guard let function = context.objectForKeyedSubscript("NashiPages")?.objectForKeyedSubscript(method), !function.isUndefined,
                  let result = function.call(withArguments: [argument])?.toString(), diagnostic == nil else {
                try token.check()
                throw NashiError.invalidInput("The Pages document could not be \(method == "write" ? "created" : "read"): \(diagnostic ?? "invalid document data").")
            }
            try token.check()
            return result
        }
    }

    private enum HTML {
        static func render(_ model: [String: Any], token: CancellationToken) throws -> String {
            let setup = model["pageSetup"] as? [String: Any] ?? [:]
            let width = number(setup["pageWidth"], 595.28), height = number(setup["pageHeight"], 841.89)
            let margins = ["topMargin", "rightMargin", "bottomMargin", "leftMargin"].map { "\(number(setup[$0], 56.69))pt" }.joined(separator: " ")
            var content = ""
            if let header = model["header"] as? String, !header.isEmpty { content += "<header>\(escape(header))</header>" }
            for block in model["blocks"] as? [[String: Any]] ?? [] {
                try token.check()
                if block["type"] as? String == "table" { content += table(block); continue }
                let paragraph = block["paragraph"] as? [String: Any] ?? [:]
                var css = characterCSS(block["character"] as? [String: Any] ?? [:])
                if let alignment = paragraph["alignment"] as? Int, alignment >= 0 && alignment <= 4 { css += "text-align:\(["left", "right", "center", "justify", "start"][alignment]);" }
                for (name, property) in [("spaceBefore", "margin-top"), ("spaceAfter", "margin-bottom"), ("leftIndent", "margin-left"), ("rightIndent", "margin-right"), ("firstLineIndent", "text-indent")] { if let value = paragraph[name] as? NSNumber { css += "\(property):\(value.doubleValue)pt;" } }
                if paragraph["pageBreakBefore"] as? Bool == true { css += "page-break-before:always;" }
                if let line = paragraph["lineSpacing"] as? NSNumber, line.doubleValue > 0 { css += "line-height:\(line.doubleValue);" }
                var runs = ""
                for run in block["runs"] as? [[String: Any]] ?? [] {
                    if run["type"] as? String == "image", let data = run["data"] as? String, Data(base64Encoded: data) != nil {
                        let fileName = run["fileName"] as? String ?? "image.png"
                        let mime = UTType(filenameExtension: (fileName as NSString).pathExtension)?.preferredMIMEType ?? "image/png"
                        let dimensions = ["width", "height"].compactMap { name -> String? in guard let value = run[name] as? NSNumber, value.doubleValue > 0 else { return nil }; return "\(name):\(value.doubleValue)pt;" }.joined()
                        runs += "<img alt=\"\(escape(run["alt"] as? String ?? ""))\" src=\"data:\(mime);base64,\(data)\" style=\"\(dimensions)\"/>"
                    } else {
                        var text = escape(run["text"] as? String ?? "").replacingOccurrences(of: "\n", with: "<br/>").replacingOccurrences(of: "\t", with: "&#9;")
                        text = "<span style=\"\(escape(characterCSS(run["character"] as? [String: Any] ?? [:])))\">\(text)</span>"
                        if let link = run["link"] as? String, let scheme = URL(string: link)?.scheme?.lowercased(), ["https", "http", "mailto"].contains(scheme) { text = "<a href=\"\(escape(link))\">\(text)</a>" }
                        runs += text
                    }
                }
                content += "<p style=\"\(escape(css))\">\(runs.isEmpty ? "<br/>" : runs)</p>"
            }
            if let footer = model["footer"] as? String, !footer.isEmpty { content += "<footer>\(escape(footer))</footer>" }
            return "<!DOCTYPE html><html><head><meta charset=\"utf-8\"/><title>Pages document</title><style>@page{size:\(width)pt \(height)pt;margin:\(margins)}body{font:12pt Helvetica,Arial,sans-serif}p{white-space:pre-wrap;margin:0 0 8pt}table{border-collapse:collapse;page-break-inside:auto}td,th{border:1px solid #888;padding:4pt;white-space:pre-wrap}img{max-width:100%;height:auto}header,footer{margin:8pt 0}</style></head><body>\(content)</body></html>"
        }
        static func table(_ block: [String: Any]) -> String {
            let rows = block["rows"] as? [[[String: Any]]] ?? []
            let merges = block["merges"] as? [[String: Any]] ?? []
            let widths = block["columnWidths"] as? [NSNumber] ?? []
            var html = "<table><colgroup>" + widths.map { "<col style=\"width:\($0.doubleValue)pt\"/>" }.joined() + "</colgroup>"
            for (row, cells) in rows.enumerated() {
                html += "<tr>"
                for (column, cell) in cells.enumerated() {
                    if merges.contains(where: { merge in let r = merge["row"] as? Int ?? 0, c = merge["column"] as? Int ?? 0; return row >= r && row < r + (merge["rowCount"] as? Int ?? 1) && column >= c && column < c + (merge["columnCount"] as? Int ?? 1) && (row != r || column != c) }) { continue }
                    let merge = merges.first { ($0["row"] as? Int) == row && ($0["column"] as? Int) == column }
                    let attributes = " rowspan=\"\(merge?["rowCount"] as? Int ?? 1)\" colspan=\"\(merge?["columnCount"] as? Int ?? 1)\""
                    html += "<td\(attributes)>\(escape(cell["text"] as? String ?? "").replacingOccurrences(of: "\n", with: "<br/>"))</td>"
                }
                html += "</tr>"
            }
            return html + "</table>"
        }
        static func characterCSS(_ value: [String: Any]) -> String {
            var css = ""
            if let font = value["fontName"] as? String { css += "font-family:'\(font.replacingOccurrences(of: "'", with: "").replacingOccurrences(of: ";", with: ""))';" }
            if let size = value["fontSize"] as? NSNumber, size.doubleValue > 0 { css += "font-size:\(size.doubleValue)pt;" }
            if let bold = value["bold"] as? Bool { css += "font-weight:\(bold ? "bold" : "normal");" }
            if let italic = value["italic"] as? Bool { css += "font-style:\(italic ? "italic" : "normal");" }
            var decoration: [String] = []
            if number(value["underline"], 0) > 0 { decoration.append("underline") }
            if number(value["strikethru"], 0) > 0 { decoration.append("line-through") }
            if !decoration.isEmpty { css += "text-decoration:\(decoration.joined(separator: " "));" }
            if let color = value["fontColor"] as? [String: Any] { css += "color:rgb(\(Int(number(color["r"], 0) * 255)),\(Int(number(color["g"], 0) * 255)),\(Int(number(color["b"], 0) * 255)));" }
            return css
        }
        static func number(_ value: Any?, _ fallback: Double) -> Double { (value as? NSNumber)?.doubleValue ?? fallback }
        static func escape(_ value: String) -> String { DocumentText.escapeXML(value) }
    }
}

/// Reads the editable OOXML document structure produced by the local office
/// converter. Images are loaded only from inside the safely extracted package.
private enum PagesDOCXReader {
    static func read(_ input: URL, token: CancellationToken) throws -> [String: Any] {
        let work = try OutputFiles.temporaryDirectory()
        defer { try? FileManager.default.removeItem(at: work) }
        let package = work.appendingPathComponent("DOCX", isDirectory: true)
        try ArchiveCodec.extract(input, into: package, password: "", token: token)
        let document = try xml(package.appendingPathComponent("word/document.xml"))
        guard let body = document.rootElement()?.first("body") else { throw NashiError.invalidInput("This Word document has no editable body.") }
        let stylesDocument = try? xml(package.appendingPathComponent("word/styles.xml"))
        var styles: [String: XMLElement] = [:]
        for style in stylesDocument?.rootElement()?.elements("style") ?? [] {
            guard let id = style.attr("styleId") else { continue }
            guard styles[id] == nil else {
                throw NashiError.invalidInput("This Word document contains duplicate style identifiers. It could not be converted to Pages.")
            }
            styles[id] = style
        }
        let defaults = stylesDocument?.rootElement()?.first("docDefaults")
        let defaultCharacter = character(defaults?.first("rPrDefault")?.first("rPr"))
        let defaultParagraph = paragraph(defaults?.first("pPrDefault")?.first("pPr"))
        let relationships = try relationMap(package.appendingPathComponent("word/_rels/document.xml.rels"))
        let reader = Reader(package: package, relationships: relationships, styles: styles, defaultCharacter: defaultCharacter, defaultParagraph: defaultParagraph, token: token)
        var model: [String: Any] = ["blocks": try body.elements(nil).compactMap { try reader.block($0) }]
        if let section = body.first("sectPr") {
            var page: [String: Any] = [:]
            if let size = section.first("pgSz") { if let w = size.number("w") { page["pageWidth"] = w / 20 }; if let h = size.number("h") { page["pageHeight"] = h / 20 } }
            if let margin = section.first("pgMar") { for (attribute,property) in [("left","leftMargin"),("right","rightMargin"),("top","topMargin"),("bottom","bottomMargin")] { if let value = margin.number(attribute) { page[property] = value / 20 } } }
            model["pageSetup"] = page
            for (reference,property) in [("headerReference","header"),("footerReference","footer")] {
                if let id = section.first(reference)?.attr("id"), let target = relationships[id], let path = reader.localPath(target), let part = try? xml(path) { model[property] = part.rootElement()?.descendants("t").map(text).joined() }
            }
        }
        return model
    }

    private final class Reader {
        let package: URL, relationships: [String:String], styles: [String:XMLElement], defaultCharacter: [String:Any], defaultParagraph: [String:Any], token: CancellationToken
        init(package: URL, relationships: [String:String], styles: [String:XMLElement], defaultCharacter: [String:Any], defaultParagraph: [String:Any], token: CancellationToken) { self.package=package;self.relationships=relationships;self.styles=styles;self.defaultCharacter=defaultCharacter;self.defaultParagraph=defaultParagraph;self.token=token }

        func resolved(_ id: String?, depth: Int = 0) -> ([String:Any],[String:Any]) {
            guard depth < 16, let id, let style = styles[id] else { return (defaultCharacter, defaultParagraph) }
            let base = resolved(style.first("basedOn")?.attr("val"), depth:depth+1)
            return (base.0.merging(character(style.first("rPr"))) { _,new in new }, base.1.merging(paragraph(style.first("pPr"))) { _,new in new })
        }
        func block(_ element: XMLElement) throws -> [String: Any]? {
            try token.check()
            if element.local == "p" {
                let properties = element.first("pPr")
                let base = resolved(properties?.first("pStyle")?.attr("val"))
                var runs: [[String:Any]] = []
                func visit(_ item: XMLElement, link: String? = nil) throws {
                    try token.check()
                    if item.local == "r" {
                        let runBase = item.first("rPr")?.first("rStyle")?.attr("val").map {resolved($0).0} ?? [:]
                        let formatting = base.0.merging(runBase) { _,new in new }.merging(character(item.first("rPr"))) { _,new in new }
                        for child in item.elements(nil) {
                            if child.local == "t" { var run: [String:Any] = ["text":text(child),"character":formatting];if let link {run["link"]=link};runs.append(run) }
                            else if child.local == "tab" { runs.append(["text":"\t","character":formatting]) }
                            else if child.local == "br" || child.local == "cr" { runs.append(["text":"\n","character":formatting]) }
                            else if ["drawing","pict"].contains(child.local), let image = try image(child) { runs.append(image) }
                        }
                    } else if item.local == "hyperlink" {
                        let destination = item.attr("id").flatMap { relationships[$0] }
                        for child in item.elements(nil) { try visit(child, link:destination) }
                    } else if ["sdt","sdtContent","ins","smartTag"].contains(item.local) { for child in item.elements(nil) { try visit(child, link:link) } }
                }
                for child in element.elements(nil) { try visit(child) }
                // An empty paragraph has no run from which to recover its font.
                // Keep the paragraph mark's formatting, including tiny end
                // paragraphs that prevent a final table from adding a blank page.
                let paragraphCharacter = runs.isEmpty ? base.0.merging(character(properties?.first("rPr"))) { _,new in new } : base.0
                return ["type":"paragraph","runs":runs,"character":paragraphCharacter,"paragraph":base.1.merging(paragraph(properties)) { _,new in new }]
            }
            if element.local == "tbl" {
                var rows: [[[String:Any]]] = [], merges: [[String:Int]] = []
                var activeVertical: [Int:(Int,Int)] = [:]
                // A compact OOXML cell can otherwise request billions of empty
                // cells, or overflow the column index with a negative span.
                let maximumColumns = 1024
                for (rowIndex,row) in element.elements("tr").enumerated() {
                    var cells: [[String:Any]] = [], column = 0
                    for cell in row.elements("tc") {
                        let properties = cell.first("tcPr")
                        let span: Int
                        if let gridSpan = properties?.first("gridSpan") {
                            guard let raw = gridSpan.attr("val"), let value = Int(raw), value > 0, value <= maximumColumns else {
                                throw NashiError.invalidInput("This Word document contains an invalid or oversized table cell span. It could not be converted to Pages.")
                            }
                            span = value
                        } else { span = 1 }
                        guard column <= maximumColumns - span else {
                            throw NashiError.invalidInput("This Word document contains a table wider than the supported limit of \(maximumColumns) columns. It could not be converted to Pages.")
                        }
                        let value = cell.elements("p").map { paragraph in paragraph.descendants("t").map(text).joined() }.joined(separator:"\n")
                        var cellModel: [String:Any] = ["text":value]
                        if let fill = properties?.first("shd")?.attr("fill"), let color = color(fill) { cellModel["formatting"] = ["fill":["kind":"color","color":color]] }
                        cells.append(cellModel)
                        if span > 1 { for _ in 1..<span { cells.append(["text":""]) } }
                        if let vertical = properties?.first("vMerge") {
                            if vertical.attr("val") == "restart" { activeVertical[column] = (rowIndex,span) }
                            else if let origin = activeVertical[column] {
                                if let index = merges.firstIndex(where: {$0["row"] == origin.0 && $0["column"] == column}) { merges[index]["rowCount"] = rowIndex - origin.0 + 1 }
                                else { merges.append(["row":origin.0,"column":column,"rowCount":rowIndex-origin.0+1,"columnCount":origin.1]) }
                            }
                        } else { activeVertical[column] = nil }
                        if span > 1 && properties?.first("vMerge") == nil { merges.append(["row":rowIndex,"column":column,"rowCount":1,"columnCount":span]) }
                        column += span
                    }
                    rows.append(cells)
                }
                let widths = element.first("tblGrid")?.elements("gridCol").map { ($0.number("w") ?? 2400) / 20 } ?? []
                return ["type":"table","rows":rows,"merges":merges,"columnWidths":widths,"headerRows":element.elements("tr").prefix(while: {$0.first("trPr")?.first("tblHeader") != nil}).count]
            }
            if ["sdt","sdtContent"].contains(element.local) {
                // Block-level content controls are unwrapped by the caller's
                // recursive body collection in ordinary office exports.
                return nil
            }
            return nil
        }
        func localPath(_ relative: String) -> URL? {
            guard URL(string:relative)?.scheme == nil else { return nil }
            let path = package.appendingPathComponent("word").appendingPathComponent(relative).standardizedFileURL
            guard path.path.hasPrefix(package.standardizedFileURL.path + "/") else { return nil }
            return path
        }
        func image(_ element: XMLElement) throws -> [String:Any]? {
            let id = element.descendants("blip").first?.attr("embed") ?? element.descendants("imagedata").first?.attr("id")
            guard let id, let target=relationships[id],let file=localPath(target),let data=try? Data(contentsOf:file), data.count <= 100_000_000 else { return nil }
            var result: [String:Any] = ["type":"image","data":data.base64EncodedString(),"fileName":file.lastPathComponent]
            if let extent=element.descendants("extent").first { if let w=extent.number("cx") { result["width"]=w/12700 };if let h=extent.number("cy") {result["height"]=h/12700} }
            if let alt=element.descendants("docPr").first?.attr("descr") {result["alt"]=alt}
            return result
        }
    }
    static func xml(_ url: URL) throws -> XMLDocument {
        let bytes = try Data(contentsOf:url)
        guard bytes.count <= 50_000_000, !String(decoding:bytes,as:UTF8.self).contains("<!ENTITY"), !DocumentText.declaresXMLEntities(bytes) else {throw NashiError.invalidInput("The document XML is not supported.")}
        return try XMLDocument(data:bytes,options:[.nodePreserveWhitespace,.nodeLoadExternalEntitiesNever])
    }
    static func relationMap(_ url: URL) throws -> [String:String] {
        guard FileManager.default.fileExists(atPath:url.path) else {return [:]}
        var relationships: [String:String] = [:]
        for item in try xml(url).rootElement()?.elements("Relationship") ?? [] {
            guard let id = item.attr("Id"), let target = item.attr("Target") else { continue }
            guard relationships[id] == nil else {
                throw NashiError.invalidInput("This Word document contains duplicate relationship identifiers. It could not be converted to Pages.")
            }
            relationships[id] = target
        }
        return relationships
    }
    static func text(_ element: XMLElement) -> String {
        if let value=element.stringValue,!value.isEmpty{return value}
        let raw=element.xmlString
        guard let open=raw.firstIndex(of:">"),let close=raw.lastIndex(of:"<"),open<close else{return ""}
        return DocumentText.decodeEntities(String(raw[raw.index(after:open)..<close]))
    }
    static func character(_ element: XMLElement?) -> [String:Any] {
        guard let element else{return [:]};var result:[String:Any]=[:]
        for (name,key) in [("b","bold"),("i","italic")] {if let child=element.first(name){result[key] = !["0","false","off"].contains(child.attr("val") ?? "1")}}
        if let size=element.first("sz")?.number("val"){result["fontSize"]=size/2}
        if let font=element.first("rFonts")?.attr("ascii") ?? element.first("rFonts")?.attr("hAnsi"){result["fontName"]=font}
        if let value=element.first("color")?.attr("val"),let color=color(value){result["fontColor"]=color}
        if let value=element.first("highlight")?.attr("val"),let color=color(value){result["backgroundColor"]=color}
        if let underline=element.first("u"){result["underline"] = underline.attr("val") == "none" ? 0 : underline.attr("val") == "double" ? 2 : 1}
        if let strike=element.first("strike"){result["strikethru"] = strike.attr("val") == "0" ? 0 : 1}
        if let align=element.first("vertAlign")?.attr("val"){result["superscript"] = align == "superscript" ? 1 : align == "subscript" ? -1 : 0}
        if let language=element.first("lang")?.attr("val"){result["language"]=language}
        return result
    }
    static func paragraph(_ element:XMLElement?) -> [String:Any] {
        guard let element else{return [:]};var result:[String:Any]=[:]
        if let alignment=element.first("jc")?.attr("val"){result["alignment"]=["left":0,"start":0,"right":1,"end":1,"center":2,"both":3,"distribute":3][alignment] ?? 0}
        if let spacing=element.first("spacing"){if let before=spacing.number("before"){result["spaceBefore"]=before/20};if let after=spacing.number("after"){result["spaceAfter"]=after/20};if let line=spacing.number("line"),spacing.attr("lineRule") == nil || spacing.attr("lineRule") == "auto"{result["lineSpacing"]=line/240}}
        if let indent=element.first("ind"){for(name,key)in[("left","leftIndent"),("right","rightIndent"),("firstLine","firstLineIndent")]{if let value=indent.number(name){result[key]=value/20}};if let hanging=indent.number("hanging"){result["firstLineIndent"] = -hanging/20}}
        for(name,key)in[("keepNext","keepWithNext"),("keepLines","keepLinesTogether"),("pageBreakBefore","pageBreakBefore"),("widowControl","widowControl")]{if let child=element.first(name){result[key] = !["0","false","off"].contains(child.attr("val") ?? "1")}}
        return result
    }
    static func color(_ text:String) -> [String:Any]? {
        guard text.count == 6,let rgb=UInt32(text,radix:16)else{return nil}
        return ["r":Double((rgb>>16)&255)/255,"g":Double((rgb>>8)&255)/255,"b":Double(rgb&255)/255,"space":"srgb"]
    }
}

private extension XMLElement {
    var local:String {localName ?? name?.split(separator:":").last.map(String.init) ?? ""}
    func elements(_ name:String?) -> [XMLElement] {(children ?? []).compactMap{$0 as? XMLElement}.filter{name == nil || $0.local == name}}
    func first(_ name:String) -> XMLElement? {elements(name).first}
    func descendants(_ name:String) -> [XMLElement] {elements(nil).flatMap{($0.local == name ? [$0] : []) + $0.descendants(name)}}
    func attr(_ name:String) -> String? {(attributes ?? []).first{($0.localName ?? $0.name?.split(separator:":").last.map(String.init)) == name}?.stringValue}
    func number(_ name:String) -> Double? {attr(name).flatMap(Double.init)}
}
