import AppKit
import CoreImage

extension NSFont { var nashiRounded:NSFont { fontDescriptor.withDesign(.rounded).flatMap { NSFont(descriptor:$0,size:pointSize) } ?? self } }
extension NSView { var nashiDark:Bool { effectiveAppearance.bestMatch(from:[.darkAqua,.aqua]) == .darkAqua } }

/// Apple's Liquid Glass on macOS 26 and later, a behind-window blur before that.
enum NashiGlass {
    static var isLiquid:Bool { if #available(macOS 26.0,*) { return true };return false }
    static func make()->NSView {
        if #available(macOS 26.0,*) { return NSGlassEffectView() }
        return frost()
    }
    /// A plain behind-window blur. Under Liquid Glass it is the frost that diffuses the desktop.
    static func frost()->NSVisualEffectView {
        let view=NSVisualEffectView();view.material = .popover;view.blendingMode = .behindWindow;view.state = .active;return view
    }
    static func setContent(_ glass:NSView,_ content:NSView) {
        content.autoresizingMask=[.width,.height]
        if #available(macOS 26.0,*),let glass=glass as? NSGlassEffectView { glass.contentView=content } else { content.frame=glass.bounds;glass.addSubview(content) }
    }
    /// Behind-window blur ignores layer corners and blurs its whole frame, which showed as a
    /// faint square; a mask image gives it the real round shape.
    static func setRadius(_ glass:NSView,_ radius:CGFloat) {
        if #available(macOS 26.0,*),let glass=glass as? NSGlassEffectView { glass.cornerRadius=radius;return }
        guard let blur=glass as? NSVisualEffectView,blur.bounds.width>0,blur.bounds.height>0 else { return }
        // Every caller uses half the height, so the size alone decides the mask.
        let size=blur.bounds.size
        if blur.maskImage?.size == size { return }
        blur.maskImage=NSImage(size:size,flipped:false) { rect in NSColor.black.setFill();NSBezierPath(roundedRect:rect,xRadius:radius,yRadius:radius).fill();return true }
    }
    static func style(_ glass:NSView,clear:Bool,tint:NSColor?,active:Bool) {
        if #available(macOS 26.0,*),let glass=glass as? NSGlassEffectView { glass.style=clear ? .clear:.regular;glass.tintColor=tint }
        else if let glass=glass as? NSVisualEffectView { glass.state=active ? .active:.inactive }
    }
}

/// Wheel panel content: a Liquid Glass base and the wheel move together; the mode
/// capsule stays put. Lengths scale from the 320 pt reference wheel.
final class WheelStageView:NSView {
    /// The glass stack stays flat (affine transforms only). A 3D transform here makes AppKit
    /// render the glass into a square offscreen image, which showed as a box around the wheel.
    let container=NSView()
    /// The slices and the center disc, in real 3D at different heights above the glass.
    let content=NSView()
    let shadowView=NSView()
    /// Stacked rings under the rim: the wheel's thickness, visible on the near side as it leans.
    let edge=NSView()
    private var rings:[CAShapeLayer]=[]
    /// Frost under the glass: how much the desktop behind the wheel melts into a blur.
    let frost=NashiGlass.frost()
    let base=NashiGlass.make()
    let tint=NSView()
    let capsuleLabel=NSTextField(labelWithString:"")
    /// A soft highlight on the glass that slides with the lean, like light on a real lens.
    let sheenHolder=NSView()
    private let sheen=CAGradientLayer()
    let wheel=WheelView(frame:NSRect(x:0,y:0,width:320,height:320))
    let disc=WheelDiscView()
    let barHost=NSView()
    let barFrost=NashiGlass.frost()
    let barGlass=NashiGlass.make()
    let barTint=NSView()
    let bar=WheelModeBar()
    var onCancel:(()->Void)?
    private(set) var size:CGFloat=320
    private var glassAlpha:CGFloat=1
    private var frostAlpha:CGFloat=0
    private var solid=false
    private var lastMotion=WheelMotion.rest(0)
    /// Pointer position over the wheel, -1…1 on each axis; the wheel leans toward it.
    private var pointerTilt=CGPoint.zero
    private var tiltTarget=CGPoint.zero
    private var tiltTimer:Timer?
    /// Inside a window (the Settings preview) the glass blurs the window, not the desktop.
    func blendWithinWindow() { for view in [frost,barFrost,base,barGlass] { (view as? NSVisualEffectView)?.blendingMode = .withinWindow } }
    /// The floating panel fades as a window; the Settings preview fades its own views instead.
    var fadesWindow=true
    var scale:CGFloat { size/320 }
    var showsBar=true { didSet { barHost.isHidden = !showsBar } }
    private var margin:CGFloat { 24*scale }
    private var barHeight:CGFloat { 34*scale }
    private static let depth:(slices:CGFloat,disc:CGFloat)=(16,38)
    private static let maxLean:CGFloat=10

    override init(frame:NSRect) {
        super.init(frame:frame)
        wantsLayer=true
        for view in [container,content,shadowView,edge,tint,sheenHolder,barHost,barTint,wheel,disc,base,barGlass] as [NSView] { view.wantsLayer=true }
        container.layer?.masksToBounds=false;content.layer?.masksToBounds=false
        let baseContent=NSView();baseContent.wantsLayer=true
        tint.autoresizingMask=[.width,.height];baseContent.addSubview(tint)
        capsuleLabel.alignment = .center;capsuleLabel.alphaValue=0;capsuleLabel.lineBreakMode = .byTruncatingMiddle;baseContent.addSubview(capsuleLabel)
        NashiGlass.setContent(base,baseContent)
        shadowView.layer?.shadowColor=NSColor.black.cgColor
        rings=(1...8).map { _ in let ring=CAShapeLayer();ring.fillColor=nil;edge.layer?.addSublayer(ring);return ring }
        sheen.type = .radial;sheenHolder.layer?.masksToBounds=true;sheenHolder.layer?.addSublayer(sheen)
        container.addSubview(shadowView);container.addSubview(edge);container.addSubview(frost);container.addSubview(base);container.addSubview(sheenHolder)
        wheel.drawsDisc=false;wheel.companion=disc;disc.wheel=wheel
        wheel.onPointer = { [weak self] point in self?.tilt(toward:point) }
        content.layerUsesCoreImageFilters=true
        content.addSubview(wheel);content.addSubview(disc)
        NashiGlass.setContent(barGlass,barTint)
        barFrost.autoresizingMask=[.width,.height];barGlass.autoresizingMask=[.width,.height];bar.autoresizingMask=[.width,.height]
        barHost.addSubview(barFrost);barHost.addSubview(barGlass);barHost.addSubview(bar)
        addSubview(container);addSubview(content);addSubview(barHost)
    }
    required init?(coder:NSCoder) { fatalError() }
    deinit { tiltTimer?.invalidate() }
    override func mouseDown(with event:NSEvent) { onCancel?() }

    /// Sizes everything for a wheel side and returns the panel content size.
    func layout(size:CGFloat)->NSSize {
        self.size=size;bar.scale=scale
        let barWidth=bar.preferredWidth(),width=max(size,barWidth)+2*margin,height=size+10*scale+barHeight+2*margin
        frame=NSRect(x:0,y:0,width:width,height:height)
        for view in [container,content,wheel,disc] { view.layer?.transform=CATransform3DIdentity }
        container.frame=NSRect(x:(width-size)/2,y:margin+barHeight+10*scale,width:size,height:size);content.frame=container.frame
        wheel.frame=content.bounds;disc.frame=content.bounds;edge.frame=container.bounds
        barHost.frame=NSRect(x:(width-barWidth)/2,y:margin,width:barWidth,height:barHeight)
        barFrost.frame=barHost.bounds;barGlass.frame=barHost.bounds
        NashiGlass.setRadius(barFrost,barHeight/2);NashiGlass.setRadius(barGlass,barHeight/2);barTint.layer?.cornerRadius=barHeight/2
        capsuleLabel.font=NSFont.systemFont(ofSize:12.5*scale,weight:.semibold).nashiRounded
        return frame.size
    }
    /// Reads Theme settings. Tinted is Apple's regular glass, Transparent its clear glass;
    /// intensity adds frost (desktop blur) under it and a light or dark wash on top.
    func applyFinish() {
        let defaults=UserDefaults.standard;solid=defaults.string(forKey:"conversionFanTheme")=="metallic"
        let tinted=defaults.string(forKey:"wheelGlassStyle") != "transparent",intensity=CGFloat(defaults.object(forKey:"wheelGlassIntensity") as? Double ?? 0.5)
        let dark=nashiDark,lerp=WheelMotion.lerp,liquid=NashiGlass.isLiquid
        // Pin light/dark so the glass never flips to another look over a bright or busy desktop.
        let look=NSAppearance(named:dark ? .darkAqua:.aqua)
        for view in [base,barGlass,frost,barFrost] as [NSView] { view.appearance=look }
        let wash=dark ? NSColor.black:NSColor.white,amount=tinted ? lerp(0,0.28,intensity):lerp(0,0.1,intensity)
        let tintColor:NSColor=solid ? NSColor(white:dark ? 0.16:0.24,alpha:0.94):wash.withAlphaComponent(amount)
        NashiGlass.style(base,clear:!tinted && !solid,tint:tintColor,active:!solid)
        NashiGlass.style(barGlass,clear:!tinted && !solid,tint:solid ? tintColor:wash.withAlphaComponent(amount*0.7),active:!solid)
        frostAlpha=solid || !liquid ? 0:tinted ? lerp(0.1,0.95,intensity):lerp(0,0.55,intensity)
        glassAlpha=liquid || solid || tinted ? 1:lerp(0.35,0.85,intensity)
        // Before Liquid Glass the tint layer and a light rim stand in for the material.
        let fill=liquid || solid ? NSColor.clear.cgColor:wash.withAlphaComponent(tinted ? lerp(0.05,0.4,intensity):lerp(0,0.15,intensity)).cgColor
        let rim=NSColor.white.withAlphaComponent(liquid || solid ? 0:dark ? 0.16:0.7).cgColor
        for layer in [tint.layer,barTint.layer] { layer?.backgroundColor=fill;layer?.borderColor=rim;layer?.borderWidth=liquid || solid ? 0:1 }
        base.alphaValue=glassAlpha;frost.alphaValue=frostAlpha;barGlass.alphaValue=glassAlpha;barFrost.alphaValue=frostAlpha
        for (index,ring) in rings.enumerated() {
            let i=CGFloat(index+1)
            ring.strokeColor=(dark ? NSColor(white:0.32-0.02*i,alpha:0.8):NSColor(white:0.8-0.035*i,alpha:0.65)).cgColor;ring.lineWidth=3*scale
        }
        sheen.colors=[NSColor.white.withAlphaComponent(dark ? 0.22:0.42).cgColor,NSColor.white.withAlphaComponent(0).cgColor]
        shadowView.layer?.shadowOpacity=solid ? 0:dark ? 0.5:0.28;shadowView.layer?.shadowRadius=34*scale
        capsuleLabel.textColor = .labelColor
    }
    func apply(_ m:WheelMotion) {
        lastMotion=m
        let sc=scale,distance=900*sc,lean=NashiMotion.enabled ? Self.maxLean:0
        let pitch=m.pitch+pointerTilt.y*lean,yaw=m.yaw+pointerTilt.x*lean,radians=CGFloat.pi/180
        CATransaction.begin();CATransaction.setDisableActions(true)
        // Glass: lifting toward the viewer reads as perspective growth, leaning as foreshortening.
        let near=distance/max(1,distance-m.lift*sc)
        container.layer?.transform=Self.centered(container.bounds.size,anchor:container.layer?.anchorPoint ?? .zero,sx:m.scale*m.stretchX*near*cos(yaw*radians),sy:m.scale*m.stretchY*near*cos(pitch*radians),clockwise:m.rotation)
        for (view,height) in [(wheel,Self.depth.slices),(disc,Self.depth.disc)] as [(NSView,CGFloat)] {
            view.layer?.transform=Self.centered(view.bounds.size,anchor:view.layer?.anchorPoint ?? .zero,sx:m.scale*m.stretchX,sy:m.scale*m.stretchY,clockwise:m.rotation,pitch:pitch,yaw:yaw,lift:(m.lift+height)*sc,distance:distance)
        }
        // Fades run on the window. Glass that is itself half transparent falls back to a flat
        // material and then snaps back, so the glass views always stay fully opaque.
        let alpha=m.alpha*m.baseAlpha
        if fadesWindow { window?.alphaValue=alpha } else { for view in [container,content,barHost] { view.alphaValue=alpha } }
        content.layer?.filters=m.blur>0.05 ? CIFilter(name:"CIGaussianBlur",parameters:[kCIInputRadiusKey:m.blur*sc]).map{[$0]}:nil
        let full=0.97*size,shape=m.baseSize.map { CGSize(width:$0.width*sc,height:$0.height*sc) } ?? CGSize(width:full,height:full)
        let w=shape.width*m.baseScale,h=shape.height*m.baseScale,rect=NSRect(x:(size-w)/2,y:(size-h)/2,width:w,height:h)
        base.frame=rect;NashiGlass.setRadius(base,h/2);tint.layer?.cornerRadius=h/2
        frost.frame=rect;NashiGlass.setRadius(frost,h/2)
        base.alphaValue=glassAlpha;frost.alphaValue=frostAlpha
        // Thickness: deeper rings slide toward the near side; they vanish when the wheel faces you.
        let leaning=solid ? 0:WheelMotion.clamp((abs(pitch)+abs(yaw))/6)
        for (index,ring) in rings.enumerated() {
            let depth=CGFloat(index+1)*1.4*sc
            ring.path=CGPath(ellipseIn:rect.insetBy(dx:ring.lineWidth/2,dy:ring.lineWidth/2).offsetBy(dx:-sin(yaw*radians)*depth,dy:-sin(pitch*radians)*depth),transform:nil)
            ring.opacity=Float(leaning)
        }
        sheenHolder.frame=rect;sheenHolder.layer?.cornerRadius=h/2;sheen.frame=sheenHolder.bounds;sheen.isHidden=solid
        let light=CGPoint(x:0.5+pointerTilt.x*0.35,y:0.5+pointerTilt.y*0.35)
        sheen.startPoint=light;sheen.endPoint=CGPoint(x:light.x+0.55,y:light.y+0.55)
        // The shadow falls away from the light and drops further as the wheel comes closer.
        let drop=(18+max(0,m.lift)*0.25)*sc
        shadowView.frame=rect.offsetBy(dx:-pointerTilt.x*14*sc,dy:-drop-pointerTilt.y*10*sc)
        shadowView.layer?.shadowPath=CGPath(ellipseIn:shadowView.bounds,transform:nil);shadowView.alphaValue=WheelMotion.clamp(1+m.lift/500)
        capsuleLabel.alphaValue=m.capsuleLabel
        if m.capsuleLabel>0 { capsuleLabel.sizeToFit();let lw=min(capsuleLabel.frame.width,w-20*sc);capsuleLabel.frame=NSRect(x:(w-lw)/2,y:(h-capsuleLabel.frame.height)/2,width:lw,height:capsuleLabel.frame.height) }
        // The capsule's labels fade; its glass only shows or hides, for the same reason.
        bar.alphaValue=m.barAlpha;barGlass.isHidden=m.barAlpha<0.02;barFrost.isHidden=barGlass.isHidden
        barHost.layer?.transform=CATransform3DMakeTranslation(0,-m.barOffset*sc,0)
        CATransaction.commit()
        if wheel.motion != m { wheel.motion=m;wheel.needsDisplay=true }
    }
    /// The wheel floats in front of the screen and leans toward the pointer, easing there.
    func tilt(toward point:NSPoint?) {
        guard NashiMotion.enabled else { return }
        let c=NSPoint(x:wheel.bounds.midX,y:wheel.bounds.midY),r=max(1,wheel.bounds.width/2)
        tiltTarget=point.map { CGPoint(x:WheelMotion.clamp(($0.x-c.x)/r,-1,1),y:WheelMotion.clamp(($0.y-c.y)/r,-1,1)) } ?? .zero
        guard tiltTimer == nil else { return }
        let timer=Timer(timeInterval:1/120,repeats:true) { [weak self] _ in Task { @MainActor in self?.stepTilt() } }
        RunLoop.main.add(timer,forMode:.common);tiltTimer=timer
    }
    private func stepTilt() {
        let dx=tiltTarget.x-pointerTilt.x,dy=tiltTarget.y-pointerTilt.y
        if abs(dx)<0.002 && abs(dy)<0.002 { pointerTilt=tiltTarget;tiltTimer?.invalidate();tiltTimer=nil }
        else { pointerTilt=CGPoint(x:pointerTilt.x+dx*0.16,y:pointerTilt.y+dy*0.16) }
        apply(lastMotion)
    }
    /// The wheel at rest on an opaque stand-in for the glass, for the Snap dust.
    func snapshot()->CGImage? {
        let q=min(1.5,window?.backingScaleFactor ?? 2),pixels=Int((size*q).rounded())
        guard pixels>0,let rep=NSBitmapImageRep(bitmapDataPlanes:nil,pixelsWide:pixels,pixelsHigh:pixels,bitsPerSample:8,samplesPerPixel:4,hasAlpha:true,isPlanar:false,colorSpaceName:.deviceRGB,bytesPerRow:0,bitsPerPixel:0) else { return nil }
        rep.size=NSSize(width:size,height:size)
        guard let context=NSGraphicsContext(bitmapImageRep:rep) else { return nil }
        let saved=wheel.motion;wheel.motion=nil;defer { wheel.motion=saved }
        NSGraphicsContext.saveGraphicsState();NSGraphicsContext.current=context
        effectiveAppearance.performAsCurrentDrawingAppearance {
            let dark=nashiDark,d=0.97*size,path=NSBezierPath(ovalIn:NSRect(x:(size-d)/2,y:(size-d)/2,width:d,height:d))
            (dark ? NSColor(srgbRed:0.17,green:0.17,blue:0.2,alpha:0.92):NSColor(srgbRed:0.98,green:0.97,blue:0.96,alpha:0.88)).setFill();path.fill()
            NSColor.white.withAlphaComponent(dark ? 0.16:0.7).setStroke();path.lineWidth=1;path.stroke()
            wheel.renderingOffscreen=true;wheel.draw(wheel.bounds);wheel.renderingOffscreen=false
        }
        NSGraphicsContext.restoreGraphicsState()
        return rep.cgImage
    }
    /// Scale and rotate about the center, whatever anchor AppKit gave the layer. Perspective
    /// is added only when something actually leans or lifts, so flat frames stay affine.
    static func centered(_ size:CGSize,anchor:CGPoint,sx:CGFloat,sy:CGFloat,clockwise:CGFloat,pitch:CGFloat=0,yaw:CGFloat=0,lift:CGFloat=0,distance:CGFloat=900)->CATransform3D {
        let dx=(0.5-anchor.x)*size.width,dy=(0.5-anchor.y)*size.height
        var t=CATransform3DMakeTranslation(dx,dy,0)
        if pitch != 0 || yaw != 0 || lift != 0 {
            var perspective=CATransform3DIdentity;perspective.m34 = -1/distance
            t=CATransform3DConcat(perspective,t)
            t=CATransform3DRotate(t,-pitch * .pi/180,1,0,0)
            t=CATransform3DRotate(t,yaw * .pi/180,0,1,0)
            t=CATransform3DTranslate(t,0,0,lift)
        }
        t=CATransform3DRotate(t,-clockwise * .pi/180,0,0,1)
        t=CATransform3DScale(t,max(sx,0.001),max(sy,0.001),1)
        return CATransform3DTranslate(t,-dx,-dy,0)
    }
}

/// The center disc on its own layer so it can float above the slices; drawing stays in `WheelView`.
final class WheelDiscView:NSView {
    weak var wheel:WheelView?
    override func hitTest(_ point:NSPoint)->NSView? { nil }
    override func draw(_ dirtyRect:NSRect) { wheel?.drawDisc() }
}

/// The capsule under the wheel. Click it, or hover a segment while dragging, to switch wheels.
final class WheelModeBar:NSView {
    private static let entries:[(WheelMode,WheelShortcut,String)]=[(.convert,.conversions,"Convert"),(.tools,.tools,"Tools"),(.actions,.actions,"Actions")]
    var mode:WheelMode = .convert { didSet { if oldValue != mode { needsDisplay=true;updateAccessibility() } } }
    var scale:CGFloat=1 { didSet { needsDisplay=true } }
    var onSelect:((WheelMode)->Void)?
    override init(frame:NSRect) {
        super.init(frame:frame);registerForDraggedTypes(WebLinkPayload.pasteboardTypes)
        setAccessibilityElement(true);setAccessibilityRole(.radioGroup);setAccessibilityLabel(L10n.text("Wheel mode"))
    }
    required init?(coder:NSCoder) { fatalError() }
    private func label(_ entry:(WheelMode,WheelShortcut,String),color:NSColor = .labelColor)->NSAttributedString {
        NSAttributedString(string:ModifierPreferences.chord(for:entry.1).symbols+"  "+L10n.text(entry.2),attributes:[.font:NSFont.systemFont(ofSize:12*scale,weight:.semibold).nashiRounded,.foregroundColor:color])
    }
    private var widths:[CGFloat] { Self.entries.map { ceil(label($0).size().width)+22*scale } }
    func preferredWidth()->CGFloat { widths.reduce(0,+)+6*scale }
    private func segments()->[(WheelMode,NSRect)] {
        var x=3*scale
        return zip(Self.entries,widths).map { entry,width in defer { x+=width };return (entry.0,NSRect(x:x,y:3*scale,width:width,height:bounds.height-6*scale)) }
    }
    override func draw(_ dirtyRect:NSRect) {
        for (entry,(mode,rect)) in zip(Self.entries,segments()) {
            let on=mode == self.mode
            if on {
                NSGraphicsContext.saveGraphicsState()
                let shadow=NSShadow();shadow.shadowColor=NSColor.black.withAlphaComponent(0.12);shadow.shadowBlurRadius=4*scale;shadow.shadowOffset=NSSize(width:0,height:-1);shadow.set()
                (nashiDark ? NSColor.white.withAlphaComponent(0.16):NSColor.white.withAlphaComponent(0.88)).setFill()
                NSBezierPath(roundedRect:rect,xRadius:rect.height/2,yRadius:rect.height/2).fill()
                NSGraphicsContext.restoreGraphicsState()
            }
            let text=label(entry,color:on ? .labelColor:.secondaryLabelColor),size=text.size()
            text.draw(at:NSPoint(x:rect.midX-size.width/2,y:rect.midY-size.height/2))
        }
    }
    private func choose(_ point:NSPoint) { if let next=segments().first(where:{$0.1.contains(point)})?.0,next != mode { onSelect?(next) } }
    override func mouseDown(with event:NSEvent) { choose(convert(event.locationInWindow,from:nil)) }
    override func draggingEntered(_ sender:NSDraggingInfo)->NSDragOperation { choose(convert(sender.draggingLocation,from:nil));return .copy }
    override func draggingUpdated(_ sender:NSDraggingInfo)->NSDragOperation { choose(convert(sender.draggingLocation,from:nil));return .copy }
    override func performDragOperation(_ sender:NSDraggingInfo)->Bool { false }
    func updateAccessibility() {
        setAccessibilityChildren(zip(Self.entries,segments()).map { entry,segment in
            let item=WheelAccessibilityItem();item.setAccessibilityRole(.radioButton);item.setAccessibilityLabel(L10n.text(entry.2));item.setAccessibilityValue(segment.0 == mode ? 1:0);item.setAccessibilityParent(self)
            if let window { item.setAccessibilityFrame(window.convertToScreen(convert(segment.1,to:nil))) }
            item.activate = { [weak self] in self?.onSelect?(segment.0);return true };return item
        })
    }
}

/// Plays `WheelMotion` frames on a stage at display rate; one run at a time.
@MainActor final class WheelAnimator {
    enum Kind { case open,close,modeSwitch }
    private struct Run { let kind:Kind,style:WheelAnimationStyle?,start:CFTimeInterval,duration:Double,done:(()->Void)? }
    private var current:Run?
    private var timer:Timer?
    private weak var stage:WheelStageView?
    var onFrame:((WheelMotion)->Void)?
    init(stage:WheelStageView) { self.stage=stage }
    deinit { timer?.invalidate() }
    /// `style == nil` is the Reduce Motion fade.
    func run(_ kind:Kind,style:WheelAnimationStyle?,done:(()->Void)?=nil) {
        let duration:Double
        switch kind { case .open:duration=style?.openDuration ?? 150;case .close:duration=style?.closeDuration ?? 150;case .modeSwitch:duration=style == nil ? 0:240 }
        current=Run(kind:kind,style:style,start:CACurrentMediaTime(),duration:duration,done:done)
        step()
        if current != nil,timer == nil {
            let timer=Timer(timeInterval:1/120,repeats:true) { [weak self] _ in Task { @MainActor in self?.step() } }
            RunLoop.main.add(timer,forMode:.common);self.timer=timer
        }
    }
    func cancel() { current=nil }
    private func step() {
        guard let run=current,let stage else { timer?.invalidate();timer=nil;return }
        let p=run.duration<=0 ? 1:min(1,(CACurrentMediaTime()-run.start)*1000/run.duration),count=stage.wheel.items.count
        let frame:WheelMotion=run.kind == .modeSwitch ? .modeSwitch(progress:p,count:count):.frame(run.style,opening:run.kind == .open,progress:p,count:count)
        stage.apply(p>=1 && run.kind != .close ? .rest(count):frame)
        onFrame?(frame)
        if p>=1 { current=nil;timer?.invalidate();timer=nil;run.done?() }
    }
}

enum WheelDust {
    /// Snap: the wheel's pixels in 24 layers that scatter up and to the right, or gather back with a gold glow.
    static func play(in host:CALayer,image:CGImage,rect local:NSRect,assembling:Bool,scale sc:CGFloat) {
        let now=CACurrentMediaTime()
        CATransaction.begin();CATransaction.setDisableActions(true)
        if assembling {
            let glow=CAGradientLayer();glow.type = .radial;glow.frame=local.insetBy(dx:local.width*0.06,dy:local.height*0.06)
            glow.colors=[NashiPalette.gold.withAlphaComponent(0.75).cgColor,NashiPalette.gold.withAlphaComponent(0.25).cgColor,NSColor.clear.cgColor];glow.locations=[0,0.45,0.7]
            glow.startPoint=CGPoint(x:0.5,y:0.5);glow.endPoint=CGPoint(x:1,y:1);glow.opacity=0;host.addSublayer(glow)
            let fade=CAKeyframeAnimation(keyPath:"opacity");fade.values=[0,0.6,0];fade.keyTimes=[0,0.4,1]
            let grow=CAKeyframeAnimation(keyPath:"transform.scale");grow.values=[0.7,0.95,1.12];grow.keyTimes=fade.keyTimes
            let group=CAAnimationGroup();group.animations=[fade,grow];group.duration=1.1;group.timingFunction=CAMediaTimingFunction(controlPoints:0.28,0.11,0.32,1);glow.add(group,forKey:"glow")
        }
        for (index,layerImage) in layers(from:image,count:WheelMotion.snapLayers).enumerated() {
            let layer=CALayer();layer.frame=local;layer.contents=layerImage;host.addSublayer(layer)
            let angle=CGFloat.pi/4+CGFloat.random(in:-0.6...0.6),distance=CGFloat.random(in:70...190)*sc,turn=CGFloat.random(in:-0.5...0.5)
            func moved(_ f:CGFloat)->NSValue { NSValue(caTransform3D:CATransform3DRotate(CATransform3DMakeTranslation(cos(angle)*distance*f,sin(angle)*distance*f,0),turn*f,0,0,1)) }
            let move=CAKeyframeAnimation(keyPath:"transform"),fade=CAKeyframeAnimation(keyPath:"opacity"),group=CAAnimationGroup()
            if assembling { move.values=[moved(1),moved(0.2),moved(0)];fade.values=[0,0.85,1];move.keyTimes=[0,0.6,1] }
            else { move.values=[moved(0),moved(0.18),moved(1)];fade.values=[1,0.9,0];move.keyTimes=[0,0.35,1];layer.opacity=0 }
            fade.keyTimes=move.keyTimes;group.animations=[move,fade]
            group.duration=assembling ? 0.72:0.85;group.beginTime=now+Double(index)*(assembling ? 0.028:0.030)
            group.timingFunction=assembling ? CAMediaTimingFunction(controlPoints:0.2,0.65,0.25,1):CAMediaTimingFunction(controlPoints:0.45,0,0.8,0.55)
            group.fillMode = .both;group.isRemovedOnCompletion=false;layer.add(group,forKey:"dust")
        }
        CATransaction.commit()
    }
    /// Spreads every pixel into one of `count` layers, weighted left to right so the wheel dissolves in a sweep.
    static func layers(from image:CGImage,count:Int)->[CGImage] {
        let w=image.width,h=image.height,bytes=w*h*4
        guard count>0,let space=CGColorSpace(name:CGColorSpace.sRGB),
              let context=CGContext(data:nil,width:w,height:h,bitsPerComponent:8,bytesPerRow:w*4,space:space,bitmapInfo:CGImageAlphaInfo.premultipliedLast.rawValue) else { return [] }
        context.draw(image,in:CGRect(x:0,y:0,width:w,height:h))
        guard let source=context.data?.assumingMemoryBound(to:UInt8.self) else { return [] }
        var output=[UInt8](repeating:0,count:bytes*count)
        output.withUnsafeMutableBufferPointer { buffer in
            for y in 0..<h { for x in 0..<w {
                let i=(y*w+x)*4;guard source[i+3] != 0 else { continue }
                let layer=min(count-1,Int(Double(count)*(Double.random(in:0..<1)+2*Double(x)/Double(w))/3)),o=layer*bytes+i
                buffer[o]=source[i];buffer[o+1]=source[i+1];buffer[o+2]=source[i+2];buffer[o+3]=source[i+3]
            } }
        }
        return (0..<count).compactMap { k in
            CGDataProvider(data:Data(output[k*bytes..<(k+1)*bytes]) as CFData).flatMap {
                CGImage(width:w,height:h,bitsPerComponent:8,bitsPerPixel:32,bytesPerRow:w*4,space:space,bitmapInfo:CGBitmapInfo(rawValue:CGImageAlphaInfo.premultipliedLast.rawValue),provider:$0,decode:nil,shouldInterpolate:false,intent:.defaultIntent)
            }
        }
    }
}

extension NSBezierPath {
    var nashiCGPath:CGPath {
        if #available(macOS 14.0,*) { return cgPath }
        let path=CGMutablePath();var points=[NSPoint](repeating:.zero,count:3)
        for index in 0..<elementCount {
            switch element(at:index,associatedPoints:&points) {
            case .moveTo:path.move(to:points[0])
            case .lineTo:path.addLine(to:points[0])
            case .curveTo:path.addCurve(to:points[2],control1:points[0],control2:points[1])
            case .closePath:path.closeSubpath()
            default:break
            }
        }
        return path
    }
}
