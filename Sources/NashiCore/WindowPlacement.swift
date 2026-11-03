import Foundation
#if canImport(CoreGraphics)
import CoreGraphics
#endif

/// Coordinates belong to the usable screen frame, excluding the menu bar and Dock/taskbar.
public enum WindowPlacement: String, CaseIterable, Sendable, Codable {
    case leftThird, middleThird, rightThird, leftHalf, rightHalf, leftTwoThirds, rightTwoThirds, maximize

    public var title: String {
        switch self {
        case .leftThird: return "Left third"
        case .middleThird: return "Middle third"
        case .rightThird: return "Right third"
        case .leftHalf: return "Left half"
        case .rightHalf: return "Right half"
        case .leftTwoThirds: return "Left two-thirds"
        case .rightTwoThirds: return "Right two-thirds"
        case .maximize: return "Maximize"
        }
    }

    public var fraction: String {
        switch self {
        case .leftThird, .middleThird, .rightThird: return "1/3"
        case .leftHalf, .rightHalf: return "1/2"
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
        case .maximize: return 0...1
        }
    }

    public func frame(in workArea: CGRect, scale: CGFloat = 1, minimumSize: CGSize = .zero) -> CGRect {
        guard workArea.width > 0, workArea.height > 0, workArea.width.isFinite, workArea.height.isFinite,
              workArea.origin.x.isFinite, workArea.origin.y.isFinite else { return .zero }
        // Fill the exact usable frame without rounding a fractional display edge.
        if self == .maximize { return workArea }
        let pixels = scale.isFinite && scale > 0 ? scale : 1
        let range = horizontalRange
        // Adjacent regions share the same rounded boundary even at odd display widths.
        let start = (workArea.width * range.lowerBound * pixels).rounded()/pixels
        // The right edge has no neighbour; keep it on the exact usable edge like maximize.
        let end = range.upperBound == 1 ? workArea.width : (workArea.width * range.upperBound * pixels).rounded()/pixels
        let requested=end-start
        let minimum=minimumSize.width.isFinite ? max(0,minimumSize.width):0
        if minimum<=requested {return CGRect(x:workArea.minX+start,y:workArea.minY,width:requested,height:workArea.height)}
        let width=min(workArea.width,max(requested,minimum))
        let x:CGFloat
        if range.lowerBound==0 {x=workArea.minX}
        else if range.upperBound==1 {x=workArea.maxX-width}
        else {x=workArea.midX-width/2}
        return CGRect(x:x,y:workArea.minY,width:width,height:workArea.height)
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
