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
    private var stage:WheelStageView?
    var wheel:WheelView? { stage?.wheel }
    var mode:WheelMode = .convert
    var inputs:[URL]=[]
    var keyMonitor:Any?
    private var keyboardMode=false
    private var closing=false
    private var focusObserver:NSObjectProtocol?
    private var keyWindowObserver:NSObjectProtocol?
    private var animator:WheelAnimator?
    private var dustPanel:NSPanel?
    init(state:AppState) {
        self.state=state
        focusObserver=NotificationCenter.default.addObserver(forName:NSApplication.didResignActiveNotification,object:NSApp,queue:.main) { [weak self] _ in Task { @MainActor in if self?.keyboardMode == true { self?.hide() } } }
    }
    func show(inputs:[URL],mode:WheelMode,at point:NSPoint,keyboard:Bool=false) {
        guard !inputs.isEmpty,!state.working else { return }
        WheelFeedback.shared.prepare()
        let exists=panel?.isVisible == true && !closing
        let switched=exists && (mode != self.mode || inputs != self.inputs)
        if closing { closing=false;animator?.cancel();removeDust() }
        self.inputs=inputs;self.mode=mode;keyboardMode=keyboard
        var items:[WheelItem]=[]
        let web=inputs.first?.isFileURL == false
        if web {
            let media=WebMediaDragPayload.parse(urls:inputs.map(\.absoluteString))
            let video=media?.videoURL != nil
            let photos=media?.isInstagramPhotoPost == true
            items=[WheelItem(title:photos ? "Download photos…":video ? "Download video…":media?.imageURL != nil ? "Download image…":"Download…",symbol:photos ? "photo.on.rectangle":video ? "play.rectangle":"arrow.down.circle",webAction:.download)]
            if video,media?.imageURL != nil { items.append(WheelItem(title:"Download thumbnail…",symbol:"photo",webAction:.thumbnail)) }
            if !photos,(video || media?.imageURL == nil) { items.append(WheelItem(title:"Audio MP3…",symbol:"waveform",webAction:.audio)) }
            items += [WheelItem(title:"Copy link",symbol:"link",webAction:.copy),WheelItem(title:"Open link",symbol:"safari",webAction:.open)]
        } else { switch mode {
        case .convert: items=WheelItem.conversions(for:inputs)
        case .tools: items=FeatureCatalog.tools(for:inputs).map{WheelItem(title:FeatureCatalog.toolNames[$0] ?? $0.title,symbol:FeatureCatalog.toolSymbols[$0],operation:.tool($0))}
        case .actions: items=FileAction.allCases.map{WheelItem(title:$0.title,symbol:$0.symbol,action:$0)}
        } }
        if stage == nil { makePanel() }
        guard let panel,let stage else { return }
        let wheel=stage.wheel
        if !exists { panel.setContentSize(stage.layout(size:CGFloat(320*UserDefaults.standard.double(forKey:"wheelScale"))));stage.applyFinish() }
        stage.showsBar = !web;stage.bar.mode=mode
        stage.capsuleLabel.stringValue=inputs.count == 1 ? inputs[0].lastPathComponent:L10n.text("\(inputs.count) files")
        wheel.centerTitle=web ? "Web media":nil;wheel.items=items;wheel.inputs=inputs;wheel.mode=mode;wheel.selected=keyboard ? (items.isEmpty ? nil:0):nil
        if !exists {
            // Center the wheel itself, not the panel, on the pointer; keep the capsule on screen.
            let screen=NSScreen.screens.first(where:{$0.frame.contains(point)}) ?? NSScreen.main
            let visible=screen?.visibleFrame ?? NSRect(x:0,y:0,width:1440,height:900),wheelFrame=stage.container.frame
            let x=min(max(visible.minX+4-wheelFrame.minX,point.x-wheelFrame.midX),visible.maxX-4-wheelFrame.maxX)
            let y=min(max(visible.minY+4-stage.barHost.frame.minY,point.y-wheelFrame.midY),visible.maxY-4-wheelFrame.maxY)
            panel.setFrameOrigin(NSPoint(x:x,y:y))
            panel.ignoresMouseEvents=false;panel.alphaValue=1
            animate(.open) { [weak self] in self?.removeDust() }
            panel.orderFrontRegardless();dustPanel?.orderFrontRegardless()
        } else if switched { animate(.modeSwitch) }
        wheel.needsDisplay=true;wheel.updateAccessibility();stage.bar.updateAccessibility()
        if keyboard { panel.makeKey();panel.makeFirstResponder(wheel) }
        if keyMonitor == nil {
            keyMonitor=NSEvent.addLocalMonitorForEvents(matching:[.keyDown,.flagsChanged]) { [weak self] event in
                guard let self,self.panel?.isVisible == true,!self.closing else { return event }
                if self.inputs.first?.isFileURL != false,event.type == .flagsChanged,let next=DragMonitor.mode(for:event.modifierFlags),next != self.mode { self.show(inputs:self.inputs,mode:next,at:NSEvent.mouseLocation,keyboard:keyboard);return nil }
                if self.inputs.first?.isFileURL != false,event.type == .keyDown,let next=DragMonitor.mode(for:event.modifierFlags),next != self.mode { self.show(inputs:self.inputs,mode:next,at:NSEvent.mouseLocation,keyboard:keyboard);return nil }
                if event.type == .keyDown { self.wheel?.keyDown(with:event);return nil };return event
            }
        }
    }
    private func makePanel() {
        let p=WheelPanel(contentRect:NSRect(x:0,y:0,width:320,height:320),styleMask:[.borderless,.nonactivatingPanel],backing:.buffered,defer:false)
        p.backgroundColor = .clear;p.isOpaque=false;p.hasShadow=false;p.level = .statusBar;p.collectionBehavior=[.canJoinAllSpaces,.fullScreenAuxiliary];p.hidesOnDeactivate=false
        let view=WheelStageView(frame:p.contentLayoutRect);p.contentView=view
        view.wheel.onSelect = { [weak self] item in self?.apply(item) };view.wheel.onCancel = { [weak self] in self?.hide() };view.onCancel = { [weak self] in self?.hide() }
        view.wheel.onDragInputs = { [weak self] urls in guard let self else { return };self.show(inputs:urls,mode:self.mode,at:NSEvent.mouseLocation) }
        view.bar.onSelect = { [weak self] next in guard let self else { return };self.show(inputs:self.inputs,mode:next,at:NSEvent.mouseLocation,keyboard:self.keyboardMode) }
        panel=p;stage=view;animator=WheelAnimator(stage:view)
        animator?.onFrame = { [weak self] frame in self?.dustPanel?.alphaValue=frame.dustAlpha }
        keyWindowObserver=NotificationCenter.default.addObserver(forName:NSWindow.didResignKeyNotification,object:p,queue:.main) { [weak self] _ in Task { @MainActor in if self?.keyboardMode == true { self?.hide() } } }
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
        state.inputs=inputs;if let action=item.action { state.perform(action) };if let op=item.operation { state.select(op) }
    }
    func hide() {
        keyboardMode=false
        if let monitor=keyMonitor { NSEvent.removeMonitor(monitor);keyMonitor=nil }
        guard let panel,panel.isVisible,!closing else { return }
        closing=true;panel.ignoresMouseEvents=true
        animate(.close) { [weak self] in self?.finishHide() }
    }
    func endDrag() { if !keyboardMode { hide() } }
    private func finishHide() {
        closing=false;panel?.orderOut(nil);panel?.ignoresMouseEvents=false;removeDust()
        if let stage { stage.apply(.rest(stage.wheel.items.count)) }
    }
    private func animate(_ kind:WheelAnimator.Kind,done:(()->Void)?=nil) {
        let style:WheelAnimationStyle?=NashiMotion.enabled ? WheelAnimationStyle.current:nil
        if style == .snap,kind != .modeSwitch { startDust(assembling:kind == .open) }
        animator?.run(kind,style:style,done:done)
    }
    /// Snap plays in its own larger click-through panel so the dust can fly past the wheel.
    private func startDust(assembling:Bool) {
        removeDust()
        guard let panel,let stage,let image=stage.snapshot() else { return }
        let wheelRect=panel.convertToScreen(stage.convert(stage.container.frame,to:nil)),pad=stage.size*0.75,frame=wheelRect.insetBy(dx:-pad,dy:-pad)
        let dust=NSPanel(contentRect:frame,styleMask:[.borderless,.nonactivatingPanel],backing:.buffered,defer:false)
        dust.backgroundColor = .clear;dust.isOpaque=false;dust.hasShadow=false;dust.ignoresMouseEvents=true;dust.level = .statusBar
        dust.collectionBehavior=[.canJoinAllSpaces,.fullScreenAuxiliary];dust.hidesOnDeactivate=false
        let host=NSView(frame:NSRect(origin:.zero,size:frame.size));host.wantsLayer=true;dust.contentView=host
        if let layer=host.layer { WheelDust.play(in:layer,image:image,rect:NSRect(x:pad,y:pad,width:wheelRect.width,height:wheelRect.height),assembling:assembling,scale:stage.scale) }
        dust.orderFrontRegardless();dustPanel=dust
    }
    private func removeDust() { dustPanel?.orderOut(nil);dustPanel=nil }
    deinit { if let focusObserver { NotificationCenter.default.removeObserver(focusObserver) };if let keyWindowObserver { NotificationCenter.default.removeObserver(keyWindowObserver) } }
}

final class WheelView:NSView {
    var items:[WheelItem]=[] { didSet { labelLayouts.removeAll();hover=items.indices.map { $0 == selected ? 1:0 };clearFocus() } }
    private var labelLayouts:[Int:WheelLabelLayout.Content]=[:]
    private var labelBounds=NSRect.zero
    private var labelGlass:Bool?
    var inputs:[URL]=[]
    var mode:WheelMode = .convert
    var centerTitle:String?
    /// Entrance/exit frame from `WheelMotion`; nil draws the wheel at rest.
    var motion:WheelMotion? { didSet { syncFocusVisibility() } }
    /// Drawing into a bitmap (Snap dust, QA renders): the focused slice is painted inline.
    var renderingOffscreen=false
    private var hover:[CGFloat]=[]
    private struct Focus { let index:Int,root:CALayer,label:CALayer,glare:CALayer,rect:NSRect,mid:CGFloat }
    /// Off when a stage draws the disc on its own floating layer.
    var drawsDisc=true
    weak var companion:NSView?
    override var needsDisplay:Bool { didSet { if needsDisplay { companion?.needsDisplay=true } } }
    private var focus:Focus?
    var selected:Int? {
        didSet {
            guard oldValue != selected else { return }
            settleHover();updateFocus()
            if selected != nil { WheelFeedback.shared.selectionChanged() }
        }
    }
    var onSelect:((WheelItem)->Void)?
    var onCancel:(()->Void)?
    var onDragInputs:(([URL])->Void)?
    /// Pointer over the wheel, nil when it leaves; the stage leans the wheel toward it.
    var onPointer:((NSPoint?)->Void)?
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
    var glass:Bool { UserDefaults.standard.string(forKey:"conversionFanTheme") != "metallic" }
    override func draw(_ dirtyRect:NSRect) {
        let glass=self.glass
        if labelBounds != bounds || labelGlass != glass { labelLayouts.removeAll();labelBounds=bounds;labelGlass=glass }
        if hover.count != items.count { hover=items.indices.map { $0 == selected ? 1:0 } }
        if !glass {
            let backdrop=NSBezierPath(ovalIn:bounds.insetBy(dx:bounds.width*0.035,dy:bounds.height*0.035));NSColor.windowBackgroundColor.withAlphaComponent(0.22).setFill();backdrop.fill()
            NSColor.gray.withAlphaComponent(0.17).setStroke();backdrop.lineWidth=0.7;backdrop.stroke()
        }
        for (i,item) in items.enumerated() { drawPetal(i,item,glass:glass) }
        if drawsDisc || renderingOffscreen { drawDisc() }
    }
    private func drawPetal(_ i:Int,_ item:WheelItem,glass:Bool) {
        let m=motion.flatMap { i<$0.petals.count ? $0.petals[i]:nil } ?? PetalMotion()
        guard m.alpha>0.002,let context=NSGraphicsContext.current?.cgContext else { return }
        let g=geometry,h=focus?.index == i && !renderingOffscreen ? 0:hover.indices.contains(i) ? hover[i]:0
        let mid=(g.startAngle-CGFloat(i)*360/CGFloat(max(1,items.count)))*(.pi/180),lift=(m.offset+(glass ? 3.5*h:0))*g.scale
        context.saveGState();defer { context.restoreGState() }
        context.translateBy(x:center.x+lift*cos(mid),y:center.y+lift*sin(mid));context.rotate(by:-m.rotation * .pi/180);context.scaleBy(x:m.scale,y:m.scale);context.translateBy(x:-center.x,y:-center.y)
        context.setAlpha(m.alpha)
        let path=petal(i)
        guard glass else {
            if selected != i,let gradient=NSGradient(starting:NSColor(calibratedWhite:0.36,alpha:1),ending:NSColor(calibratedWhite:0.20,alpha:1)) { gradient.draw(in:path,angle:90) }
            else if let gradient=NSGradient(starting:NashiPalette.gold,ending:NSColor(NashiPalette.goldEnd)) { gradient.draw(in:path,angle:80) }
            NSColor.gray.withAlphaComponent(0.24).setStroke();path.lineWidth=0.7;path.stroke()
            drawLabel(i,item,glass:false,color:selected == i ? NashiPalette.ink:.white);return
        }
        let dark=nashiDark
        NSColor.white.withAlphaComponent(dark ? 0.06:0.14).setFill();path.fill()
        if h>0.01 {
            // The one tinted element: the slice under the pointer lifts out with a soft gold glow.
            context.saveGState();context.setAlpha(m.alpha*h)
            context.setShadow(offset:.zero,blur:18*g.scale,color:NashiPalette.gold.withAlphaComponent(0.5).cgColor)
            NashiPalette.gold.setFill();path.fill()
            NSGradient(starting:NashiPalette.gold,ending:NSColor(NashiPalette.goldEnd))?.draw(in:path,angle:mid*180 / .pi)
            context.restoreGState()
        }
        NSColor.white.withAlphaComponent(dark ? 0.1:0.4).setStroke();path.lineWidth=0.75;path.stroke()
        if h>0.01 { context.saveGState();path.addClip();context.setAlpha(m.alpha*h*0.8);NSColor.white.withAlphaComponent(0.9).setStroke();path.lineWidth=2.5;path.stroke();context.restoreGState() }
        if h<0.99 { context.saveGState();context.setAlpha(m.alpha*(1-h));drawLabel(i,item,glass:true,color:.labelColor);context.restoreGState() }
        if h>0.01 { context.saveGState();context.setAlpha(m.alpha*h);drawLabel(i,item,glass:true,color:NashiPalette.ink);context.restoreGState() }
    }
    private func drawLabel(_ i:Int,_ item:WheelItem,glass:Bool,color:NSColor,icon:Bool=true,text:Bool=true) {
        let scale=min(bounds.width,bounds.height)/320
        let source=item.symbol.flatMap { NSImage(systemSymbolName:$0,accessibilityDescription:item.title) }
        let layout:WheelLabelLayout.Content
        if let cached=labelLayouts[i] { layout=cached }
        else {
            // Glass: sentence case, bold format names over their symbol, small labels under tool symbols.
            let format:Bool
            if case .convert? = item.operation { format=true } else { format=false }
            let size:CGFloat=glass ? (format ? 13:source == nil ? 15:10.5):(source == nil ? 13:9.5)
            let font=NSFont.systemFont(ofSize:size*scale,weight:glass && (format || source == nil) ? .bold:.semibold).nashiRounded
            layout=WheelLabelLayout.fitted(glass ? L10n.text(item.title):L10n.uppercase(item.title),font:font,color:.labelColor,
                                           inside:petal(i),centers:geometry.labelCenters(i),
                                           maximumWidth:(glass ? 80:84)*scale,iconSize:source == nil ? nil:(glass ? 19:20)*scale,scale:scale)
            labelLayouts[i]=layout
        }
        if !icon {
        } else if let placement=item.windowPlacement,let slot=layout.iconRect {
            WindowPlacementGlyph.draw(placement,in:slot,color:color)
        } else if let source,let slot=layout.iconRect {
            let image=source.withSymbolConfiguration(.init(paletteColors:[color])) ?? source
            image.isTemplate=false
            let fit=min(slot.width/max(1,image.size.width),slot.height/max(1,image.size.height))
            let size=NSSize(width:image.size.width*fit,height:image.size.height*fit)
            image.draw(in:NSRect(x:slot.midX-size.width/2,y:slot.midY-size.height/2,width:size.width,height:size.height))
        }
        if text { WheelLabelLayout.draw(layout,color:color) }
    }
    func drawDisc() {
        let glass=self.glass
        let g=geometry,discScale=motion?.discScale ?? 1,discAlpha=motion?.discAlpha ?? 1
        guard discAlpha>0.002,let context=NSGraphicsContext.current?.cgContext else { return }
        context.saveGState();defer { context.restoreGState() }
        context.translateBy(x:center.x,y:center.y);context.scaleBy(x:discScale,y:discScale);context.translateBy(x:-center.x,y:-center.y);context.setAlpha(discAlpha)
        func key(_ setting:String,_ fallback:String)->String { let name=UserDefaults.standard.string(forKey:setting) ?? fallback;return ModifierChord.legacy(name)?.symbols ?? name }
        let emptyTitle=mode == .tools ? "No file tools\n\(key("convertKey","command")) Convert\n\(key("actionsKey","control")) Actions":"No formats\n\(key("toolsKey","option")) Tools\n\(key("actionsKey","control")) Actions"
        let chosen=selected.flatMap { items.indices.contains($0) ? items[$0].title:nil }
        guard glass else {
            let disc=g.disc;NSColor.controlBackgroundColor.setFill();disc.fill()
            let title=chosen ?? (items.isEmpty ? emptyTitle:(centerTitle ?? mode.rawValue.capitalized))
            WheelLabelLayout.draw(WheelLabelLayout.fitted(L10n.text(title),font:NSFont.systemFont(ofSize:(items.isEmpty ? 10:11)*g.scale,weight:.semibold),
                                                          color:.labelColor,inside:disc,centers:[center],maximumWidth:inner*2-16*g.scale,scale:g.scale))
            return
        }
        let r=inner-7*g.scale,disc=NSBezierPath(ovalIn:NSRect(x:center.x-r,y:center.y-r,width:2*r,height:2*r)),dark=nashiDark
        context.saveGState();context.setShadow(offset:CGSize(width:0,height:-3*g.scale),blur:14*g.scale,color:NSColor.black.withAlphaComponent(0.14).cgColor)
        (dark ? NSColor(white:0.2,alpha:0.72):NSColor.white.withAlphaComponent(0.62)).setFill();disc.fill();context.restoreGState()
        NSColor.white.withAlphaComponent(dark ? 0.12:0.6).setStroke();disc.lineWidth=1;disc.stroke()
        if items.isEmpty {
            WheelLabelLayout.draw(WheelLabelLayout.fitted(L10n.text(emptyTitle),font:NSFont.systemFont(ofSize:10*g.scale,weight:.semibold).nashiRounded,color:.labelColor,inside:disc,centers:[center],maximumWidth:r*2-12*g.scale,scale:g.scale))
            return
        }
        let titleFont=NSFont.systemFont(ofSize:(chosen == nil ? 12.5:14)*g.scale,weight:.semibold).nashiRounded
        let title=chosen.map { L10n.text($0) } ?? centerTitle.map { L10n.text($0) } ?? fileTitle(font:titleFont,width:r*2-22*g.scale)
        if centerTitle == nil {
            // File wheels name their mode above the title, like a small-caps eyebrow.
            let caption=NSAttributedString(string:L10n.uppercase(mode == .convert ? "Convert":mode == .tools ? "Tools":"Actions"),attributes:[.font:NSFont.systemFont(ofSize:9*g.scale,weight:.bold).nashiRounded,.kern:0.9*g.scale,.foregroundColor:NSColor.secondaryLabelColor])
            let size=caption.size();caption.draw(at:NSPoint(x:center.x-size.width/2,y:center.y+11*g.scale))
        }
        WheelLabelLayout.draw(WheelLabelLayout.fitted(title,font:titleFont,color:.labelColor,inside:disc,centers:[NSPoint(x:center.x,y:center.y-(centerTitle == nil ? 6:0)*g.scale)],maximumWidth:r*2-22*g.scale,scale:g.scale))
    }
    /// One file name on one line, shortened in the middle so the extension stays visible.
    private func fileTitle(font:NSFont,width:CGFloat)->String {
        guard inputs.count == 1 else { return L10n.text("\(inputs.count) files") }
        let name=inputs[0].lastPathComponent
        func fits(_ text:String)->Bool { NSAttributedString(string:text,attributes:[.font:font]).size().width<=width }
        if fits(name) { return name }
        var head=name.count/2,tail=name.count-head
        while head+tail>4 {
            if head>=tail { head-=1 } else { tail-=1 }
            let candidate=String(name.prefix(head))+"…"+String(name.suffix(tail))
            if fits(candidate) { return candidate }
        }
        return String(name.prefix(2))+"…"
    }
    /// Jumps the hover glow to the current selection (renders, tests).
    func settleHover() { hover=items.indices.map { $0 == selected ? 1:0 };needsDisplay=true }
    // Apple TV-style focus: the slice under the pointer lifts out on its own layer, tilts
    // toward the pointer in 3D, carries a glare that follows it and a parallax label.
    private var usesFocusLayer:Bool { glass && NashiMotion.enabled && layer != nil && !renderingOffscreen }
    private func updateFocus() {
        if let old=focus {
            focus=nil
            CATransaction.begin();CATransaction.setAnimationDuration(0.16);CATransaction.setCompletionBlock { old.root.removeFromSuperlayer() }
            old.root.opacity=0;old.root.transform=CATransform3DIdentity
            CATransaction.commit()
        }
        guard let index=selected,usesFocusLayer,items.indices.contains(index),let host=layer else { return }
        let g=geometry,path=petal(index),rect=path.bounds.insetBy(dx:-10*g.scale,dy:-10*g.scale)
        let mid=(g.startAngle-CGFloat(index)*360/CGFloat(max(1,items.count)))*(.pi/180),item=items[index]
        guard let plate=image(rect,{ drawFocusPlate(path,mid:mid) }),let text=image(rect,{ drawLabel(index,item,glass:true,color:NashiPalette.ink,icon:false) }),
              let symbol=image(rect,{ drawLabel(index,item,glass:true,color:NashiPalette.ink,text:false) }) else { return }
        let shape=path.copy() as! NSBezierPath;shape.transform(using:AffineTransform(translationByX:-rect.minX,byY:-rect.minY))
        CATransaction.begin();CATransaction.setDisableActions(true)
        let root=CALayer();root.frame=rect;root.contents=plate;root.contentsGravity = .resize
        root.shadowPath=shape.nashiCGPath;root.shadowColor=NashiPalette.gold.cgColor;root.shadowOpacity=0.55;root.shadowRadius=16*g.scale;root.shadowOffset=CGSize(width:0,height:-3*g.scale)
        let clip=CALayer();clip.frame=root.bounds;let mask=CAShapeLayer();mask.path=shape.nashiCGPath;clip.mask=mask
        let glare=CAGradientLayer();glare.type = .radial;glare.colors=[NSColor.white.withAlphaComponent(0.6).cgColor,NSColor.white.withAlphaComponent(0).cgColor]
        glare.startPoint=CGPoint(x:0.5,y:0.5);glare.endPoint=CGPoint(x:1,y:1);let side=max(rect.width,rect.height)*1.1
        glare.bounds=CGRect(x:0,y:0,width:side,height:side);glare.position=CGPoint(x:rect.width/2,y:rect.height*0.7);glare.opacity=0
        clip.addSublayer(glare)
        let label=CALayer();label.frame=root.bounds;label.contents=text;label.contentsGravity = .resize
        // The symbol on its own layer, pivoting on its center, so it can play like a little GIF.
        let icon=CALayer();icon.contents=symbol;icon.contentsGravity = .resize
        if let slot=labelLayouts[index]?.iconRect { icon.anchorPoint=CGPoint(x:(slot.midX-rect.minX)/rect.width,y:(slot.midY-rect.minY)/rect.height) }
        icon.frame=root.bounds;label.addSublayer(icon)
        root.addSublayer(clip);root.addSublayer(label);host.addSublayer(root)
        let next=Focus(index:index,root:root,label:label,glare:glare,rect:rect,mid:mid);focus=next
        let target=focusTransform(next,tilt:.zero);root.transform=target;root.opacity=1
        CATransaction.commit()
        let lift=CASpringAnimation(keyPath:"transform");lift.fromValue=NSValue(caTransform3D:CATransform3DIdentity);lift.toValue=NSValue(caTransform3D:target)
        lift.damping=15;lift.stiffness=280;lift.duration=lift.settlingDuration;root.add(lift,forKey:"lift")
        let fade=CABasicAnimation(keyPath:"opacity");fade.fromValue=0;fade.toValue=1;fade.duration=0.12;root.add(fade,forKey:"fade")
        if labelLayouts[index]?.iconRect != nil { playSymbol(icon) }
        syncFocusVisibility()
    }
    /// While entrance motion still hides the focused slice, its lifted copy waits too.
    private func syncFocusVisibility() {
        guard let focus else { return }
        CATransaction.begin();CATransaction.setDisableActions(true)
        focus.root.isHidden=(motion.flatMap { focus.index<$0.petals.count ? $0.petals[focus.index].alpha:nil } ?? 1)<0.99
        CATransaction.commit()
    }
    private func focusTransform(_ focus:Focus,tilt:CGPoint)->CATransform3D {
        var t=CATransform3DIdentity;t.m34 = -1/(700*geometry.scale)
        let lift=4*geometry.scale
        t=CATransform3DTranslate(t,lift*cos(focus.mid),lift*sin(focus.mid),0)
        t=CATransform3DRotate(t,tilt.x*0.2,0,1,0)
        t=CATransform3DRotate(t,-tilt.y*0.2,1,0,0)
        return CATransform3DScale(t,1.07,1.07,1)
    }
    /// The pointer's side of the slice sinks back, the glare and label follow it.
    private func updateTilt(_ point:NSPoint) {
        guard let focus,focus.root.superlayer != nil else { return }
        let tilt=CGPoint(x:WheelMotion.clamp((point.x-focus.rect.midX)/(focus.rect.width/2),-1,1),y:WheelMotion.clamp((point.y-focus.rect.midY)/(focus.rect.height/2),-1,1))
        CATransaction.begin();CATransaction.setAnimationDuration(0.15);CATransaction.setAnimationTimingFunction(CAMediaTimingFunction(name:.easeOut))
        focus.root.transform=focusTransform(focus,tilt:tilt)
        focus.label.transform=CATransform3DMakeTranslation(tilt.x*3*geometry.scale,tilt.y*3*geometry.scale,0)
        focus.glare.position=CGPoint(x:point.x-focus.rect.minX,y:point.y-focus.rect.minY);focus.glare.opacity=1
        CATransaction.commit()
    }
    /// A pop on arrival, then a gentle loop for as long as the slice is focused. Core Animation
    /// runs it on the render server: no redraws, no cost to the rest of the wheel.
    private func playSymbol(_ icon:CALayer) {
        let now=CACurrentMediaTime()
        let pop=CAKeyframeAnimation(keyPath:"transform.scale");pop.values=[0.6,1.22,0.94,1.04,1];pop.keyTimes=[0,0.35,0.6,0.8,1];pop.duration=0.55
        pop.timingFunction=CAMediaTimingFunction(name:.easeOut);icon.add(pop,forKey:"pop")
        let breathe=CABasicAnimation(keyPath:"transform.scale");breathe.fromValue=1;breathe.toValue=1.1;breathe.duration=0.7;breathe.autoreverses=true;breathe.repeatCount = .infinity
        breathe.beginTime=now+0.55;breathe.timingFunction=CAMediaTimingFunction(name:.easeInEaseOut);icon.add(breathe,forKey:"breathe")
        let sway=CAKeyframeAnimation(keyPath:"transform.rotation.z");sway.values=[0,0.1,-0.1,0.05,0];sway.keyTimes=[0,0.25,0.5,0.75,1];sway.duration=1.4;sway.repeatCount = .infinity
        sway.beginTime=now+0.55;icon.add(sway,forKey:"sway")
    }
    private func clearFocus() { focus?.root.removeFromSuperlayer();focus=nil }
    private func drawFocusPlate(_ path:NSBezierPath,mid:CGFloat) {
        NSGradient(starting:NashiPalette.gold,ending:NSColor(NashiPalette.goldEnd))?.draw(in:path,angle:mid*180 / .pi)
        NSGraphicsContext.saveGraphicsState();path.addClip();NSColor.white.withAlphaComponent(0.75).setStroke();path.lineWidth=2.5;path.stroke();NSGraphicsContext.restoreGraphicsState()
    }
    private func image(_ rect:NSRect,_ body:()->Void)->CGImage? {
        let scale=window?.backingScaleFactor ?? 2
        guard let rep=NSBitmapImageRep(bitmapDataPlanes:nil,pixelsWide:Int(ceil(rect.width*scale)),pixelsHigh:Int(ceil(rect.height*scale)),bitsPerSample:8,samplesPerPixel:4,hasAlpha:true,isPlanar:false,colorSpaceName:.deviceRGB,bytesPerRow:0,bitsPerPixel:0) else { return nil }
        rep.size=rect.size
        guard let context=NSGraphicsContext(bitmapImageRep:rep) else { return nil }
        NSGraphicsContext.saveGraphicsState();NSGraphicsContext.current=context
        effectiveAppearance.performAsCurrentDrawingAppearance { let t=NSAffineTransform();t.translateX(by:-rect.minX,yBy:-rect.minY);t.concat();body() }
        NSGraphicsContext.restoreGraphicsState()
        return rep.cgImage
    }
    /// Uses the same rendered label geometry; never substitutes a test-only layout.
    func labelAuditIssues(_ index:Int,title:String)->[String] {
        guard let layout=labelLayouts[index] else { return ["Wheel label did not render: "+title] }
        var issues:[String]=[]
        if !WheelLabelLayout.fits(layout,inside:petal(index),padding:3.5*geometry.scale) {issues.append("Wheel label is clipped: "+title)}
        for line in layout.lines where abs(line.ink.midX-layout.textRect.midX)>0.5*geometry.scale {issues.append("Wheel line is not centered: "+line.text)}
        return issues
    }
    func updateSelection(_ point:NSPoint) { selected=items.indices.first{petal($0).contains(point)};updateTilt(point) }
    func updateAccessibility() {
        setAccessibilityLabel(inputs.first?.isFileURL == false ? "Nashi web media wheel":"Nashi \(mode.rawValue) wheel")
        let children=items.enumerated().map { index,item -> WheelAccessibilityItem in
            let child=WheelAccessibilityItem();child.setAccessibilityRole(.button);child.setAccessibilityEnabled(true);child.setAccessibilityLabel(L10n.text(item.operation?.title ?? item.title));child.setAccessibilityParent(self)
            if let window { child.setAccessibilityFrame(window.convertToScreen(convert(petal(index).bounds,to:nil))) }
            child.activate = { [weak self] in guard let self else { return false };self.onSelect?(item);return true };return child
        }
        setAccessibilityChildren(children)
    }
    override func mouseMoved(with event:NSEvent) { let point=convert(event.locationInWindow,from:nil);updateSelection(point);onPointer?(point) }
    override func mouseExited(with event:NSEvent) { selected=nil;onPointer?(nil) }
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
    override func scrollWheel(with event:NSEvent) { if let onCancel { onCancel() } else { super.scrollWheel(with:event) } }
    private func incoming(_ sender:NSDraggingInfo)->Bool {
        let urls=WebLinkPayload.urls(from:sender.draggingPasteboard)
        guard !urls.isEmpty else { return false }
        if urls != inputs { onDragInputs?(urls) };let point=convert(sender.draggingLocation,from:nil);updateSelection(point);onPointer?(point);return true
    }
    override func draggingEntered(_ sender:NSDraggingInfo)->NSDragOperation { incoming(sender) ? .copy:[] }
    override func draggingUpdated(_ sender:NSDraggingInfo)->NSDragOperation { incoming(sender) ? .copy:[] }
    override func draggingExited(_ sender:NSDraggingInfo?) { selected=nil;onPointer?(nil) }
    override func prepareForDragOperation(_ sender:NSDraggingInfo)->Bool { selected != nil }
    override func performDragOperation(_ sender:NSDraggingInfo)->Bool { guard incoming(sender),let selected,items.indices.contains(selected) else { onCancel?();return false };onSelect?(items[selected]);return true }
    override func concludeDragOperation(_ sender:NSDraggingInfo?) { onCancel?() }
}
extension WheelItem {
    /// The conversion slices, each with a symbol that says what the format is for.
    static func conversions(for inputs:[URL])->[WheelItem] {
        FeatureCatalog.formats(for:inputs).map { WheelItem(title:$0 == "gz" ? "GZIP":$0.uppercased(),symbol:formatSymbols[$0] ?? "doc",operation:.convert($0)) }
    }
    static let formatSymbols:[String:String]=[
        "jpg":"photo","png":"photo.on.rectangle","heic":"camera","webp":"globe","avif":"sparkles","tiff":"camera.aperture","bmp":"square.grid.3x3","gif":"play.square.stack","svg":"scribble.variable",
        "pdf":"doc.richtext","docx":"doc.text","pages":"doc.text.image","rtf":"textformat","rtfd":"paperclip","txt":"text.alignleft","md":"number","html":"chevron.left.forwardslash.chevron.right","epub":"book",
        "csv":"tablecells","tsv":"tablecells","xls":"tablecells","xlsx":"tablecells","ods":"tablecells","ppt":"rectangle.on.rectangle.angled","pptx":"rectangle.on.rectangle.angled",
        "mp4":"film","mov":"video","mkv":"film","avi":"film","wmv":"film","webm":"film",
        "mp3":"music.note","m4a":"music.note","ogg":"music.note.list","opus":"music.note.list","wma":"music.note.list","wav":"waveform","aiff":"waveform","flac":"hifispeaker",
        "srt":"captions.bubble","vtt":"captions.bubble","zip":"archivebox","tar":"archivebox","gz":"archivebox","rar":"archivebox"
    ]
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
        if urls.first?.isFileURL != false || mode != .actions {
            shown=true;lastMode=mode;onActivate(urls,mode,NSEvent.mouseLocation)
        }
    }
}
