import AppKit
import SwiftUI
import NashiCore

@MainActor final class NotchPanelController {
    private let state:AppState
    private let model=NotchPanelModel()
    private let chooseFiles:()->Void
    private let chooseTool:(ToolKind)->Void
    private var panel:NotchAccessoryPanel?
    private var trackingHost:NotchTrackingHostView?
    private var defaultsObserver:NSObjectProtocol?
    private var screenObserver:NSObjectProtocol?
    private var frameTimer:Timer?

    init(state:AppState,chooseFiles:@escaping()->Void,chooseTool:@escaping(ToolKind)->Void) {
        self.state=state;self.chooseFiles=chooseFiles;self.chooseTool=chooseTool
        model.onExpandedChange={ [weak self] _ in self?.updatePanelFrame(animated:true) }
        defaultsObserver=NotificationCenter.default.addObserver(forName:UserDefaults.didChangeNotification,object:nil,queue:.main) { [weak self] _ in
            Task { @MainActor in self?.synchronizePreference() }
        }
        screenObserver=NotificationCenter.default.addObserver(forName:NSApplication.didChangeScreenParametersNotification,object:nil,queue:.main) { [weak self] _ in
            Task { @MainActor in self?.updatePanelFrame(animated:false) }
        }
        synchronizePreference()
    }

    func synchronizePreference() {
        setEnabled(UserDefaults.standard.bool(forKey:"notchModeEnabled"))
    }

    func setEnabled(_ enabled:Bool) {
        guard enabled else { tearDownPanel();return }
        if panel == nil { createPanel() }
        guard let panel else { return }
        if !panel.isVisible { panel.orderFrontRegardless() }
    }

    func stop() {
        if let defaultsObserver { NotificationCenter.default.removeObserver(defaultsObserver) }
        if let screenObserver { NotificationCenter.default.removeObserver(screenObserver) }
        defaultsObserver=nil;screenObserver=nil
        tearDownPanel()
    }

    private func createPanel() {
        guard let screen=NSScreen.main ?? NSScreen.screens.first else { return }
        let frame=targetFrame(on:screen,expanded:false)
        let accessory=NotchAccessoryPanel(contentRect:frame,styleMask:[.borderless,.nonactivatingPanel],backing:.buffered,defer:false)
        accessory.isReleasedWhenClosed=false
        accessory.isOpaque=false
        accessory.backgroundColor = .clear
        accessory.hasShadow=false
        accessory.level = .statusBar
        accessory.collectionBehavior=[.canJoinAllSpaces,.fullScreenAuxiliary,.ignoresCycle]
        accessory.hidesOnDeactivate=false
        accessory.isFloatingPanel=true
        accessory.animationBehavior = .none
        accessory.appearance=NSAppearance(named:.darkAqua)

        let host=NotchTrackingHostView(frame:NSRect(origin:.zero,size:frame.size))
        let content=NotchWorkspaceView(state:state,model:model,media:state.filePreview,onChooseFiles:chooseFiles,onChooseTool:chooseTool)
        let hosting=NSHostingView(rootView:content)
        hosting.frame=host.bounds
        hosting.autoresizingMask=[.width,.height]
        host.addSubview(hosting)
        host.onPointerEntered={ [weak self] in self?.model.pointerEntered() }
        host.onPointerExited={ [weak self] in self?.model.pointerExited() }
        host.onDraggingEntered={ [weak self] in self?.model.draggingEntered() }
        host.onDraggingExited={ [weak self] in self?.model.draggingExited() }
        host.onDraggingEnded={ [weak self] in self?.model.draggingEnded() }
        host.onFilesDropped={ [weak self] urls in guard let self else { return };self.model.acceptedDrop(urls,state:self.state) }
        accessory.contentView=host
        panel=accessory;trackingHost=host
        accessory.orderFrontRegardless()
    }

    private func tearDownPanel() {
        frameTimer?.invalidate();frameTimer=nil
        model.stop()
        if let panel { panel.orderOut(nil);panel.close() }
        panel=nil;trackingHost=nil
    }

    private func updatePanelFrame(animated:Bool) {
        guard let panel,let screen=panel.screen ?? NSScreen.main ?? NSScreen.screens.first else { return }
        let target=targetFrame(on:screen,expanded:model.isExpanded)
        frameTimer?.invalidate();frameTimer=nil
        guard animated,NashiMotion.enabled,panel.frame != target else { panel.setFrame(target,display:true);return }
        let start=panel.frame
        let began=ProcessInfo.processInfo.systemUptime
        frameTimer=Timer.scheduledTimer(withTimeInterval:1/60,repeats:true) { [weak self,weak panel] timer in
            guard let self,let panel else { timer.invalidate();return }
            let time=ProcessInfo.processInfo.systemUptime-began
            let response=0.35,damping=0.78
            let omega=2*Double.pi/response
            let damped=omega*sqrt(1-damping*damping)
            let coefficient=damping/sqrt(1-damping*damping)
            let eased=1-exp(-damping*omega*time)*(cos(damped*time)+coefficient*sin(damped*time))
            let amount=CGFloat(eased)
            let frame=NSRect(x:start.minX+(target.minX-start.minX)*amount,
                             y:start.minY+(target.minY-start.minY)*amount,
                             width:start.width+(target.width-start.width)*amount,
                             height:start.height+(target.height-start.height)*amount)
            panel.setFrame(frame,display:true)
            if time>=0.7 { panel.setFrame(target,display:true);timer.invalidate();self.frameTimer=nil }
        }
    }

    private func targetFrame(on screen:NSScreen,expanded:Bool)->NSRect {
        let metrics=NotchScreenMetrics(frame:screen.frame,visibleFrame:screen.visibleFrame,safeAreaTop:screen.safeAreaInsets.top,
                                       auxiliaryTopLeftArea:screen.auxiliaryTopLeftArea,auxiliaryTopRightArea:screen.auxiliaryTopRightArea)
        let notch=NotchPanelGeometry.placement(on:metrics,size:NSSize(width:1,height:1))
        let width:CGFloat
        let height:CGFloat
        if expanded { width=540;height=470 }
        else { width=notch.isNotched ? max(168,min(232,notch.notchWidth+36)):164;height=44 }
        return NotchPanelGeometry.placement(on:metrics,size:NSSize(width:width,height:height)).frame
    }
}

@MainActor private final class NotchAccessoryPanel:NSPanel {
    override var canBecomeKey:Bool { true }
    override var canBecomeMain:Bool { false }
}

@MainActor private final class NotchTrackingHostView:NSView {
    var onPointerEntered:(()->Void)?
    var onPointerExited:(()->Void)?
    var onDraggingEntered:(()->Void)?
    var onDraggingExited:(()->Void)?
    var onDraggingEnded:(()->Void)?
    var onFilesDropped:(([URL])->Void)?
    private var tracking:NSTrackingArea?

    override init(frame frameRect:NSRect) {
        super.init(frame:frameRect)
        registerForDraggedTypes([.fileURL])
    }

    required init?(coder:NSCoder) {
        super.init(coder:coder)
        registerForDraggedTypes([.fileURL])
    }

    override func updateTrackingAreas() {
        if let tracking { removeTrackingArea(tracking) }
        let area=NSTrackingArea(rect:.zero,options:[.mouseEnteredAndExited,.activeAlways,.inVisibleRect],owner:self,userInfo:nil)
        tracking=area;addTrackingArea(area)
        super.updateTrackingAreas()
    }

    override func mouseEntered(with event:NSEvent) { onPointerEntered?() }
    override func mouseExited(with event:NSEvent) { onPointerExited?() }

    override func draggingEntered(_ sender:NSDraggingInfo)->NSDragOperation {
        guard !Self.fileURLs(sender.draggingPasteboard).isEmpty else { return [] }
        onDraggingEntered?();return .copy
    }

    override func draggingUpdated(_ sender:NSDraggingInfo)->NSDragOperation {
        Self.fileURLs(sender.draggingPasteboard).isEmpty ? []:.copy
    }

    override func draggingExited(_ sender:NSDraggingInfo?) { onDraggingExited?() }

    override func prepareForDragOperation(_ sender:NSDraggingInfo)->Bool {
        !Self.fileURLs(sender.draggingPasteboard).isEmpty
    }

    override func performDragOperation(_ sender:NSDraggingInfo)->Bool {
        let urls=Self.fileURLs(sender.draggingPasteboard)
        guard !urls.isEmpty else { return false }
        onFilesDropped?(urls);return true
    }

    override func concludeDragOperation(_ sender:NSDraggingInfo?) { onDraggingEnded?() }
    override func draggingEnded(_ sender:NSDraggingInfo?) { onDraggingEnded?() }

    private static func fileURLs(_ pasteboard:NSPasteboard)->[URL] {
        (pasteboard.readObjects(forClasses:[NSURL.self],options:[.urlReadingFileURLsOnly:true]) as? [URL]) ?? []
    }
}
