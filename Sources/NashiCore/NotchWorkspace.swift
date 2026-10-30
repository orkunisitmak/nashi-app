import Foundation

/// Pure screen metrics let the notch panel be placed consistently and tested without AppKit.
public struct NotchScreenMetrics {
    public let frame: CGRect
    public let visibleFrame: CGRect
    public let safeAreaTop: CGFloat
    public let auxiliaryTopLeftArea: CGRect?
    public let auxiliaryTopRightArea: CGRect?

    public init(frame: CGRect, visibleFrame: CGRect, safeAreaTop: CGFloat,
                auxiliaryTopLeftArea: CGRect?, auxiliaryTopRightArea: CGRect?) {
        self.frame=frame
        self.visibleFrame=visibleFrame
        self.safeAreaTop=safeAreaTop
        self.auxiliaryTopLeftArea=auxiliaryTopLeftArea
        self.auxiliaryTopRightArea=auxiliaryTopRightArea
    }
}

public struct NotchPanelPlacement: Equatable {
    public let frame: CGRect
    public let isNotched: Bool
    public let notchWidth: CGFloat
}

public enum NotchPanelGeometry {
    /// A notched Mac anchors the panel to the bottom of its camera housing. Other displays
    /// use the visible-frame top, which sits just below the menu bar.
    public static func placement(on screen: NotchScreenMetrics, size: CGSize,
                                 fallbackTopInset: CGFloat = 6) -> NotchPanelPlacement {
        let left=screen.auxiliaryTopLeftArea
        let right=screen.auxiliaryTopRightArea
        let validLeft=left.map(isUsable) ?? false
        let validRight=right.map(isUsable) ?? false
        let gap: CGFloat
        if let left, let right, validLeft, validRight, screen.safeAreaTop > 0, right.minX > left.maxX {
            gap=right.minX-left.maxX
        } else {
            gap=0
        }
        let isNotched=gap > 0
        let width=min(max(1,size.width),max(1,screen.frame.width-16))
        let height=max(1,size.height)
        let top=isNotched ? screen.frame.maxY-screen.safeAreaTop : screen.visibleFrame.maxY-max(0,fallbackTopInset)
        let frame=CGRect(x:screen.frame.midX-width/2,y:top-height,width:width,height:height)
        return NotchPanelPlacement(frame:frame,isNotched:isNotched,notchWidth:gap)
    }

    private static func isUsable(_ rect: CGRect) -> Bool {
        !rect.isNull && !rect.isInfinite && rect.width.isFinite && rect.height.isFinite && rect.width > 0 && rect.height > 0
    }
}

/// State shared by the AppKit panel and pure tests. The caller applies the 250 ms delay;
/// this value decides whether the delayed collapse is still valid when it fires.
public enum NotchInteractionPolicy {
    public static let collapseDelayMilliseconds=250
}

/// A local-only focus countdown used by the optional notch widgets tab.
public struct NotchFocusTimer: Equatable {
    public static let defaultDurationSeconds=25*60
    public static let presetMinutes=[5,15,25,45]
    public private(set) var durationSeconds:Int
    public private(set) var remainingSeconds:Int
    public private(set) var isRunning=false

    public init(durationSeconds:Int=NotchFocusTimer.defaultDurationSeconds,remainingSeconds:Int?=nil) {
        let duration=max(1,durationSeconds)
        self.durationSeconds=duration
        self.remainingSeconds=min(duration,max(0,remainingSeconds ?? duration))
    }

    public mutating func start() {
        if remainingSeconds>0 { isRunning=true }
    }

    public mutating func pause() { isRunning=false }

    public mutating func restart() {
        remainingSeconds=durationSeconds
        isRunning=false
    }

    public mutating func reset(durationSeconds:Int=NotchFocusTimer.defaultDurationSeconds) {
        self.durationSeconds=max(1,durationSeconds)
        remainingSeconds=self.durationSeconds
        isRunning=false
    }

    /// Returns true once when this tick reaches zero.
    @discardableResult public mutating func tick() -> Bool {
        guard isRunning,remainingSeconds>0 else { return false }
        remainingSeconds-=1
        if remainingSeconds==0 { isRunning=false;return true }
        return false
    }
}

/// Bounds a notch preview seek request to a finite media timeline.
public enum NotchMediaControl {
    public static func targetTime(current:Double,offset:Double,duration:Double)->Double? {
        guard current.isFinite,offset.isFinite,duration.isFinite,duration>=0 else { return nil }
        return min(duration,max(0,current+offset))
    }
}

public struct NotchInteractionState: Equatable {
    public private(set) var isExpanded=false
    public private(set) var pointerInside=false
    public private(set) var dragging=false

    public init() {}
    public var mayCollapse: Bool { !pointerInside && !dragging }

    public mutating func expand() { isExpanded=true }

    public mutating func collapse() { isExpanded=false }

    public mutating func pointerEntered() {
        pointerInside=true
        isExpanded=true
    }

    public mutating func pointerExited() { pointerInside=false }

    public mutating func draggingEntered() {
        dragging=true
        isExpanded=true
    }

    public mutating func draggingEnded() { dragging=false }

    public mutating func collapseIfAllowed() -> Bool {
        guard mayCollapse else { return false }
        isExpanded=false
        return true
    }
}

public enum NotchDropShelfTargets {
    /// Keep format chips identical to the existing conversion wheel's compatibility rules.
    public static func formats(for input: URL) -> [String] {
        FeatureCatalog.formats(for:[input])
    }
}
