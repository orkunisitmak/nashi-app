import SwiftUI
import AppKit
import PDFKit
import UniformTypeIdentifiers
import NashiCore

private let pdfPageDragType=UTType(exportedAs:"app.nashi.pdf-page")

struct PDFWorkspaceView:View {
    @Binding var editor:PDFPageEditorState
    let onExtract:()->Void
    let onError:(String)->Void
    @State private var draggedID:UUID?
    @State private var left=0.0
    @State private var right=0.0
    @State private var top=0.0
    @State private var bottom=0.0
    @State private var customWidth=595.276

    /// Presents multi-selection page cards, batch tools, crop margins and undoable width settings.
    var body:some View {
        VStack(alignment:.leading,spacing:14) {
            HStack {
                Text("\(editor.selectedIndexes.count) / \(editor.items.count)").font(.caption.monospacedDigit())
                LText("pages selected").font(.caption)
                Spacer()
                if editor.isDirty { LText("Unsaved changes").font(.caption).foregroundStyle(.orange) }
                Button("Undo") { editor.undo() }.disabled(!editor.canUndo)
                Button("Redo") { editor.redo() }.disabled(!editor.canRedo)
            }
            LText("Shift-click selects a range. Command-click selects individual pages. Drag to reorder; Delete removes selected pages.")
                .font(.caption).foregroundStyle(.secondary).fixedSize(horizontal:false,vertical:true)
            ScrollView {
                LazyVGrid(columns:[GridItem(.adaptive(minimum:110))],spacing:14) {
                    ForEach(Array(editor.items.enumerated()),id:\.element.id) { index,item in
                        PDFPageCard(item:item,position:index+1,selected:editor.selectedIDs.contains(item.id))
                            .contentShape(Rectangle())
                            .onTapGesture {
                                NSApp.keyWindow?.makeFirstResponder(nil)
                                let flags=NSEvent.modifierFlags
                                editor.select(index,extending:flags.contains(.shift),toggling:flags.contains(.command))
                            }
                            .accessibilityElement(children:.ignore)
                            .accessibilityLabel(L10n.text("Page")+" \(index+1)")
                            .accessibilityAddTraits(editor.selectedIDs.contains(item.id) ? [.isButton,.isSelected]:.isButton)
                            .accessibilityAction { NSApp.keyWindow?.makeFirstResponder(nil);editor.select(index) }
                            .onDrag {
                                NSApp.keyWindow?.makeFirstResponder(nil)
                                if !editor.selectedIDs.contains(item.id) { editor.select(index) }
                                draggedID=item.id
                                let provider=NSItemProvider()
                                provider.registerDataRepresentation(forTypeIdentifier:pdfPageDragType.identifier,visibility:.ownProcess) { done in done(Data(item.id.uuidString.utf8),nil);return nil }
                                return provider
                            }
                            .onDrop(of:[pdfPageDragType],delegate:PDFPageDropDelegate(editor:$editor,draggedID:$draggedID,insertion:index))
                    }
                    LText("Drop here to move to end").font(.caption).frame(maxWidth:.infinity,minHeight:60)
                        .background(NashiPalette.inset,in:RoundedRectangle(cornerRadius:8))
                        .onDrop(of:[pdfPageDragType],delegate:PDFPageDropDelegate(editor:$editor,draggedID:$draggedID,insertion:editor.items.count))
                }.padding(3)
            }.frame(height:260).uiAuditProbe("editor.preview")
            HStack {
                Button("Rotate") { editor.rotateSelected() }.disabled(!editor.hasSelection)
                Button("Duplicate") { editor.duplicateSelected() }.disabled(!editor.hasSelection || editor.items.count+editor.selectedIndexes.count>10_000)
                Button("Delete") { editor.deleteSelected() }.disabled(!editor.canDelete)
                Button("Move Left") { editor.moveSelected(by:-1) }.disabled(!editor.canMoveLeft)
                Button("Move Right") { editor.moveSelected(by:1) }.disabled(!editor.canMoveRight)
            }
            if editor.hasSelection,!editor.canDelete { LText("Keep at least one page. Deselect a page to delete the others.").font(.caption).foregroundStyle(.secondary) }
            HStack {
                Button("Add PDF…",action:addPDF)
                Button("Add blank page") { editor.insertBlank() }.disabled(editor.items.count>=10_000)
                Button("Extract selected pages",action:onExtract).disabled(!editor.hasSelection)
                Spacer()
                Button("Select all") { editor.selectAll() }
            }
            DisclosureGroup("Crop selected pages") {
                VStack(alignment:.leading,spacing:10) {
                    LText("Margins in PDF points, before page rotation. Cropping hides content outside the page; it does not redact it.").font(.caption).foregroundStyle(.secondary)
                    HStack { margin("Left",value:$left);margin("Right",value:$right);margin("Top",value:$top);margin("Bottom",value:$bottom) }
                    HStack {
                        Button("Apply crop") {
                            do { try editor.cropSelected(left:left,right:right,top:top,bottom:bottom) }
                            catch { onError(error.localizedDescription) }
                        }.disabled(!editor.hasSelection)
                        Button("Reset crop") { editor.resetSelectedCrop() }.disabled(!editor.hasSelection)
                    }
                }.padding(.top,8)
            }
            Picker("Page width",selection:Binding(get:{editor.widthMode},set:{editor.setWidth($0)})) {
                Text("Keep original widths").tag(PDFWidthMode.original)
                Text("Match narrowest page").tag(PDFWidthMode.narrowest)
                Text("Match widest page").tag(PDFWidthMode.widest)
                Text("A4 width").tag(PDFWidthMode.a4)
                Text("Custom width").tag(PDFWidthMode.custom)
            }
            if editor.widthMode == .custom {
                HStack {
                    TextField("Width in points",value:$customWidth,format:.number).frame(width:140)
                    Button("Apply width") {
                        guard customWidth.isFinite,customWidth>0,customWidth<=14_400 else { onError("Choose a page width between 0 and 14,400 points.");return }
                        editor.setWidth(.custom,custom:customWidth)
                    }
                    Text("\(editor.customWidth, specifier:"%.2f") pt").font(.caption)
                }
            }
            if editor.widthMode != .original { LText("Widths scale proportionally. Documents with forms, links or attachments may not support this operation.").font(.caption).foregroundStyle(.secondary) }
        }.onChange(of:editor.customWidth) { customWidth=$0 }
    }
    /// Builds a translated margin label and numeric field for one unrotated PDF-point inset.
    private func margin(_ title:String,value:Binding<Double>)->some View {
        VStack(alignment:.leading,spacing:4) { LText(title).font(.caption);TextField(title,value:value,format:.number).frame(minWidth:60) }
    }
    /// Validates every chosen PDF and the combined page count before importing any selected file.
    private func addPDF() {
        let panel=NSOpenPanel();panel.allowedContentTypes=[.pdf];panel.allowsMultipleSelection=true
        guard panel.runModal() == .OK else{return}
        do {
            // Validate every chosen document before changing the workspace.
            let documents=try panel.urls.map{try PDFEngine.open($0)}
            guard editor.items.count+documents.reduce(0,{$0+$1.pageCount})<=10_000 else{onError("Choose no more than 10,000 output pages at once.");return}
            for (url,document) in zip(panel.urls,documents) { editor.insert(document:document,sourceURL:url) }
        } catch { onError(error.localizedDescription) }
    }
}

private struct PDFPageCard:View {
    let item:PDFEditorPage
    let position:Int
    let selected:Bool
    @State private var thumbnail:NSImage?
    private var revision:String { "\(item.id):\(item.page.rotation):\(NSStringFromRect(item.page.bounds(for:.cropBox)))" }
    /// Renders a selectable card and refreshes its cached thumbnail when identity, rotation or crop changes.
    var body:some View {
        VStack(spacing:6) {
            if let thumbnail { Image(nsImage:thumbnail).resizable().scaledToFit().frame(height:140) }
            else { ProgressView().frame(height:140) }
            Text("\(position)").font(.caption)
        }.frame(maxWidth:.infinity).padding(8)
            .background(selected ? Color.nashi.opacity(0.2):Color.primary.opacity(0.03),in:RoundedRectangle(cornerRadius:8))
            .overlay(RoundedRectangle(cornerRadius:8).stroke(selected ? Color.nashi:Color.clear,lineWidth:2))
            .task(id:revision) { thumbnail=item.thumbnail(of:NSSize(width:110,height:140)) }
    }
}

private struct PDFPageDropDelegate:DropDelegate {
    @Binding var editor:PDFPageEditorState
    @Binding var draggedID:UUID?
    let insertion:Int
    /// Accepts only an in-process page drag whose page still belongs to the active selection.
    func validateDrop(info:DropInfo)->Bool { draggedID.map{editor.selectedIDs.contains($0)} ?? false }
    /// Advertises a move operation so the drag preserves page-instance identity.
    func dropUpdated(info:DropInfo)->DropProposal? { DropProposal(operation:.move) }
    /// Moves the selected block to the requested insertion point and clears the active drag token.
    func performDrop(info:DropInfo)->Bool {
        guard validateDrop(info:info) else{return false}
        editor.moveSelected(to:insertion);draggedID=nil;return true
    }
}
