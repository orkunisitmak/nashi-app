import Foundation
#if canImport(CoreGraphics)
import CoreGraphics
#endif

/// Coordinates belong to the usable screen frame, excluding the menu bar and Dock/taskbar.
public enum WindowPlacement: String, CaseIterable, Sendable, Codable {
    case leftThird, middleThird, rightThird, leftHalf, rightHalf, topHalf, bottomHalf, leftTwoThirds, rightTwoThirds, maximize

    public var title: String {
        switch self {
        case .leftThird: return "Left third"
        case .middleThird: return "Middle third"
        case .rightThird: return "Right third"
        case .leftHalf: return "Left half"
        case .rightHalf: return "Right half"
        case .topHalf: return "Top half"
        case .bottomHalf: return "Bottom half"
        case .leftTwoThirds: return "Left two-thirds"
        case .rightTwoThirds: return "Right two-thirds"
        case .maximize: return "Maximize"
        }
    }

    public var fraction: String {
        switch self {
        case .leftThird, .middleThird, .rightThird: return "1/3"
        case .leftHalf, .rightHalf, .topHalf, .bottomHalf: return "1/2"
        case .leftTwoThirds, .rightTwoThirds: return "2/3"
        case .maximize: return "100%"
        }
    }

    public var horizontalRange: ClosedRange<Double> {
        switch self {
        case .leftThird: return 0...(1.0/3)
        case .middleThird: return (1.0/3)...(2.0/3)
        case .rightThird: return (2.0/3)...1
        case .leftHalf: return 0...0.5
        case .rightHalf: return 0.5...1
        case .leftTwoThirds: return 0...(2.0/3)
        case .rightTwoThirds: return (1.0/3)...1
        case .topHalf, .bottomHalf, .maximize: return 0...1
        }
    }

    /// Measured from the top of the work area.
    public var verticalRange: ClosedRange<Double> {
        switch self {
        case .topHalf: return 0...0.5
        case .bottomHalf: return 0.5...1
        default: return 0...1
        }
    }

    public func frame(in workArea: CGRect, scale: CGFloat = 1, minimumSize: CGSize = .zero) -> CGRect {
        guard workArea.width > 0, workArea.height > 0, workArea.width.isFinite, workArea.height.isFinite,
              workArea.origin.x.isFinite, workArea.origin.y.isFinite else { return .zero }
        // Fill the exact usable frame without rounding a fractional display edge.
        if self == .maximize { return workArea }
        let pixels = scale.isFinite && scale > 0 ? scale : 1
        let (x, width) = Self.span(origin: workArea.minX, length: workArea.width, range: horizontalRange,
                                   pixels: pixels, minimum: minimumSize.width)
        // Cocoa's y axis grows upward, so the top of the work area is maxY.
        let (top, height) = Self.span(origin: 0, length: workArea.height, range: verticalRange,
                                      pixels: pixels, minimum: minimumSize.height)
        return CGRect(x: x, y: workArea.maxY-top-height, width: width, height: height)
    }

    /// A window that cannot be resized keeps its size and sits at this layout's anchor.
    public func frame(in workArea: CGRect, scale: CGFloat = 1, fixedSize: CGSize) -> CGRect {
        let slot = frame(in: workArea, scale: scale)
        guard slot != .zero else { return .zero }
        let width = min(max(0, fixedSize.width), workArea.width), height = min(max(0, fixedSize.height), workArea.height)
        let x = horizontalRange.lowerBound == 0 ? slot.minX : horizontalRange.upperBound == 1 ? slot.maxX-width : slot.midX-width/2
        let y = verticalRange.upperBound == 1 && verticalRange.lowerBound > 0 ? slot.minY : slot.maxY-height
        return CGRect(x: min(max(x, workArea.minX), workArea.maxX-width),
                      y: min(max(y, workArea.minY), workArea.maxY-height), width: width, height: height)
    }

    /// The same layout against the opposite edge, for layouts that hug the left or right edge.
    public var mirrored: WindowPlacement? {
        switch self {
        case .leftThird: return .rightThird
        case .rightThird: return .leftThird
        case .leftHalf: return .rightHalf
        case .rightHalf: return .leftHalf
        case .leftTwoThirds: return .rightTwoThirds
        case .rightTwoThirds: return .leftTwoThirds
        case .middleThird, .topHalf, .bottomHalf, .maximize: return nil
        }
    }

    /// Repeating a left/right layout on a window that already fills it continues on the
    /// neighbouring display, against its facing edge: right half → next display's left half,
    /// and once more → that display's right half. Frames share one coordinate space.
    public static func destination(for placement: WindowPlacement, window: CGRect, displays: [CGRect],
                                   current: Int, scale: CGFloat = 1, resizable: Bool = true) -> (placement: WindowPlacement, display: Int) {
        guard displays.indices.contains(current), let mirrored = placement.mirrored else { return (placement, current) }
        let area = displays[current]
        // Minimum or fixed sizes keep the anchor, so compare against the frame the app allowed.
        let matches = { (expected: CGRect) in
            abs(window.minX-expected.minX) < 4 && abs(window.maxX-expected.maxX) < 4 &&
            abs(window.minY-expected.minY) < 4 && abs(window.maxY-expected.maxY) < 4
        }
        guard resizable ? matches(placement.frame(in: area, scale: scale, minimumSize: window.size))
                        : matches(placement.frame(in: area, scale: scale, fixedSize: window.size)) else { return (placement, current) }
        let rightward = placement.horizontalRange.upperBound == 1
        let ahead = displays.indices.filter { $0 != current && (rightward ? displays[$0].midX > area.midX : displays[$0].midX < area.midX) }
        // Prefer displays beside this one over ones that are only diagonally further along.
        let beside = ahead.filter { displays[$0].minY < area.maxY && displays[$0].maxY > area.minY }
        guard let next = (beside.isEmpty ? ahead : beside).min(by: { abs(displays[$0].midX-area.midX) < abs(displays[$1].midX-area.midX) })
        else { return (placement, current) }
        return (mirrored, next)
    }

    /// Adjacent regions share the same rounded boundary even at odd display sizes. A larger
    /// minimum keeps the leading/centered/trailing anchor and never exceeds the work area.
    private static func span(origin: CGFloat, length: CGFloat, range: ClosedRange<Double>,
                             pixels: CGFloat, minimum: CGFloat) -> (CGFloat, CGFloat) {
        if range == 0...1 { return (origin, length) }
        let start = (length * range.lowerBound * pixels).rounded()/pixels
        let end = (length * range.upperBound * pixels).rounded()/pixels
        let requested = end-start
        let floor = minimum.isFinite ? max(0, minimum) : 0
        if floor <= requested { return (origin+start, requested) }
        let size = min(length, floor)
        if range.lowerBound == 0 { return (origin, size) }
        if range.upperBound == 1 { return (origin+length-size, size) }
        return (origin+length/2-size/2, size)
    }
}

/// A mouse drag must move/resize the captured window; pointer travel alone is a file/text drag.
public struct WindowDragIntent: Sendable {
    public private(set) var cancelled = false
    public init() {}
    public mutating func cancel() { cancelled = true }
    public mutating func accepts(pointerDistance: Double, frameChanged: Bool,
                                 freshFileDrag: Bool, requested: Bool) -> Bool {
        if freshFileDrag { cancelled = true }
        return !cancelled && frameChanged && pointerDistance > 4 && requested
    }
}

/// A recorded global key combination. Key codes are macOS virtual key codes.
public struct HotKeyCombo: Hashable, Codable, Sendable {
    public struct Modifiers: OptionSet, Hashable, Codable, Sendable {
        public let rawValue: UInt8
        public init(rawValue: UInt8) { self.rawValue = rawValue }
        public static let control = Modifiers(rawValue: 1)
        public static let option = Modifiers(rawValue: 2)
        public static let shift = Modifiers(rawValue: 4)
        public static let command = Modifiers(rawValue: 8)
    }
    public var keyCode: UInt16
    public var modifiers: Modifiers
    /// Display text for the key itself; it does not take part in equality.
    public var key: String

    public init(keyCode: UInt16, modifiers: Modifiers, key: String) {
        self.keyCode = keyCode; self.modifiers = modifiers
        self.key = Self.specialKeys[keyCode] ?? key.uppercased()
    }
    public static func == (lhs: HotKeyCombo, rhs: HotKeyCombo) -> Bool { lhs.keyCode == rhs.keyCode && lhs.modifiers == rhs.modifiers }
    public func hash(into hasher: inout Hasher) { hasher.combine(keyCode); hasher.combine(modifiers.rawValue) }

    public var symbols: String {
        [(Modifiers.control,"⌃"),(.option,"⌥"),(.shift,"⇧"),(.command,"⌘")].filter { modifiers.contains($0.0) }.map(\.1).joined() + key
    }
    /// Shift alone would swallow ordinary typing in every other app.
    public var isValid: Bool { !modifiers.isDisjoint(with: [.control, .option, .command]) && !key.isEmpty }

    public static let specialKeys: [UInt16: String] = [
        123:"←", 124:"→", 125:"↓", 126:"↑", 36:"↩", 48:"⇥", 49:"Space", 51:"⌫", 117:"⌦", 115:"↖", 119:"↘", 116:"⇞", 121:"⇟",
        122:"F1", 120:"F2", 99:"F3", 118:"F4", 96:"F5", 97:"F6", 98:"F7", 100:"F8", 101:"F9", 109:"F10", 103:"F11", 111:"F12",
    ]
    /// ⌃⇧D downloads a copied link and cannot be reused.
    public static let downloadLink = HotKeyCombo(keyCode: 2, modifiers: [.control, .shift], key: "D")
}

extension WindowPlacement {
    /// Carbon hot key identifier; 1 belongs to the download-link shortcut.
    public var hotKeyID: UInt32 { UInt32(Self.allCases.firstIndex(of: self)!) + 10 }
    public init?(hotKeyID: UInt32) {
        guard hotKeyID >= 10, Int(hotKeyID-10) < Self.allCases.count else { return nil }
        self = Self.allCases[Int(hotKeyID-10)]
    }
    public var defaultShortcut: HotKeyCombo {
        let base: HotKeyCombo.Modifiers = [.option, .command]
        switch self {
        case .leftHalf: return HotKeyCombo(keyCode: 123, modifiers: base, key: "")
        case .rightHalf: return HotKeyCombo(keyCode: 124, modifiers: base, key: "")
        case .maximize: return HotKeyCombo(keyCode: 3, modifiers: base, key: "F")
        case .topHalf: return HotKeyCombo(keyCode: 126, modifiers: base, key: "")
        case .bottomHalf: return HotKeyCombo(keyCode: 125, modifiers: base, key: "")
        case .middleThird: return HotKeyCombo(keyCode: 125, modifiers: base.union(.control), key: "")
        case .leftThird: return HotKeyCombo(keyCode: 123, modifiers: base.union(.control), key: "")
        case .rightThird: return HotKeyCombo(keyCode: 124, modifiers: base.union(.control), key: "")
        case .leftTwoThirds: return HotKeyCombo(keyCode: 123, modifiers: base.union(.shift), key: "")
        case .rightTwoThirds: return HotKeyCombo(keyCode: 124, modifiers: base.union(.shift), key: "")
        }
    }
    var shortcutStorageKey: String { "windowShortcut.\(rawValue)" }
}

/// Every layout has its own global shortcut. An empty stored value means the user cleared it.
public enum WindowShortcutPreferences {
    public enum Result: Equatable, Sendable { case saved, invalid, duplicate(String) }

    public static func shortcut(for placement: WindowPlacement, defaults: UserDefaults = .standard) -> HotKeyCombo? {
        guard let data = defaults.data(forKey: placement.shortcutStorageKey) else { return placement.defaultShortcut }
        guard !data.isEmpty, let stored = try? JSONDecoder().decode(HotKeyCombo.self, from: data), stored.isValid else { return nil }
        return stored
    }
    /// Passing nil clears the shortcut for that layout.
    @discardableResult
    public static func set(_ combo: HotKeyCombo?, for placement: WindowPlacement, defaults: UserDefaults = .standard) -> Result {
        guard let combo else { defaults.set(Data(), forKey: placement.shortcutStorageKey); return .saved }
        guard combo.isValid else { return .invalid }
        if combo == HotKeyCombo.downloadLink { return .duplicate("Download Link…") }
        if let other = WindowPlacement.allCases.first(where: { $0 != placement && shortcut(for: $0, defaults: defaults) == combo }) {
            return .duplicate(other.title)
        }
        guard let data = try? JSONEncoder().encode(combo) else { return .invalid }
        defaults.set(data, forKey: placement.shortcutStorageKey)
        return .saved
    }
    public static func reset(defaults: UserDefaults = .standard) {
        for placement in WindowPlacement.allCases { defaults.removeObject(forKey: placement.shortcutStorageKey) }
    }
}
