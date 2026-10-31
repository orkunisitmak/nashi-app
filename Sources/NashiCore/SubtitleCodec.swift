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
            return "WEBVTT\n\n" + cues.map { "\(timestamp($0.start, vtt: true)) --> \(timestamp($0.end, vtt: true))\($0.settings.isEmpty ? "" : " " + $0.settings)\n\(vttText($0.text))\n" }.joined(separator: "\n")
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
                // WebVTT ends cue text at the next timing line. SRT files that
                // omit the blank line also put the next cue's number before it.
                if timing.firstMatch(in: lines[index], range: NSRange(location: 0, length: (lines[index] as NSString).length)) != nil {
                    if text.last?.trimmingCharacters(in: .whitespaces).allSatisfy({ $0.isASCII && $0.isNumber }) == true { text.removeLast() }
                    break
                }
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
    /// WebVTT reads a bare "<" as the start of a tag and ends a cue at "-->",
    /// so SRT text keeps its markup tags and escapes everything else.
    private static func vttText(_ text: String) -> String {
        text.replacingOccurrences(of: #"<(?!/?[A-Za-z][^<>]*>)"#, with: "&lt;", options: .regularExpression)
            .replacingOccurrences(of: "-->", with: "--&gt;")
    }
    private static func srtText(_ text: String) -> String {
        let withoutTags = text.replacingOccurrences(of: #"</?(?:c(?:\.[^ >]*)?|v|lang|ruby|rt)(?:\s+[^>]*)?>|<\d{2}:\d{2}[^>]*>"#, with: "", options: .regularExpression)
        // WebVTT preserves no-break spaces and Unicode direction marks.
        let webVTT = withoutTags.replacingOccurrences(of: "&nbsp;", with: "\u{00A0}")
            .replacingOccurrences(of: "&lrm;", with: "\u{200E}")
            .replacingOccurrences(of: "&rlm;", with: "\u{200F}")
        return DocumentText.decodeEntities(webVTT, html: true)
    }
    private static func plainText(_ text: String) -> String {
        let withoutTags = text.replacingOccurrences(of: "<[^>]+>", with: "", options: .regularExpression)
        // Direction marks keep right-to-left lines readable in the exported text too.
        return DocumentText.decodeEntities(withoutTags.replacingOccurrences(of: "&lrm;", with: "\u{200E}").replacingOccurrences(of: "&rlm;", with: "\u{200F}"), html: true)
    }
}

enum DocumentText {
    static func read(_ url: URL) throws -> String {
        let data = try Data(contentsOf: url)
        // The UTF-32LE byte order mark begins with the UTF-16LE one.
        if data.starts(with: [0xff, 0xfe, 0, 0]) || data.starts(with: [0, 0, 0xfe, 0xff]) {
            guard let text = String(data: data, encoding: .utf32) else {
                throw NashiError.invalidInput("The UTF-32 text in \(url.lastPathComponent) is damaged.")
            }
            return text
        }
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
    /// `html` selects HTML character reference rules, which WebVTT cue text
    /// uses: every HTML named reference, not only the XML ones.
    static func decodeEntities(_ text: String, html: Bool = false) -> String {
        // One pass over the original text: a decoded "&" must never start
        // another reference ("&#38;lt;" is the literal text "&lt;").
        var result = text
        let named = ["nbsp": " ", "lt": "<", "gt": ">", "quot": "\"", "apos": "'", "amp": "&"]
        let pattern = try! NSRegularExpression(pattern: html ? #"&(#[xX][0-9A-Fa-f]+|#\d+|[A-Za-z][A-Za-z0-9]*);"# : #"&(#x[0-9A-Fa-f]+|#\d+|nbsp|lt|gt|quot|apos|amp);"#)
        for match in pattern.matches(in: result, range: NSRange(result.startIndex..., in: result)).reversed() {
            let name = (result as NSString).substring(with: match.range(at: 1))
            let replacement: String?
            if let value = named[name] { replacement = value }
            else if !name.hasPrefix("#") { replacement = htmlNamedReferences[name] }
            else {
                let number = name.dropFirst()
                let value = number.first == "x" || number.first == "X" ? UInt32(number.dropFirst(), radix: 16) : UInt32(number)
                replacement = html ? htmlNumericReference(value) : value.flatMap(UnicodeScalar.init).map { String($0) }
            }
            if let replacement, let range = Range(match.range, in: result) { result.replaceSubrange(range, with: replacement) }
        }
        return result
    }
    /// The HTML 4.01 named references (HTML values), as name:hex code point.
    private static let htmlNamedReferences: [String: String] = Dictionary(uniqueKeysWithValues: """
        quot:22 amp:26 lt:3C gt:3E nbsp:A0 iexcl:A1 cent:A2 pound:A3 curren:A4 yen:A5 brvbar:A6 sect:A7
        uml:A8 copy:A9 ordf:AA laquo:AB not:AC shy:AD reg:AE macr:AF deg:B0 plusmn:B1 sup2:B2 sup3:B3
        acute:B4 micro:B5 para:B6 middot:B7 cedil:B8 sup1:B9 ordm:BA raquo:BB frac14:BC frac12:BD frac34:BE
        iquest:BF Agrave:C0 Aacute:C1 Acirc:C2 Atilde:C3 Auml:C4 Aring:C5 AElig:C6 Ccedil:C7 Egrave:C8
        Eacute:C9 Ecirc:CA Euml:CB Igrave:CC Iacute:CD Icirc:CE Iuml:CF ETH:D0 Ntilde:D1 Ograve:D2
        Oacute:D3 Ocirc:D4 Otilde:D5 Ouml:D6 times:D7 Oslash:D8 Ugrave:D9 Uacute:DA Ucirc:DB Uuml:DC
        Yacute:DD THORN:DE szlig:DF agrave:E0 aacute:E1 acirc:E2 atilde:E3 auml:E4 aring:E5 aelig:E6
        ccedil:E7 egrave:E8 eacute:E9 ecirc:EA euml:EB igrave:EC iacute:ED icirc:EE iuml:EF eth:F0
        ntilde:F1 ograve:F2 oacute:F3 ocirc:F4 otilde:F5 ouml:F6 divide:F7 oslash:F8 ugrave:F9 uacute:FA
        ucirc:FB uuml:FC yacute:FD thorn:FE yuml:FF OElig:152 oelig:153 Scaron:160 scaron:161 Yuml:178
        fnof:192 circ:2C6 tilde:2DC Alpha:391 Beta:392 Gamma:393 Delta:394 Epsilon:395 Zeta:396 Eta:397
        Theta:398 Iota:399 Kappa:39A Lambda:39B Mu:39C Nu:39D Xi:39E Omicron:39F Pi:3A0 Rho:3A1 Sigma:3A3
        Tau:3A4 Upsilon:3A5 Phi:3A6 Chi:3A7 Psi:3A8 Omega:3A9 alpha:3B1 beta:3B2 gamma:3B3 delta:3B4
        epsilon:3B5 zeta:3B6 eta:3B7 theta:3B8 iota:3B9 kappa:3BA lambda:3BB mu:3BC nu:3BD xi:3BE
        omicron:3BF pi:3C0 rho:3C1 sigmaf:3C2 sigma:3C3 tau:3C4 upsilon:3C5 phi:3C6 chi:3C7 psi:3C8
        omega:3C9 thetasym:3D1 upsih:3D2 piv:3D6 ensp:2002 emsp:2003 thinsp:2009 zwnj:200C zwj:200D
        lrm:200E rlm:200F ndash:2013 mdash:2014 lsquo:2018 rsquo:2019 sbquo:201A ldquo:201C rdquo:201D
        bdquo:201E dagger:2020 Dagger:2021 bull:2022 hellip:2026 permil:2030 prime:2032 Prime:2033
        lsaquo:2039 rsaquo:203A oline:203E frasl:2044 euro:20AC image:2111 weierp:2118 real:211C trade:2122
        alefsym:2135 larr:2190 uarr:2191 rarr:2192 darr:2193 harr:2194 crarr:21B5 lArr:21D0 uArr:21D1
        rArr:21D2 dArr:21D3 hArr:21D4 forall:2200 part:2202 exist:2203 empty:2205 nabla:2207 isin:2208
        notin:2209 ni:220B prod:220F sum:2211 minus:2212 lowast:2217 radic:221A prop:221D infin:221E
        ang:2220 and:2227 or:2228 cap:2229 cup:222A int:222B there4:2234 sim:223C cong:2245 asymp:2248
        ne:2260 equiv:2261 le:2264 ge:2265 sub:2282 sup:2283 nsub:2284 sube:2286 supe:2287 oplus:2295
        otimes:2297 perp:22A5 sdot:22C5 lceil:2308 rceil:2309 lfloor:230A rfloor:230B lang:27E8 rang:27E9
        loz:25CA spades:2660 clubs:2663 hearts:2665 diams:2666
        """.split(whereSeparator: \.isWhitespace).map { entry -> (String, String) in
            let parts = entry.split(separator: ":")
            return (String(parts[0]), String(UnicodeScalar(UInt32(parts[1], radix: 16)!)!))
        })
    /// HTML numeric reference rules: 0x80-0x9F name windows-1252 characters,
    /// and NUL, surrogates and values past U+10FFFF become U+FFFD.
    private static func htmlNumericReference(_ value: UInt32?) -> String {
        let windows1252: [UInt32: UInt32] = [
            0x80: 0x20AC, 0x82: 0x201A, 0x83: 0x0192, 0x84: 0x201E, 0x85: 0x2026, 0x86: 0x2020, 0x87: 0x2021,
            0x88: 0x02C6, 0x89: 0x2030, 0x8A: 0x0160, 0x8B: 0x2039, 0x8C: 0x0152, 0x8E: 0x017D, 0x91: 0x2018,
            0x92: 0x2019, 0x93: 0x201C, 0x94: 0x201D, 0x95: 0x2022, 0x96: 0x2013, 0x97: 0x2014, 0x98: 0x02DC,
            0x99: 0x2122, 0x9A: 0x0161, 0x9B: 0x203A, 0x9C: 0x0153, 0x9E: 0x017E, 0x9F: 0x0178
        ]
        guard let value, value != 0, let scalar = UnicodeScalar(windows1252[value] ?? value) else { return "\u{FFFD}" }
        return String(scalar)
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
