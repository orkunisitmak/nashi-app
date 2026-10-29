import SwiftUI
import NashiCore

/// Settings → Theme: the wheel's glass and how it arrives and leaves, shown live on the real wheel.
struct ThemeSettingsView:View {
    @AppStorage("conversionFanTheme") private var fanTheme="glass"
    @AppStorage("wheelGlassStyle") private var glassStyle="tinted"
    @AppStorage("wheelGlassIntensity") private var glassIntensity=0.5
    @AppStorage(WheelAnimationStyle.storageKey) private var animation=WheelAnimationStyle.depth.rawValue
    @AppStorage("reduceAnimations") private var reduceAnimations=false
    @AppStorage("appLanguage") private var language="en"
    @Environment(\.accessibilityReduceMotion) private var systemReducedMotion
    @State private var replay=0
    var body:some View {
        ScrollView { VStack(alignment:.leading,spacing:20) {
            NashiPageHeading(eyebrow:"Theme",title:"Make the wheel yours.",subtitle:"Pick its glass and the way it arrives and leaves.").padding(.bottom,4)
            VStack(spacing:10) {
                WheelPreview(finish:fanTheme,glassStyle:glassStyle,intensity:glassIntensity,animation:animation,replay:replay,language:language)
                    .frame(height:340).frame(maxWidth:.infinity)
                    .accessibilityLabel(LocalizedStringKey("Wheel preview"))
                HStack {
                    LText("Hover the slices. Changes show here as you make them.").font(.caption).foregroundStyle(.secondary)
                    Spacer()
                    Button { replay += 1 } label:{ Label("Play animation",systemImage:"play.fill") }.buttonStyle(NashiButtonStyle(.secondary,compact:true))
                }
            }
            GroupBox("Wheel glass") {
                VStack(alignment:.leading,spacing:16) {
                    HStack { Text("Wheel finish");Spacer();Picker("Wheel finish",selection:$fanTheme) { Text("Glass").tag("glass");Text("Solid").tag("metallic") }.labelsHidden().pickerStyle(.segmented).frame(width:230) }
                    Group {
                        HStack { Text("Glass style");Spacer();Picker("Glass style",selection:$glassStyle) { Text("Transparent").tag("transparent");Text("Tinted").tag("tinted") }.labelsHidden().pickerStyle(.segmented).frame(width:230) }
                        HStack {
                            Text("Glass intensity");Spacer()
                            Slider(value:$glassIntensity,in:0...1) { Text("Glass intensity") } minimumValueLabel:{ Image(systemName:"circle.dotted") } maximumValueLabel:{ Image(systemName:"circle.fill") }
                                .labelsHidden().frame(width:230)
                        }
                        LText("Transparent shows more of what’s behind the wheel. Tinted is easier to read.").font(.caption).foregroundStyle(.secondary)
                    }.disabled(fanTheme != "glass")
                }.padding(10)
            }
            GroupBox("Wheel animation") {
                VStack(alignment:.leading,spacing:6) {
                    ForEach(WheelAnimationStyle.allCases) { style in row(style) }
                    if reduceAnimations || systemReducedMotion { LText("Reduce Motion is on, so the wheel simply fades.").font(.caption).foregroundStyle(.secondary) }
                }.padding(10)
            }
        }.padding(26).frame(maxWidth:680) }.frame(maxWidth:.infinity)
    }
    private func row(_ style:WheelAnimationStyle)->some View {
        let selected=animation == style.rawValue
        return Button {
            animation=style.rawValue
        } label:{
            HStack(alignment:.top,spacing:12) {
                Image(systemName:selected ? "checkmark.circle.fill":"circle").font(.system(size:16)).foregroundStyle(selected ? Color.nashi:Color.secondary)
                VStack(alignment:.leading,spacing:3) {
                    HStack(spacing:7) {
                        LText(style.title).font(.system(size:13,weight:.semibold,design:.rounded))
                        if style == .depth { NashiPill(title:"Recommended",color:.nashi) }
                    }
                    LText(style.summary).font(.system(size:11)).foregroundStyle(.secondary).fixedSize(horizontal:false,vertical:true)
                    LText("Opens in \(duration(style.openDuration)) · Closes in \(duration(style.closeDuration))").font(.system(size:10)).foregroundStyle(.tertiary).monospacedDigit()
                }
                Spacer(minLength:0)
            }
            .padding(.horizontal,12).padding(.vertical,10)
            .background(selected ? NashiPalette.sage.opacity(0.55):Color.clear,in:RoundedRectangle(cornerRadius:12,style:.continuous))
            .contentShape(RoundedRectangle(cornerRadius:12,style:.continuous))
        }
        .buttonStyle(.plain).accessibilityAddTraits(selected ? .isSelected:[])
    }
    private func duration(_ milliseconds:Double)->String {
        guard milliseconds>=1000 else { return "\(Int(milliseconds)) ms" }
        let formatter=NumberFormatter();formatter.locale=Locale(identifier:language);formatter.minimumFractionDigits=1;formatter.maximumFractionDigits=1
        return (formatter.string(from:NSNumber(value:milliseconds/1000)) ?? "\(milliseconds/1000)")+" s"
    }
}

/// The settings it receives only trigger updates; the view reads the same defaults the real wheel reads.
private struct WheelPreview:NSViewRepresentable {
    let finish:String,glassStyle:String,intensity:Double,animation:String,replay:Int,language:String
    func makeNSView(context:Context)->WheelPreviewView { WheelPreviewView(frame:.zero) }
    func updateNSView(_ view:WheelPreviewView,context:Context) { view.update(animation:animation,replay:replay,language:language) }
}

/// The real wheel, always open, over a sample wallpaper so the glass has something to bend.
final class WheelPreviewView:NSView {
    private let stage=WheelStageView(frame:.zero)
    private let wallpaper=CAGradientLayer()
    private let blobs=[CAShapeLayer(),CAShapeLayer(),CAShapeLayer()]
    private let dust=CALayer()
    private lazy var animator:WheelAnimator = {
        let animator=WheelAnimator(stage:stage);animator.onFrame = { [weak self] frame in self?.dust.opacity=Float(frame.dustAlpha) };return animator
    }()
    private var shown:(animation:String,replay:Int,language:String)?
    private var playing=0
    override init(frame:NSRect) {
        super.init(frame:frame)
        wantsLayer=true;layer?.cornerRadius=18;layer?.masksToBounds=true
        layer?.addSublayer(wallpaper);blobs.forEach { layer?.addSublayer($0) }
        stage.fadesWindow=false;stage.blendWithinWindow()
        stage.wheel.onSelect = { [weak self] _ in self?.play() };stage.onCancel = {}
        stage.bar.onSelect = { [weak self] mode in self?.show(mode:mode,switching:true) }
        addSubview(stage);layer?.addSublayer(dust)
    }
    required init?(coder:NSCoder) { fatalError() }
    override func viewDidChangeEffectiveAppearance() { super.viewDidChangeEffectiveAppearance();paint();stage.applyFinish() }
    override func layout() {
        super.layout()
        CATransaction.begin();CATransaction.setDisableActions(true)
        wallpaper.frame=bounds;dust.frame=bounds;paint()
        stage.setFrameOrigin(NSPoint(x:(bounds.width-stage.frame.width)/2,y:(bounds.height-stage.frame.height)/2))
        CATransaction.commit()
    }
    func update(animation:String,replay:Int,language:String) {
        if shown == nil { _=stage.layout(size:250);show(mode:.convert,switching:false);needsLayout=true }
        else if shown?.language != language { show(mode:stage.wheel.mode,switching:false) }
        stage.applyFinish();stage.wheel.needsDisplay=true;stage.bar.needsDisplay=true
        let changed=shown.map { $0.animation != animation || $0.replay != replay } ?? true
        shown=(animation,replay,language)
        if changed { play() }
    }
    private func show(mode:WheelMode,switching:Bool) {
        let url=FileManager.default.temporaryDirectory.appendingPathComponent(L10n.text("Preview")+".heic")
        let inputs=[url]
        stage.wheel.inputs=inputs;stage.wheel.mode=mode;stage.bar.mode=mode;stage.capsuleLabel.stringValue=url.lastPathComponent
        switch mode {
        case .convert:stage.wheel.items=WheelItem.conversions(for:inputs)
        case .tools:stage.wheel.items=FeatureCatalog.tools(for:inputs).map { WheelItem(title:FeatureCatalog.toolNames[$0] ?? $0.title,symbol:FeatureCatalog.toolSymbols[$0],operation:.tool($0)) }
        case .actions:stage.wheel.items=FileAction.allCases.map { WheelItem(title:$0.title,symbol:$0.symbol,action:$0) }
        }
        if switching { animator.run(.modeSwitch,style:NashiMotion.enabled ? WheelAnimationStyle.current:nil) } else { stage.apply(.rest(stage.wheel.items.count)) }
    }
    /// Closes, waits a beat, then opens again with the current style.
    private func play() {
        playing += 1;let token=playing
        let style:WheelAnimationStyle?=NashiMotion.enabled ? WheelAnimationStyle.current:nil
        if style == .snap { startDust(assembling:false) }
        animator.run(.close,style:style) { [weak self] in
            Task { @MainActor [weak self] in
                try? await Task.sleep(nanoseconds:350_000_000)
                guard let self,token == self.playing else { return }
                if style == .snap { self.startDust(assembling:true) }
                self.animator.run(.open,style:style) { [weak self] in self?.dust.sublayers=nil }
            }
        }
    }
    private func startDust(assembling:Bool) {
        dust.sublayers=nil
        let saved=stage.wheel.motion;stage.apply(.rest(stage.wheel.items.count))
        let image=stage.snapshot()
        if let saved { stage.apply(saved) }
        guard let image else { return }
        WheelDust.play(in:dust,image:image,rect:convert(stage.container.frame,from:stage),assembling:assembling,scale:stage.scale)
    }
    private func paint() {
        let dark=nashiDark
        wallpaper.colors=(dark ? [(0.10,0.06,0.24),(0.23,0.07,0.22),(0.04,0.15,0.31)]:[(1,0.84,0.63),(0.97,0.67,0.77),(0.66,0.78,1)]).map { NSColor(srgbRed:$0.0,green:$0.1,blue:$0.2,alpha:1).cgColor }
        wallpaper.startPoint=CGPoint(x:0,y:1);wallpaper.endPoint=CGPoint(x:1,y:0)
        let w=bounds.width,h=bounds.height
        let shapes:[(NSRect,CGFloat,NSColor)]=[(NSRect(x:w*0.5-220,y:h*0.45,width:170,height:170),85,.systemOrange),(NSRect(x:w*0.5+40,y:h*0.12,width:200,height:130),30,.systemIndigo),(NSRect(x:w*0.5-150,y:h*0.08,width:90,height:90),45,.systemPink)]
        for (layer,shape) in zip(blobs,shapes) { layer.path=CGPath(roundedRect:shape.0,cornerWidth:shape.1,cornerHeight:shape.1,transform:nil);layer.fillColor=shape.2.withAlphaComponent(dark ? 0.7:0.85).cgColor }
    }
}
