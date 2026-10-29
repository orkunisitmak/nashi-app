import XCTest
@testable import NashiCore

final class AppLocalizationTests:XCTestCase {
    func testEveryToolAndWindowPlacementHasTurkishCopy() {
        for source in Array(FeatureCatalog.toolNames.values)+ToolKind.allCases.map(\.title)+WindowPlacement.allCases.map(\.title) {
            XCTAssertNotEqual(AppLocalization.text(source,language:"tr"),source,source)
        }
        XCTAssertEqual(AppLocalization.text("Language",language:"tr"),"Dil")
        XCTAssertEqual(AppLocalization.text("Library",language:"tr"),"Kitaplık")
        XCTAssertEqual(AppLocalization.text("READ QR CODES",language:"tr"),"QR KODLARINI OKU")
        XCTAssertEqual(AppLocalization.text("Resize Image".uppercased(),language:"tr"),"GÖRSELİ BOYUTLANDIR")
    }
    func testWordBoundTemplatesDoNotSwallowProseOrTitles() {
        XCTAssertNotEqual(AppLocalization.text("Add or change files",language:"tr"),"Add or change dosya")
        XCTAssertEqual(AppLocalization.text("3 files",language:"tr"),"3 dosya")
        XCTAssertEqual(AppLocalization.text("Convert to JPG",language:"tr"),"JPG biçimine dönüştür")
        XCTAssertEqual(AppLocalization.text("Convert to JPG · Nashi",language:"tr"),"JPG biçimine dönüştür · Nashi")
    }
    func testTurkishUppercaseKeepsLatinProductAndKeyNames() {
        XCTAssertEqual(AppLocalization.text("SHOW NASHI IN FINDER",language:"tr"),"NASHI'Yİ FINDER'DA GÖSTER")
        XCTAssertEqual(AppLocalization.uppercased("Shift'e bas",language:"tr"),"SHIFT'E BAS")
        XCTAssertEqual(AppLocalization.uppercased("Görseli seç",language:"tr"),"GÖRSELİ SEÇ")
        XCTAssertEqual(AppLocalization.uppercased("Shift",language:"en"),"SHIFT")
    }
    func testFallbackAndFormattingKeepOriginalInput() {
        XCTAssertEqual(AppLocalization.normalizedLanguage(nil),"en")
        XCTAssertEqual(AppLocalization.normalizedLanguage("fr"),"fr")
        XCTAssertEqual(AppLocalization.normalizedLanguage("de"),"en")
        XCTAssertEqual(AppLocalization.text("Library",language:"en"),"Library")
        XCTAssertTrue(AppLocalization.matches("Crop Image",query:"kırp",language:"tr"))
        XCTAssertTrue(AppLocalization.matches("Crop Image",query:"görsel",language:"tr"))
        XCTAssertEqual(AppLocalization.text("Home.png",language:"tr"),"Home.png")
        let path="Türkçe {0} $1 \\ image.png"
        XCTAssertEqual(AppLocalization.text("Original size: "+path,language:"tr"),"Orijinal boyut: "+path)
        XCTAssertEqual(AppLocalization.text("Nicely done. 12 files are ready.",language:"tr"),"Hazır! 12 dosyan hazır.")
    }
    func testCatalogPlaceholdersMatchWithoutChangingFormatsOrShortcuts() {
        let marker=try! NSRegularExpression(pattern:#"\{[0-9]\}"#)
        func tokens(_ value:String)->[String] { marker.matches(in:value,range:NSRange(value.startIndex...,in:value)).compactMap{Range($0.range,in:value).map{String(value[$0])}}.sorted() }
        for (source,translation) in AppLocalization.turkish {
            XCTAssertFalse(translation.isEmpty,source)
            XCTAssertEqual(tokens(source),tokens(translation),source)
        }
        for code in ["png","MP4","Ctrl+Shift","⌘S","00:01.250"] { XCTAssertEqual(AppLocalization.text(code,language:"tr"),code) }
    }
    func testTranslatedCatalogsOnlyUseTurkishSourceStrings() {
        let reference=Set(AppLocalization.turkish.keys)
        XCTAssertFalse(reference.isEmpty)
        for language in AppLocalization.supportedLanguages where language != "en" {
            let catalog=AppLocalization.catalog(for:language)
            XCTAssertTrue(Set(catalog.keys).isSubset(of:reference),"\(language) has keys that Turkish.json lacks")
            XCTAssertFalse(catalog.values.contains{$0.isEmpty},language)
        }
        XCTAssertTrue(AppLocalization.catalog(for:"en").isEmpty)
    }
    func testFrenchCopyPlaceholdersAndCasing() {
        let marker=try! NSRegularExpression(pattern:#"\{[0-9]\}"#)
        func tokens(_ value:String)->[String] { marker.matches(in:value,range:NSRange(value.startIndex...,in:value)).compactMap{Range($0.range,in:value).map{String(value[$0])}}.sorted() }
        for (source,translation) in AppLocalization.french { XCTAssertEqual(tokens(source),tokens(translation),source) }
        for source in Array(FeatureCatalog.toolNames.values)+ToolKind.allCases.map(\.title)+WindowPlacement.allCases.map(\.title) {
            XCTAssertNotEqual(AppLocalization.text(source,language:"fr"),source,source)
        }
        XCTAssertEqual(AppLocalization.text("Library",language:"fr"),"Bibliothèque")
        XCTAssertEqual(AppLocalization.text("Nicely done. 12 files are ready.",language:"fr"),"Bien joué. 12 fichiers sont prêts.")
        XCTAssertEqual(AppLocalization.text("Resize Image".uppercased(),language:"fr"),"REDIMENSIONNER L’IMAGE")
        XCTAssertEqual(AppLocalization.uppercased("Bibliothèque à jour",language:"fr"),"BIBLIOTHÈQUE À JOUR")
        XCTAssertEqual(AppLocalization.text("Original size: Français {0} $1 \\ image.png",language:"fr"),"Taille d’origine\u{00A0}: Français {0} $1 \\ image.png")
        XCTAssertEqual(AppLocalization.text("Home.png",language:"fr"),"Home.png")
        XCTAssertTrue(AppLocalization.matches("Crop Image",query:"recadrer",language:"fr"))
        for code in ["png","MP4","Ctrl+Shift","⌘S","00:01.250"] { XCTAssertEqual(AppLocalization.text(code,language:"fr"),code) }
    }
}
