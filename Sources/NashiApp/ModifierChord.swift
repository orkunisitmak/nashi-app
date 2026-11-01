import AppKit

struct ModifierChord: RawRepresentable, Hashable, Identifiable, Sendable {
    let rawValue: UInt
    var id: UInt { rawValue }
    static let supported: NSEvent.ModifierFlags = [.control,.option,.shift,.command]
    private static let components: [(NSEvent.ModifierFlags,String,String)] = [(.control,"Control","⌃"),(.option,"Option","⌥"),(.shift,"Shift","⇧"),(.command,"Command","⌘")]
    init(rawValue: UInt) { self.rawValue = rawValue & Self.supported.rawValue }
    init(_ flags: NSEvent.ModifierFlags) { self.init(rawValue:flags.rawValue) }
    var flags: NSEvent.ModifierFlags { .init(rawValue:rawValue) }
    var symbols: String { Self.components.filter{flags.contains($0.0)}.map(\.2).joined() }
    var title: String { Self.components.filter{flags.contains($0.0)}.map(\.1).joined(separator:" + ") }
    var isEmpty: Bool { rawValue == 0 }
    static func legacy(_ name:String)->ModifierChord? {
        let values:[String:NSEvent.ModifierFlags] = ["control":.control,"option":.option,"shift":.shift,"command":.command]
        var flags:NSEvent.ModifierFlags=[]
        for part in name.lowercased().split(separator:"+") { guard let value=values[String(part)] else { return nil };flags.insert(value) }
        return flags.isEmpty ? nil:ModifierChord(flags)
    }
}

/// The non-modifier part of a drag shortcut. Mouse button 0 is the drag itself and is never a trigger.
enum DragTrigger: Hashable, Sendable {
    case mouse(Int), key(UInt16)
    private static let keyNames: [UInt16:String] = [36:"Return",48:"Tab",49:"Space",51:"Delete",96:"F5",97:"F6",98:"F7",99:"F3",100:"F8",101:"F9",103:"F11",109:"F10",111:"F12",115:"Home",116:"Page Up",117:"Forward Delete",118:"F4",119:"End",120:"F2",121:"Page Down",122:"F1",123:"←",124:"→",125:"↓",126:"↑"]
    init?(name:String) {
        if name.hasPrefix("mouse"),let button=Int(name.dropFirst(5)),(1...31).contains(button) { self = .mouse(button) }
        else if name.hasPrefix("key"),let code=UInt16(name.dropFirst(3)) { self = .key(code) }
        else { return nil }
    }
    var name: String { switch self { case .mouse(let button):return "mouse\(button)";case .key(let code):return "key\(code)" } }
    var title: String {
        switch self {
        case .mouse(1):return L10n.text("Right Click")
        case .mouse(2):return L10n.text("Middle Click")
        case .mouse(let button):return L10n.text("Mouse \(button+1)")
        case .key(let code):
            if let name=Self.keyNames[code] { return name }
            let event=CGEvent(keyboardEventSource:nil,virtualKey:code,keyDown:true).flatMap{NSEvent(cgEvent:$0)}
            return event?.charactersIgnoringModifiers.flatMap{$0.isEmpty ? nil:$0.localizedUppercase} ?? "Key \(code)"
        }
    }
    /// Polled like the modifier flags, so neither needs an event tap or input-monitoring permission.
    var isPressed: Bool {
        switch self {
        case .mouse(let button):return NSEvent.pressedMouseButtons & (1<<button) != 0
        case .key(let code):return CGEventSource.keyState(.combinedSessionState,key:code)
        }
    }
}

struct DragShortcut: Hashable, Sendable {
    let chord: ModifierChord
    let trigger: DragTrigger?
    init(chord:ModifierChord,trigger:DragTrigger?=nil) { self.chord=chord;self.trigger=trigger }
    /// Reads the `convertKey`-style name, which older builds treat as unknown and replace with their default.
    init?(name:String) {
        var flags=NSEvent.ModifierFlags(),trigger:DragTrigger?
        for part in name.lowercased().split(separator:"+").map(String.init) {
            if let chord=ModifierChord.legacy(part) { flags.formUnion(chord.flags) }
            else if trigger == nil,let parsed=DragTrigger(name:part) { trigger=parsed }
            else { return nil }
        }
        self.init(chord:ModifierChord(flags),trigger:trigger)
        if isEmpty { return nil }
    }
    var isEmpty: Bool { chord.isEmpty && trigger == nil }
    var name: String { ([chord.title.lowercased().replacingOccurrences(of:" + ",with:"+")]+[trigger?.name].compactMap{$0}).filter{!$0.isEmpty}.joined(separator:"+") }
    var symbols: String { ([chord.symbols]+[trigger?.title].compactMap{$0}).filter{!$0.isEmpty}.joined(separator:" ") }
    var title: String { ([chord.title]+[trigger?.title].compactMap{$0}).filter{!$0.isEmpty}.joined(separator:" + ") }
    var label: String { [chord.symbols,title].filter{!$0.isEmpty}.joined(separator:"  ") }
}

enum WheelShortcut: String, CaseIterable, Identifiable {
    case conversions, tools, actions
    var id: String { rawValue }
    var title: String { switch self { case .conversions:return "Conversion wheel";case .tools:return "File tools wheel";case .actions:return "File actions wheel" } }
    var storageKey: String { switch self { case .conversions:return "conversionWheelDragModifierMask";case .tools:return "toolsWheelDragModifierMask";case .actions:return "actionsWheelDragModifierMask" } }
    var legacyKey: String { switch self { case .conversions:return "convertKey";case .tools:return "toolsKey";case .actions:return "actionsKey" } }
    var defaultChord: ModifierChord { switch self { case .conversions:return .init(.shift);case .tools:return .init(.command);case .actions:return .init(.control) } }
}

enum ModifierPreferences {
    static func chord(for wheel:WheelShortcut,defaults:UserDefaults = .standard)->ModifierChord {
        if let stored=defaults.object(forKey:wheel.storageKey) as? NSNumber {
            let chord=ModifierChord(rawValue:stored.uintValue)
            if !chord.isEmpty { return chord }
        }
        if let name=defaults.string(forKey:wheel.legacyKey),let chord=ModifierChord.legacy(name) { return chord }
        return wheel.defaultChord
    }
    static func shortcut(for wheel:WheelShortcut,defaults:UserDefaults = .standard)->DragShortcut {
        if let name=defaults.string(forKey:wheel.legacyKey),let shortcut=DragShortcut(name:name),shortcut.trigger != nil { return shortcut }
        return DragShortcut(chord:chord(for:wheel,defaults:defaults))
    }
    static func conflict(_ shortcut:DragShortcut,for wheel:WheelShortcut,defaults:UserDefaults = .standard)->WheelShortcut? {
        WheelShortcut.allCases.first{$0 != wheel && self.shortcut(for:$0,defaults:defaults)==shortcut}
    }
    @discardableResult static func set(_ shortcut:DragShortcut,for wheel:WheelShortcut,defaults:UserDefaults = .standard)->Bool {
        guard !shortcut.isEmpty,conflict(shortcut,for:wheel,defaults:defaults) == nil else { return false }
        // A zero mask sends older builds to their default instead of a chord two triggers may share.
        defaults.set(shortcut.trigger == nil ? shortcut.chord.rawValue:0,forKey:wheel.storageKey)
        defaults.set(shortcut.name,forKey:wheel.legacyKey)
        return true
    }
    /// Without `pressed` only the modifiers are known, so a plain shortcut wins over one that also needs a trigger.
    static func wheel(for flags:NSEvent.ModifierFlags,pressed:((DragTrigger)->Bool)?=nil,defaults:UserDefaults = .standard)->WheelShortcut? {
        let current=ModifierChord(flags)
        let shortcuts=WheelShortcut.allCases.map{(wheel:$0,shortcut:shortcut(for:$0,defaults:defaults))}
        let plain=shortcuts.first{!current.isEmpty && $0.shortcut.chord==current && $0.shortcut.trigger==nil}?.wheel
        guard let pressed else { return plain ?? shortcuts.first{!current.isEmpty && $0.shortcut.chord==current}?.wheel }
        // A held trigger is the more specific match when its modifiers also form a plain shortcut.
        return shortcuts.first{$0.shortcut.chord==current && $0.shortcut.trigger.map(pressed)==true}?.wheel ?? plain
    }
}
