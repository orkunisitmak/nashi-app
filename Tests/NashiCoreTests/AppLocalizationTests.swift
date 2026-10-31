import XCTest
import CoreGraphics
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
    func testQRFoundCountUsesFrenchSingularForZeroAndOne() {
        XCTAssertEqual(AppLocalization.text("0 found",language:"fr"),"0 trouvé")
        XCTAssertEqual(AppLocalization.text("1 found",language:"fr"),"1 trouvé")
        XCTAssertEqual(AppLocalization.text("2 found",language:"fr"),"2 trouvés")
        XCTAssertEqual(AppLocalization.text("1 found",language:"tr"),"1 bulundu")
        XCTAssertEqual(AppLocalization.text("12 found",language:"tr"),"12 bulundu")
    }
    /// Literals from Wheel.swift: the web-link petals and the empty-wheel centre (with the configured shortcut symbols).
    func testWheelWebPetalsAndEmptyWheelAreTranslated() {
        let petals=["Download photos…","Download video…","Download image…","Download…","Download thumbnail…","Audio MP3…","Copy link"]
        for language in ["tr","fr"] {
            for source in petals { XCTAssertNotEqual(AppLocalization.text(source,language:language),source,"\(language): \(source)") }
        }
        XCTAssertEqual(AppLocalization.uppercased(AppLocalization.text("Download video…",language:"tr"),language:"tr"),"VİDEOYU İNDİR…")
        XCTAssertEqual(AppLocalization.text("No formats\n⌘ Tools\n⌃ Actions",language:"tr"),"Biçim yok\n⌘ Araçlar\n⌃ İşlemler")
        XCTAssertEqual(AppLocalization.text("No file tools\n⇧ Convert\n⌃ Actions",language:"fr"),"Aucun outil de fichier\n⇧ Convertir\n⌃ Actions")
    }
    func testFrenchSelectedCountIsSingularForZeroAndOne() {
        // French uses the singular for 0 and 1 ("1 sélectionné"); Turkish never inflects after a number.
        XCTAssertEqual(AppLocalization.text("1 selected",language:"fr"),"1 sélectionné")
        XCTAssertEqual(AppLocalization.text("0 selected",language:"fr"),"0 sélectionné")
        XCTAssertEqual(AppLocalization.text("2 selected",language:"fr"),"2 sélectionnés")
        XCTAssertEqual(AppLocalization.text("1 selected",language:"tr"),"1 seçili")
        XCTAssertEqual(AppLocalization.text("1 selected",language:"en"),"1 selected")
    }
    /// Crop and redact batches report "<file name>: <message>". The message is translated; the file name, even one
    /// that contains ": " followed by catalogued copy, stays exactly as it is.
    func testFilePrefixedBatchErrorsTranslateOnlyTheMessage() throws {
        let directory=FileManager.default.temporaryDirectory.appendingPathComponent("nashi-l10n-\(UUID().uuidString)",isDirectory:true)
        try FileManager.default.createDirectory(at:directory,withIntermediateDirectories:true)
        defer { try? FileManager.default.removeItem(at:directory) }
        func image(_ width:Int,_ height:Int) -> CGImage {
            let context=CGContext(data:nil,width:width,height:height,bitsPerComponent:8,bytesPerRow:0,space:CGColorSpace(name:CGColorSpace.sRGB)!,bitmapInfo:CGImageAlphaInfo.premultipliedLast.rawValue)!
            context.setFillColor(CGColor(red:0.9,green:0.5,blue:0.1,alpha:1));context.fill(CGRect(x:0,y:0,width:width,height:height));return context.makeImage()!
        }
        let large=directory.appendingPathComponent("Large.png"),small=directory.appendingPathComponent("Scan.png: Preview unavailable: köşe.png")
        try ImageEngine.write(image(400,300),to:large);try ImageEngine.write(image(40,25),to:small)
        var options=OperationOptions();options.crop=EditRect(x:0,y:0,width:200,height:150);options.outputDirectory=directory
        XCTAssertThrowsError(try ConversionEngine.run(.init(inputs:[large,small],operation:.tool(.cropImage),options:options))) { error in
            let message=error.localizedDescription,name=small.lastPathComponent
            XCTAssertEqual(message,name+": The crop rectangle must fit completely inside the 40 × 25 pixel image.")
            XCTAssertEqual(AppLocalization.text(message,language:"tr"),name+": Kırpma alanı 40 × 25 piksellik görselin tamamen içinde kalmalı.")
            XCTAssertEqual(AppLocalization.text(message,language:"fr"),name+"\u{00A0}: Le rectangle de recadrage doit tenir entièrement dans l’image de 40 × 25 pixels.")
            XCTAssertEqual(AppLocalization.text(message,language:"en"),message)
        }
        XCTAssertEqual(AppLocalization.text("Report: v2.png: Choose an image.",language:"tr"),"Report: v2.png: Bir görsel seç.")
        // An untranslated message leaves everything, including copy inside the file name, unchanged.
        let untranslated="Scan.png: Preview unavailable: köşe.png: Could not resize image."
        XCTAssertEqual(AppLocalization.text(untranslated,language:"tr"),untranslated)
        // Without a file extension before ": ", or with more than a catalog message after it, nothing changes.
        for source in ["Output: Choose an image.","Version 1.2: Choose an image.","photo.png: Choose an image. Again"] {
            XCTAssertEqual(AppLocalization.text(source,language:"tr"),source)
        }
    }
}
