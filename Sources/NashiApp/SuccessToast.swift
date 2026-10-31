import AppKit
import SwiftUI

struct SuccessNotice {
    let title:String
    let detail:String
    var files:[URL]=[]
    /// File names stay verbatim; counts and result copy in the detail line are UI copy.
    var detailIsFileName=false

    static func filesReady(_ files:[URL])->SuccessNotice {
        SuccessNotice(title:files.isEmpty ? "Nicely done.":"Nicely done. \(files.count) file\(files.count == 1 ? " is":"s are") ready.",
                      detail:files.first?.lastPathComponent ?? "Your task is complete.",files:files,detailIsFileName:!files.isEmpty)
    }
    static func action(_ title:String,files:[URL])->SuccessNotice {
        SuccessNotice(title:title,detail:files.count == 1 ? files[0].lastPathComponent:"\(files.count) files",files:files,detailIsFileName:files.count == 1)
    }
}

@MainActor final class SuccessToastController {
    private(set) var panel:NSPanel?
    private var dismissal:Task<Void,Never>?
    static let visibleDuration:TimeInterval=3
    static let fadeDuration:TimeInterval=0.28

    func show(_ notice:SuccessNotice) {
        dismiss()
        let screen=NSScreen.screens.first { $0.frame.contains(NSEvent.mouseLocation) } ?? NSScreen.main
        let visible=screen?.visibleFrame ?? NSRect(x:0,y:0,width:1440,height:900)
        let size=NSSize(width:min(560,visible.width-32),height:88)
        let frame=NSRect(x:visible.midX-size.width/2,y:visible.minY+28,width:size.width,height:size.height)
        let toast=SuccessToastPanel(contentRect:frame,styleMask:[.borderless,.nonactivatingPanel],backing:.buffered,defer:false)
        toast.title="Nashi — Complete"
        toast.isReleasedWhenClosed=false
        toast.isOpaque=false
        toast.backgroundColor = .clear
        toast.hasShadow=true
        toast.level = .floating
        toast.isFloatingPanel=true
        toast.hidesOnDeactivate=false
        toast.animationBehavior = .none
        toast.collectionBehavior=[.canJoinAllSpaces,.fullScreenAuxiliary,.transient,.ignoresCycle]
        toast.contentView=NSHostingView(rootView:SuccessToastView(notice:notice).tint(Color.nashi).nashiLanguage())
        panel=toast
        let shownAt=ProcessInfo.processInfo.systemUptime
        toast.orderFrontRegardless()
        NSAccessibility.post(element:toast,notification:.announcementRequested,userInfo:[.announcement:notice.title,.priority:NSAccessibilityPriorityLevel.medium.rawValue])
        dismissal=Task { @MainActor [weak self,weak toast] in
            let remaining=max(0,Self.visibleDuration-(ProcessInfo.processInfo.systemUptime-shownAt))
            do { try await Task.sleep(nanoseconds:UInt64(remaining*1_000_000_000)) }
            catch { return }
            guard !Task.isCancelled,let self,let toast,self.panel === toast else { return }
            self.fadeOut(toast)
        }
    }

    func dismiss() {
        dismissal?.cancel();dismissal=nil
        panel?.orderOut(nil);panel?.close();panel=nil
    }

    private func fadeOut(_ toast:NSPanel) {
        guard NashiMotion.enabled else { dismiss();return }
        NSAnimationContext.runAnimationGroup { context in
            context.duration=Self.fadeDuration
            context.timingFunction=CAMediaTimingFunction(name:.easeOut)
            toast.animator().alphaValue=0
        } completionHandler:{ [weak self,weak toast] in
            Task { @MainActor in
                guard let self,let toast,self.panel === toast else { return }
                self.dismiss()
            }
        }
    }
}

private final class SuccessToastPanel:NSPanel {
    override var canBecomeKey:Bool { false }
    override var canBecomeMain:Bool { false }
}

private struct SuccessToastView:View {
    let notice:SuccessNotice
    var body:some View {
        HStack(spacing:13) {
            NashiCheck()
            VStack(alignment:.leading,spacing:5) {
                LText(notice.title).font(.system(size:13,weight:.semibold,design:.rounded)).lineLimit(2)
                Group { if notice.detailIsFileName { Text(notice.detail) } else { LText(notice.detail) } }.font(.system(size:11)).foregroundStyle(.secondary).lineLimit(1).truncationMode(.middle)
            }.frame(maxWidth:.infinity,alignment:.leading)
            if !notice.files.isEmpty {
                Button("Show in Finder") { NSWorkspace.shared.activateFileViewerSelecting(notice.files) }
                    .buttonStyle(NashiButtonStyle(.secondary,compact:true))
            }
        }.padding(.horizontal,18).frame(maxWidth:.infinity,maxHeight:.infinity)
            .background(NashiPalette.sage.opacity(0.6)).background(NashiPalette.canvas)
            .clipShape(RoundedRectangle(cornerRadius:22,style:.continuous))
    }
}
