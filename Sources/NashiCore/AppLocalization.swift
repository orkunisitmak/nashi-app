import Foundation

/// UI copy only. File names, document contents, identifiers and export parameters stay verbatim.
public enum AppLocalization {
    public static let supportedLanguages = ["en", "tr", "fr"]
    public static func normalizedLanguage(_ value: String?) -> String { supportedLanguages.contains(value ?? "") ? value! : "en" }
    public static func matches(_ source:String,query:String,language:String)->Bool {
        let locale=Locale(identifier:normalizedLanguage(language))
        // Pasted queries keep stray spaces; a blank query filters nothing, like an empty one.
        let needle=query.trimmingCharacters(in:.whitespacesAndNewlines)
        guard !needle.isEmpty else { return true }
        return [source,text(source,language:language)].contains { $0.range(of:needle,options:[.caseInsensitive,.diacriticInsensitive],locale:locale) != nil }
    }
    private static func loadCatalog(_ name:String) -> [String:String] {
        guard let url = Bundle.module.url(forResource: name, withExtension: "json", subdirectory: "Resources"),
              let data = try? Data(contentsOf: url),
              let values = try? JSONDecoder().decode([String:String].self, from: data) else { return [:] }
        return values
    }
    public static let turkish: [String: String] = loadCatalog("Turkish")
    public static let french: [String: String] = loadCatalog("French")
    /// Source-to-translation table for a non-English language; empty for English.
    public static func catalog(for language: String) -> [String: String] {
        switch normalizedLanguage(language) {
        case "tr": return turkish
        case "fr": return french
        default: return [:]
        }
    }
    /// Product and key names that keep Latin capitals in Turkish upper case (SHIFT, not SHİFT).
    public static let latinWords: Set<String> = {
        guard let url = Bundle.module.url(forResource: "LatinWords", withExtension: "json", subdirectory: "Resources"),
              let data = try? Data(contentsOf: url),
              let words = try? JSONDecoder().decode([String].self, from: data) else { return [] }
        return Set(words)
    }()
    /// Upper case for UI copy. Turkish words follow Turkish rules; names in `latinWords` do not.
    public static func uppercased(_ value:String, language:String) -> String {
        let latin = Locale(identifier:"en_US_POSIX")
        switch normalizedLanguage(language) {
        case "tr": break
        case "fr": return value.uppercased(with:Locale(identifier:"fr_FR"))
        default: return value.uppercased(with:latin)
        }
        let turkishLocale = Locale(identifier:"tr_TR")
        var result = "", word = ""
        func flush() {
            guard !word.isEmpty else { return }
            result += latinWords.contains(word) ? word.uppercased(with:latin) : word.uppercased(with:turkishLocale)
            word = ""
        }
        for character in value {
            if character.isLetter { word.append(character) } else { flush(); result.append(character) }
        }
        flush()
        return result
    }
    // A placeholder at the very start or end of a key that touches a word ("{0} files", "Convert to {0}")
    // stands for one token such as a count, format or version, so it may not capture text with spaces.
    // Mirrors windows/Nashi.Core/AppLocalization.cs.
    private static func wordBound(_ key: String, token: String) -> Bool {
        if key.hasPrefix(token), key.dropFirst(token.count).range(of:#"^ \p{L}"#, options:.regularExpression) != nil { return true }
        if key.hasSuffix(token), key.dropLast(token.count).range(of:#"\p{L} $"#, options:.regularExpression) != nil { return true }
        return false
    }
    private struct Template {
        let expression: NSRegularExpression
        let replacement: String
    }
    private struct Table {
        let entries: [String: String]
        let uppercase: [String: String]
        let templates: [Template]
        init(language: String) {
            entries = AppLocalization.catalog(for: language)
            uppercase = Dictionary(entries.filter { !$0.key.contains("{0}") }.map {
                ($0.key.uppercased(with: Locale(identifier:"en_US_POSIX")), AppLocalization.uppercased($0.value, language:language))
            }, uniquingKeysWith: { first, _ in first })
            templates = entries.filter { $0.key.contains("{0}") }.sorted { $0.key.count > $1.key.count }.compactMap { source, target in
                var pattern = NSRegularExpression.escapedPattern(for: source)
                for index in 0..<10 { pattern = pattern.replacingOccurrences(of: NSRegularExpression.escapedPattern(for:"{\(index)}"), with: AppLocalization.wordBound(source, token:"{\(index)}") ? #"(\S+?)"# : "(.*?)") }
                guard let expression = try? NSRegularExpression(pattern:"^"+pattern+"$",options:[.dotMatchesLineSeparators]) else { return nil }
                return Template(expression:expression,replacement:target)
            }
        }
    }
    private static let tables: [String: Table] = Dictionary(uniqueKeysWithValues: supportedLanguages.filter { $0 != "en" }.map { ($0, Table(language: $0)) })
    public static func text(_ source: String, language: String) -> String {
        guard let table = tables[normalizedLanguage(language)] else { return source }
        if let value = table.entries[source] ?? table.uppercase[source] { return value }
        guard source.count<16_384 else { return source }
        // Window titles: translate the title, keep the app suffix ("Convert to JPG · Nashi").
        let suffix = " · Nashi"
        if source.count > suffix.count, source.hasSuffix(suffix) { return text(String(source.dropLast(suffix.count)), language: language) + suffix }
        for template in table.templates {
            guard let match = template.expression.firstMatch(in:source,range:NSRange(source.startIndex...,in:source)) else { continue }
            let marker = try! NSRegularExpression(pattern:#"\{([0-9])\}"#)
            var result=template.replacement
            for token in marker.matches(in:template.replacement,range:NSRange(template.replacement.startIndex...,in:template.replacement)).reversed() {
                guard let indexRange=Range(token.range(at:1),in:template.replacement),let index=Int(template.replacement[indexRange]),
                      index+1<match.numberOfRanges,let valueRange=Range(match.range(at:index+1),in:source),
                      let replacementRange=Range(token.range,in:result) else { continue }
                result.replaceSubrange(replacementRange,with:source[valueRange])
            }
            return result
        }
        return source
    }
}
