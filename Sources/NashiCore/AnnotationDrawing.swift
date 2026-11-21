import Foundation

/// Builds exactly one mark from the active tool and a mouse stroke.
/// Shapes and straight lines must never inherit the freehand trail.
public enum AnnotationDrawing {
    public static func draft(style: ImageAnnotation, points: [ImagePoint]) -> ImageAnnotation? {
        guard let first=points.first,let last=points.last,points.count>=2,
              points.allSatisfy({$0.x.isFinite && $0.y.isFinite}) else { return nil }
        var item=style
        let freehand=style.kind == .freehand || style.kind == .highlight
        let geometry=freehand ? points:[first,last]
        let minX=geometry.map(\.x).min()!,maxX=geometry.map(\.x).max()!
        let minY=geometry.map(\.y).min()!,maxY=geometry.map(\.y).max()!
        let width=maxX-minX,height=maxY-minY
        let linear=freehand || style.kind == .line || style.kind == .arrow
        guard linear ? max(width,height)>1:(width>=1 && height>=1) else { return nil }
        // Blank text paints nothing, so it must not become an invisible, selectable mark.
        if style.kind == .text && style.text.trimmingCharacters(in:.whitespacesAndNewlines).isEmpty { return nil }
        item.rect=EditRect(x:minX,y:minY,width:width,height:height)
        if freehand { item.points=points }
        else if style.kind == .line || style.kind == .arrow { item.points=[first,last] }
        else { item.points=[] }
        return item
    }
}
