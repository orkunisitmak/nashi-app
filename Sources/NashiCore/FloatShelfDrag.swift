import Foundation

/// Float keeps references, not copies, so a drag out can move or trash a shelf file.
public enum FloatShelfDrag {
    /// The items to drop after a drag ends: only those the drag carried whose file is gone.
    /// Every other item stays, even when its volume is unmounted or briefly unreachable.
    public static func vanished<ID:Hashable>(_ items:[(id:ID,url:URL)],dragged:Set<ID>,exists:(URL)->Bool={FileManager.default.fileExists(atPath:$0.path)})->Set<ID> {
        Set(items.filter{dragged.contains($0.id) && !exists($0.url)}.map(\.id))
    }
}
