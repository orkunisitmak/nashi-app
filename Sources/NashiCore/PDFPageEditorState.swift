import PDFKit

public enum PDFWidthMode: String, Codable, CaseIterable, Sendable {
    case original, narrowest, widest, a4, custom
}

/// An output page is an instance, so duplicate pages keep independent selection and edits.
public struct PDFEditorPage: Identifiable {
    public let id: UUID
    public let sourceIndex: Int?
    public let sourcePageIndex: Int
    public let page: PDFPage
    public let originalCropBox: CGRect

    /// Creates one page instance with an immutable source reference and its imported crop bounds.
    init(sourceIndex: Int?, sourcePageIndex: Int, page: PDFPage, id: UUID = UUID(), originalCropBox: CGRect? = nil) {
        self.id=id;self.sourceIndex=sourceIndex;self.sourcePageIndex=sourcePageIndex;self.page=page
        self.originalCropBox=originalCropBox ?? page.bounds(for:.cropBox)
    }
    /// Copies the instance metadata while optionally assigning a fresh identity to a duplicated page.
    func replacing(page: PDFPage, newIdentity: Bool = false) -> Self {
        .init(sourceIndex:sourceIndex,sourcePageIndex:sourcePageIndex,page:page,id:newIdentity ? UUID():id,originalCropBox:originalCropBox)
    }
    /// Gives a detached snapshot page a temporary document while PDFKit renders its visible crop bounds.
    public func thumbnail(of size:NSSize)->NSImage {
        let document=PDFDocument()
        guard let copy=page.copy() as? PDFPage else{return NSImage(size:size)}
        document.insert(copy,at:0)
        let preview=copy.thumbnail(of:size,for:.cropBox)
        guard let raster=preview.cgImage(forProposedRect:nil,context:nil,hints:nil) else{return preview}
        return NSImage(cgImage:raster,size:preview.size)
    }
}

/// Page objects in snapshots are immutable: every rotation/crop edits a copy.
public struct PDFPageEditorState {
    public private(set) var items:[PDFEditorPage]=[]
    public private(set) var sourceURLs:[URL?]=[]
    public private(set) var selectedIDs:Set<UUID>=[]
    private var anchor:UUID?
    private var focus:UUID?
    private var undoStack:[Snapshot]=[]
    private var redoStack:[Snapshot]=[]
    private var savedFingerprint=""
    public private(set) var widthMode:PDFWidthMode = .original
    public private(set) var customWidth:Double = 595.276

    private struct Snapshot {
        let items:[PDFEditorPage]
        let sourceURLs:[URL?]
        let selectedIDs:Set<UUID>
        let anchor:UUID?
        let focus:UUID?
        let widthMode:PDFWidthMode
        let customWidth:Double
    }
    /// Starts an empty workspace with a clean saved baseline.
    public init() { markSaved() }
    public var pages:[PDFPage] { items.map(\.page) }
    public var sourceIndexes:[Int] { items.map(\.sourcePageIndex) }
    public var selectedIndexes:[Int] { items.indices.filter{selectedIDs.contains(items[$0].id)} }
    public var selectedPage:Int? { items.firstIndex{ $0.id==focus && selectedIDs.contains($0.id) } ?? selectedIndexes.first }
    public var hasSelection:Bool { !selectedIndexes.isEmpty }
    public var canDelete:Bool { hasSelection && selectedIndexes.count<items.count }
    public var canMoveLeft:Bool { selectedIndexes.contains{ $0>0 && !selectedIDs.contains(items[$0-1].id) } }
    public var canMoveRight:Bool { selectedIndexes.contains{ $0+1<items.count && !selectedIDs.contains(items[$0+1].id) } }
    public var canUndo:Bool { !undoStack.isEmpty }
    public var canRedo:Bool { !redoStack.isEmpty }
    public var rotations:[Int:Int] { Dictionary(uniqueKeysWithValues:items.enumerated().map{($0.offset,$0.element.page.rotation)}) }
    public var isDirty:Bool { fingerprint != savedFingerprint }
    private var snapshot:Snapshot { .init(items:items,sourceURLs:sourceURLs,selectedIDs:selectedIDs,anchor:anchor,focus:focus,widthMode:widthMode,customWidth:customWidth) }
    private var fingerprint:String {
        items.map { "\($0.id):\($0.page.rotation):\(NSStringFromRect($0.page.bounds(for:.cropBox)))" }.joined(separator:"|")+"\(widthMode.rawValue):\(customWidth)"
    }
    /// Records the current page content and width settings as the saved baseline without changing history.
    public mutating func markSaved() { savedFingerprint=fingerprint }
    /// Imports independent page copies and clears selection, source references, history and width settings.
    public mutating func reset(document:PDFDocument?=nil, sourceURL:URL?=nil) {
        items=[];sourceURLs=[];selectedIDs=[];anchor=nil;focus=nil;undoStack=[];redoStack=[];widthMode = .original;customWidth=595.276
        if let document {
            sourceURLs=[sourceURL]
            items=(0..<document.pageCount).compactMap { index in
                guard let page=document.page(at:index)?.copy() as? PDFPage else { return nil }
                return PDFEditorPage(sourceIndex:0,sourcePageIndex:index,page:page)
            }
        }
        markSaved()
    }
    /// Selects a page, extends from the anchor with Shift, or toggles membership with Command.
    public mutating func select(_ index:Int, extending:Bool=false, toggling:Bool=false) {
        guard items.indices.contains(index) else { selectedIDs=[];anchor=nil;focus=nil;return }
        let id=items[index].id;focus=id
        if extending,let anchor,let start=items.firstIndex(where:{$0.id==anchor}) {
            let range=Set(items[min(start,index)...max(start,index)].map(\.id))
            selectedIDs=toggling ? selectedIDs.union(range):range
        } else if toggling {
            if selectedIDs.contains(id) { selectedIDs.remove(id) } else { selectedIDs.insert(id) }
            anchor=id
        } else { selectedIDs=[id];anchor=id }
    }
    /// Selects all page instances and anchors subsequent range selection at the first page.
    public mutating func selectAll() { selectedIDs=Set(items.map(\.id));anchor=items.first?.id;focus=items.last?.id }
    /// Snapshots one edit, caps the session history at 100 steps, and invalidates redo history.
    private mutating func record() { undoStack.append(snapshot);if undoStack.count>100 { undoStack.removeFirst() };redoStack=[] }
    /// Restores page instances, source references, selection focus and width settings together from one history entry.
    private mutating func restore(_ value:Snapshot) {
        items=value.items;sourceURLs=value.sourceURLs;selectedIDs=value.selectedIDs;anchor=value.anchor;focus=value.focus;widthMode=value.widthMode;customWidth=value.customWidth
    }
    /// Restores the preceding snapshot and retains the current snapshot for redo.
    public mutating func undo() { guard let previous=undoStack.popLast() else{return};redoStack.append(snapshot);restore(previous) }
    /// Reapplies the next saved snapshot without changing the saved-content baseline.
    public mutating func redo() { guard let next=redoStack.popLast() else{return};undoStack.append(snapshot);restore(next) }
    /// Rotates copied selected pages clockwise by a quarter turn as one undoable edit.
    public mutating func rotateSelected() {
        guard hasSelection else{return};record()
        for index in selectedIndexes {
            guard let page=items[index].page.copy() as? PDFPage else{continue}
            page.rotation=(page.rotation+90)%360;items[index]=items[index].replacing(page:page)
        }
    }
    /// Inserts independent copies after each selected page while enforcing the output-page limit.
    public mutating func duplicateSelected() {
        guard hasSelection,items.count+selectedIndexes.count<=10_000 else{return};record()
        for index in selectedIndexes.reversed() {
            if let page=items[index].page.copy() as? PDFPage { items.insert(items[index].replacing(page:page,newIdentity:true),at:index+1) }
        }
    }
    /// Removes the full selection in one history step, refusing to delete every page.
    public mutating func deleteSelected() {
        guard canDelete else{return};record()
        items.removeAll{selectedIDs.contains($0.id)};selectedIDs=[];anchor=nil;focus=nil
    }
    /// Moves selected blocks one position without swapping selected pages past each other or crossing bounds.
    public mutating func moveSelected(by offset:Int) {
        guard offset == -1 || offset == 1,(offset<0 ? canMoveLeft:canMoveRight) else{return};record()
        let indexes=offset<0 ? selectedIndexes:Array(selectedIndexes.reversed())
        for index in indexes {
            let next=index+offset
            if items.indices.contains(next),!selectedIDs.contains(items[next].id) { items.swapAt(index,next) }
        }
    }
    /// The insertion position refers to the pre-drop order, including the dragged pages.
    public mutating func moveSelected(to insertion:Int) {
        guard hasSelection,(0...items.count).contains(insertion) else{return}
        let chosen=items.filter{selectedIDs.contains($0.id)},remaining=items.filter{!selectedIDs.contains($0.id)}
        let target=insertion-selectedIndexes.filter{$0<insertion}.count
        var result=remaining;result.insert(contentsOf:chosen,at:target)
        guard result.map(\.id) != items.map(\.id) else{return};record();items=result
    }
    /// Imports a PDF after the selection, assigns stable source indices, and records one undoable insertion.
    public mutating func insert(document:PDFDocument,sourceURL:URL) {
        let source=sourceURLs.count
        let added=(0..<document.pageCount).compactMap { index -> PDFEditorPage? in
            guard let page=document.page(at:index)?.copy() as? PDFPage else{return nil}
            return .init(sourceIndex:source,sourcePageIndex:index,page:page)
        }
        guard !added.isEmpty,items.count+added.count<=10_000 else{return};record();sourceURLs.append(sourceURL)
        let position=(selectedIndexes.last.map{$0+1}) ?? items.count
        items.insert(contentsOf:added,at:position);selectedIDs=Set(added.map(\.id));anchor=added.first?.id;focus=added.last?.id
    }
    /// Inserts a blank page after the selection using its unrotated crop size, or A4 when unselected.
    public mutating func insertBlank() {
        guard items.count<10_000 else{return};record()
        let size=selectedPage.map{items[$0].page.bounds(for:.cropBox).size} ?? CGSize(width:595.276,height:841.89)
        let page=PDFPage();page.setBounds(CGRect(origin:.zero,size:size),for:.mediaBox)
        let added=PDFEditorPage(sourceIndex:nil,sourcePageIndex:0,page:page)
        items.insert(added,at:selectedIndexes.last.map{$0+1} ?? items.count);selectedIDs=[added.id];anchor=added.id;focus=added.id
    }
    /// Insets are expressed in unrotated PDF points, relative to each current crop box.
    public mutating func cropSelected(left:Double,right:Double,top:Double,bottom:Double) throws {
        let insets=[left,right,top,bottom]
        guard hasSelection,insets.allSatisfy({$0.isFinite && $0>=0}) else{throw NashiError.invalidInput("Choose pages and nonnegative crop margins.")}
        guard selectedIndexes.allSatisfy({index in let box=items[index].page.bounds(for:.cropBox);return box.width>left+right && box.height>top+bottom}) else{throw NashiError.invalidInput("Crop margins must leave a visible area on every selected page.")}
        guard insets.contains(where:{$0>0}) else{return};record()
        for index in selectedIndexes {
            guard let page=items[index].page.copy() as? PDFPage else{continue}
            let box=page.bounds(for:.cropBox)
            page.setBounds(CGRect(x:box.minX+left,y:box.minY+bottom,width:box.width-left-right,height:box.height-top-bottom),for:.cropBox)
            items[index]=items[index].replacing(page:page)
        }
    }
    /// Restores imported crop bounds on copies of the selected pages in one undoable edit.
    public mutating func resetSelectedCrop() {
        guard selectedIndexes.contains(where:{items[$0].page.bounds(for:.cropBox) != items[$0].originalCropBox}) else{return};record()
        for index in selectedIndexes {
            guard let page=items[index].page.copy() as? PDFPage else{continue}
            page.setBounds(items[index].originalCropBox,for:.cropBox);items[index]=items[index].replacing(page:page)
        }
    }
    /// Validates finite positive width settings and records a history entry only when they change.
    public mutating func setWidth(_ mode:PDFWidthMode,custom:Double?=nil) {
        let points=custom ?? customWidth
        guard points.isFinite,points>0,points<=14_400,(mode != widthMode || points != customWidth) else{return}
        record();widthMode=mode;customWidth=points
    }
    /// Serializes source references, rotations, crop boxes and blank sizes in current workspace order.
    public func exportPlan(selectedOnly:Bool=false)->[PDFWorkspacePage] {
        let chosen=selectedOnly ? items.filter{selectedIDs.contains($0.id)}:items
        return chosen.map { item in
            let box=item.page.bounds(for:.cropBox),media=item.page.bounds(for:.mediaBox)
            return .init(sourceIndex:item.sourceIndex,pageIndex:item.sourcePageIndex,rotation:item.page.rotation,
                         cropBox:PDFPageBox(box),blankSize:PDFPageBox(media))
        }
    }
}
