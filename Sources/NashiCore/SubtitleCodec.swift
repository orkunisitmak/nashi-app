import Foundation

enum SubtitleCodec {
    struct Cue: Equatable {
        var start: Int64
        var end: Int64
        var text: String
        var settings: String = ""
    }

    static func convert(_ input: URL, to format: String, token: CancellationToken) throws -> String {
        try token.check()
        let source = try DocumentText.read(input)
        let cues = try parse(source)
        switch format {
        case "vtt":
            return "WEBVTT\n\n" + cues.map { "\(timestamp($0.start, vtt: true)) --> \(timestamp($0.end, vtt: true))\($0.settings.isEmpty ? "" : " " + $0.settings)\n\($0.text)\n" }.joined(separator: "\n")
        case "srt":
            return cues.enumerated().map { index, cue in
                "\(index + 1)\n\(timestamp(cue.start, vtt: false)) --> \(timestamp(cue.end, vtt: false))\n\(srtText(cue.text))\n"
            }.joined(separator: "\n")
        case "txt": return cues.map { plainText($0.text) }.joined(separator: "\n\n") + "\n"
        default: throw NashiError.unsupported("Cannot convert subtitles to \(format.uppercased()).")
        }
    }

    static func parse(_ source: String) throws -> [Cue] {
        let normalized = source.replacingOccurrences(of: "\u{feff}", with: "").replacingOccurrences(of: "\r\n", with: "\n").replacingOccurrences(of: "\r", with: "\n")
        let lines = normalized.components(separatedBy: "\n")
        let timing = try NSRegularExpression(pattern: #"^\s*((?:\d+:)?\d{2}:\d{2}[.,]\d{1,3})\s*-->\s*((?:\d+:)?\d{2}:\d{2}[.,]\d{1,3})(?:\s+(.*))?\s*$"#)
        var cues: [Cue] = [], index = 0
        while index < lines.count {
            let line = lines[index].trimmingCharacters(in: .whitespaces)
            if line == "NOTE" || line.hasPrefix("NOTE ") || line.hasPrefix("NOTE\t") || line == "STYLE" || line == "REGION" {
                index += 1
                while index < lines.count && !lines[index].trimmingCharacters(in: .whitespaces).isEmpty { index += 1 }
                continue
            }
            let ns = lines[index] as NSString
            guard let match = timing.firstMatch(in: lines[index], range: NSRange(location: 0, length: ns.length)) else { index += 1; continue }
            let start = try milliseconds(ns.substring(with: match.range(at: 1)))
            let end = try milliseconds(ns.substring(with: match.range(at: 2)))
            guard end > start else { throw NashiError.invalidInput("A subtitle cue ends before it starts.") }
            let settings = match.range(at: 3).location == NSNotFound ? "" : ns.substring(with: match.range(at: 3)).trimmingCharacters(in: .whitespaces)
            index += 1
            var text: [String] = []
            while index < lines.count && !lines[index].trimmingCharacters(in: .whitespaces).isEmpty {
                text.append(lines[index]); index += 1
            }
            cues.append(.init(start: start, end: end, text: text.joined(separator: "\n"), settings: settings))
        }
        guard !cues.isEmpty else { throw NashiError.invalidInput("No timed subtitle cues were found in this file.") }
        return cues
    }

    private static func milliseconds(_ value: String) throws -> Int64 {
        let parts = value.replacingOccurrences(of: ",", with: ".").split(separator: ":").map(String.init)
        guard parts.count == 2 || parts.count == 3 else { throw NashiError.invalidInput("Invalid subtitle timestamp: \(value)") }
        let seconds = parts.last!.split(separator: ".", omittingEmptySubsequences: false)
        guard seconds.count == 2, let sec = Int64(seconds[0]), let min = Int64(parts[parts.count - 2]),
              sec < 60, min < 60, let hour = parts.count == 3 ? Int64(parts[0]) : 0,
              let fraction = Int64(String(seconds[1]).padding(toLength: 3, withPad: "0", startingAt: 0)) else {
            throw NashiError.invalidInput("Invalid subtitle timestamp: \(value)")
        }
        let remainder = (min * 60 + sec) * 1000 + fraction
        guard hour <= (Int64.max - remainder) / 3_600_000 else {
            throw NashiError.invalidInput("This subtitle timestamp is too large to convert: \(value)")
        }
        return hour * 3_600_000 + remainder
    }
    private static func timestamp(_ value: Int64, vtt: Bool) -> String {
        let hour = value / 3_600_000, min = value / 60_000 % 60, sec = value / 1000 % 60, ms = value % 1000
        return String(format: "%02lld:%02lld:%02lld%@%03lld", hour, min, sec, vtt ? "." : ",", ms)
    }
    private static func srtText(_ text: String) -> String {
        let withoutTags = text.replacingOccurrences(of: #"</?(?:c(?:\.[^ >]*)?|v|lang|ruby|rt)(?:\s+[^>]*)?>|<\d{2}:\d{2}[^>]*>"#, with: "", options: .regularExpression)
        // WebVTT preserves no-break spaces and Unicode direction marks.
        let webVTT = withoutTags.replacingOccurrences(of: "&nbsp;", with: "\u{00A0}")
            .replacingOccurrences(of: "&lrm;", with: "\u{200E}")
            .replacingOccurrences(of: "&rlm;", with: "\u{200F}")
        return DocumentText.decodeEntities(webVTT)
    }
    private static func plainText(_ text: String) -> String {
        let withoutTags = text.replacingOccurrences(of: "<[^>]+>", with: "", options: .regularExpression)
        // Direction marks keep right-to-left lines readable in the exported text too.
        return DocumentText.decodeEntities(withoutTags.replacingOccurrences(of: "&lrm;", with: "\u{200E}").replacingOccurrences(of: "&rlm;", with: "\u{200F}"))
    }
}

enum DocumentText {
    static func read(_ url: URL) throws -> String {
        let data = try Data(contentsOf: url)
        // UTF-16 decoders can accept arbitrary pairs of legacy single-byte
        // characters. Only prefer UTF-16 when a BOM or a clear alternating
        // zero-byte pattern identifies it; otherwise a CP1252 document can
        // silently turn into unrelated CJK characters.
        if data.starts(with: [0xff, 0xfe]) || data.starts(with: [0xfe, 0xff]) {
            guard let text = String(data: data, encoding: .utf16) else {
                throw NashiError.invalidInput("The UTF-16 text in \(url.lastPathComponent) is damaged.")
            }
            return text
        }
        if data.count >= 4 && data.count.isMultiple(of: 2) {
            let bytes = data.prefix(128 * 1024)
            let pairs = bytes.count / 2
            var evenZeros = 0, oddZeros = 0
            for index in stride(from: 0, to: bytes.count, by: 2) {
                if bytes[index] == 0 { evenZeros += 1 }
                if bytes[index + 1] == 0 { oddZeros += 1 }
            }
            let encoding: String.Encoding?
            if oddZeros * 3 >= pairs && oddZeros > evenZeros * 2 { encoding = .utf16LittleEndian }
            else if evenZeros * 3 >= pairs && evenZeros > oddZeros * 2 { encoding = .utf16BigEndian }
            else { encoding = nil }
            if let encoding, let text = String(data: data, encoding: encoding) { return text }
        }
        for encoding: String.Encoding in [.utf8, .windowsCP1252, .macOSRoman] {
            if let text = String(data: data, encoding: encoding) { return text }
        }
        throw NashiError.invalidInput("The text encoding of \(url.lastPathComponent) could not be read.")
    }
    static func escapeXML(_ text: String) -> String {
        let valid = String(text.unicodeScalars.filter { $0.value == 9 || $0.value == 10 || $0.value == 13 || ($0.value >= 0x20 && $0.value != 0xfffe && $0.value != 0xffff) })
        return valid.replacingOccurrences(of: "&", with: "&amp;").replacingOccurrences(of: "<", with: "&lt;").replacingOccurrences(of: ">", with: "&gt;").replacingOccurrences(of: "\"", with: "&quot;").replacingOccurrences(of: "'", with: "&apos;")
    }
    static func decodeEntities(_ text: String) -> String {
        // One pass over the original text: a decoded "&" must never start
        // another reference ("&#38;lt;" is the literal text "&lt;").
        var result = text
        let named = ["nbsp": " ", "lt": "<", "gt": ">", "quot": "\"", "apos": "'", "amp": "&"]
        let pattern = try! NSRegularExpression(pattern: #"&(#x[0-9A-Fa-f]+|#\d+|nbsp|lt|gt|quot|apos|amp);"#)
        for match in pattern.matches(in: result, range: NSRange(result.startIndex..., in: result)).reversed() {
            let name = (result as NSString).substring(with: match.range(at: 1))
            let replacement: String?
            if let value = named[name] { replacement = value }
            else {
                let number = name.dropFirst()
                let value = number.first == "x" ? UInt32(number.dropFirst(), radix: 16) : UInt32(number)
                replacement = value.flatMap(UnicodeScalar.init).map { String($0) }
            }
            if let replacement, let range = Range(match.range, in: result) { result.replaceSubrange(range, with: replacement) }
        }
        return result
    }
    /// Reports whether XML data declares entities in its DTD. Only the prolog
    /// is read: parsing stops at the root element, before any expansion, and
    /// the parser detects UTF-16 itself, unlike a UTF-8 text search.
    static func declaresXMLEntities(_ data: Data) -> Bool {
        final class Gate: NSObject, XMLParserDelegate {
            var declared = false
            func parser(_ parser: XMLParser, foundInternalEntityDeclarationWithName name: String, value: String?) { declared = true; parser.abortParsing() }
            func parser(_ parser: XMLParser, foundExternalEntityDeclarationWithName name: String, publicID: String?, systemID: String?) { declared = true; parser.abortParsing() }
            func parser(_ parser: XMLParser, foundUnparsedEntityDeclarationWithName name: String, publicID: String?, systemID: String?, notationName: String?) { declared = true; parser.abortParsing() }
            func parser(_ parser: XMLParser, didStartElement elementName: String, namespaceURI: String?, qualifiedName: String?, attributes: [String: String] = [:]) { parser.abortParsing() }
        }
        let parser = XMLParser(data: data), gate = Gate()
        parser.shouldResolveExternalEntities = false
        parser.delegate = gate
        _ = parser.parse()
        return gate.declared
    }
}
