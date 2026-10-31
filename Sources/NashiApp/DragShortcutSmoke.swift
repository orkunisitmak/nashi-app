import AppKit
import SwiftUI

/// Own-window QA: real Settings recorder and queued events in an isolated preference suite.
@MainActor final class DragShortcutSmoke:NSObject,NSApplicationDelegate {
    private struct Host:View {
        let defaults:UserDefaults
        let onRecording:(Bool)->Void
        @State private var revision=0
        var body: some View { DragShortcutRecorder(wheel:.tools,defaults:defaults,onRecording:onRecording) { revision += 1 }.id(revision).frame(width:320,height:80) }
    }
    private let output:URL
    private let suite="local.nashi.drag-shortcut-smoke"
    private lazy var defaults=UserDefaults(suiteName:suite)!
    private var window:NSWindow!
    private var checks:[String]=[]
    private var recording=false
    private var saved:String { ModifierPreferences.shortcut(for:.tools,defaults:defaults).name }
    init(output:URL) { self.output=output }
    func applicationDidFinishLaunching(_ notification:Notification) {
        Task {
            var failure:String?
            do { try await run() } catch { failure=String(describing:error) }
            defaults.removePersistentDomain(forName:suite)
            let report:[String:Any]=["passed":failure == nil,"checks":checks,"error":failure as Any? ?? NSNull()]
            try? FileManager.default.createDirectory(at:output,withIntermediateDirectories:true)
            if let data=try? JSONSerialization.data(withJSONObject:report,options:[.prettyPrinted,.sortedKeys]) { try? data.write(to:output.appendingPathComponent("drag-shortcuts.json")) }
            window?.close();exit(failure == nil ? 0:1)
        }
    }
    private func check(_ value:Bool,_ label:String)throws { if !value { throw NSError(domain:"Nashi Shortcut QA",code:1,userInfo:[NSLocalizedDescriptionKey:label]) };checks.append(label) }
    private func pause()async { try? await Task.sleep(nanoseconds:120_000_000) }
    private func post(_ event:NSEvent?)async { if let event { NSApp.postEvent(event,atStart:false) };await pause() }
    private func click(_ point:NSPoint)async {
        for type in [NSEvent.EventType.leftMouseDown,.leftMouseUp] { await post(NSEvent.mouseEvent(with:type,location:point,modifierFlags:[],timestamp:ProcessInfo.processInfo.systemUptime,windowNumber:window.windowNumber,context:nil,eventNumber:0,clickCount:1,pressure:1)) }
    }
    /// SwiftUI delivers the click on its own schedule, so wait for the recorder rather than a fixed delay.
    private func startRecording()async throws {
        for _ in 0..<8 {
            await click(center)
            for _ in 0..<8 where !recording { await pause() }
            if recording { return }
        }
        throw NSError(domain:"Nashi Shortcut QA",code:2,userInfo:[NSLocalizedDescriptionKey:"Recording did not start"])
    }
    private func saved(is name:String)async->Bool {
        for _ in 0..<16 where saved != name { await pause() }
        return saved == name
    }
    private var center:NSPoint { NSPoint(x:window.contentLayoutRect.midX,y:window.contentLayoutRect.midY) }
    private func key(_ type:NSEvent.EventType,_ code:UInt16,_ flags:NSEvent.ModifierFlags=[])async {
        await post(NSEvent.keyEvent(with:type,location:.zero,modifierFlags:flags,timestamp:ProcessInfo.processInfo.systemUptime,windowNumber:window.windowNumber,context:nil,characters:"",charactersIgnoringModifiers:"",isARepeat:false,keyCode:code))
    }
    private func mouse(_ button:Int64,_ flags:CGEventFlags=[])async {
        let event=CGEvent(mouseEventSource:nil,mouseType:button == 1 ? .rightMouseDown:.otherMouseDown,mouseCursorPosition:.zero,mouseButton:.center)
        event?.setIntegerValueField(.mouseEventButtonNumber,value:button);event?.flags=flags
        await post(event.flatMap{NSEvent(cgEvent:$0)})
    }
    private func run()async throws {
        defaults.removePersistentDomain(forName:suite)
        func wheel(_ flags:NSEvent.ModifierFlags,_ held:DragTrigger?=nil)->WheelShortcut? { ModifierPreferences.wheel(for:flags,pressed:{$0==held},defaults:defaults) }
        try check(wheel(.shift) == .conversions && wheel(.command) == .tools && wheel(.control) == .actions && wheel([]) == nil,"Default modifiers keep their wheels")
        for name in ["control+shift","command+mouse3","key49"] { try check(DragShortcut(name:name)?.name == name,"Name round trip: "+name) }
        try check(DragShortcut(name:"") == nil && DragShortcut(name:"mouse0") == nil && DragShortcut(name:"command+nonsense") == nil,"Empty, drag-button and unknown names are refused")
        try check(ModifierChord.legacy("command+mouse3") == nil,"Older builds fall back to their default")
        try check(DragShortcut(chord:ModifierChord(.command),trigger:.mouse(3)).symbols == "⌘ Mouse 4" && DragShortcut(chord:ModifierChord(rawValue:0),trigger:.mouse(1)).label == "Right Click","Trigger titles read naturally")

        ModifierPreferences.set(DragShortcut(chord:ModifierChord(.command),trigger:.mouse(4)),for:.actions,defaults:defaults)
        try check(wheel(.command) == .tools && wheel(.command,.mouse(4)) == .actions,"A held trigger beats its plain modifier shortcut")
        ModifierPreferences.set(DragShortcut(chord:ModifierChord(.command),trigger:.mouse(3)),for:.conversions,defaults:defaults)
        try check(ModifierPreferences.wheel(for:.command,defaults:defaults) == .tools,"Without trigger state a plain shortcut keeps priority")
        try check(ModifierPreferences.chord(for:.conversions,defaults:defaults) == .init(.shift) && ModifierPreferences.chord(for:.actions,defaults:defaults) == .init(.control),"Older builds use each wheel's default for triggers sharing a chord")
        ModifierPreferences.set(DragShortcut(chord:ModifierChord(.option),trigger:.key(3)),for:.tools,defaults:defaults)
        try check(ModifierPreferences.wheel(for:.option,defaults:defaults) == .tools,"Without trigger state a triggered shortcut still matches its modifiers")
        for wheel in WheelShortcut.allCases { defaults.removeObject(forKey:wheel.storageKey);defaults.removeObject(forKey:wheel.legacyKey) }

        window=NSWindow(contentRect:NSRect(x:200,y:200,width:320,height:80),styleMask:[.titled],backing:.buffered,defer:false)
        window.contentView=NSHostingView(rootView:Host(defaults:defaults) { [weak self] in self?.recording=$0 }.nashiLanguage());window.isReleasedWhenClosed=false
        NSApp.activate(ignoringOtherApps:true);window.makeKeyAndOrderFront(nil);await pause()
        try check(window.isKeyWindow,"Own window is key")

        try await startRecording();await mouse(3,.maskCommand)
        try await check(saved(is:"command+mouse3"),"Records a modified mouse button")
        try check(wheel(.command,.mouse(3)) == .tools && wheel(.command) == nil,"Mouse shortcut matches only while its button is held")
        try await startRecording();await mouse(1)
        try await check(saved(is:"mouse1"),"Records a bare right click")
        try check(wheel([],.mouse(1)) == .tools,"Bare mouse shortcut needs no modifier")
        try await startRecording();await key(.keyDown,3,.option)
        try await check(saved(is:"option+key3"),"Records a key with modifiers")
        try check(wheel(.option,.key(3)) == .tools && wheel([],.key(3)) == nil,"Key shortcut needs its exact modifiers")
        try await startRecording();await key(.flagsChanged,59,.control);await key(.flagsChanged,58,[.control,.option]);await key(.flagsChanged,58,.control)
        try check(saved == "option+key3","A modifier chord waits for its release")
        await key(.flagsChanged,59)
        try await check(saved(is:"control+option"),"Records a modifier chord on release")
        try await startRecording();await key(.flagsChanged,56,.shift);await key(.flagsChanged,56)
        try check(saved == "control+option","Refuses another wheel's shortcut")
        try await startRecording();await key(.keyDown,53);await key(.keyDown,3)
        try check(saved == "control+option","Escape cancels recording")
        try await startRecording();await click(NSPoint(x:4,y:4));await key(.keyDown,3)
        try check(saved == "control+option","Clicking elsewhere cancels recording")
        try check(defaults.object(forKey:WheelShortcut.tools.storageKey) as? UInt == ModifierChord([.control,.option]).rawValue,"Modifier mask stays readable for older builds")
    }
}
