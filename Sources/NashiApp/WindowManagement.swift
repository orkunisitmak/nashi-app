import AppKit
import ApplicationServices
import NashiCore

enum WindowPlacementGlyph {
    static func draw(_ placement:WindowPlacement,in slot:NSRect,color:NSColor) {
        let rect=NSRect(x:slot.midX-12,y:slot.midY-8,width:24,height:16)
        let outline=NSBezierPath(roundedRect:rect,xRadius:2,yRadius:2)
        color.setStroke();outline.lineWidth=1.3;outline.stroke()
        let range=placement.horizontalRange,vertical=placement.verticalRange
        color.withAlphaComponent(0.7).setFill()
        NSBezierPath(rect:NSRect(x:rect.minX+2+(rect.width-4)*range.lowerBound,y:rect.maxY-2-(rect.height-4)*vertical.upperBound,
                                width:(rect.width-4)*(range.upperBound-range.lowerBound),height:(rect.height-4)*(vertical.upperBound-vertical.lowerBound))).fill()
    }
}

/// Observes an existing native move/resize gesture. It never captures or rewrites mouse events.
@MainActor final class WindowManagementController {
    private struct Target {
        let element:AXUIElement
        let pid:pid_t
        let initialFrame:CGRect
        let origin:NSPoint
        var intent=WindowDragIntent()
    }
    private var timer:Timer?
    private var target:Target?
    private var wasDown=false
    private var fileSession=DragPasteboardSession()
    private var wheelPanel:NSPanel?
    private var wheel:WheelView?
    private var preview:NSPanel?
    private var lastPlacement:WindowPlacement?
    private var qaCaptureAttempted=false
    private var accessibilityTrusted=false
    private var nextAccessibilityCheck:TimeInterval=0
    private var placementTask:Task<Void,Never>?
    private let fileWheelIsVisible:()->Bool
    var onNotice:((String)->Void)?
    /// Called when macOS grants or revokes the window-control permission.
    var onAccessibilityChange:(()->Void)?

    init(fileWheelIsVisible:@escaping()->Bool) {
        self.fileWheelIsVisible=fileWheelIsVisible
        timer=Timer(timeInterval:0.025,repeats:true) { [weak self] _ in
            Task { @MainActor in self?.tick() }
        }
        RunLoop.main.add(timer!,forMode:.common)
    }

    func stop() { placementTask?.cancel();timer?.invalidate();timer=nil;cancel();target=nil }

    static var permissionName:String {
        ProcessInfo.processInfo.operatingSystemVersion.majorVersion>=27 ? "Device Control and Data Access":"Accessibility"
    }
    static func requestAccess() {
        let options=[kAXTrustedCheckOptionPrompt.takeUnretainedValue() as String:true] as CFDictionary
        _=AXIsProcessTrustedWithOptions(options)
        // The pane anchor is unchanged on macOS 27 even though its visible name changed.
        if let url=URL(string:"x-apple.systempreferences:com.apple.preference.security?Privacy_Accessibility") {
            NSWorkspace.shared.open(url)
        }
    }

    private func tick() {
        let down=NSEvent.pressedMouseButtons & 1 != 0 || CGEventSource.buttonState(.combinedSessionState,button:.left)
        let freshFile=fileSession.containsFreshDrag(isDown:down,changeCount:NSPasteboard(name:.drag).changeCount,
                                                    now:ProcessInfo.processInfo.systemUptime)
        let point=NSEvent.mouseLocation
        let now=ProcessInfo.processInfo.systemUptime
        if now>=nextAccessibilityCheck {
            let trusted=AXIsProcessTrusted();nextAccessibilityCheck=now+1
            if trusted != accessibilityTrusted { accessibilityTrusted=trusted;onAccessibilityChange?() }
        }
        defer { wasDown=down }
        guard UserDefaults.standard.bool(forKey:"enabled"),
              UserDefaults.standard.bool(forKey:"windowManagementEnabled"),accessibilityTrusted else {
            placementTask?.cancel();cancel();target=nil;return
        }
        if !down {
            if wasDown,let captured=target,let placement=currentPlacement(at:point),
               !captured.intent.cancelled,!freshFile {
                let screen=screen(at:point)
                cancel();target=nil
                // Let the originating app finish its native tracking loop before resizing it.
                placementTask?.cancel()
                placementTask=Task { [weak self] in
                    try? await Task.sleep(nanoseconds:80_000_000)
                    guard !Task.isCancelled else{return}
                    await self?.apply(placement,to:captured.element,pid:captured.pid,on:screen)
                }
            } else { cancel();target=nil }
            return
        }
        if !wasDown { placementTask?.cancel();target=capture(at:point) }
        guard var candidate=target else { return }
        if CGEventSource.keyState(.combinedSessionState,key:53) {
            candidate.intent.cancel();target=candidate;cancel();return
        }
        if freshFile || fileWheelIsVisible() {
            candidate.intent.cancel();target=candidate;cancel();return
        }
        guard let frame=Self.frame(of:candidate.element) else {
            cancel();target=nil;return
        }
        let flags=NSEvent.modifierFlags.union(NSEvent.ModifierFlags(rawValue:UInt(CGEventSource.flagsState(.combinedSessionState).rawValue)))
        let changed=abs(frame.minX-candidate.initialFrame.minX)>2 || abs(frame.minY-candidate.initialFrame.minY)>2 ||
                    abs(frame.width-candidate.initialFrame.width)>2 || abs(frame.height-candidate.initialFrame.height)>2
        let accepts=candidate.intent.accepts(pointerDistance:hypot(point.x-candidate.origin.x,point.y-candidate.origin.y),
                                             frameChanged:changed,freshFileDrag:freshFile,
                                             requested:!flags.intersection([.shift,.command]).isEmpty)
        target=candidate
        if accepts,wheelPanel?.isVisible != true { show(at:point) }
        guard wheelPanel?.isVisible == true,let wheel,let panel=wheelPanel else { return }
        wheel.updateSelection(wheel.convert(panel.convertPoint(fromScreen:point),from:nil))
        updatePreview(currentPlacement(at:point),on:screen(at:point))
        captureOwnWheelForQA()
    }

    /// An explicit QA invocation renders only our own view, without recording the screen.
    private func captureOwnWheelForQA() {
        guard !qaCaptureAttempted,wheel?.selected != nil,
              let index=CommandLine.arguments.firstIndex(of:"--capture-window-wheel"),index+1<CommandLine.arguments.count,
              let view=wheelPanel?.contentView,
              let bitmap=view.bitmapImageRepForCachingDisplay(in:view.bounds) else { return }
        qaCaptureAttempted=true
        view.cacheDisplay(in:view.bounds,to:bitmap)
        guard let data=bitmap.representation(using:.png,properties:[:]) else { return }
        let directory=URL(fileURLWithPath:CommandLine.arguments[index+1],isDirectory:true)
        do {
            try FileManager.default.createDirectory(at:directory,withIntermediateDirectories:true)
            try data.write(to:directory.appendingPathComponent("mac-window-wheel-view.png"),options:.atomic)
        } catch { fputs("Nashi window QA view capture failed.\n",stderr) }
    }

    private func capture(at point:NSPoint)->Target? {
        let p=Self.accessibilityPoint(point)
        var hit:AXUIElement?
        let system=AXUIElementCreateSystemWide();AXUIElementSetMessagingTimeout(system,0.1)
        guard AXUIElementCopyElementAtPosition(system,Float(p.x),Float(p.y),&hit) == .success,
              let hit else { return nil }
        AXUIElementSetMessagingTimeout(hit,0.075)
        let role=Self.attribute(hit,kAXRoleAttribute) as? String
        // Text selection, tab rearrangement and scrollbars are never window-arranging gestures.
        guard ![kAXTextFieldRole,kAXTextAreaRole,kAXScrollBarRole,"AXSecureTextField"].contains(role ?? "") else { return nil }
        var element:AXUIElement?
        if role==kAXWindowRole { element=hit }
        else if let window=Self.attribute(hit,kAXWindowAttribute),CFGetTypeID(window)==AXUIElementGetTypeID() { element=(window as! AXUIElement) }
        guard let window=element else { return nil }
        AXUIElementSetMessagingTimeout(window,0.075)
        guard let frame=Self.frame(of:window) else { return nil }
        var pid:pid_t=0
        guard AXUIElementGetPid(window,&pid) == .success,Self.isUsable(window,pid:pid) else { return nil }
        // A title bar/toolbar or a resize border, followed by a real frame change.
        let inTop=p.y>=frame.minY-5 && p.y<=frame.minY+88
        let edge=min(abs(p.x-frame.minX),abs(p.x-frame.maxX),abs(p.y-frame.minY),abs(p.y-frame.maxY))<=9
        guard inTop || edge else { return nil }
        AXUIElementSetMessagingTimeout(window,0.075)
        return Target(element:window,pid:pid,initialFrame:frame,origin:point)
    }

    private func show(at point:NSPoint) {
        WheelFeedback.shared.prepare()
        if wheelPanel==nil {
            let size=CGFloat(320*UserDefaults.standard.double(forKey:"wheelScale"))
            let panel=NSPanel(contentRect:NSRect(x:0,y:0,width:size,height:size),styleMask:[.borderless,.nonactivatingPanel],backing:.buffered,defer:false)
            panel.title="Nashi Window Management";panel.isOpaque=false;panel.backgroundColor = .clear
            panel.level = .statusBar;panel.ignoresMouseEvents=true;panel.hasShadow=false
            panel.collectionBehavior=[.canJoinAllSpaces,.fullScreenAuxiliary];panel.hidesOnDeactivate=false
            let effect=NSVisualEffectView(frame:NSRect(x:0,y:0,width:size,height:size))
            effect.material = .popover;effect.blendingMode = .behindWindow;effect.state = .active
            effect.wantsLayer=true;effect.layer?.cornerRadius=size/2;effect.layer?.masksToBounds=true
            let view=WheelView(frame:effect.bounds);view.autoresizingMask=[.width,.height]
            view.centerTitle="Arrange window";view.mode = .tools
            view.items=WindowPlacement.allCases.map { WheelItem(title:$0.title,symbol:"rectangle",windowPlacement:$0) }
            view.onCancel={ [weak self] in self?.cancel();self?.target?.intent.cancel() }
            effect.addSubview(view);panel.contentView=effect;wheelPanel=panel;wheel=view
        }
        guard let panel=wheelPanel else { return }
        let area=screen(at:point).visibleFrame
        panel.setFrameOrigin(NSPoint(x:min(max(area.minX+4,point.x-panel.frame.width/2),area.maxX-panel.frame.width-4),
                                     y:min(max(area.minY+4,point.y-panel.frame.height/2),area.maxY-panel.frame.height-4)))
        wheel?.selected=nil;wheel?.needsDisplay=true;wheel?.updateAccessibility()
        panel.orderFrontRegardless()
    }

    private func currentPlacement(at point:NSPoint)->WindowPlacement? {
        guard wheelPanel?.isVisible==true,let panel=wheelPanel,let wheel else { return nil }
        let local=wheel.convert(panel.convertPoint(fromScreen:point),from:nil)
        guard let index=wheel.items.indices.first(where:{wheel.petal($0).contains(local)}) else { return nil }
        return wheel.items[index].windowPlacement
    }

    private func updatePreview(_ placement:WindowPlacement?,on screen:NSScreen) {
        guard let placement else { preview?.orderOut(nil);lastPlacement=nil;return }
        if preview==nil {
            let p=NSPanel(contentRect:.zero,styleMask:[.borderless,.nonactivatingPanel],backing:.buffered,defer:false)
            p.title="Nashi Placement Preview";p.level = .floating;p.ignoresMouseEvents=true
            p.backgroundColor = .clear;p.isOpaque=false;p.hasShadow=false;p.hidesOnDeactivate=false
            p.collectionBehavior=[.canJoinAllSpaces,.fullScreenAuxiliary]
            p.contentView=WindowPlacementPreviewView();preview=p
        }
        let frame=placement.frame(in:screen.visibleFrame,scale:screen.backingScaleFactor)
        if preview?.frame != frame { preview?.setFrame(frame,display:true) }
        if let view=preview?.contentView as? WindowPlacementPreviewView { view.title=L10n.text(placement.title)+" · "+placement.fraction;view.needsDisplay=true }
        if lastPlacement != placement || preview?.isVisible != true { preview?.orderFrontRegardless() }
        lastPlacement=placement
    }

    /// A layout shortcut places the frontmost app's focused window without showing the wheel.
    func arrangeFocusedWindow(_ placement:WindowPlacement) {
        guard AXIsProcessTrusted() else {
            onNotice?("Allow \(Self.permissionName) once to move and resize other apps’ windows.");return
        }
        guard let app=NSWorkspace.shared.frontmostApplication else { return }
        // Busy Chromium/Electron apps can take longer than a drag-time query to answer.
        let element=AXUIElementCreateApplication(app.processIdentifier);AXUIElementSetMessagingTimeout(element,1)
        guard let window=Self.frontWindow(of:element,pid:app.processIdentifier),let frame=Self.frame(of:window) else { NSSound.beep();return }
        // Accessibility frames start at the top-left of the primary display; NSScreen frames at the bottom-left.
        let top=NSScreen.screens.first?.frame.maxY ?? 0
        let cocoa=CGRect(x:frame.minX,y:top-frame.maxY,width:frame.width,height:frame.height)
        let screens=NSScreen.screens,current=screen(at:NSPoint(x:cocoa.midX,y:cocoa.midY))
        let index=screens.firstIndex(of:current) ?? 0
        let next=WindowPlacement.destination(for:placement,window:cocoa,displays:screens.map(\.visibleFrame),current:index,
                                             scale:current.backingScaleFactor,resizable:Self.isResizable(window))
        let screen=screens.indices.contains(next.display) ? screens[next.display]:current
        placementTask?.cancel()
        placementTask=Task { [weak self] in await self?.apply(next.placement,to:window,pid:app.processIdentifier,on:screen) }
    }

    /// The focused window, else the main window, else the first window the app lists.
    private static func frontWindow(of app:AXUIElement,pid:pid_t)->AXUIElement? {
        var candidates:[CFTypeRef]=[kAXFocusedWindowAttribute,kAXMainWindowAttribute].compactMap { attribute(app,$0) }
        if let windows=attribute(app,kAXWindowsAttribute) as? [AXUIElement] { candidates+=windows as [CFTypeRef] }
        for candidate in candidates where CFGetTypeID(candidate)==AXUIElementGetTypeID() {
            let window=candidate as! AXUIElement
            AXUIElementSetMessagingTimeout(window,1)
            if isUsable(window,pid:pid) { return window }
        }
        return nil
    }

    private func cancel() { wheelPanel?.orderOut(nil);wheel?.selected=nil;preview?.orderOut(nil);lastPlacement=nil }
    private func screen(at point:NSPoint)->NSScreen { NSScreen.screens.first(where:{$0.frame.contains(point)}) ?? NSScreen.main ?? NSScreen.screens[0] }

    private func apply(_ placement:WindowPlacement,to window:AXUIElement,pid:pid_t,on screen:NSScreen) async {
        guard AXIsProcessTrusted(),Self.isUsable(window,pid:pid) else { return }
        AXUIElementSetMessagingTimeout(window,1)
        // With AXEnhancedUserInterface on (Chrome, Electron, assistive tools) apps animate
        // every AX move, so the frame reads back stale. Pause it while placing the window.
        let app=AXUIElementCreateApplication(pid);AXUIElementSetMessagingTimeout(app,1)
        let enhanced=Self.attribute(app,"AXEnhancedUserInterface") as? Bool==true
        if enhanced { AXUIElementSetAttributeValue(app,"AXEnhancedUserInterface" as CFString,kCFBooleanFalse) }
        defer { if enhanced { AXUIElementSetAttributeValue(app,"AXEnhancedUserInterface" as CFString,kCFBooleanTrue) } }
        let resizable=Self.isResizable(window)
        var wanted=resizable ? placement.frame(in:screen.visibleFrame,scale:screen.backingScaleFactor)
                             : placement.frame(in:screen.visibleFrame,scale:screen.backingScaleFactor,fixedSize:Self.frame(of:window)?.size ?? .zero)
        for attempt in 0..<4 {
            guard !Task.isCancelled,Self.isUsable(window,pid:pid) else{return}
            let acknowledged=Self.requestFrame(wanted,of:window,resize:resizable)
            // AX writes can be acknowledged before Chrome/Electron applies the
            // native layout. Do not report an error from the immediate read.
            try? await Task.sleep(nanoseconds:120_000_000)
            guard !Task.isCancelled,let actual=Self.frame(of:window) else{return}
            let origin=Self.accessibilityPoint(NSPoint(x:wanted.minX,y:wanted.maxY))
            if abs(actual.width-wanted.width)<3,abs(actual.height-wanted.height)<3,
               abs(actual.minX-origin.x)<3,abs(actual.minY-origin.y)<3 {return}
            let wider=actual.width>wanted.width+3,taller=actual.height>wanted.height+3
            if acknowledged,attempt>=1,(wider && (taller || abs(actual.height-wanted.height)<3)) ||
                                        (taller && abs(actual.width-wanted.width)<3) {
                // Respect an app's minimum width or height, while retaining the chosen
                // left/center/right or top/bottom anchor instead of rejecting the placement.
                wanted=placement.frame(in:screen.visibleFrame,scale:screen.backingScaleFactor,minimumSize:actual.size)
            } else if acknowledged,attempt>=2,actual.width<wanted.width-3 || actual.height<wanted.height-3 {
                // The app kept a smaller, fixed size: anchor that size instead. Wait one more
                // round first so a slow resize is not mistaken for a refusal.
                wanted=placement.frame(in:screen.visibleFrame,scale:screen.backingScaleFactor,fixedSize:actual.size)
            }
        }
        if !Task.isCancelled {onNotice?("This window could not be arranged. The app may restrict resizing.")}
    }

    static func attribute(_ element:AXUIElement,_ name:String)->CFTypeRef? {
        var value:CFTypeRef?
        return AXUIElementCopyAttributeValue(element,name as CFString,&value) == .success ? value:nil
    }
    static func frame(of element:AXUIElement)->CGRect? {
        guard let p=attribute(element,kAXPositionAttribute),let s=attribute(element,kAXSizeAttribute),
              CFGetTypeID(p)==AXValueGetTypeID(),CFGetTypeID(s)==AXValueGetTypeID() else { return nil }
        var point=CGPoint.zero,size=CGSize.zero
        guard AXValueGetValue(p as! AXValue,.cgPoint,&point),AXValueGetValue(s as! AXValue,.cgSize,&size) else { return nil }
        return CGRect(origin:point,size:size)
    }
    private static func isUsable(_ window:AXUIElement,pid:pid_t)->Bool {
        // Java, Qt and some Electron apps report their main windows as dialogs or without a
        // subrole; only system-owned surfaces are excluded.
        let subrole=attribute(window,kAXSubroleAttribute) as? String
        guard pid>0,NSRunningApplication(processIdentifier:pid)?.isTerminated==false,
              attribute(window,kAXRoleAttribute) as? String==kAXWindowRole,
              ![kAXSystemDialogSubrole,kAXSystemFloatingWindowSubrole,kAXUnknownSubrole].contains(subrole ?? ""),
              attribute(window,kAXMinimizedAttribute) as? Bool != true,
              attribute(window,"AXFullScreen") as? Bool != true else { return false }
        // Some apps answer "not settable" yet accept the write, so only a definite refusal counts.
        var position:DarwinBoolean=true
        return AXUIElementIsAttributeSettable(window,kAXPositionAttribute as CFString,&position) != .success || position.boolValue
    }
    private static func isResizable(_ window:AXUIElement)->Bool {
        var size:DarwinBoolean=true
        return AXUIElementIsAttributeSettable(window,kAXSizeAttribute as CFString,&size) != .success || size.boolValue
    }
    static func accessibilityPoint(_ point:NSPoint)->CGPoint {
        CGPoint(x:point.x,y:(NSScreen.screens.first?.frame.maxY ?? 0)-point.y)
    }
    static func setFrame(_ frame:NSRect,of window:AXUIElement)->Bool {
        guard requestFrame(frame,of:window),let actual=Self.frame(of:window) else{return false}
        let position=accessibilityPoint(NSPoint(x:frame.minX,y:frame.maxY))
        return abs(actual.width-frame.width)<3 && abs(actual.height-frame.height)<3 &&
               abs(actual.minX-position.x)<3 && abs(actual.minY-position.y)<3
    }
    private static func requestFrame(_ frame:NSRect,of window:AXUIElement,resize:Bool=true)->Bool {
        var size=frame.size,position=accessibilityPoint(NSPoint(x:frame.minX,y:frame.maxY))
        guard let s=AXValueCreate(.cgSize,&size),let p=AXValueCreate(.cgPoint,&position) else { return false }
        // A refused size still moves the window; the caller then anchors the size the app kept.
        if resize { _=AXUIElementSetAttributeValue(window,kAXSizeAttribute as CFString,s) }
        guard AXUIElementSetAttributeValue(window,kAXPositionAttribute as CFString,p) == .success else { return false }
        // Some apps constrain the first resize to the old display. Repeat after the move.
        if resize { _=AXUIElementSetAttributeValue(window,kAXSizeAttribute as CFString,s) }
        _=AXUIElementSetAttributeValue(window,kAXPositionAttribute as CFString,p)
        return true
    }
}

private final class WindowPlacementPreviewView:NSView {
    var title=""
    override func draw(_ dirtyRect:NSRect) {
        let shape=NSBezierPath(roundedRect:bounds.insetBy(dx:4,dy:4),xRadius:15,yRadius:15)
        NashiPalette.gold.withAlphaComponent(0.16).setFill();shape.fill()
        NashiPalette.gold.withAlphaComponent(0.8).setStroke();shape.lineWidth=2;shape.stroke()
        let text=NSAttributedString(string:title,attributes:[.font:NSFont.systemFont(ofSize:14,weight:.semibold),.foregroundColor:NSColor.labelColor])
        let size=text.size(),rect=NSRect(x:bounds.midX-size.width/2-16,y:bounds.maxY-62,width:size.width+32,height:38)
        NSColor.windowBackgroundColor.withAlphaComponent(0.95).setFill();NSBezierPath(roundedRect:rect,xRadius:11,yRadius:11).fill()
        text.draw(at:NSPoint(x:rect.midX-size.width/2,y:rect.midY-size.height/2))
    }
}
