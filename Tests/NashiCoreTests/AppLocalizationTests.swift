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

    func testNotchPlacementUsesCameraSafeAreaAndCentersOnScreen() {
        let screen=NotchScreenMetrics(frame:CGRect(x:0,y:0,width:1512,height:982),visibleFrame:CGRect(x:0,y:0,width:1512,height:958),safeAreaTop:37,auxiliaryTopLeftArea:CGRect(x:0,y:945,width:660,height:37),auxiliaryTopRightArea:CGRect(x:852,y:945,width:660,height:37))
        let placement=NotchPanelGeometry.placement(on:screen,size:CGSize(width:500,height:300))
        XCTAssertTrue(placement.isNotched)
        XCTAssertEqual(placement.notchWidth,192)
        XCTAssertEqual(placement.frame,CGRect(x:506,y:645,width:500,height:300))
    }

    func testNotchlessPlacementSitsBelowVisibleMenuBarArea() {
        let screen=NotchScreenMetrics(frame:CGRect(x:0,y:0,width:1440,height:900),visibleFrame:CGRect(x:0,y:0,width:1440,height:875),safeAreaTop:0,auxiliaryTopLeftArea:nil,auxiliaryTopRightArea:nil)
        let placement=NotchPanelGeometry.placement(on:screen,size:CGSize(width:500,height:300))
        XCTAssertFalse(placement.isNotched)
        XCTAssertEqual(placement.notchWidth,0)
        XCTAssertEqual(placement.frame,CGRect(x:470,y:569,width:500,height:300))
    }

    func testInvalidAuxiliaryAreasFallBackToNotchlessPlacement() {
        let screen=NotchScreenMetrics(frame:CGRect(x:0,y:0,width:1200,height:800),visibleFrame:CGRect(x:0,y:0,width:1200,height:776),safeAreaTop:32,auxiliaryTopLeftArea:CGRect(x:0,y:768,width:600,height:32),auxiliaryTopRightArea:CGRect(x:590,y:768,width:610,height:32))
        XCTAssertFalse(NotchPanelGeometry.placement(on:screen,size:CGSize(width:400,height:200)).isNotched)
    }

    func testHoverCollapseUsesTheSpecifiedDelay() {
        XCTAssertEqual(NotchInteractionPolicy.collapseDelayMilliseconds,250)
    }

    func testFocusTimerPausesCompletesAndNeverGoesBelowZero() {
        var timer=NotchFocusTimer(durationSeconds:2)
        XCTAssertFalse(timer.tick())
        timer.start()
        XCTAssertFalse(timer.tick())
        XCTAssertEqual(timer.remainingSeconds,1)
        XCTAssertTrue(timer.tick())
        XCTAssertFalse(timer.isRunning)
        XCTAssertEqual(timer.remainingSeconds,0)
        XCTAssertFalse(timer.tick())
        timer.start()
        XCTAssertFalse(timer.isRunning)
    }

    func testFocusTimerResetRestoresTheDefaultSession() {
        var timer=NotchFocusTimer(durationSeconds:8)
        timer.start()
        timer.reset()
        XCTAssertEqual(timer.remainingSeconds,NotchFocusTimer.defaultDurationSeconds)
        XCTAssertFalse(timer.isRunning)
    }

    func testFocusTimerPresetsResetAndClampRestoredProgress() {
        var timer=NotchFocusTimer(durationSeconds:15*60,remainingSeconds:20*60)
        XCTAssertEqual(timer.durationSeconds,15*60)
        XCTAssertEqual(timer.remainingSeconds,15*60)
        timer.reset(durationSeconds:45*60)
        XCTAssertEqual(timer.durationSeconds,45*60)
        XCTAssertEqual(timer.remainingSeconds,45*60)
        XCTAssertEqual(NotchFocusTimer.presetMinutes,[5,15,25,45])
    }

    func testCompletedFocusTimerRestartsWithItsSelectedDuration() {
        var timer=NotchFocusTimer(durationSeconds:1)
        timer.start()
        XCTAssertTrue(timer.tick())
        timer.restart()
        XCTAssertEqual(timer.durationSeconds,1)
        XCTAssertEqual(timer.remainingSeconds,1)
        XCTAssertFalse(timer.isRunning)
    }

    func testNotchMediaSeekStaysInsideTheMediaTimeline() {
        XCTAssertEqual(NotchMediaControl.targetTime(current:3,offset:-10,duration:40)!,0.0)
        XCTAssertEqual(NotchMediaControl.targetTime(current:35,offset:10,duration:40)!,40.0)
        XCTAssertEqual(NotchMediaControl.targetTime(current:12,offset:-10,duration:40)!,2.0)
        XCTAssertNil(NotchMediaControl.targetTime(current:.infinity,offset:10,duration:40))
        XCTAssertNil(NotchMediaControl.targetTime(current:1,offset:1,duration:.nan))
    }

    func testHoverExpandsAndDelayedCollapseCanRunAfterPointerLeaves() {
        var state=NotchInteractionState()
        state.pointerEntered()
        XCTAssertTrue(state.isExpanded)
        XCTAssertFalse(state.mayCollapse)
        state.pointerExited()
        XCTAssertTrue(state.mayCollapse)
        XCTAssertTrue(state.collapseIfAllowed())
        XCTAssertFalse(state.isExpanded)
    }

    func testDragKeepsPanelExpandedUntilDropSessionEnds() {
        var state=NotchInteractionState()
        state.draggingEntered()
        state.pointerExited()
        XCTAssertTrue(state.isExpanded)
        XCTAssertFalse(state.mayCollapse)
        XCTAssertFalse(state.collapseIfAllowed())
        state.draggingEnded()
        XCTAssertTrue(state.mayCollapse)
        XCTAssertTrue(state.collapseIfAllowed())
    }

    func testToolGridUsesTheFullCurrentCatalog() {
        XCTAssertEqual(ToolKind.allCases.count,27)
        XCTAssertEqual(FeatureCatalog.toolSymbols.count,27)
    }

    func testNotchCountsKeepTheirLocalizationPlaceholders() {
        XCTAssertEqual(AppLocalization.text("{0} files",language:"tr"),"{0} dosya")
        XCTAssertEqual(AppLocalization.text("{0} tools",language:"tr"),"{0} araçlar")
        XCTAssertEqual(AppLocalization.text("Convert to {0}",language:"tr"),"{0} biçimine dönüştür")
        XCTAssertEqual(AppLocalization.text("Convert {0} to {1}",language:"tr"),"{0} dosyasını {1} biçimine dönüştür")
    }

    func testNotchWidgetsHaveTurkishCopy() {
        for source in ["Widgets","Focus timer","Quick note","To-do list","Add a task","Start focus timer","Pause focus timer","Reset focus timer","A quiet {0}-minute session","Set focus timer to {0} minutes","Add to Float","Keep these files together in a floating shelf","Paste files, text, an image, or a link from the clipboard","Pause Nashi preview","Play Nashi preview","Nashi media preview","Seek Nashi media preview","Go back 10 seconds in Nashi preview","Go forward 10 seconds in Nashi preview","Notes and tasks stay on this Mac."] {
            XCTAssertNotEqual(AppLocalization.text(source,language:"tr"),source,source)
        }
        XCTAssertEqual(AppLocalization.text("A quiet 15-minute session",language:"tr"),"Sakin bir 15 dakikalık oturum")
        XCTAssertEqual(AppLocalization.text("Set focus timer to 45 minutes",language:"tr"),"Odak sayacını 45 dakikaya ayarla")
    }

    func testFormatChipsUseCompatibleTargetsFromExistingCatalog() {
        XCTAssertEqual(NotchDropShelfTargets.formats(for:URL(fileURLWithPath:"/tmp/photo.JPG")),FeatureCatalog.formats(for:[URL(fileURLWithPath:"/tmp/photo.JPG")]))
        XCTAssertTrue(NotchDropShelfTargets.formats(for:URL(fileURLWithPath:"/tmp/unknown.bin")).isEmpty)
        XCTAssertTrue(NotchDropShelfTargets.formats(for:URL(fileURLWithPath:"/tmp/photo.jpg")).contains("png"))
        XCTAssertFalse(NotchDropShelfTargets.formats(for:URL(fileURLWithPath:"/tmp/photo.jpg")).contains("mp3"))
    }

}
