import AppKit
import Carbon
import SwiftUI
import NashiCore

// Carbon also declares a WindowPlacement type.
private typealias WindowPlacement=NashiCore.WindowPlacement

extension Notification.Name {
    /// Posted with a Bool object; global window shortcuts pause while a new one is typed.
    static let windowShortcutRecording=Notification.Name("NashiWindowShortcutRecording")
}

extension HotKeyCombo {
    init(_ event:NSEvent) {
        let flags=event.modifierFlags
        var modifiers:Modifiers=[]
        if flags.contains(.control) { modifiers.insert(.control) }
        if flags.contains(.option) { modifiers.insert(.option) }
        if flags.contains(.shift) { modifiers.insert(.shift) }
        if flags.contains(.command) { modifiers.insert(.command) }
        self.init(keyCode:event.keyCode,modifiers:modifiers,key:event.charactersIgnoringModifiers ?? "")
    }
    var carbonModifiers:UInt32 {
        var mask=0
        if modifiers.contains(.control) { mask|=controlKey }
        if modifiers.contains(.option) { mask|=optionKey }
        if modifiers.contains(.shift) { mask|=shiftKey }
        if modifiers.contains(.command) { mask|=cmdKey }
        return UInt32(mask)
    }
}

/// Lists every window layout with a Record button that captures the next key combination.
struct WindowShortcutsEditor:View {
    @State private var recording:WindowPlacement?
    @State private var monitor:Any?
    @State private var message:String?
    @State private var revision=0

    var body:some View {
        VStack(alignment:.leading,spacing:8) {
            ForEach(WindowPlacement.allCases,id:\.self) { placement in
                HStack {
                    WindowPlacementIcon(placement:placement).frame(width:30,height:20)
                    LText(placement.title);Text(placement.fraction).font(.caption).foregroundStyle(.secondary);Spacer()
                    Text(label(for:placement))
                        .font(.system(.body,design:.rounded).weight(.semibold)).monospacedDigit()
                        .frame(minWidth:110).padding(.vertical,4).padding(.horizontal,8)
                        .background(RoundedRectangle(cornerRadius:7).fill(recording==placement ? Color(nsColor:NashiPalette.gold).opacity(0.2):Color.secondary.opacity(0.12)))
                    Button(recording==placement ? "Cancel":"Record") { recording==placement ? stop():start(placement) }
                        .frame(minWidth:80)
                        .accessibilityLabel(L10n.text(recording==placement ? "Cancel":"Record")+" "+L10n.text(placement.title))
                }
            }
            HStack(alignment:.top) {
                // Wrap beside the button instead of truncating the longer Turkish/French copy.
                Group {
                    if let message { Text(message) }
                    else { Text("Press Record, then type a combination with ⌘, ⌥ or ⌃. Delete clears it; Escape cancels.") }
                }
                .font(.caption).foregroundStyle(.secondary)
                .fixedSize(horizontal:false,vertical:true).frame(maxWidth:.infinity,alignment:.leading)
                Button("Restore defaults") { stop();WindowShortcutPreferences.reset();message=nil;revision+=1 }
            }
        }
        .id(revision)
        .onDisappear(perform:stop)
        .onReceive(NotificationCenter.default.publisher(for:NSWindow.willCloseNotification)) { _ in stop() }
        .onReceive(NotificationCenter.default.publisher(for:NSApplication.didResignActiveNotification)) { _ in stop() }
    }

    private func label(for placement:WindowPlacement)->String {
        if recording==placement { return L10n.text("Type a shortcut…") }
        return WindowShortcutPreferences.shortcut(for:placement)?.symbols ?? L10n.text("None")
    }

    private func start(_ placement:WindowPlacement) {
        stop()
        recording=placement;message=nil
        NotificationCenter.default.post(name:.windowShortcutRecording,object:true)
        monitor=NSEvent.addLocalMonitorForEvents(matching:.keyDown) { event in
            guard recording != nil else { return event }
            let bare=event.modifierFlags.intersection([.command,.control,.option,.shift]).isEmpty
            if event.keyCode==53,bare { stop();return nil }
            if [51,117].contains(event.keyCode),bare { WindowShortcutPreferences.set(nil,for:placement);stop();revision+=1;return nil }
            let combo=HotKeyCombo(event)
            switch WindowShortcutPreferences.set(combo,for:placement) {
            case .saved: stop();revision+=1
            case .invalid: message=L10n.text("Include ⌘, ⌥ or ⌃ in the shortcut.");NSSound.beep()
            case .duplicate(let owner): message=L10n.text("\(combo.symbols) is already used by \(L10n.text(owner)).");NSSound.beep()
            }
            return nil
        }
    }

    private func stop() {
        if let monitor { NSEvent.removeMonitor(monitor) }
        monitor=nil
        if recording != nil { recording=nil;NotificationCenter.default.post(name:.windowShortcutRecording,object:false) }
    }
}

/// The same miniature screen the window wheel draws for each layout.
private struct WindowPlacementIcon:NSViewRepresentable {
    let placement:WindowPlacement
    func makeNSView(context:Context)->GlyphView { GlyphView(placement:placement) }
    func updateNSView(_ view:GlyphView,context:Context) { view.placement=placement;view.needsDisplay=true }
    final class GlyphView:NSView {
        var placement:WindowPlacement
        init(placement:WindowPlacement) { self.placement=placement;super.init(frame:.zero) }
        required init?(coder:NSCoder) { nil }
        override func draw(_ dirtyRect:NSRect) { WindowPlacementGlyph.draw(placement,in:bounds,color:.secondaryLabelColor) }
    }
}
