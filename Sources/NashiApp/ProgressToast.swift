import AppKit
import SwiftUI
import Combine

/// The same chrome-free card hosts every local conversion/tool and web job.
@MainActor final class ProgressToastController {
    private(set) var panel:NSPanel?
    private var observation:AnyCancellable?

    func show(state:AppState) {
        guard panel == nil else { return }
        let screen=NSScreen.screens.first { $0.frame.contains(NSEvent.mouseLocation) } ?? NSScreen.main
        let visible=screen?.visibleFrame ?? NSRect(x:0,y:0,width:1440,height:900)
        let width=min(520,visible.width-32)
        let frame=NSRect(x:visible.midX-width/2,y:visible.minY+28,width:width,height:Self.height(error:false))
        let toast=ProgressToastPanel(contentRect:frame,styleMask:[.borderless,.nonactivatingPanel],backing:.buffered,defer:false)
        toast.title="Nashi — Progress"
        toast.isReleasedWhenClosed=false;toast.isOpaque=false;toast.backgroundColor = .clear
        toast.hasShadow=true;toast.level = .floating;toast.isFloatingPanel=true
        toast.hidesOnDeactivate=false;toast.animationBehavior = .none
        toast.collectionBehavior=[.canJoinAllSpaces,.fullScreenAuxiliary,.transient,.ignoresCycle]
        toast.contentView=NSHostingView(rootView:ProgressToastContent(state:state).tint(Color.nashi).nashiLanguage())
        panel=toast
        // Rises a little from the Dock edge and settles, like a system notice.
        if NashiMotion.enabled {
            toast.setFrame(frame.offsetBy(dx:0,dy:-18),display:false);toast.alphaValue=0;toast.orderFrontRegardless()
            NSAnimationContext.runAnimationGroup { context in
                context.duration=0.45;context.timingFunction=CAMediaTimingFunction(controlPoints:0.2,0.9,0.25,1)
                toast.animator().setFrame(frame,display:true);toast.animator().alphaValue=1
            }
        } else { toast.orderFrontRegardless() }
        observation=state.$working.combineLatest(state.$error).receive(on:RunLoop.main).sink { [weak self,weak toast] working,error in
            guard let self,let toast,self.panel === toast else { return }
            if !working && error == nil { self.dismiss();return }
            var resized=toast.frame;resized.size.height=Self.height(error:error != nil)
            guard resized != toast.frame else { return }
            if NashiMotion.enabled { NSAnimationContext.runAnimationGroup { context in context.duration=0.3;toast.animator().setFrame(resized,display:true) } }
            else { toast.setFrame(resized,display:true) }
        }
    }
    func dismiss() {
        observation?.cancel();observation=nil
        guard let old=panel else { return }
        panel=nil
        guard NashiMotion.enabled,old.isVisible else { old.orderOut(nil);old.close();return }
        NSAnimationContext.runAnimationGroup({ context in
            context.duration=0.22;context.timingFunction=CAMediaTimingFunction(name:.easeIn)
            old.animator().setFrame(old.frame.offsetBy(dx:0,dy:-12),display:true);old.animator().alphaValue=0
        },completionHandler:{ old.orderOut(nil);old.close() })
    }
    private static func height(error:Bool)->CGFloat { error ? 112:84 }
}

private final class ProgressToastPanel:NSPanel {
    override var canBecomeKey:Bool { false }
    override var canBecomeMain:Bool { false }
}

/// The floating card: glass, a gold ring around the pear, a soft bar and a quiet Cancel.
private struct ProgressToastContent:View {
    @ObservedObject var state:AppState
    @Environment(\.colorScheme) private var scheme
    var body:some View {
        VStack(alignment:.leading,spacing:12) {
            if let error=state.error {
                HStack(alignment:.top,spacing:12) {
                    Image(systemName:"exclamationmark.circle.fill").font(.system(size:20)).foregroundStyle(.orange)
                    VStack(alignment:.leading,spacing:3) {
                        Text("Let’s try that again.").font(.system(size:13,weight:.semibold,design:.rounded))
                        LText(error).font(.system(size:11)).foregroundStyle(.secondary).lineLimit(3).textSelection(.enabled)
                    }
                    Spacer(minLength:8)
                    Button { state.error=nil } label:{ Image(systemName:"xmark") }.buttonStyle(NashiButtonStyle(.quiet,compact:true)).accessibilityLabel("Dismiss error")
                }.transition(.opacity)
            } else {
                HStack(spacing:12) {
                    ProgressRing(state:state).frame(width:32,height:32)
                    VStack(alignment:.leading,spacing:3) {
                        Text("A fresh file is on its way.").font(.system(size:13,weight:.semibold,design:.rounded))
                        LText(state.progressLabel).font(.system(size:11)).foregroundStyle(.secondary).lineLimit(1).truncationMode(.middle)
                    }
                    Spacer(minLength:8)
                    if state.progressIsDeterminate { Text("\(Int(state.progress*100))%").font(.system(size:12,weight:.semibold,design:.rounded)).monospacedDigit().foregroundStyle(.secondary) }
                    Button("Cancel") { state.token.cancel() }.buttonStyle(NashiButtonStyle(.secondary,compact:true))
                }
                SoftProgressBar(state:state)
            }
        }
        .padding(.horizontal,18).padding(.vertical,15)
        .frame(maxWidth:.infinity,maxHeight:.infinity,alignment:.leading)
        .toastGlass(dark:scheme == .dark)
        .nashiAnimation(value:state.error != nil)
    }
}

private extension View {
    /// Liquid Glass on macOS 26 and later, a behind-window blur with a light rim before that.
    @ViewBuilder func toastGlass(dark:Bool)->some View {
        let shape=RoundedRectangle(cornerRadius:22,style:.continuous)
        if #available(macOS 26.0,*) { glassEffect(.regular,in:shape) }
        else { background(ToastGlass()).overlay(shape.strokeBorder(Color.white.opacity(dark ? 0.12:0.6),lineWidth:0.75)).clipShape(shape) }
    }
}

private struct ToastGlass:NSViewRepresentable {
    func makeNSView(context:Context)->NSVisualEffectView { let view=NSVisualEffectView();view.material = .popover;view.blendingMode = .behindWindow;view.state = .active;return view }
    func updateNSView(_ view:NSVisualEffectView,context:Context) {}
}

private struct ProgressRing:View {
    @ObservedObject var state:AppState
    @State private var spinning=false
    @Environment(\.accessibilityReduceMotion) private var systemReduced
    @AppStorage("reduceAnimations") private var reduced=false
    var body:some View {
        let determinate=state.progressIsDeterminate,still=systemReduced || reduced
        ZStack {
            Circle().stroke(Color.primary.opacity(0.08),lineWidth:3)
            Circle().trim(from:0,to:determinate ? max(0.02,state.progress):0.28)
                .stroke(AngularGradient(colors:[.nashi,NashiPalette.goldEnd,.nashi],center:.center),style:StrokeStyle(lineWidth:3,lineCap:.round))
                .rotationEffect(.degrees(determinate || still ? -90:(spinning ? 270:-90)))
                .animation(determinate || still ? nil:.linear(duration:1.1).repeatForever(autoreverses:false),value:spinning)
            NashiMark(size:15)
        }
        .nashiAnimation(value:state.progress)
        .onAppear { spinning=true }
        .accessibilityHidden(true)
    }
}

private struct SoftProgressBar:View {
    @ObservedObject var state:AppState
    @State private var glide=false
    @Environment(\.accessibilityReduceMotion) private var systemReduced
    @AppStorage("reduceAnimations") private var reduced=false
    var body:some View {
        let fill=LinearGradient(colors:[.nashi,NashiPalette.goldEnd],startPoint:.leading,endPoint:.trailing),still=systemReduced || reduced
        GeometryReader { proxy in
            ZStack(alignment:.leading) {
                Capsule().fill(Color.primary.opacity(0.07))
                if state.progressIsDeterminate {
                    Capsule().fill(fill).frame(width:max(5,proxy.size.width*state.progress))
                } else if still {
                    Capsule().fill(fill.opacity(0.45))
                } else {
                    // No invented percentage for opaque work: a soft segment glides back and forth.
                    Capsule().fill(fill).frame(width:proxy.size.width*0.3).offset(x:glide ? proxy.size.width*0.7:0)
                        .animation(.easeInOut(duration:1.2).repeatForever(autoreverses:true),value:glide)
                }
            }
        }
        .frame(height:4).nashiAnimation(value:state.progress)
        .onAppear { glide=true }
        .accessibilityElement().accessibilityLabel(L10n.text(state.progressIsDeterminate ? "Progress":"In progress"))
        .accessibilityValue(state.progressIsDeterminate ? L10n.text("\(Int(state.progress*100)) percent"):L10n.text(state.progressLabel))
    }
}

struct JobProgressBar:View {
    @ObservedObject var state:AppState
    var body:some View {
        if state.progressIsDeterminate {
            GeometryReader { proxy in
                ZStack(alignment:.leading) {
                    Capsule().fill(NashiPalette.inset)
                    Capsule().fill(LinearGradient(colors:[.nashi,NashiPalette.goldEnd],startPoint:.leading,endPoint:.trailing))
                        .frame(width:max(0,proxy.size.width*state.progress))
                }
            }.frame(height:5).nashiAnimation(value:state.progress)
                .accessibilityElement().accessibilityLabel("Progress").accessibilityValue("\(Int(state.progress*100)) percent")
        } else {
            // The native indeterminate indicator respects Reduce Motion and never
            // invents a percentage for an opaque PDFKit/Office/archive operation.
            ProgressView().progressViewStyle(.linear).frame(height:5)
                .accessibilityLabel("In progress").accessibilityValue(state.progressLabel)
        }
    }
}
