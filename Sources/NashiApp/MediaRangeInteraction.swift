import AppKit
import SwiftUI
import NashiCore

/// One immutable mouse-down snapshot, measured in the stationary lane. Neither a
/// resized handle nor SwiftUI's next range value can rebase an ongoing drag.
struct MediaRangeDrag {
    enum Target:Equatable { case start(Int),end(Int),move(Int),seek,add }
    enum Change:Equatable { case range(Int,TimeRange),seek(Double),addition(TimeRange) }
    let target:Target
    let origin:Double
    let duration:Double
    let secondsPerPoint:Double
    let initial:TimeRange?

    static func begin(x:Double,duration:Double,width:Double,ranges:[TimeRange],allowsAdding:Bool)->Self? {
        guard duration.isFinite,duration>0,width.isFinite,width>0,x.isFinite else { return nil }
        let secondsPerPoint=duration/width
        var target:Target=allowsAdding ? .add:.seek,initial:TimeRange?
        // Match the painted order when ranges overlap. Handles get a forgiving
        // hit area, and overlapping handles choose the closest edge.
        for (index,range) in ranges.enumerated().reversed() where range.start.isFinite && range.end.isFinite {
            let left=range.start/secondsPerPoint,right=left+max(14,(range.end-range.start)/secondsPerPoint)
            let startCenter=left+6,endCenter=right-6
            let startHit=abs(x-startCenter)<=10,endHit=abs(x-endCenter)<=10
            if startHit || endHit {
                target=startHit && (!endHit || abs(x-startCenter)<=abs(x-endCenter)) ? .start(index):.end(index)
            } else if x>=left && x<=right { target = .move(index) }
            else { continue }
            initial=TimeRange(start:max(0,min(duration,range.start)),end:max(0,min(duration,range.end)))
            break
        }
        return Self(target:target,origin:x,duration:duration,secondsPerPoint:secondsPerPoint,initial:initial)
    }
    func change(at x:Double)->Change {
        let x=x.isFinite ? x:origin
        let delta=(x-origin)*secondsPerPoint,minimum=min(0.02,duration)
        func time(_ point:Double)->Double { max(0,min(duration,point*secondsPerPoint)) }
        switch target {
        case .start(let index):
            let range=initial!
            // An edge already inside the minimum length may stay put, never jump outward.
            let limit=range.start<=range.end ? max(range.end-minimum,range.start):range.end-minimum
            return .range(index,TimeRange(start:max(0,min(limit,range.start+delta)),end:range.end))
        case .end(let index):
            let range=initial!
            let limit=range.start<=range.end ? min(range.start+minimum,range.end):range.start+minimum
            return .range(index,TimeRange(start:range.start,end:min(duration,max(limit,range.end+delta))))
        case .move(let index):
            let range=initial!,length=max(0,range.end-range.start)
            let start=max(0,min(duration-length,range.start+delta))
            return .range(index,TimeRange(start:start,end:start+length))
        case .seek:return .seek(time(x))
        case .add:return .addition(TimeRange(start:min(time(origin),time(x)),end:max(time(origin),time(x))))
        }
    }
}

/// The transparent overlay owns the entire mouse sequence. Visual clips have no
/// competing gestures; AppKit keeps dispatching drags/up even beyond the lane.
struct MediaRangeInteraction: NSViewRepresentable {
    let duration:Double
    let ranges:[TimeRange]
    let label:String
    let onSeek:(Double)->Void
    let onChange:(Int,TimeRange)->Void
    let onPending:(TimeRange?)->Void
    var onAdd:((TimeRange)->Void)?

    func makeNSView(context:Context)->LaneView {
        let view=LaneView();view.setAccessibilityElement(true);view.setAccessibilityRole(.group)
        view.setAccessibilityHelp(L10n.text("Drag either edge to resize the range, or drag its middle to move it."))
        view.setAccessibilityIdentifier("media.range.timeline")
        return view
    }
    func updateNSView(_ view:LaneView,context:Context) {
        let changed=view.ranges != ranges || view.duration != duration
        view.duration=duration;view.ranges=ranges;view.onSeek=onSeek;view.onChange=onChange;view.onPending=onPending;view.onAdd=onAdd
        view.editingEnabled=context.environment.isEnabled
        view.refreshAccessibility()
        view.setAccessibilityLabel("\(label.isEmpty ? "Media range":label) timeline")
        if changed { view.window?.invalidateCursorRects(for:view) }
    }
    static func dismantleNSView(_ view:LaneView,coordinator:()) { view.cancel();view.onSeek=nil;view.onChange=nil;view.onPending=nil;view.onAdd=nil }

    final class LaneView:NSView {
        var duration=0.0
        var ranges:[TimeRange]=[]
        var onSeek:((Double)->Void)?
        var onChange:((Int,TimeRange)->Void)?
        var onPending:((TimeRange?)->Void)?
        var onAdd:((TimeRange)->Void)?
        private var drag:MediaRangeDrag?
        var editingEnabled=true
        private var endpointElements:[RangeEndpointAccessibilityElement]=[]
        private var observedValues:[String:Double]=[:]

        /// Retain semantic children while values change so VoiceOver keeps its focus.
        func refreshAccessibility() {
            let indices=ranges.indices.filter { isEditableRange($0) }
            let keys=indices.flatMap { index in ["media.range.\(index).start","media.range.\(index).end"] }
            if endpointElements.map({$0.identifier}) != keys {
                let previous=Dictionary(uniqueKeysWithValues:endpointElements.map{($0.identifier,$0)})
                endpointElements=indices.flatMap { index in [true,false].map { start in
                    let id="media.range.\(index).\(start ? "start":"end")"
                    return previous[id] ?? RangeEndpointAccessibilityElement(lane:self,index:index,start:start)
                } }
                setAccessibilityChildren(endpointElements)
                NSAccessibility.post(element:self,notification:.layoutChanged)
            }
            for element in endpointElements {
                let value=element.value
                if let previous=observedValues[element.identifier],previous != value {
                    NSAccessibility.post(element:element,notification:.valueChanged)
                }
                observedValues[element.identifier]=value
            }
            observedValues=observedValues.filter{keys.contains($0.key)}
        }
        func isEditableRange(_ index:Int)->Bool {
            guard duration.isFinite,duration>0,ranges.indices.contains(index) else{return false}
            let range=ranges[index]
            return range.start.isFinite && range.end.isFinite && range.start>=0 && range.end<=duration && range.end>range.start
        }
        func canEditRange(_ index:Int)->Bool { editingEnabled && onChange != nil && isEditableRange(index) }
        func endpointBounds(index:Int,start:Bool)->ClosedRange<Double>? {
            guard isEditableRange(index) else{return nil}
            let range=ranges[index],minimum=min(0.02,duration)
            // A range already shorter than the minimum keeps its current edge reachable.
            return start ? 0...max(0,range.end-minimum,range.start):min(duration,range.start+minimum,range.end)...duration
        }
        @discardableResult func setEndpoint(index:Int,start:Bool,value:Double)->Bool {
            guard canEditRange(index),value.isFinite,let limits=endpointBounds(index:index,start:start) else{return false}
            let bounded=max(limits.lowerBound,min(limits.upperBound,value)),old=ranges[index]
            let next=start ? TimeRange(start:bounded,end:old.end):TimeRange(start:old.start,end:bounded)
            guard next != old else{return false}
            ranges[index]=next
            onChange?(index,next)
            refreshAccessibility()
            return true
        }
        func endpointFrame(index:Int,start:Bool)->NSRect {
            guard isEditableRange(index),let window else{return .zero}
            let range=ranges[index],left=range.start/duration*Double(bounds.width)
            let width=max(14,(range.end-range.start)/duration*Double(bounds.width))
            let center=start ? left+6:left+width-6
            let rect=NSRect(x:center-10,y:0,width:20,height:bounds.height).intersection(bounds)
            guard !rect.isEmpty else{return .zero}
            return window.convertToScreen(convert(rect,to:nil))
        }
        override var isFlipped:Bool { true }
        override var acceptsFirstResponder:Bool { true }
        override var mouseDownCanMoveWindow:Bool { false }
        override func acceptsFirstMouse(for event:NSEvent?)->Bool { true }
        override func mouseDown(with event:NSEvent) {
            window?.makeFirstResponder(self)
            drag=MediaRangeDrag.begin(x:Double(convert(event.locationInWindow,from:nil).x),duration:duration,width:Double(bounds.width),ranges:ranges,allowsAdding:onAdd != nil)
            update(event,ending:false)
        }
        override func mouseDragged(with event:NSEvent) { update(event,ending:false) }
        override func mouseUp(with event:NSEvent) { update(event,ending:true);cancel() }
        override func viewWillMove(toWindow newWindow:NSWindow?) { if newWindow==nil { cancel() };super.viewWillMove(toWindow:newWindow) }
        func cancel() { drag=nil;onPending?(nil) }
        private func update(_ event:NSEvent,ending:Bool) {
            guard let drag else { return }
            switch drag.change(at:Double(convert(event.locationInWindow,from:nil).x)) {
            case .range(let index,let range):if ranges.indices.contains(index) { onChange?(index,range) }
            case .seek(let time):onSeek?(time)
            case .addition(let range):
                if ending {
                    if range.end-range.start>0.01 { onAdd?(range) } else { onSeek?(range.end) }
                } else { onPending?(range) }
            }
        }
        override func resetCursorRects() {
            super.resetCursorRects()
            guard duration.isFinite,duration>0,bounds.width>0 else { return }
            if onAdd != nil { addCursorRect(bounds,cursor:.crosshair) }
            for range in ranges where range.start.isFinite && range.end.isFinite {
                let left=range.start/duration*Double(bounds.width),width=max(14,(range.end-range.start)/duration*Double(bounds.width))
                let body=NSRect(x:left,y:0,width:width,height:bounds.height).intersection(bounds)
                if !body.isEmpty { addCursorRect(body,cursor:.openHand) }
                for x in [left-4,left+width-16] {
                    let handle=NSRect(x:x,y:0,width:20,height:bounds.height).intersection(bounds)
                    if !handle.isEmpty { addCursorRect(handle,cursor:.resizeLeftRight) }
                }
            }
        }
    }
}

/// Virtual sliders expose the existing range callbacks without creating new
/// pointer hit targets or sending global input events.
@MainActor final class RangeEndpointAccessibilityElement:NSAccessibilityElement {
    weak var lane:MediaRangeInteraction.LaneView?
    let index:Int
    let start:Bool
    let identifier:String
    init(lane:MediaRangeInteraction.LaneView,index:Int,start:Bool) {
        self.lane=lane;self.index=index;self.start=start
        identifier="media.range.\(index).\(start ? "start":"end")"
        super.init()
        setAccessibilityElement(true)
        setAccessibilityRole(.slider)
        setAccessibilityParent(lane)
        setAccessibilityIdentifier(identifier)
    }
    var value:Double {
        guard let lane,lane.isEditableRange(index) else{return 0}
        return start ? lane.ranges[index].start:lane.ranges[index].end
    }
    override func accessibilityLabel()->String? {
        let label=L10n.text(start ? "Start":"End")
        return (lane?.ranges.count ?? 0)>1 ? "\(label) \(index+1)":label
    }
    override func accessibilityHelp()->String? { L10n.text("Seconds") }
    override func accessibilityValue()->Any? { NSNumber(value:value) }
    override func accessibilityValueDescription()->String? { String(format:"%.3f",value)+" "+L10n.text("Seconds") }
    override func accessibilityMinValue()->Any? { NSNumber(value:lane?.endpointBounds(index:index,start:start)?.lowerBound ?? 0) }
    override func accessibilityMaxValue()->Any? { NSNumber(value:lane?.endpointBounds(index:index,start:start)?.upperBound ?? 0) }
    override func accessibilityFrame()->NSRect { lane?.endpointFrame(index:index,start:start) ?? .zero }
    override func isAccessibilityEnabled()->Bool { lane?.canEditRange(index) ?? false }
    override func setAccessibilityValue(_ value:Any?) {
        let number=(value as? NSNumber)?.doubleValue ?? (value as? String).flatMap{Double($0.replacingOccurrences(of:",",with:"."))}
        if let number { lane?.setEndpoint(index:index,start:start,value:number) }
    }
    override func accessibilityPerformIncrement()->Bool { lane?.setEndpoint(index:index,start:start,value:value+0.1) ?? false }
    override func accessibilityPerformDecrement()->Bool { lane?.setEndpoint(index:index,start:start,value:value-0.1) ?? false }
    override func isAccessibilitySelectorAllowed(_ selector:Selector)->Bool {
        if NSStringFromSelector(selector)=="setAccessibilityValue:" {return isAccessibilityEnabled()}
        return super.isAccessibilitySelectorAllowed(selector)
    }
}
