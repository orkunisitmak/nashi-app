import AppKit
import NashiCore

enum WheelMode:String,CaseIterable { case convert,tools,actions }
final class WheelPanel:NSPanel { override var canBecomeKey:Bool { true } }
enum WebWheelAction { case download,thumbnail,audio,copy,open }
struct WheelItem {
    var title:String
    var symbol:String? = nil
    var operation:NashiCore.Operation? = nil
    var action:FileAction? = nil
    var webAction:WebWheelAction? = nil
    var windowPlacement:WindowPlacement? = nil
}

@MainActor final class WheelController {
    let state:AppState
    var panel:NSPanel?
    var wheel:WheelView?
    var mode:WheelMode = .convert
    var inputs:[URL]=[]
    var keyMonitor:Any?
    private var keyboardMode=false
    private var focusObserver:NSObjectProtocol?
    private var keyWindowObserver:NSObjectProtocol?
    init(state:AppState) {
        self.state=state
        focusObserver=NotificationCenter.default.addObserver(forName:NSApplication.didResignActiveNotification,object:NSApp,queue:.main) { [weak self] _ in Task { @MainActor in if self?.keyboardMode == true { self?.hide() } } }
    }
    func show(inputs:[URL],mode:WheelMode,at point:NSPoint,keyboard:Bool=false) {
        guard !inputs.isEmpty,!state.working else { return }
        WheelFeedback.shared.prepare()
        self.inputs=inputs;self.mode=mode;keyboardMode=keyboard
        var items:[WheelItem]=[]
        if inputs.first?.isFileURL == false {
            let media=WebMediaDragPayload.parse(urls:inputs.map(\.absoluteString))
            let video=media?.videoURL != nil
            let photos=media?.isInstagramPhotoPost == true
            items=[WheelItem(title:photos ? "Download photos…":video ? "Download video…":media?.imageURL != nil ? "Download image…":"Download…",symbol:photos ? "photo.on.rectangle":video ? "play.rectangle":"arrow.down.circle",webAction:.download)]
            if video,media?.imageURL != nil { items.append(WheelItem(title:"Download thumbnail…",symbol:"photo",webAction:.thumbnail)) }
            if !photos,(video || media?.imageURL == nil) { items.append(WheelItem(title:"Audio MP3…",symbol:"waveform",webAction:.audio)) }
            items += [WheelItem(title:"Copy link",symbol:"link",webAction:.copy),WheelItem(title:"Open link",symbol:"safari",webAction:.open)]
        } else { switch mode {
        case .convert: items=FeatureCatalog.formats(for:inputs).map{WheelItem(title:$0 == "gz" ? "GZIP":$0.uppercased(),operation:.convert($0))}
        case .tools: items=FeatureCatalog.tools(for:inputs).map{WheelItem(title:FeatureCatalog.toolNames[$0] ?? $0.title,symbol:FeatureCatalog.toolSymbols[$0],operation:.tool($0))}
        case .actions: items=FileAction.allCases.map{WheelItem(title:$0.title,symbol:$0.symbol,action:$0)}
        } }
        let exists=panel?.isVisible ?? false
        let desiredSize=CGFloat(320*UserDefaults.standard.double(forKey:"wheelScale"))
        if let panel,!exists,abs(panel.frame.width-desiredSize)>0.5 { panel.setContentSize(NSSize(width:desiredSize,height:desiredSize));panel.contentView?.layer?.cornerRadius=desiredSize/2 }
        if panel == nil {
            let size=CGFloat(320*UserDefaults.standard.double(forKey:"wheelScale"))
            let p=WheelPanel(contentRect:NSRect(x:0,y:0,width:size,height:size),styleMask:[.borderless,.nonactivatingPanel],backing:.buffered,defer:false)
            p.backgroundColor = .clear;p.isOpaque=false;p.hasShadow=false;p.level = .statusBar;p.collectionBehavior=[.canJoinAllSpaces,.fullScreenAuxiliary];p.hidesOnDeactivate=false
            let effect=NSVisualEffectView(frame:NSRect(x:0,y:0,width:size,height:size));effect.material = .popover;effect.blendingMode = .behindWindow;effect.state = .active;effect.wantsLayer=true;effect.layer?.cornerRadius=size/2;effect.layer?.masksToBounds=true
            let view=WheelView(frame:effect.bounds);view.autoresizingMask=[.width,.height];effect.addSubview(view);p.contentView=effect
            view.onSelect = { [weak self] item in self?.apply(item) };view.onCancel = { [weak self] in self?.hide() }
            view.onDragInputs = { [weak self] urls in guard let self else { return };self.show(inputs:urls,mode:self.mode,at:NSEvent.mouseLocation) }
            self.panel=p;self.wheel=view
            keyWindowObserver=NotificationCenter.default.addObserver(forName:NSWindow.didResignKeyNotification,object:p,queue:.main) { [weak self] _ in Task { @MainActor in if self?.keyboardMode == true { self?.hide() } } }
        }
        wheel?.centerTitle=inputs.first?.isFileURL == false ? "Web media":nil;wheel?.items=items;wheel?.inputs=inputs;wheel?.mode=mode;wheel?.selected=keyboard ? (items.isEmpty ? nil:0):nil
        if !exists,let p=panel {
            let screen=NSScreen.screens.first(where:{$0.frame.contains(point)}) ?? NSScreen.main
            let visible=screen?.visibleFrame ?? NSRect(x:0,y:0,width:1440,height:900)
            let x=min(max(visible.minX+4,point.x-p.frame.width/2),visible.maxX-p.frame.width-4)
            let y=min(max(visible.minY+4,point.y-p.frame.height/2),visible.maxY-p.frame.height-4)
            p.setFrameOrigin(NSPoint(x:x,y:y))
        }
        if let effect=panel?.contentView as? NSVisualEffectView { effect.state=UserDefaults.standard.string(forKey:"conversionFanTheme")=="metallic" ? .inactive:.active }
        if !exists,NashiMotion.enabled {
            panel?.alphaValue=0;panel?.orderFrontRegardless()
            NSAnimationContext.runAnimationGroup { context in context.duration=0.13;panel?.animator().alphaValue=1 }
        } else { panel?.alphaValue=1;panel?.orderFrontRegardless() }
        wheel?.needsDisplay=true;wheel?.updateAccessibility()
        if keyboard { panel?.makeKey();panel?.makeFirstResponder(wheel) }
        if keyMonitor == nil {
            keyMonitor=NSEvent.addLocalMonitorForEvents(matching:[.keyDown,.flagsChanged]) { [weak self] event in
                guard let self,self.panel?.isVisible == true else { return event }
                if self.inputs.first?.isFileURL != false,event.type == .flagsChanged,let next=DragMonitor.mode(for:event.modifierFlags),next != self.mode { self.show(inputs:self.inputs,mode:next,at:NSEvent.mouseLocation,keyboard:keyboard);return nil }
                if self.inputs.first?.isFileURL != false,event.type == .keyDown,let next=DragMonitor.mode(for:event.modifierFlags),next != self.mode { self.show(inputs:self.inputs,mode:next,at:NSEvent.mouseLocation,keyboard:keyboard);return nil }
                if event.type == .keyDown { self.wheel?.keyDown(with:event);return nil };return event
            }
        }
    }
    func apply(_ item:WheelItem) {
        hide()
        if let action=item.webAction,let media=WebMediaDragPayload.parse(urls:inputs.map(\.absoluteString)) {
            switch action {
            case .download:state.onWebDownload?(media.primaryURL,media.isInstagramPhotoPost || (media.videoURL == nil && media.imageURL != nil) ? .original:.video)
            case .thumbnail:if let image=media.imageURL { state.onWebDownload?(image,.original) }
            case .audio:state.onWebDownload?(media.videoURL ?? media.primaryURL,.audioMP3)
            case .copy:NSPasteboard.general.clearContents();NSPasteboard.general.writeObjects([media.primaryURL as NSURL])
            case .open:NSWorkspace.shared.open(media.primaryURL)
            };return
        }
        // A job may have started while the wheel was open; keep its selection.
        guard !state.working else { return }
        state.inputs=inputs;if let action=item.action { state.perform(action) };if let op=item.operation { state.select(op) }
    }
    func hide() { keyboardMode=false;panel?.orderOut(nil);if let monitor=keyMonitor { NSEvent.removeMonitor(monitor);keyMonitor=nil } }
    func endDrag() { if !keyboardMode { hide() } }
    deinit { if let focusObserver { NotificationCenter.default.removeObserver(focusObserver) };if let keyWindowObserver { NotificationCenter.default.removeObserver(keyWindowObserver) } }
}

final class WheelView:NSView {
    var items:[WheelItem]=[] { didSet { labelLayouts.removeAll() } }
    private var labelLayouts:[Int:WheelLabelLayout.Content]=[:]
    private var labelBounds=NSRect.zero
    var inputs:[URL]=[]
    var mode:WheelMode = .convert
    var centerTitle:String?
    var selected:Int? {
        didSet {
            guard oldValue != selected else { return }
            needsDisplay=true
            if selected != nil { WheelFeedback.shared.selectionChanged() }
        }
    }
    var onSelect:((WheelItem)->Void)?
    var onCancel:(()->Void)?
    var onDragInputs:(([URL])->Void)?
    private var tracking:NSTrackingArea?
    override var acceptsFirstResponder:Bool { true }
    override init(frame:NSRect) { super.init(frame:frame);registerForDraggedTypes(WebLinkPayload.pasteboardTypes);setAccessibilityRole(.group);setAccessibilityLabel("Nashi \(mode.rawValue) wheel") }
    required init?(coder:NSCoder) { fatalError() }
    override func updateTrackingAreas() { if let tracking { removeTrackingArea(tracking) };tracking=NSTrackingArea(rect:bounds,options:[.activeAlways,.mouseMoved,.mouseEnteredAndExited,.inVisibleRect],owner:self);addTrackingArea(tracking!);super.updateTrackingAreas() }
    var geometry:WheelGeometry { WheelGeometry(bounds:bounds,count:items.count) }
    var center:NSPoint { geometry.center }
    var radius:CGFloat { geometry.radius }
    var inner:CGFloat { geometry.inner }
    func petal(_ index:Int)->NSBezierPath { geometry.petal(index) }
    override func draw(_ dirtyRect:NSRect) {
        if labelBounds != bounds { labelLayouts.removeAll();labelBounds=bounds }
        let solid=UserDefaults.standard.string(forKey:"conversionFanTheme")=="metallic"
        let backdrop=NSBezierPath(ovalIn:bounds.insetBy(dx:bounds.width*0.035,dy:bounds.height*0.035));NSColor.windowBackgroundColor.withAlphaComponent(0.22).setFill();backdrop.fill()
        NSColor.gray.withAlphaComponent(0.17).setStroke();backdrop.lineWidth=0.7;backdrop.stroke()
        for (i,item) in items.enumerated() {
            let path=petal(i)
            if solid,selected != i,let gradient=NSGradient(starting:NSColor(calibratedWhite:0.36,alpha:1),ending:NSColor(calibratedWhite:0.20,alpha:1)) { gradient.draw(in:path,angle:90) }
            else if selected==i,let gradient=NSGradient(starting:NashiPalette.gold,ending:NSColor(NashiPalette.goldEnd)) { gradient.draw(in:path,angle:80) }
            else { NSColor.controlBackgroundColor.withAlphaComponent(0.88).setFill();path.fill() }
            NSColor.gray.withAlphaComponent(0.24).setStroke();path.lineWidth=0.7;path.stroke()
            let foreground=selected==i ? NashiPalette.ink:solid ? NSColor.white:NSColor.labelColor
            let scale=min(bounds.width,bounds.height)/320
            let source=item.symbol.flatMap { NSImage(systemSymbolName:$0,accessibilityDescription:item.title) }
            let baseFont=NSFont.systemFont(ofSize:(source == nil ? 13:9.5)*scale,weight:.semibold)
            let font=baseFont.fontDescriptor.withDesign(.rounded).flatMap{NSFont(descriptor:$0,size:baseFont.pointSize)} ?? baseFont
            let layout:WheelLabelLayout.Content
            if let cached=labelLayouts[i] { layout=cached }
            else {
                layout=WheelLabelLayout.fitted(L10n.uppercase(item.title),font:font,color:.labelColor,
                                               inside:path,centers:geometry.labelCenters(i),
                                               maximumWidth:84*scale,iconSize:source == nil ? nil:20*scale,scale:scale)
                labelLayouts[i]=layout
            }
            if let placement=item.windowPlacement,let slot=layout.iconRect {
                WindowPlacementGlyph.draw(placement,in:slot,color:foreground)
            } else if let source,let slot=layout.iconRect {
                let image=source.withSymbolConfiguration(.init(paletteColors:[foreground])) ?? source
                image.isTemplate=false
                let fit=min(slot.width/max(1,image.size.width),slot.height/max(1,image.size.height))
                let size=NSSize(width:image.size.width*fit,height:image.size.height*fit)
                let rect=NSRect(x:slot.midX-size.width/2,y:slot.midY-size.height/2,width:size.width,height:size.height)
                image.draw(in:rect)
            }
            WheelLabelLayout.draw(layout,color:foreground)
        }
        let disc=geometry.disc;NSColor.controlBackgroundColor.setFill();disc.fill()
        func key(_ setting:String,_ fallback:String)->String { let name=UserDefaults.standard.string(forKey:setting) ?? fallback;return ModifierChord.legacy(name)?.symbols ?? name }
        let emptyTitle=mode == .tools ? "No file tools\n\(key("convertKey","shift")) Convert\n\(key("actionsKey","control")) Actions":"No formats\n\(key("toolsKey","command")) Tools\n\(key("actionsKey","control")) Actions"
        let title=selected.flatMap{items.indices.contains($0) ? items[$0].title:nil} ?? (items.isEmpty ? emptyTitle:(centerTitle ?? mode.rawValue.capitalized))
        let layout=WheelLabelLayout.fitted(L10n.text(title),font:NSFont.systemFont(ofSize:(items.isEmpty ? 10:11)*geometry.scale,weight:.semibold),
                                           color:.labelColor,inside:disc,centers:[center],
                                           maximumWidth:inner*2-16*geometry.scale,scale:geometry.scale)
        WheelLabelLayout.draw(layout)
    }
    /// Uses the same rendered label geometry; never substitutes a test-only layout.
    func labelAuditIssues(_ index:Int,title:String)->[String] {
        guard let layout=labelLayouts[index] else { return ["Wheel label did not render: "+title] }
        var issues:[String]=[]
        if !WheelLabelLayout.fits(layout,inside:petal(index),padding:3.5*geometry.scale) {issues.append("Wheel label is clipped: "+title)}
        for line in layout.lines where abs(line.ink.midX-layout.textRect.midX)>0.5*geometry.scale {issues.append("Wheel line is not centered: "+line.text)}
        return issues
    }
    func updateSelection(_ point:NSPoint) { selected=items.indices.first{petal($0).contains(point)} }
    func updateAccessibility() {
        setAccessibilityLabel(inputs.first?.isFileURL == false ? "Nashi web media wheel":"Nashi \(mode.rawValue) wheel")
        let children=items.enumerated().map { index,item -> WheelAccessibilityItem in
            let child=WheelAccessibilityItem();child.setAccessibilityRole(.button);child.setAccessibilityEnabled(true);child.setAccessibilityLabel(L10n.text(item.operation?.title ?? item.title));child.setAccessibilityParent(self)
            if let window { child.setAccessibilityFrame(window.convertToScreen(convert(petal(index).bounds,to:nil))) }
            child.activate = { [weak self] in guard let self else { return false };self.onSelect?(item);return true };return child
        }
        setAccessibilityChildren(children)
    }
    override func mouseMoved(with event:NSEvent) { updateSelection(convert(event.locationInWindow,from:nil)) }
    override func mouseExited(with event:NSEvent) { selected=nil }
    override func mouseDown(with event:NSEvent) { updateSelection(convert(event.locationInWindow,from:nil));if let selected { onSelect?(items[selected]) } else { onCancel?() } }
    override func keyDown(with event:NSEvent) {
        switch event.keyCode {
        case 53,48: onCancel?()
        case 123,126: if !items.isEmpty { selected=((selected ?? 0)-1+items.count)%items.count }
        case 124,125: if !items.isEmpty { selected=((selected ?? -1)+1)%items.count }
        case 115: selected=items.isEmpty ? nil:0
        case 119: selected=items.isEmpty ? nil:items.count-1
        case 36,49: if let selected { onSelect?(items[selected]) }
        default: break
        }
    }
    override func scrollWheel(with event:NSEvent) { onCancel?() }
    private func incoming(_ sender:NSDraggingInfo)->Bool {
        let urls=WebLinkPayload.urls(from:sender.draggingPasteboard)
        guard !urls.isEmpty else { return false }
        if urls != inputs { onDragInputs?(urls) }
        // show() refuses new inputs while a job runs; the petals still belong to the old ones.
        guard urls == inputs else { selected=nil;return false }
        updateSelection(convert(sender.draggingLocation,from:nil));return true
    }
    override func draggingEntered(_ sender:NSDraggingInfo)->NSDragOperation { incoming(sender) ? .copy:[] }
    override func draggingUpdated(_ sender:NSDraggingInfo)->NSDragOperation { incoming(sender) ? .copy:[] }
    override func draggingExited(_ sender:NSDraggingInfo?) { selected=nil }
    override func prepareForDragOperation(_ sender:NSDraggingInfo)->Bool { selected != nil }
    override func performDragOperation(_ sender:NSDraggingInfo)->Bool { guard incoming(sender),let selected,items.indices.contains(selected) else { onCancel?();return false };onSelect?(items[selected]);return true }
    override func concludeDragOperation(_ sender:NSDraggingInfo?) { onCancel?() }
}
final class WheelAccessibilityItem:NSAccessibilityElement {
    var activate:(()->Bool)?
    override func accessibilityPerformPress()->Bool { activate?() ?? false }
}

@MainActor final class DragMonitor {
    private var timer:Timer?
    private var shown=false
    private var lastMode:WheelMode?
    private var downOrigin=NSPoint.zero
    private var session=DragPasteboardSession()
    private var traceState=""
    let onActivate:([URL],WheelMode,NSPoint)->Void
    let onEnd:()->Void
    init(onEnd:@escaping()->Void,onActivate:@escaping([URL],WheelMode,NSPoint)->Void) {
        self.onEnd=onEnd
        self.onActivate=onActivate
        timer=Timer(timeInterval:0.025,repeats:true) { [weak self] _ in Task { @MainActor in self?.tick() } }
        RunLoop.main.add(timer!,forMode:.common)
    }
    static func mode(for flags:NSEvent.ModifierFlags)->WheelMode? {
        switch ModifierPreferences.wheel(for:flags) {
        case .conversions:return .convert
        case .tools:return .tools
        case .actions:return .actions
        case nil:return nil
        }
    }
    private func tick() {
        let down=NSEvent.pressedMouseButtons & 1 != 0 || CGEventSource.buttonState(.combinedSessionState,button:.left)
        let globalFlags=NSEvent.ModifierFlags(rawValue:UInt(CGEventSource.flagsState(.combinedSessionState).rawValue))
        let flags=NSEvent.modifierFlags.union(globalFlags).intersection([.shift,.command,.control,.option])
        let board=NSPasteboard(name:.drag)
        let fresh=session.containsFreshDrag(isDown:down,changeCount:board.changeCount,now:ProcessInfo.processInfo.systemUptime)
        #if DEBUG
        let trace="drag down=\(down) flags=\(flags.rawValue) pasteboard=\(board.changeCount) shown=\(shown)"
        if trace != traceState {
            traceState=trace;NSLog("Nashi %@",trace)
            let url=FileManager.default.temporaryDirectory.appendingPathComponent("nashi-drag-\(ProcessInfo.processInfo.processIdentifier).log")
            if !FileManager.default.fileExists(atPath:url.path) { FileManager.default.createFile(atPath:url.path,contents:nil,attributes:[.posixPermissions:0o600]) }
            if let handle=try? FileHandle(forWritingTo:url) { _ = try? handle.seekToEnd();try? handle.write(contentsOf:Data((trace+"\n").utf8));try? handle.close() }
        }
        #endif
        guard down,UserDefaults.standard.bool(forKey:"enabled") else {
            if shown { onEnd() }
            shown=false;lastMode=nil;downOrigin=NSEvent.mouseLocation;return
        }
        let point=NSEvent.mouseLocation
        guard hypot(point.x-downOrigin.x,point.y-downOrigin.y)>4,fresh else { return }
        guard let mode=Self.mode(for:flags),!shown || mode != lastMode else { return }
        let urls=WebLinkPayload.urls(from:board)
        guard !urls.isEmpty else { return }
        if urls.first?.isFileURL != false || mode == .tools {
            shown=true;lastMode=mode;onActivate(urls,mode,NSEvent.mouseLocation)
        }
    }
}
