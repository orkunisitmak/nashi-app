import Foundation

/// UI copy only. File names, document contents, identifiers and export parameters stay verbatim.
public enum AppLocalization {
    public static let supportedLanguages = ["en", "tr"]
    public static func normalizedLanguage(_ value: String?) -> String { value == "tr" ? "tr" : "en" }
    public static func matches(_ source:String,query:String,language:String)->Bool {
        let locale=Locale(identifier:normalizedLanguage(language))
        return [source,text(source,language:language)].contains { $0.range(of:query,options:[.caseInsensitive,.diacriticInsensitive],locale:locale) != nil }
    }
    public static let turkish: [String: String] = {
        guard let url = Bundle.module.url(forResource: "Turkish", withExtension: "json", subdirectory: "Resources"),
              let data = try? Data(contentsOf: url),
              let values = try? JSONDecoder().decode([String:String].self, from: data) else { return [:] }
        return values
    }()
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
        guard normalizedLanguage(language) == "tr" else { return value.uppercased(with:latin) }
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
    private static let uppercase = Dictionary(turkish.filter { !$0.key.contains("{0}") }.map {
        ($0.key.uppercased(with: Locale(identifier:"en_US_POSIX")), uppercased($0.value, language:"tr"))
    }, uniquingKeysWith: { first, _ in first })
    private struct Template {
        let expression: NSRegularExpression
        let replacement: String
    }
    private static let templates: [Template] = turkish.filter { $0.key.contains("{0}") }.sorted { $0.key.count > $1.key.count }.compactMap { source, target in
        var pattern = NSRegularExpression.escapedPattern(for: source)
        for index in 0..<10 { pattern = pattern.replacingOccurrences(of: NSRegularExpression.escapedPattern(for:"{\(index)}"), with:"(.*?)") }
        guard let expression = try? NSRegularExpression(pattern:"^"+pattern+"$",options:[.dotMatchesLineSeparators]) else { return nil }
        return Template(expression:expression,replacement:target)
    }
    public static func text(_ source: String, language: String) -> String {
        guard normalizedLanguage(language) == "tr" else { return source }
        if let value = turkish[source] ?? uppercase[source] { return value }
        guard source.count<16_384 else { return source }
        for template in templates {
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
