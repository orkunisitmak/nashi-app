import AppKit
import NashiCore

/// `--wheel-smoke <dir>`: motion endpoints, Snap dust, glass label fit and PNG renders. No input; the only window is its own progress toast.
@MainActor final class WheelSmoke:NSObject,NSApplicationDelegate {
    private let output:URL
    private var checks:[String]=[]
    /// Writes only inside a new `wheel-smoke-<id>` folder, so nothing already in `output` is touched.
    init(output:URL) { self.output=output.appendingPathComponent("wheel-smoke-"+UUID().uuidString.prefix(8),isDirectory:true) }
    func applicationDidFinishLaunching(_ notification:Notification) {
        var failure:String?
        let defaults=UserDefaults.standard,saved=["appLanguage","conversionFanTheme","wheelScale"].map { ($0,defaults.object(forKey:$0)) }
        do { try run() } catch { failure=String(describing:error) }
        for (key,value) in saved { defaults.set(value,forKey:key) }
        let report:[String:Any]=["passed":failure == nil,"checks":checks,"error":failure as Any? ?? NSNull()]
        // A missing report must not look like a pass.
        do { try JSONSerialization.data(withJSONObject:report,options:[.prettyPrinted,.sortedKeys]).write(to:output.appendingPathComponent("wheel-smoke.json")) }
        catch { fputs("wheel-smoke.json could not be written: \(error)\n",stderr);failure=failure ?? String(describing:error) }
        exit(failure == nil ? 0:1)
    }
    private func check(_ value:Bool,_ label:String)throws { guard value else { throw NSError(domain:"Nashi Wheel QA",code:1,userInfo:[NSLocalizedDescriptionKey:label]) };checks.append(label) }
    private func run()throws {
        try FileManager.default.createDirectory(at:output.deletingLastPathComponent(),withIntermediateDirectories:true)
        try FileManager.default.createDirectory(at:output,withIntermediateDirectories:false)
        let scratch=FileManager.default.temporaryDirectory.appendingPathComponent("nashi-wheel-smoke-"+UUID().uuidString,isDirectory:true)
        try FileManager.default.createDirectory(at:scratch,withIntermediateDirectories:true)
        defer { try? FileManager.default.removeItem(at:scratch) }
        try checkMotion()
        try checkDust()
        try checkShortcutMigration()
        let fixtures=try ["Tatil.heic","Sunum.pdf","Röportaj.mov","Teklif.docx"].map { name -> URL in
            let url=scratch.appendingPathComponent(name);try Data().write(to:url);return url
        }
        try checkLabels(fixtures)
        try render(fixtures[0])
        try checkProgressToast()
    }
    /// The real progress toast rendered in light/dark and EN/TR, plus the catalog text its VoiceOver label and value use.
    /// State lives in a throwaway suite; SwiftUI builds no accessibility tree without an assistive client, so the text is checked at its source.
    private func checkProgressToast()throws {
        for (language,expected) in [("en",["Progress","In progress","40 percent"]),("tr",["İlerleme","Sürüyor","yüzde 40"])] {
            try check([AppLocalization.text("Progress",language:language),AppLocalization.text("In progress",language:language),AppLocalization.text("40 percent",language:language)] == expected,"Progress VoiceOver text in \(language): "+expected.joined(separator:" / "))
        }
        let suite="nashi-wheel-smoke-"+UUID().uuidString,defaults=UserDefaults(suiteName:suite)!
        defer { defaults.removePersistentDomain(forName:suite);NSApp.appearance=nil }
        let state=AppState(defaults:defaults);state.working=true;state.progressIsDeterminate=true;state.progress=0.4
        for language in ["en","tr"] { for dark in [false,true] {
            UserDefaults.standard.set(language,forKey:"appLanguage");NSApp.appearance=NSAppearance(named:dark ? .darkAqua:.aqua)
            let toast=ProgressToastController();toast.show(state:state)
            defer { toast.dismiss() }
            guard let content=toast.panel?.contentView else { throw NSError(domain:"Nashi Wheel QA",code:1,userInfo:[NSLocalizedDescriptionKey:"Progress toast did not open"]) }
            RunLoop.main.run(until:Date().addingTimeInterval(0.6));content.layoutSubtreeIfNeeded()
            let name="progress-\(language)-\(dark ? "dark":"light").png"
            guard let rep=content.bitmapImageRepForCachingDisplay(in:content.bounds) else { throw NSError(domain:"Nashi Wheel QA",code:1,userInfo:[NSLocalizedDescriptionKey:"Progress toast could not render"]) }
            content.cacheDisplay(in:content.bounds,to:rep);try writePNG(rep,name)
            checks.append("Rendered \(name)")
        } }
    }
    private func checkMotion()throws {
        let count=8,rest=WheelMotion.rest(count)
        func visible(_ m:WheelMotion)->CGFloat { m.alpha*max(m.baseAlpha,m.discAlpha,m.petals.map(\.alpha).max() ?? 0)*min(1,m.scale) }
        for style in [nil]+WheelAnimationStyle.allCases.map(Optional.some) {
            let name=style?.rawValue ?? "reduced"
            let end=WheelMotion.frame(style,opening:true,progress:1,count:count)
            try check(end.petals == rest.petals && abs(end.alpha-1)<0.001 && abs(end.scale-1)<0.001 && abs(end.rotation)<0.001 && abs(end.pitch)<0.001 && abs(end.yaw)<0.001 && abs(end.lift)<0.001 && end.baseSize == nil && abs(end.baseAlpha-1)<0.001,"\(name): opening ends at rest")
            try check(visible(WheelMotion.frame(style,opening:true,progress:0,count:count))<0.999 || style == .island,"\(name): opening starts hidden or reduced")
            try check(visible(WheelMotion.frame(style,opening:false,progress:1,count:count))<0.01,"\(name): closing ends invisible")
            try check(abs(visible(WheelMotion.frame(style,opening:false,progress:0,count:count))-1)<0.02 || style == .classic || style == .snap,"\(name): closing starts from the open wheel")
        }
        let island=WheelMotion.frame(.island,opening:true,progress:0.1,count:count)
        try check(island.baseSize.map { $0.width>$0.height } == true && island.capsuleLabel>0.9,"Island opens from a file capsule")
        try check(WheelMotion.spring(0) == 0 && WheelMotion.spring(1) == 1 && WheelMotion.spring(0.22,0.42)>1,"Spring settles exactly and low damping overshoots")
        try check(WheelMotion.modeSwitch(progress:1,count:count).petals == rest.petals,"Mode switch ends at rest")
    }
    private func checkShortcutMigration()throws {
        func chords(_ saved:[String:String])->[ModifierChord] {
            let suite="nashi-wheel-smoke-"+UUID().uuidString,defaults=UserDefaults(suiteName:suite)!
            defer { defaults.removePersistentDomain(forName:suite) }
            saved.forEach { defaults.set($0.value,forKey:$0.key) }
            ModifierPreferences.keepPreviousDefaults(defaults:defaults)
            return WheelShortcut.allCases.map { ModifierPreferences.chord(for:$0,defaults:defaults) }
        }
        try check(chords([:]) == [.init(.command),.init(.option),.init(.control)],"New users get Command, Option, Control")
        try check(chords(["toolsKey":"command"]) == [.init(.shift),.init(.command),.init(.control)],"Tools kept on Command moves conversions back to Shift")
        try check(chords(["convertKey":"option"]) == [.init(.option),.init(.command),.init(.control)],"Conversions on Option keeps tools on Command")
        try check(chords(["actionsKey":"option"]) == [.init(.shift),.init(.command),.init(.option)],"Actions on Option keeps conversions on Shift and tools on Command")    }
    private func checkDust()throws {
        let size=64,space=CGColorSpace(name:CGColorSpace.sRGB)!
        guard let context=CGContext(data:nil,width:size,height:size,bitsPerComponent:8,bytesPerRow:size*4,space:space,bitmapInfo:CGImageAlphaInfo.premultipliedLast.rawValue) else { throw NSError(domain:"Nashi Wheel QA",code:2) }
        context.setFillColor(NSColor.systemOrange.cgColor);context.fillEllipse(in:CGRect(x:4,y:4,width:56,height:56))
        func opaque(_ image:CGImage)->Int {
            let c=CGContext(data:nil,width:size,height:size,bitsPerComponent:8,bytesPerRow:size*4,space:space,bitmapInfo:CGImageAlphaInfo.premultipliedLast.rawValue)!
            c.draw(image,in:CGRect(x:0,y:0,width:size,height:size));let p=c.data!.assumingMemoryBound(to:UInt8.self)
            return (0..<size*size).filter { p[$0*4+3] != 0 }.count
        }
        let source=context.makeImage()!,layers=WheelDust.layers(from:source,count:WheelMotion.snapLayers)
        try check(layers.count == WheelMotion.snapLayers && layers.map(opaque).reduce(0,+) == opaque(source),"Snap dust keeps every pixel exactly once across 24 layers")
    }
    private func checkLabels(_ fixtures:[URL])throws {
        let defaults=UserDefaults.standard;defaults.set("glass",forKey:"conversionFanTheme")
        for language in ["en","tr"] { for scale in [0.85,1.0,1.4] { for url in fixtures { for mode in WheelMode.allCases {
            defaults.set(language,forKey:"appLanguage")
            let side=320*scale,wheel=WheelView(frame:NSRect(x:0,y:0,width:side,height:side))
            wheel.items=Self.items([url],mode);wheel.inputs=[url];wheel.mode=mode
            try draw(wheel,size:NSSize(width:side,height:side),dark:false) { wheel.draw(wheel.bounds) }
            let issues=wheel.items.enumerated().flatMap { wheel.labelAuditIssues($0.offset,title:$0.element.title) }
            try check(issues.isEmpty,"Glass labels fit: \(language) \(Int(scale*100))% \(url.pathExtension) \(mode.rawValue)"+(issues.isEmpty ? "":" "+issues.joined(separator:", ")))
        } } } }
        let bar=WheelModeBar(frame:.zero);defaults.set("tr",forKey:"appLanguage")
        try check(bar.preferredWidth()>200 && bar.preferredWidth()<420,"Mode capsule fits its three Turkish labels")
    }
    /// Writes PNGs of the glass wheel over a stand-in wallpaper: rest, hover and mid-entrance frames.
    private func render(_ url:URL)throws {
        UserDefaults.standard.set("tr",forKey:"appLanguage")
        for dark in [false,true] { for mode in WheelMode.allCases {
            let stage=WheelStageView(frame:.zero);let size=stage.layout(size:320)
            stage.wheel.items=Self.items([url],mode);stage.wheel.inputs=[url];stage.wheel.mode=mode;stage.bar.mode=mode
            stage.wheel.renderingOffscreen=true;stage.wheel.selected=mode == .actions ? nil:2;stage.wheel.settleHover()
            try save(stage,size:size,dark:dark,name:"wheel-\(mode.rawValue)-\(dark ? "dark":"light").png")
        } }
        for style in WheelAnimationStyle.allCases { for progress in [0.25,0.5] {
            let stage=WheelStageView(frame:.zero);let size=stage.layout(size:320)
            stage.wheel.items=Self.items([url],.convert);stage.wheel.inputs=[url]
            stage.capsuleLabel.stringValue=url.lastPathComponent
            stage.apply(.frame(style,opening:true,progress:progress,count:stage.wheel.items.count))
            try save(stage,size:size,dark:false,name:"open-\(style.rawValue)-\(Int(progress*100)).png")
        } }
        checks.append("Rendered wheel PNGs")
        let lean=WheelStageView(frame:.zero);_=lean.layout(size:320);lean.wheel.items=Self.items([url],.convert)
        lean.apply(.frame(.depth,opening:true,progress:0.3,count:lean.wheel.items.count))
        try check(CATransform3DIsAffine(lean.container.layer!.transform) && !CATransform3DIsAffine(lean.wheel.layer!.transform),"Glass stays flat (no square) while the slices lean in 3D")
        lean.apply(.rest(lean.wheel.items.count))
        try check(CATransform3DIsAffine(lean.container.layer!.transform),"At rest the glass has no perspective at all")
        let missing=WheelItem.formatSymbols.filter { NSImage(systemSymbolName:$0.value,accessibilityDescription:nil) == nil }.map(\.key)
        try check(missing.isEmpty,"Every format has a system symbol"+(missing.isEmpty ? "":": missing "+missing.sorted().joined(separator:",")))
        let preview=WheelPreviewView(frame:NSRect(x:0,y:0,width:600,height:340));preview.update(animation:"depth",replay:0,language:"tr");preview.layoutSubtreeIfNeeded()
        let previewStage=preview.subviews.compactMap { $0 as? WheelStageView }.first
        try check(previewStage.map { !$0.wheel.items.isEmpty && preview.bounds.contains($0.frame) } == true,"Theme preview shows the whole live wheel inside its card")
        if NashiMotion.enabled {
            let stage=WheelStageView(frame:.zero);_=stage.layout(size:320);stage.wheel.items=Self.items([url],.tools);stage.wheel.selected=1
            let lifted=stage.wheel.layer?.sublayers?.first { $0.contents != nil && $0.shadowPath != nil && $0.sublayers?.count == 2 }
            try check(lifted != nil && !CATransform3DIsIdentity(lifted!.transform),"Hovered slice lifts onto its own tilting layer")
            stage.wheel.items=Self.items([url],.convert)
            try check(stage.wheel.layer?.sublayers?.contains { $0.shadowPath != nil } != true,"Changing slices removes the lifted layer")
        }
    }
    private func save(_ stage:WheelStageView,size:NSSize,dark:Bool,name:String)throws {
        try draw(stage,size:size,dark:dark,name:name) {
            let bounds=NSRect(origin:.zero,size:size)
            NSGradient(colors:dark ? [NSColor(srgbRed:0.1,green:0.06,blue:0.24,alpha:1),NSColor(srgbRed:0.05,green:0.15,blue:0.31,alpha:1)]:[NSColor(srgbRed:1,green:0.84,blue:0.63,alpha:1),NSColor(srgbRed:0.66,green:0.78,blue:1,alpha:1)])?.draw(in:bounds,angle:-45)
            NSColor.systemPink.withAlphaComponent(0.8).setFill();NSBezierPath(ovalIn:NSRect(x:30,y:size.height*0.5,width:120,height:120)).fill()
            NSColor.systemIndigo.withAlphaComponent(0.8).setFill();NSBezierPath(roundedRect:NSRect(x:size.width*0.55,y:90,width:150,height:110),xRadius:24,yRadius:24).fill()
            let c=stage.container.frame,m=stage.wheel.motion ?? .rest(stage.wheel.items.count)
            // Stand-in for the live blur: a translucent disc, transformed like the container.
            NSGraphicsContext.saveGraphicsState()
            let t=NSAffineTransform();t.translateX(by:c.midX,yBy:c.midY);t.rotate(byDegrees:-m.rotation);t.scaleX(by:m.scale*m.stretchX,yBy:m.scale*m.stretchY);t.translateX(by:-c.width/2,yBy:-c.height/2);t.concat()
            let base=stage.base.frame;(dark ? NSColor(white:0.12,alpha:0.62*m.baseAlpha*m.alpha):NSColor(white:1,alpha:0.55*m.baseAlpha*m.alpha)).setFill()
            NSBezierPath(roundedRect:base,xRadius:base.height/2,yRadius:base.height/2).fill()
            NSGraphicsContext.current?.cgContext.setAlpha(m.alpha);stage.wheel.draw(stage.wheel.bounds)
            NSGraphicsContext.restoreGraphicsState()
            let b=stage.barHost.frame;(dark ? NSColor(white:0.12,alpha:0.62*m.barAlpha):NSColor(white:1,alpha:0.55*m.barAlpha)).setFill()
            NSBezierPath(roundedRect:b,xRadius:b.height/2,yRadius:b.height/2).fill()
            NSGraphicsContext.saveGraphicsState();let bt=NSAffineTransform();bt.translateX(by:b.minX,yBy:b.minY);bt.concat();stage.bar.draw(stage.bar.bounds);NSGraphicsContext.restoreGraphicsState()
        }
    }
    private func draw(_ view:NSView,size:NSSize,dark:Bool,name:String?=nil,_ body:()->Void)throws {
        let scale:CGFloat=2
        guard let rep=NSBitmapImageRep(bitmapDataPlanes:nil,pixelsWide:Int(size.width*scale),pixelsHigh:Int(size.height*scale),bitsPerSample:8,samplesPerPixel:4,hasAlpha:true,isPlanar:false,colorSpaceName:.deviceRGB,bytesPerRow:0,bitsPerPixel:0) else { return }
        rep.size=size
        guard let context=NSGraphicsContext(bitmapImageRep:rep) else { return }
        view.appearance=NSAppearance(named:dark ? .darkAqua:.aqua)
        NSGraphicsContext.saveGraphicsState();NSGraphicsContext.current=context
        view.effectiveAppearance.performAsCurrentDrawingAppearance(body)
        NSGraphicsContext.restoreGraphicsState()
        if let name { try writePNG(rep,name) }
    }
    private func writePNG(_ rep:NSBitmapImageRep,_ name:String)throws {
        guard let data=rep.representation(using:.png,properties:[:]) else { throw NSError(domain:"Nashi Wheel QA",code:1,userInfo:[NSLocalizedDescriptionKey:"Could not encode \(name)"]) }
        try data.write(to:output.appendingPathComponent(name))
    }
    private static func items(_ inputs:[URL],_ mode:WheelMode)->[WheelItem] {
        switch mode {
        case .convert:return WheelItem.conversions(for:inputs)
        case .tools:return FeatureCatalog.tools(for:inputs).map { WheelItem(title:FeatureCatalog.toolNames[$0] ?? $0.title,symbol:FeatureCatalog.toolSymbols[$0],operation:.tool($0)) }
        case .actions:return FileAction.allCases.map { WheelItem(title:$0.title,symbol:$0.symbol,action:$0) }
        }
    }
}
