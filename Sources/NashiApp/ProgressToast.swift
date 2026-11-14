import AppKit
import SwiftUI
import Combine
import NashiCore

/// The same chrome-free card hosts every local conversion/tool and web job.
@MainActor final class ProgressToastController {
    private(set) var panel:NSPanel?
    private var observation:AnyCancellable?

    func show(state:AppState) {
        guard panel == nil else { return }
        let screen=NSScreen.screens.first { $0.frame.contains(NSEvent.mouseLocation) } ?? NSScreen.main
        let visible=screen?.visibleFrame ?? NSRect(x:0,y:0,width:1440,height:900)
        let width=min(560,visible.width-32)
        let frame=NSRect(x:visible.midX-width/2,y:visible.minY+28,width:width,height:104)
        let toast=ProgressToastPanel(contentRect:frame,styleMask:[.borderless,.nonactivatingPanel],backing:.buffered,defer:false)
        toast.title="Nashi — Progress"
        toast.isReleasedWhenClosed=false;toast.isOpaque=false;toast.backgroundColor = .clear
        toast.hasShadow=true;toast.level = .floating;toast.isFloatingPanel=true
        toast.hidesOnDeactivate=false;toast.animationBehavior = .none
        toast.collectionBehavior=[.canJoinAllSpaces,.fullScreenAuxiliary,.transient,.ignoresCycle]
        let host=NSHostingView(rootView:JobProgressView(state:state).frame(maxWidth:.infinity,maxHeight:.infinity).tint(Color.nashi).nashiLanguage())
        host.wantsLayer=true;host.layer?.cornerRadius=18;host.layer?.masksToBounds=true
        toast.contentView=host;panel=toast;toast.orderFrontRegardless()
        observation=state.$working.combineLatest(state.$error).receive(on:RunLoop.main).sink { [weak self,weak toast] working,error in
            guard let self,let toast,self.panel === toast else { return }
            if !working && error == nil { self.dismiss();return }
            var resized=toast.frame;resized.size.height=error == nil ? 104:140
            toast.setFrame(resized,display:true)
        }
    }
    func dismiss() {
        observation?.cancel();observation=nil
        let old=panel;panel=nil;old?.orderOut(nil);old?.close()
    }
}

private final class ProgressToastPanel:NSPanel {
    override var canBecomeKey:Bool { false }
    override var canBecomeMain:Bool { false }
}

struct JobProgressBar:View {
    @ObservedObject var state:AppState
    @AppStorage("appLanguage") private var language="en"
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
                .accessibilityLabel("In progress").accessibilityValue(AppLocalization.text(state.progressLabel,language:language))
        }
    }
}
