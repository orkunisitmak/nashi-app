import Foundation

/// LibreOffice resolves external resource references inside office documents
/// at its own file permission level. The conversion sandbox only denies IP
/// networking, so an untrusted document that carries host-path references
/// (OOXML relationship targets, ODF href attributes or an `xml:base` that
/// re-anchors them) pulls local files into the export before conversion.
/// Office inputs are therefore staged with
/// those references removed; the converter then renders its standard missing
/// image placeholder. Package entries are rewritten without touching their
/// internal paths, so a freshly prepared file still converts identically.
enum OfficeInputSanitizer {
    /// Formats the converter reads through an explicit text import, so it never
    /// chooses a markup parser for them and no reference can be resolved.
    static let textFormats: Set<String> = ["txt", "md", "csv", "tsv"]

    /// Whether the staged copy of an input has to be inspected. For everything
    /// else the converter picks its parser from the content of the file, not
    /// from the extension it carries, so the extension cannot decide what is
    /// inspected: flat markup saved as `.doc` is still parsed as markup.
    static func inspects(_ format: String) -> Bool { !textFormats.contains(format) }

    static func sanitize(_ input: URL, output: URL, token: CancellationToken) throws {
        guard inspects(input.pathExtension.lowercased()) else {
            try FileManager.default.copyItem(at: input, to: output)
            return
        }
        // A package signature is unpacked as a package whatever extension it
        // carries; anything else is read the way the converter would read it.
        try stagePackage(input, output: output, token: token)
    }

    private static func stagePackage(_ input: URL, output: URL, token: CancellationToken) throws {
        let fm = FileManager.default
        guard isZIP(input) else { try stageFlat(input, output: output, token: token); return }
        let work = try OutputFiles.temporaryDirectory()
        defer { try? fm.removeItem(at: work) }
        let unpacked = work.appendingPathComponent("Package", isDirectory: true)
        try ArchiveCodec.extract(input, into: unpacked, token: token)
        try token.check()
        for part in markupParts(in: unpacked) {
            try token.check()
            let text = try DocumentText.read(part)
            let extensionName = part.pathExtension.lowercased()
            // A drawn SVG is the one markup part whose document type declaration is tolerated; see
            // `declaresAnExpandableDocumentType` for the narrow shape that survives and why.
            let refuses = extensionName == "svg" ? declaresAnExpandableDocumentType(text) : declaresDocumentType(text)
            guard !refuses else { throw NashiError.invalidInput("This document declares an XML document type (DOCTYPE), which Nashi does not convert.") }
            let clean = extensionName == "rels" ? stripRelationships(text) : stripReferences(text)
            if clean != text { try reencode(clean).write(to: part, atomically: true, encoding: .utf8) }
        }
        try token.check()
        try? fm.removeItem(at: output)
        let zip = URL(fileURLWithPath: "/usr/bin/zip")
        var packed: [String] = []
        if fm.fileExists(atPath: unpacked.appendingPathComponent("mimetype").path) {
            // ODF keeps a stored first entry, mirroring EPUBCodec.
            try ProcessRunner.run(zip, ["-q", "-X", "-0", output.path, "mimetype"], directory: unpacked, token: token)
            packed = ["mimetype"]
        }
        let entries = (try fm.contentsOfDirectory(atPath: unpacked.path)).filter { !packed.contains($0) }.sorted()
        guard !entries.isEmpty else { throw NashiError.invalidInput("This document package is empty.") }
        // Stored entry names are attacker controlled. A name such as "-TT…" or
        // "-x…" reaches /usr/bin/zip as an option, which silently drops parts
        // from the repacked document or fails the job. Relative paths cannot be
        // read as options, and zip strips the "./" prefix from the stored name.
        try ProcessRunner.run(zip, ["-q", "-X", "-r", output.path] + entries.map { "./" + $0 }, directory: unpacked, token: token)
    }

    /// The converter chooses the parser from the file content, not the extension:
    /// flat ODF markup saved under `.odt` has no ZIP signature and its
    /// `xlink:href` targets are still resolved. Markup is therefore stripped
    /// here as well; other signatures (legacy binary formats, mislabelled
    /// files) are copied untouched so the converter keeps its own detection.
    private static func stageFlat(_ input: URL, output: URL, token: CancellationToken) throws {
        let fm = FileManager.default
        func copyUnchanged() throws {
            try? fm.removeItem(at: output)
            try fm.copyItem(at: input, to: output)
        }
        // Deciding by reading as text costs a legacy binary or a large PDF the
        // whole file, and the converter only finds markup behind a declaration
        // or a root element, so the first bytes settle that question first.
        guard headSuggestsMarkup(input) else { try copyUnchanged(); return }
        guard let text = try? DocumentText.read(input), isXMLMarkup(text) else { try copyUnchanged(); return }
        guard !declaresDocumentType(text) else { throw NashiError.invalidInput("This document declares an XML document type (DOCTYPE), which Nashi does not convert.") }
        try token.check()
        let clean = stripReferences(text)
        guard clean != text else { try copyUnchanged(); return }
        try reencode(clean).write(to: output, atomically: true, encoding: .utf8)
    }

    /// The stripped text is written back as UTF-8, so a declaration naming
    /// another encoding (a UTF-16 flat document) would lie about the bytes that
    /// follow it and the converter would reject the staged file.
    private static let declaredEncoding = regex(#"^\s*<\?xml[^>]*?encoding\s*=\s*(["'])([^"']*)\1"#)

    /// Rewrites the declared encoding name in place; the rest of the declaration
    /// has to survive, otherwise the staged file is no longer markup.
    private static func reencode(_ text: String) -> String {
        var result = text
        let matches = declaredEncoding.matches(in: text, range: NSRange(text.startIndex..., in: text))
        for match in matches.reversed() {
            guard let value = Range(match.range(at: 2), in: text) else { continue }
            result.replaceSubrange(value, with: "UTF-8")
        }
        return result
    }

    /// A byte-order mark or whitespace may stand before the declaration, and
    /// UTF-16 stores an ASCII `<` beside a zero byte; behind anything else there
    /// is no markup for the converter to find. The exact answer still comes from
    /// `isXMLMarkup` once the file is decoded.
    private static func headSuggestsMarkup(_ input: URL) -> Bool {
        guard let handle = try? FileHandle(forReadingFrom: input), let head = try? handle.read(upToCount: 512) else { return false }
        for byte in head {
            switch byte {
            case 0x00, 0x09...0x0D, 0x20, 0xBB, 0xBF, 0xEF, 0xFE, 0xFF: continue
            case 0x3C: return true
            default: return false
            }
        }
        return false
    }

    private static func isZIP(_ url: URL) -> Bool {
        guard let handle = try? FileHandle(forReadingFrom: url), let head = try? handle.read(upToCount: 4) else { return false }
        return head == Data([0x50, 0x4B, 0x03, 0x04])
    }

    private static let markupHead = regex("^\\s*<\\s*[A-Za-z_?!]")

    private static func isXMLMarkup(_ text: String) -> Bool {
        markupHead.firstMatch(in: text, range: NSRange(text.startIndex..., in: text)) != nil
    }

    /// Every XML part of the package can carry a reference, not only the parts
    /// named `.xml`: an embedded `.svg` image resolves its own `xlink:href`, so
    /// a host path stored there reaches the converter unchanged.
    private static let markupExtensions: Set<String> = ["xml", "rels", "svg", "xhtml", "xht", "html"]

    private static func markupParts(in root: URL) -> [URL] {
        guard let walk = FileManager.default.enumerator(at: root, includingPropertiesForKeys: nil) else { return [] }
        return walk.compactMap { $0 as? URL }.filter { markupExtensions.contains($0.pathExtension.lowercased()) }
    }

    /// Replaces a host-path resource reference with a document-internal anchor
    /// the converter cannot resolve. Matches file:, absolute, home-relative and
    /// parent-directory escapes in href, any namespace-prefixed href and src
    /// attributes. XML permits single quotes and whitespace around `=`, and a
    /// prefixed name such as `x:href` is the same XLink reference to the
    /// converter, so all of those spellings are covered. The lookbehind keeps
    /// unrelated attributes such as `data-src` out of the rewrite.
    private static let referencePattern = regex("(?<![-_A-Za-z0-9:])((?:[A-Za-z_][-A-Za-z0-9._]*:)?(?:href|src))\\s*=\\s*(?:\"([^\"]*)\"|'([^']*)')")

    /// An `xml:base` attribute makes the converter resolve every relative
    /// reference below it against that base, so a host-path base smuggles
    /// otherwise innocent `href="kept.png"` targets out of the staged package.
    /// The prefix is fixed by XML (an unrelated `base` attribute is not an XML
    /// base), so only this exact name is considered, and only a base the
    /// converter would resolve outside the document is rewritten: a relative
    /// base such as `images/` describes the document's own layout and survives.
    private static let xmlBasePattern = regex("(?<![-_A-Za-z0-9:])(xml:base)\\s*=\\s*(?:\"([^\"]*)\"|'([^']*)')")

    private static func stripReferences(_ text: String) -> String {
        neutralise(xmlBasePattern, in: neutralise(referencePattern, in: text))
    }

    /// Rewrites each attribute a pattern names whose value the converter would
    /// resolve outside the staged document. The attribute name and its original
    /// quote style are restored, so the part stays parseable markup.
    ///
    /// A pattern is only searched inside a tag. An `href=` typed into a paragraph is
    /// document text, not an attribute, and rewriting it deletes the markup around
    /// it: an unquoted value match runs to the next quote in the file, which can sit
    /// past a `</text:p>` inside a later tag, so the replacement swallows that
    /// closing tag and the element that follows it and leaves the staged part
    /// unparseable. Tags are located first (see `tagRanges(in:)`) and the search is
    /// confined to them.
    private static func neutralise(_ pattern: NSRegularExpression, in text: String) -> String {
        var result = text
        let matches = tagRanges(in: text).flatMap { pattern.matches(in: text, range: NSRange($0, in: text)) }
        for match in matches.sorted(by: { $0.range.location > $1.range.location }) {
            guard match.numberOfRanges > 3, let name = substring(match.range(at: 1), in: text),
                  let found = attributeValue(match, in: text, doubleQuoted: 2, singleQuoted: 3),
                  isHostPath(found.value), let range = Range(match.range, in: result) else { continue }
            result.replaceSubrange(range, with: name + "=" + found.quote + "about:blank" + found.quote)
        }
        return result
    }

    /// The ranges of the tags in `text`, quoted values included. A tag starts at a
    /// `<` that begins a name or a markup declaration and ends at the first `>`
    /// outside a quoted attribute value; what sits between two tags is text and is
    /// never a candidate for a rewrite.
    ///
    /// Quote tracking only starts once a tag is open. A `"` sitting in prose would
    /// otherwise stay open across the next tag and hide its attributes from the
    /// patterns, which is a way to smuggle a host path past the sanitizer, and a tag
    /// cannot open inside a quoted value because XML forbids `<` there. The closing
    /// `>` of a value such as `href="a>b"` is quoted, so it cannot end the tag early
    /// either. Comments and CDATA hold text that may carry either bracket, so each is
    /// skipped as a unit. An unterminated tag runs to the end of the part so a
    /// truncated file is sanitised rather than passed through; a comment or CDATA
    /// that is never closed ends the scan, which is the same verdict the converter
    /// gives that part - it is not parseable markup, so there is no staged document
    /// for a reference to resolve against.
    private static func tagRanges(in text: String) -> [Range<String.Index>] {
        var ranges: [Range<String.Index>] = []
        var opening: String.Index?
        var quote: Character?
        var index = text.startIndex
        while index < text.endIndex {
            let character = text[index]
            if let open = quote {
                if character == open { quote = nil }
            } else if opening != nil && (character == "\"" || character == "'") {
                quote = character
            } else if character == ">", let start = opening {
                ranges.append(start..<text.index(after: index))
                opening = nil
            } else if character == "<", opening == nil {
                let marker = text[index...]
                if marker.hasPrefix("<![CDATA[") {
                    let body = text.index(index, offsetBy: "<![CDATA[".count)
                    guard let closing = text.range(of: "]]>", range: body..<text.endIndex) else { break }
                    index = closing.upperBound
                    continue
                }
                if marker.hasPrefix("<!--") {
                    let body = text.index(index, offsetBy: "<!--".count)
                    guard let closing = text.range(of: "-->", range: body..<text.endIndex) else { break }
                    index = closing.upperBound
                    continue
                }
                if opensTag(after: index, in: text) { opening = index }
            }
            index = text.index(after: index)
        }
        if let start = opening { ranges.append(start..<text.endIndex) }
        return ranges
    }

    /// A `<` opens a tag only when the character after it can begin a name or a
    /// markup declaration. A bare `<` that survived a sloppy exporter is text.
    private static func opensTag(after index: String.Index, in text: String) -> Bool {
        let next = text.index(after: index)
        guard next < text.endIndex else { return false }
        let character = text[next]
        return character.isLetter || character == "_" || character == "/" || character == "!" || character == "?"
    }

    /// A `<!DOCTYPE` declaration can carry an internal DTD subset that defines an
    /// entity - `<!ENTITY host "file:///tmp/secret.png">` referenced later as
    /// `xlink:href="&host;"`. `decodeCharacterReferences` deliberately leaves a
    /// named entity reference literal, so that value passes the host-path check
    /// here while the converter's parser expands it before resolving the
    /// reference. Real OOXML and ODF parts never carry a DOCTYPE, so a part that
    /// does is refused instead of sanitised. Only a declaration counts: `tagRanges`
    /// skips comments and CDATA and ignores a `<` that cannot open markup, so the
    /// same string written in a paragraph or inside a comment does not trip this.
    private static func declaresDocumentType(_ text: String) -> Bool {
        for range in tagRanges(in: text) where text[range].lowercased().hasPrefix("<!doctype") { return true }
        return false
    }

    /// A drawn `.svg` part does carry a document type declaration in the wild, and this converter will not
    /// load the part without one: measured with the app's own sandbox profile, `A&nbsp;B` inside `<text>` renders
    /// with the standard `<!DOCTYPE svg PUBLIC "-//W3C//DTD SVG 1.1//EN" …>` declaration and the same file
    /// reports "source file could not be loaded" once the declaration is removed. The declaration is therefore
    /// kept here, in this one part type, and only while it names nothing the converter could resolve: no
    /// internal subset (`[…]`), no `SYSTEM` keyword, no entity declaration, and no location other than the
    /// standard W3C SVG 1.1 DTD URL. The location rule is what catches `<!DOCTYPE svg PUBLIC
    /// "-//W3C//DTD SVG 1.1//EN" "file:///etc/x.dtd">` and a `"local.dtd"` shipped inside the package: a PUBLIC
    /// form carries its location as a plain quoted string, so the `SYSTEM` keyword test never sees it, and
    /// `stripReferences` does not look inside a declaration. The location is read by position - the second
    /// literal of the declaration - so nothing sitting in that slot is exempt for looking like an identifier,
    /// and a third literal is refused because a declaration has nothing else to quote. The public identifier
    /// itself is not compared, since it names the declaration rather than a place, and an export with a
    /// different identifier at the standard location still converts. That this
    /// converter reads no named DTD at all is measured too - a `SYSTEM "file:///…/evil.dtd"` declaration whose
    /// entity points at a local file failed to load rather than printing it - but nothing here leans on it, so
    /// a converter build this machine has not measured cannot widen the rule through it.
    private static func declaresAnExpandableDocumentType(_ text: String) -> Bool {
        for range in tagRanges(in: text) {
            let tag = text[range].lowercased()
            guard tag.hasPrefix("<!doctype") else { continue }
            if tag.contains("[") || tag.contains("system") || tag.contains("entity") { return true }
            let literals = quotedDeclarationLiterals(tag)
            // The accepted spellings are the two an export actually carries: a declaration with no quoted
            // literal, and a `PUBLIC` declaration whose location - its second literal, by position - is
            // the standard DTD. Position is what makes this a location check: a literal that begins with
            // `-//x.dtd` is a location when it sits in that slot, and exempting anything that looked like an
            // identifier would let such a literal name a file beside the drawing.
            if tag.contains("public") {
                // A `PUBLIC` declaration missing either of its literals is refused too. Measured without
                // one - an unquoted location and a declaration that carries only the identifier - the
                // converter reads no declaration at all: nothing is fetched and the drawing is silently
                // missing from the page. That is refused here rather than exported as a conversion with a
                // hole in it.
                if literals.count != 2 || literals[1] != standardSvgDtdLocation { return true }
            } else if !literals.isEmpty {
                return true
            }
        }
        return false
    }

    /// The one DTD location a `.svg` part may point at: the W3C SVG 1.1 DTD. Held lowercased because a
    /// declaration is matched case-insensitively - the canonical spelling is
    /// `http://www.w3.org/Graphics/SVG/1.1/DTD/svg11.dtd` and an export that capitalises a path segment
    /// differently still names the same remote file, never one on this machine.
    private static let standardSvgDtdLocation = "http://www.w3.org/Graphics/SVG/1.1/DTD/svg11.dtd".lowercased()

    /// Every quoted string of a lowercased declaration, public identifier and location alike, in order. A
    /// quote left open at the end of the tag yields an empty literal: such a declaration is not well-formed
    /// XML, and reporting it keeps an unfinished literal from hiding a location.
    private static func quotedDeclarationLiterals(_ tag: String) -> [String] {
        var literals: [String] = []
        var open: Character?
        var literal = ""
        for character in tag {
            if let quote = open {
                if character == quote {
                    literals.append(literal)
                    literal = ""
                    open = nil
                } else {
                    literal.append(character)
                }
            } else if character == #"""# || character == "'" {
                open = character
                literal = ""
            }
        }
        if open != nil { literals.append("") }
        return literals
    }

    /// Drops an OOXML relationship that points outside the package through a
    /// host path. Relative targets and hyperlinks stay: LibreOffice resolves
    /// them against the staged document only, and links are inert on import.
    private static let relationshipPattern = regex("<Relationship\\b(?:[^<>'\"]|\"[^\"]*\"|'[^']*')*(?:/>|>.*?</Relationship>)")
    private static let targetPattern = regex("Target\\s*=\\s*(?:\"([^\"]*)\"|'([^']*)')")
    private static let targetModePattern = regex("TargetMode\\s*=\\s*(?:\"([^\"]*)\"|'([^']*)')")

    private static func stripRelationships(_ text: String) -> String {
        var result = text
        let full = NSRange(text.startIndex..., in: text)
        for match in relationshipPattern.matches(in: text, range: full).reversed() {
            guard let hit = substring(match.range, in: result), isHostTarget(hit) else { continue }
            if let range = Range(match.range, in: result) { result.removeSubrange(range) }
        }
        return result
    }

    private static func isHostTarget(_ element: String) -> Bool {
        let full = NSRange(element.startIndex..., in: element)
        guard let mode = targetModePattern.firstMatch(in: element, range: full),
              let modeValue = attributeValue(mode, in: element, doubleQuoted: 1, singleQuoted: 2),
              isExternalMode(modeValue.value),
              let found = targetPattern.firstMatch(in: element, range: full),
              let value = attributeValue(found, in: element, doubleQuoted: 1, singleQuoted: 2) else { return false }
        return isHostPath(value.value)
    }

    /// `Ext&#101;rnal` is `External` to the converter: it decodes character
    /// references in every attribute value before it reads them, as it does for
    /// the target, and the token type of the attribute ignores the encoding, the
    /// case and the surrounding whitespace.
    private static func isExternalMode(_ value: String) -> Bool {
        decodeCharacterReferences(value).trimmingCharacters(in: .whitespacesAndNewlines).lowercased() == "external"
    }

    /// A value the converter resolves outside the staged document. The converter
    /// trims surrounding whitespace before it uses a target, so that is dropped
    /// here too; drive and UNC roots are absolute on the Windows build.
    private static func isHostPath(_ value: String) -> Bool {
        let target = decodeCharacterReferences(value).trimmingCharacters(in: .whitespacesAndNewlines).lowercased()
        if target.hasPrefix("file:") || target.hasPrefix("/") || target.hasPrefix("~") { return true }
        if target.hasPrefix(#"\\"#) || isDriveRoot(target) { return true }
        return target.contains("../") || target.contains("..\\")
    }

    private static func isDriveRoot(_ target: String) -> Bool {
        let characters = Array(target)
        guard characters.count >= 3, characters[0].isLetter, characters[1] == ":" else { return false }
        return characters[2] == "/" || characters[2] == "\\"
    }

    /// Reads the value of a `("…")|('…')` attribute alternation together with
    /// the quote character that has to be restored.
    private static func attributeValue(_ match: NSTextCheckingResult, in text: String, doubleQuoted: Int, singleQuoted: Int) -> (value: String, quote: String)? {
        if let value = substring(match.range(at: doubleQuoted), in: text) { return (value, "\"") }
        if let value = substring(match.range(at: singleQuoted), in: text) { return (value, "'") }
        return nil
    }

    /// The converter resolves character references before it uses an attribute
    /// value, so `&#102;ile:/…` is a `file:` target there even though the letter
    /// `f` never appears in this file. Only the XML predefined references and
    /// the two XML whitespace references are resolved; anything else is literal
    /// text that a parser would reject outright.
    private static let characterReferencePattern = regex("&#([0-9]{1,7});|&#[xX]([0-9A-Fa-f]{1,6});|&(amp|lt|gt|quot|apos|Tab|NewLine);")

    private static func decodeCharacterReferences(_ value: String) -> String {
        guard value.contains("&") else { return value }
        var result = ""
        var cursor = value.startIndex
        let full = NSRange(value.startIndex..., in: value)
        for match in characterReferencePattern.matches(in: value, range: full) {
            guard let span = Range(match.range, in: value) else { continue }
            result += value[cursor..<span.lowerBound]
            result += resolvedCharacter(match, in: value) ?? String(value[span])
            cursor = span.upperBound
        }
        result += value[cursor...]
        return result
    }

    private static func resolvedCharacter(_ match: NSTextCheckingResult, in text: String) -> String? {
        if let digits = substring(match.range(at: 1), in: text), let code = UInt32(digits), let scalar = Unicode.Scalar(code) { return String(Character(scalar)) }
        if let hex = substring(match.range(at: 2), in: text), let code = UInt32(hex, radix: 16), let scalar = Unicode.Scalar(code) { return String(Character(scalar)) }
        switch substring(match.range(at: 3), in: text) {
        case "amp": return "&"
        case "lt": return "<"
        case "gt": return ">"
        case "quot": return "\""
        case "apos": return "'"
        case "Tab": return "\t"
        case "NewLine": return "\n"
        default: return nil
        }
    }

    private static func substring(_ range: NSRange, in text: String) -> String? {
        Range(range, in: text).map { String(text[$0]) }
    }

    private static func regex(_ pattern: String) -> NSRegularExpression {
        guard let value = try? NSRegularExpression(pattern: pattern, options: [.caseInsensitive, .dotMatchesLineSeparators]) else {
            preconditionFailure("invalid sanitizer pattern")
        }
        return value
    }
}
