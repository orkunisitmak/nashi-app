import AppKit
import AVFoundation
import SwiftUI
import PDFKit
import NashiCore
import NashiUpdates

struct UIAuditOptions {
    static let scenes=["home.empty","home.selected","library","settings.general","settings.formats","settings.updates","wheel.pdf-multiple","editor.crop-image","editor.annotate-image","editor.frame-image"]
    static let notchScenes=["island.collapsed","island.collapsed-progress","island.collapsed-media","island.expanded.drop","island.expanded.drop.empty","island.expanded.tools","island.expanded.history","island.expanded.widgets","island.expanded.media"]
    static let supportedScenes=scenes+notchScenes
    var output:URL
    var scenes=Self.scenes
    var appearances=["light","dark"]
    var languages=["en","tr"]
    var hold=false
    init(arguments:[String])throws {
        guard let start=arguments.firstIndex(of:"--ui-audit"),start+1<arguments.count,!arguments[start+1].hasPrefix("--") else {throw UIAuditError("Use --ui-audit OUTPUT [--scenes scene,...] [--appearance light|dark|both] [--language en|tr|fr|both] [--hold]")}
        output=URL(fileURLWithPath:arguments[start+1],isDirectory:true).standardizedFileURL
        var index=start+2
        while index<arguments.count {
            let key=arguments[index];index+=1
            if key=="--hold" {hold=true;continue}
            guard index<arguments.count else{throw UIAuditError("Missing value for "+key)}
            let value=arguments[index];index+=1
            switch key {
            case "--scenes":scenes=value.split(separator:",").map(String.init);guard !scenes.isEmpty,Set(scenes).count==scenes.count,scenes.allSatisfy(Self.supportedScenes.contains) else{throw UIAuditError("Unknown or repeated audit scene. Supported: "+Self.supportedScenes.joined(separator:","))}
            case "--appearance":guard ["light","dark","both"].contains(value) else{throw UIAuditError("Appearance must be light, dark or both")};appearances=value=="both" ? ["light","dark"]:[value]
            case "--language":guard AppLocalization.supportedLanguages.contains(value) || value=="both" else{throw UIAuditError("Language must be en, tr, fr or both")};languages=value=="both" ? ["en","tr"]:[value]
            default:throw UIAuditError("Unknown audit argument: "+key)
            }
        }
    }
}
struct UIAuditError:LocalizedError { let message:String;init(_ message:String){self.message=message};var errorDescription:String?{message} }

/// An explicit, isolated review mode. It never starts the production delegate,
/// global hotkeys, window manager, updater or clipboard import path.
@MainActor final class UIAudit:NSObject,NSApplicationDelegate {
    let options:UIAuditOptions
    private var window:NSWindow?
    private var records:[[String:Any]]=[]
    private var fixtureRoot:URL?
    private var suites:Set<String>=[]
    private var observedPreferences:[String:String]=[:]
    private var originalArguments:[String:Any]?
    private var outputPrepared=false
    private var finished=false
    private var successful=false
    private var validatorChecks:[String]=[]
    private var fixtures:[String:URL]=[:]
    init(options:UIAuditOptions){self.options=options}
    func applicationDidFinishLaunching(_ notification:Notification) {
        setvbuf(stdout,nil,_IOLBF,0)
        Task {
            do {try await run()}catch{records.append(["scene":"setup","status":"failed","issues":[error.localizedDescription]])}
            successful=records.count==options.scenes.count*options.appearances.count*options.languages.count && records.allSatisfy{($0["status"] as? String)=="captured"}
            if outputPrepared {
                do {try writeManifest()}catch{successful=false;fputs("UI audit manifest could not be saved.\n",stderr)}
            }else{fputs("UI audit output was not prepared; no manifest was written.\n",stderr)}
            finished=true
            print("UI_AUDIT "+(successful ? "PASS":"FAIL")+" "+options.output.path)
            if !options.hold || window==nil {NSApp.terminate(nil)}
        }
    }
    func applicationShouldTerminateAfterLastWindowClosed(_ sender:NSApplication)->Bool {finished}
    func applicationWillTerminate(_ notification:Notification) {
        window?.close()
        for suite in suites {UserDefaults.standard.removePersistentDomain(forName:suite)}
        suites.removeAll()
        if let originalArguments {UserDefaults.standard.setVolatileDomain(originalArguments,forName:UserDefaults.argumentDomain)}
        if let fixtureRoot {try? FileManager.default.removeItem(at:fixtureRoot)}
        if !successful {exit(1)}
    }
    private func prepareOutput()throws {
        let manager=FileManager.default,marker=options.output.appendingPathComponent(".nashi-ui-audit")
        if manager.fileExists(atPath:options.output.path),!manager.fileExists(atPath:marker.path) {
            let entries=try manager.contentsOfDirectory(atPath:options.output.path)
            guard entries.isEmpty else{throw UIAuditError("Choose an empty output directory; UI audit will not overwrite unrelated files.")}
        }
        try manager.createDirectory(at:options.output,withIntermediateDirectories:true)
        try Data("Nashi UI audit v1\n".utf8).write(to:marker,options:.atomic)
        outputPrepared=true
    }
    private func makeFixtures()throws {
        let directory=FileManager.default.temporaryDirectory.appendingPathComponent("Nashi-UIAudit-"+UUID().uuidString,isDirectory:true)
        try FileManager.default.createDirectory(at:directory,withIntermediateDirectories:true);fixtureRoot=directory
        let imageURL=directory.appendingPathComponent("İstanbul – uzun dosya adı ve görsel örneği 🍐.png")
        let bitmap=NSBitmapImageRep(bitmapDataPlanes:nil,pixelsWide:640,pixelsHigh:400,bitsPerSample:8,samplesPerPixel:4,hasAlpha:true,isPlanar:false,colorSpaceName:.deviceRGB,bytesPerRow:640*4,bitsPerPixel:32)!
        NSGraphicsContext.saveGraphicsState();NSGraphicsContext.current=NSGraphicsContext(bitmapImageRep:bitmap)
        NSColor(calibratedRed:0.16,green:0.30,blue:0.21,alpha:1).setFill();NSBezierPath(rect:NSRect(x:0,y:0,width:640,height:400)).fill()
        NSColor(calibratedRed:0.89,green:0.72,blue:0.35,alpha:1).setFill();NSBezierPath(roundedRect:NSRect(x:40,y:50,width:300,height:230),xRadius:24,yRadius:24).fill();NSGraphicsContext.restoreGraphicsState()
        guard let data=bitmap.representation(using:.png,properties:[:])else{throw UIAuditError("Image fixture could not be generated")};try data.write(to:imageURL)
        let textURL=directory.appendingPathComponent("Türkçe notlar – kaynak dosya.txt");try "Nashi audit fixture · ıİşğü 🍐\nOriginal files stay intact.".write(to:textURL,atomically:true,encoding:.utf8)
        for number in 1...2 {
            let pdf=PDFDocument();for _ in 0..<2 {guard let page=PDFPage(image:NSImage(data:data)!)else{throw UIAuditError("PDF fixture could not be generated")};pdf.insert(page,at:pdf.pageCount)}
            let path=directory.appendingPathComponent("Örnek belge \(number).pdf");guard pdf.write(to:path)else{throw UIAuditError("PDF fixture could not be saved")};fixtures["pdf\(number)"]=path
        }
        let audioURL=directory.appendingPathComponent("Nashi preview.wav")
        let audioFormat=AVAudioFormat(commonFormat:.pcmFormatInt16,sampleRate:44_100,channels:1,interleaved:true)!
        let audioBuffer=AVAudioPCMBuffer(pcmFormat:audioFormat,frameCapacity:44_100)!
        audioBuffer.frameLength=audioBuffer.frameCapacity
        memset(audioBuffer.int16ChannelData![0],0,Int(audioBuffer.frameLength)*MemoryLayout<Int16>.size)
        let audioFile=try AVAudioFile(forWriting:audioURL,settings:audioFormat.settings)
        try audioFile.write(from:audioBuffer)
        fixtures["audio"]=audioURL
        fixtures["image"]=imageURL;fixtures["text"]=textURL;fixtures["missing"]=directory.appendingPathComponent("Missing original.png")
    }
    private func run()async throws {
        try prepareOutput()
        validatorChecks=try UIAuditChecks.run()
        validatorChecks += try UIAuditChecks.redactionPreviews(at:options.output.appendingPathComponent("redaction-preview"))
        let originalArguments=UserDefaults.standard.volatileDomain(forName:UserDefaults.argumentDomain)
        self.originalArguments=originalArguments
        try makeFixtures()
        for language in options.languages {for appearance in options.appearances {for scene in options.scenes {
            // Release the previous SwiftUI graph before changing the process-only
            // overrides. Every cell gets a new AppStorage identity and domain.
            window?.contentView=nil;window?.close();window=nil
            let suite="Nashi.UIAudit."+UUID().uuidString;suites.insert(suite)
            guard let defaults=UserDefaults(suiteName:suite) else{throw UIAuditError("Isolated audit preferences could not be created")}
            var arguments=originalArguments;arguments["appLanguage"]=language;arguments["appAppearance"]=appearance;arguments["reduceAnimations"]=true;arguments["soundsAndHapticsEnabled"]=false;arguments["sound"]=false;arguments["reveal"]=false
            UserDefaults.standard.setVolatileDomain(arguments,forName:UserDefaults.argumentDomain)
            // Each UserDefaults instance caches its own volatile search domains.
            // Set both stores so NSArgumentDomain cannot mask this cell's suite.
            defaults.setVolatileDomain(arguments,forName:UserDefaults.argumentDomain)
            defaults.set(language,forKey:"appLanguage");defaults.set(appearance,forKey:"appAppearance");defaults.set(true,forKey:"reduceAnimations");defaults.set(false,forKey:"notchModeEnabled")
            NSApp.appearance=NSAppearance(named:appearance=="dark" ? .darkAqua:.aqua)
            observedPreferences=[:]
            do {try await capture(scene:scene,appearance:appearance,language:language,defaults:defaults)}
            catch{records.append(["scene":scene,"appearance":appearance,"language":language,"status":"failed","issues":[error.localizedDescription]])}
        }}}
    }
    private func capture(scene:String,appearance:String,language:String,defaults:UserDefaults)async throws {
        window?.close();window=nil
        let state=AppState(defaults:defaults),recorder=UIAuditLayout(),updater=AppUpdater()
        defer { state.filePreview.close() }
        let image=fixtures["image"]!,text=fixtures["text"]!,pdf=fixtures["pdf1"]!
        if scene=="home.selected" || scene.hasPrefix("editor.") || scene=="island.expanded.drop" || scene=="island.expanded.tools" {state.inputs=[image]}
        if scene=="island.collapsed-media" || scene=="island.expanded.media" {state.filePreview.show([fixtures["audio"]!])}
        if scene=="island.collapsed-progress" {state.inputs=[image];state.working=true;state.progress=0.42;state.progressIsDeterminate=true;state.progressLabel="Convert to PNG";state.activeInputs=[image]}
        if scene=="island.expanded.history" {
            state.history=[JobRecord(id:UUID(uuidString:"00000000-0000-0000-0000-000000000011")!,title:"Convert to PNG",inputs:[image.path],outputs:[image.path],date:Date(timeIntervalSince1970:1_767_225_600))]
            state.working=true;state.progress=0.62;state.progressIsDeterminate=true;state.progressLabel="Compress";state.activeInputs=[image]
        }
        if scene=="library" {
            state.history=[(image,"Convert to PNG"),(text,"Pasted text"),(pdf,"Merge PDFs"),(fixtures["missing"]!,"Missing original")].enumerated().map { index,item in JobRecord(id:UUID(uuidString:String(format:"00000000-0000-0000-0000-%012d",index+1))!,title:item.1,inputs:[],outputs:[item.0.path],date:Date(timeIntervalSince1970:1_767_225_600)) }
            state.libraryVisible=true
        }
        let isEditor=scene.hasPrefix("editor.")
        let view:AnyView,size:NSSize
        switch scene {
        case "home.empty","home.selected","library":view=AnyView(HomeView(state:state,updater:updater));size=NSSize(width:700,height:580)
        case "island.collapsed","island.collapsed-progress","island.collapsed-media","island.expanded.drop","island.expanded.drop.empty","island.expanded.tools","island.expanded.history","island.expanded.widgets","island.expanded.media":
            let model=NotchPanelModel(defaults:defaults)
            if scene=="island.expanded.tools" {model.select(.tools)}
            if scene=="island.expanded.history" {model.select(.history)}
            if scene=="island.expanded.drop" || scene=="island.expanded.drop.empty" {model.select(.drop)}
            if scene=="island.expanded.widgets" {
                model.quickNote="Plan the next small step.\nKeep a clean copy."
                model.tasks=[.init(title:"Review the image"),.init(title:"Export a fresh copy",isComplete:true)]
                model.select(.widgets)
            }
            view=AnyView(NotchWorkspaceView(state:state,model:model,media:state.filePreview,onChooseFiles:{},onChooseTool:{_ in}))
            size=scene=="island.collapsed" ? NSSize(width:164,height:44):scene=="island.collapsed-progress" || scene=="island.collapsed-media" ? NSSize(width:266,height:44):NSSize(width:540,height:470)
        case "settings.general","settings.formats","settings.updates":
            let section:SettingsSection=scene=="settings.formats" ? .formats:scene=="settings.updates" ? .updates:.general
            view=AnyView(SettingsView(updater:updater,initialSection:section));size=NSSize(width:790,height:590)
        case "editor.crop-image","editor.annotate-image","editor.frame-image":
            let tool:ToolKind=scene=="editor.crop-image" ? .cropImage:scene=="editor.annotate-image" ? .annotateImage:.frameImage
            view=AnyView(ToolEditor(state:state,tool:tool));size=NSSize(width:900,height:420)
        case "wheel.pdf-multiple":
            let wheel=WheelView(frame:NSRect(x:0,y:0,width:320,height:320));let inputs=[fixtures["pdf1"]!,fixtures["pdf2"]!]
            wheel.mode = .tools;wheel.inputs=inputs
            wheel.items=FeatureCatalog.tools(for:inputs).map{WheelItem(title:FeatureCatalog.toolNames[$0] ?? $0.title,symbol:FeatureCatalog.toolSymbols[$0],operation:.tool($0))};wheel.centerTitle="Tools"
            let host=NSWindow(contentRect:NSRect(x:0,y:0,width:320,height:320),styleMask:[.titled,.closable],backing:.buffered,defer:false);host.isReleasedWhenClosed=false;host.title="Nashi UI Audit · "+scene;host.contentView=wheel;host.center();host.makeKeyAndOrderFront(nil);window=host;NSApp.activate(ignoringOtherApps:true)
            wheel.updateAccessibility()
            try await settle(recorder:nil)
            observedPreferences=try await verifyPreferences(scene:scene,appearance:appearance,language:language,defaults:defaults,view:wheel)
            try save(scene:scene,appearance:appearance,language:language,view:wheel,recorder:nil,issues:wheel.items.enumerated().flatMap{index,item in wheel.labelAuditIssues(index,title:item.title)})
            return
        default:throw UIAuditError("Unknown audit scene")
        }
        let root=view.nashiLanguage().defaultAppStorage(defaults).uiAuditRoot(recorder)
        let host:NSWindow
        if isEditor {host=ToolPanel(contentRect:NSRect(origin:.zero,size:size),styleMask:[.borderless,.closable],backing:.buffered,defer:false)}
        else {host=NSWindow(contentRect:NSRect(origin:.zero,size:size),styleMask:[.titled,.closable],backing:.buffered,defer:false)}
        host.isReleasedWhenClosed=false;host.title="Nashi UI Audit · "+scene;host.contentView=NSHostingView(rootView:root);host.center();window=host;host.makeKeyAndOrderFront(nil);NSApp.activate(ignoringOtherApps:true)
        try await settle(recorder:isEditor ? recorder:nil)
        guard let content=host.contentView else{throw UIAuditError("Scene has no content view")}
        observedPreferences=try await verifyPreferences(scene:scene,appearance:appearance,language:language,defaults:defaults,view:content)
        if scene=="island.expanded.drop.empty" && !ownAccessibilityElements(content).contains(where:{$0.accessibilityIdentifier()=="NashiPasteFromClipboard"}) {
            throw UIAuditError("The empty Drop Shelf does not expose its clipboard paste action.")
        }
        if scene.hasSuffix("media") {
            let identifiers=Set(ownAccessibilityElements(content).compactMap{$0.accessibilityIdentifier()})
            guard identifiers.contains("NashiPreviewPlayPause") else { throw UIAuditError("The Nashi media playback control is missing from the notch scene.") }
            if scene=="island.expanded.media",!identifiers.isSuperset(of:["NashiPreviewSeek","NashiPreviewBack10","NashiPreviewForward10"]) { throw UIAuditError("The expanded Nashi media controls do not expose seeking and skip actions.") }
        }
        let issues=recorder.issues(in:content.bounds,editor:isEditor)
        try save(scene:scene,appearance:appearance,language:language,view:content,recorder:recorder,issues:issues)
    }
    private func settle(recorder:UIAuditLayout?)async throws {
        let deadline=Date().addingTimeInterval(12);var previous:[String:CGRect]=[:],stable=0
        while Date()<deadline {
            window?.contentView?.layoutSubtreeIfNeeded();window?.displayIfNeeded()
            let artworkReady=await FileArtworkCache.shared.pendingCount==0
            if let recorder {
                if recorder.status.hasPrefix("failed:"){throw UIAuditError(recorder.status)}
                if artworkReady,recorder.status=="ready",!recorder.frames.isEmpty,recorder.frames==previous {stable+=1}else{stable=0}
                previous=recorder.frames
            }else if artworkReady{stable+=1}else{stable=0}
            if stable>=3 {return}
            try await Task.sleep(nanoseconds:40_000_000)
        }
        throw UIAuditError("Scene readiness/layout did not settle before the audit deadline")
    }
    private func ownAccessibilityElements(_ view:NSView)->[NSAccessibilityProtocol] {
        var queue:[Any]=[view],elements:[NSAccessibilityProtocol]=[],visited=Set<ObjectIdentifier>()
        while !queue.isEmpty,elements.count<2000 {
            let item=queue.removeFirst()
            guard let element=item as? NSAccessibilityProtocol else{continue}
            let identity=ObjectIdentifier(element as AnyObject)
            guard visited.insert(identity).inserted else{continue}
            elements.append(element);queue.append(contentsOf:element.accessibilityChildren() ?? [])
        }
        return elements
    }
    private func nativeViews(_ view:NSView)->[NSView] {[view]+view.subviews.flatMap{nativeViews($0)}}
    private func renderedSelections(_ view:NSView)->[String:String] {
        let elements=ownAccessibilityElements(view),views=nativeViews(view)
        var values:[String:String]=[:]
        if let language=elements.first(where:{$0.accessibilityIdentifier()=="AppLanguage"}) {
            if let value=language.accessibilityValue() as? String {values["languagePicker"]=value}
            else if let title=language.accessibilityTitle(){values["languagePicker"]=title}
        }
        if let control=views.compactMap({$0 as? NSPopUpButton}).first(where:{["English","Türkçe","Français"].contains($0.titleOfSelectedItem ?? "")}),let selected=control.titleOfSelectedItem {
            values["languagePicker"]=selected
        }
        if values["languagePicker"]==nil {
            let diagnostic=elements.filter{$0.accessibilityIdentifier()=="AppLanguage" || $0.accessibilityRole() == .popUpButton}.prefix(12).map { element in
                "id=\(element.accessibilityIdentifier() ?? "nil") role=\(element.accessibilityRole()?.rawValue ?? "nil") title=\(element.accessibilityTitle() ?? "nil") label=\(element.accessibilityLabel() ?? "nil") value=\(String(describing:element.accessibilityValue()))"
            }
            let native=views.compactMap{$0 as? NSPopUpButton}.map{"native=\(type(of:$0)) title=\($0.titleOfSelectedItem ?? "nil")"}
            values["languagePickerDiagnostics"]=(diagnostic+native).joined(separator:"; ")
        }
        // AppKit-backed and SwiftUI accessibility-backed segmented pickers are
        // both queried in-process. No AXUIElement/TCC or input injection is used.
        let expected=[L10n.text("System"),L10n.text("Light"),L10n.text("Dark")]
        if let control=views.compactMap({$0 as? NSSegmentedControl}).first(where:{candidate in
            candidate.segmentCount==3 && (0..<3).map{candidate.label(forSegment:$0) ?? ""}==expected
        }),control.selectedSegment>=0 {values["appearancePicker"]=["system","light","dark"][control.selectedSegment]}
        if values["appearancePicker"]==nil {
            for element in elements {
                let children=(element.accessibilityChildren() ?? []).compactMap{$0 as? NSAccessibilityProtocol}
                let choices=children.filter{child in expected.contains(child.accessibilityLabel() ?? child.accessibilityTitle() ?? "")}
                guard choices.count==3 else{continue}
                if let selected=choices.first(where:{($0.accessibilityValue() as? NSNumber)?.intValue==1}),let index=expected.firstIndex(of:selected.accessibilityLabel() ?? selected.accessibilityTitle() ?? "") {
                    values["appearancePicker"]=["system","light","dark"][index];break
                }
            }
        }
        return values
    }
    private func verifyPreferences(scene:String,appearance:String,language:String,defaults:UserDefaults,view:NSView)async throws->[String:String] {
        let deadline=Date().addingTimeInterval(3)
        var values:[String:String]=[:],issues:[String]=[]
        repeat {
            values=["suiteAppearance":defaults.string(forKey:"appAppearance") ?? "missing","suiteLanguage":defaults.string(forKey:"appLanguage") ?? "missing","standardAppearance":UserDefaults.standard.string(forKey:"appAppearance") ?? "missing","standardLanguage":L10n.language,"effectiveAppearance":view.effectiveAppearance.bestMatch(from:[.aqua,.darkAqua]) == .darkAqua ? "dark":"light"]
            issues=[]
            for key in ["suiteAppearance","standardAppearance","effectiveAppearance"] where values[key] != appearance {issues.append(key+"="+(values[key] ?? "missing"))}
            for key in ["suiteLanguage","standardLanguage"] where values[key] != language {issues.append(key+"="+(values[key] ?? "missing"))}
            if scene=="settings.general" {
                values.merge(renderedSelections(view),uniquingKeysWith:{$1})
                if values["appearancePicker"] != appearance {issues.append("rendered appearance picker="+(values["appearancePicker"] ?? "unavailable"))}
                if values["languagePicker"] != (["tr":"Türkçe","fr":"Français"][language] ?? "English") {issues.append("rendered language picker="+(values["languagePicker"] ?? "unavailable"))}
            }
            if issues.isEmpty {return values}
            try await Task.sleep(nanoseconds:40_000_000)
            view.layoutSubtreeIfNeeded()
        }while Date()<deadline
        if let diagnostic=values["languagePickerDiagnostics"] {issues.append("language picker diagnostics: "+(diagnostic.isEmpty ? "No native popup or matching accessibility node found":diagnostic))}
        throw UIAuditError("Requested \(appearance)/\(language) preferences did not reach the rendered scene: "+issues.joined(separator:", "))
    }
    private func save(scene:String,appearance:String,language:String,view:NSView,recorder:UIAuditLayout?,issues:[String])throws {
        guard let bitmap=view.bitmapImageRepForCachingDisplay(in:view.bounds) else{throw UIAuditError("Own-view renderer could not allocate a bitmap")}
        view.cacheDisplay(in:view.bounds,to:bitmap)
        let raster=UIAuditRaster.inspect(bitmap)
        var captureIssues=issues
        if !raster.nonBlank {captureIssues.append("The own-view render is blank or contains insufficient visible scene content.")}
        guard let data=bitmap.representation(using:.png,properties:[:]) else{throw UIAuditError("Own-view renderer did not return PNG data")}
        let file=scene+"-"+appearance+"-"+language+".png";try data.write(to:options.output.appendingPathComponent(file),options:.atomic)
        records.append(["scene":scene,"appearance":appearance,"language":language,"image":file,"status":captureIssues.isEmpty ? "captured":"failed","issues":captureIssues,"frames":recorder?.jsonFrames ?? [:],"width":view.bounds.width,"height":view.bounds.height,"renderedSamples":raster.samples,"visibleSamples":raster.visibleSamples,"sampledColors":raster.colors,"observedPreferences":observedPreferences])
    }
    private func writeManifest()throws {
        guard outputPrepared else{throw UIAuditError("Output destination did not pass audit preflight")}
        let report:[String:Any]=["schemaVersion":1,"platform":"macOS","passed":successful,"renderTechnique":"own-view-cacheDisplay","validatorChecks":validatorChecks,"limitations":["Render proofs do not include live window-server glass or native titlebar chrome; verify those through Computer Use.","Relative history date labels use the run date."],"captures":records]
        try FileManager.default.createDirectory(at:options.output,withIntermediateDirectories:true)
        let data=try JSONSerialization.data(withJSONObject:report,options:[.prettyPrinted,.sortedKeys]);try data.write(to:options.output.appendingPathComponent("manifest.json"),options:.atomic)
    }
}
