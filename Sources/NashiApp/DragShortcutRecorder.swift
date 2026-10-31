import AppKit
import SwiftUI

/// Captures a drag shortcut from the Settings window; it never listens outside Nashi.
struct DragShortcutRecorder: View {
    let wheel:WheelShortcut
    var defaults:UserDefaults = .standard
    let onChange:()->Void
    @State private var monitor:Any?
    @State private var held=NSEvent.ModifierFlags()
    @State private var conflict:WheelShortcut?
    var body: some View {
        VStack(alignment:.trailing,spacing:4) {
            if monitor == nil {
                Button(action:start) { Text(verbatim:ModifierPreferences.shortcut(for:wheel,defaults:defaults).label) }
                    .buttonStyle(NashiButtonStyle(.secondary,compact:true)).accessibilityLabel(L10n.text(wheel.title))
            } else {
                // Not a button: the click that ends recording must not start it again.
                LText("Press keys or a mouse button…").font(.system(size:12,weight:.semibold)).foregroundStyle(.secondary).frame(height:28)
            }
            if let conflict { LText("Already assigned to \(L10n.text(conflict.title)).").font(.caption).foregroundStyle(.red) }
        }
        .onDisappear(perform:stop)
        .onReceive(NotificationCenter.default.publisher(for:NSWindow.didResignKeyNotification)) { _ in stop() }
    }
    private func start() {
        conflict=nil;held=[]
        monitor=NSEvent.addLocalMonitorForEvents(matching:[.keyDown,.flagsChanged,.leftMouseDown,.rightMouseDown,.otherMouseDown]) { event in
            switch event.type {
            case .leftMouseDown:stop();return event
            case .keyDown where event.keyCode == 53:stop()
            case .keyDown:if !event.isARepeat { save(DragShortcut(chord:ModifierChord(event.modifierFlags),trigger:.key(event.keyCode))) }
            case .rightMouseDown,.otherMouseDown:save(DragShortcut(chord:ModifierChord(event.modifierFlags),trigger:.mouse(event.buttonNumber)))
            default:
                // A modifier-only shortcut is complete once every key of the chord has been released.
                let current=ModifierChord(event.modifierFlags).flags
                held.formUnion(current)
                if current.isEmpty,!held.isEmpty { save(DragShortcut(chord:ModifierChord(held))) }
            }
            return nil
        }
    }
    private func stop() { if let monitor { NSEvent.removeMonitor(monitor);self.monitor=nil } }
    private func save(_ shortcut:DragShortcut) {
        stop();conflict=ModifierPreferences.conflict(shortcut,for:wheel,defaults:defaults)
        if ModifierPreferences.set(shortcut,for:wheel,defaults:defaults) { onChange() }
    }
}
