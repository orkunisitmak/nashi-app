import AppKit

struct ModifierChord: RawRepresentable, Hashable, Identifiable, Sendable {
    let rawValue: UInt
    var id: UInt { rawValue }
    static let supported: NSEvent.ModifierFlags = [.control,.option,.shift,.command,.function]
    private static let components: [(NSEvent.ModifierFlags,String,String)] = [(.control,"Control","⌃"),(.option,"Option","⌥"),(.shift,"Shift","⇧"),(.command,"Command","⌘"),(.function,"Fn","fn")]
    init(rawValue: UInt) { self.rawValue = rawValue & Self.supported.rawValue }
    init(_ flags: NSEvent.ModifierFlags) { self.init(rawValue:flags.rawValue) }
    var flags: NSEvent.ModifierFlags { .init(rawValue:rawValue) }
    var symbols: String { Self.components.filter{flags.contains($0.0)}.map(\.2).joined() }
    var title: String { Self.components.filter{flags.contains($0.0)}.map(\.1).joined(separator:" + ") }
    var isEmpty: Bool { rawValue == 0 }
    static let all: [ModifierChord] = (1..<(1<<components.count)).map { bits in
        var flags: NSEvent.ModifierFlags = []
        for (index,component) in components.enumerated() where bits & (1<<index) != 0 { flags.insert(component.0) }
        return ModifierChord(flags)
    }.sorted { lhs,rhs in
        let lc=lhs.rawValue.nonzeroBitCount,rc=rhs.rawValue.nonzeroBitCount
        return lc == rc ? lhs.rawValue < rhs.rawValue : lc < rc
    }
    static func legacy(_ name:String)->ModifierChord? {
        let values:[String:NSEvent.ModifierFlags] = ["control":.control,"option":.option,"shift":.shift,"command":.command,"fn":.function]
        var flags:NSEvent.ModifierFlags=[]
        for part in name.lowercased().split(separator:"+") { guard let value=values[String(part)] else { return nil };flags.insert(value) }
        return flags.isEmpty ? nil:ModifierChord(flags)
    }
}

enum WheelShortcut: String, CaseIterable, Identifiable {
    case conversions, tools, actions, projects
    var id: String { rawValue }
    var title: String { switch self { case .conversions:return "Conversion wheel";case .tools:return "File tools wheel";case .actions:return "File actions wheel";case .projects:return "Projects wheel" } }
    var storageKey: String { switch self { case .conversions:return "conversionWheelDragModifierMask";case .tools:return "toolsWheelDragModifierMask";case .actions:return "actionsWheelDragModifierMask";case .projects:return "projectsWheelDragModifierMask" } }
    var legacyKey: String { switch self { case .conversions:return "convertKey";case .tools:return "toolsKey";case .actions:return "actionsKey";case .projects:return "projectsKey" } }
    var defaultChord: ModifierChord { switch self { case .conversions:return .init(.shift);case .tools:return .init(.command);case .actions:return .init(.control);case .projects:return .init(.function) } }
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
    @discardableResult static func set(_ chord:ModifierChord,for wheel:WheelShortcut,defaults:UserDefaults = .standard)->Bool {
        guard !chord.isEmpty,!WheelShortcut.allCases.contains(where:{$0 != wheel && self.chord(for:$0,defaults:defaults)==chord}) else { return false }
        defaults.set(chord.rawValue,forKey:wheel.storageKey)
        defaults.set(chord.title.lowercased().replacingOccurrences(of:" + ",with:"+"),forKey:wheel.legacyKey)
        return true
    }
    static func available(for wheel:WheelShortcut,defaults:UserDefaults = .standard)->[ModifierChord] {
        let reserved=Set(WheelShortcut.allCases.filter{$0 != wheel}.map{chord(for:$0,defaults:defaults)})
        return ModifierChord.all.filter{!reserved.contains($0)}
    }
    static func wheel(for flags:NSEvent.ModifierFlags,defaults:UserDefaults = .standard)->WheelShortcut? {
        let current=ModifierChord(flags)
        guard !current.isEmpty else { return nil }
        return WheelShortcut.allCases.first{chord(for:$0,defaults:defaults)==current}
    }
}
