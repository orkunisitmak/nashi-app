import Foundation

/// Session history for annotation edits. A continuous pointer interaction is one undo step.
public struct ImageAnnotationHistory {
    private var previous: [[ImageAnnotation]] = []
    private var next: [[ImageAnnotation]] = []
    private var interactionStart: [ImageAnnotation]?
    private let limit: Int
    public init(limit: Int = 100) { self.limit = max(1, limit) }
    public var canUndo: Bool { interactionStart == nil && !previous.isEmpty }
    public var canRedo: Bool { interactionStart == nil && !next.isEmpty }
    public mutating func record(before: [ImageAnnotation], after: [ImageAnnotation]) {
        guard interactionStart == nil, before != after else { return }
        previous.append(before)
        if previous.count > limit { previous.removeFirst(previous.count - limit) }
        next = []
    }
    public mutating func beginInteraction(_ current: [ImageAnnotation]) {
        if interactionStart == nil { interactionStart = current }
    }
    public mutating func endInteraction(_ current: [ImageAnnotation]) {
        guard let before = interactionStart else { return }
        interactionStart = nil
        record(before: before, after: current)
    }
    public mutating func undo(_ current: [ImageAnnotation]) -> [ImageAnnotation]? {
        guard canUndo, let value = previous.popLast() else { return nil }
        next.append(current)
        return value
    }
    public mutating func redo(_ current: [ImageAnnotation]) -> [ImageAnnotation]? {
        guard canRedo, let value = next.popLast() else { return nil }
        previous.append(current)
        return value
    }
    public mutating func reset() { previous = []; next = []; interactionStart = nil }
}
