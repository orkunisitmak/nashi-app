import XCTest
import PDFKit
import AppKit
import CoreText
@testable import NashiCore

final class PDFWorkspaceTests:XCTestCase {
    private var root:URL!
    override func setUpWithError() throws { root=try OutputFiles.temporaryDirectory() }
    override func tearDownWithError() throws { try? FileManager.default.removeItem(at:root) }
    private func source(_ name:String,widths:[Double]=[300,500])throws->URL {
        let url=root.appendingPathComponent(name+".pdf")
        let context=CGContext(consumer:CGDataConsumer(url:url as CFURL)!,mediaBox:nil,nil)!
        for (index,width) in widths.enumerated() {
            var box=CGRect(x:0,y:0,width:width,height:600)
            context.beginPDFPage([kCGPDFContextMediaBox:Data(bytes:&box,count:MemoryLayout<CGRect>.size)] as CFDictionary)
            let line=CTLineCreateWithAttributedString(NSAttributedString(string:"\(name) page \(index+1)",attributes:[.font:NSFont.systemFont(ofSize:18)]))
            context.textPosition=CGPoint(x:40,y:100);CTLineDraw(line,context);context.endPDFPage()
        }
        context.closePDF();return url
    }
    private func export(_ editor:PDFPageEditorState,selectedOnly:Bool=false)throws->URL {
        var options=OperationOptions();options.outputDirectory=root
        options.pdfWorkspacePages=editor.exportPlan(selectedOnly:selectedOnly);options.pdfWidthMode=editor.widthMode;options.pdfCustomWidth=editor.customWidth;options.pdfExtractSelection=selectedOnly
        return try ConversionEngine.run(.init(inputs:editor.sourceURLs.compactMap{$0},operation:.tool(.organizePDF),options:options))[0]
    }
    func testMultiSelectionDeleteUndoAndRedoPreservePageInstancesAndSelection()throws {
        let url=try source("Original",widths:[300,300,300,300,300])
        var editor=PDFPageEditorState();editor.reset(document:try PDFEngine.open(url),sourceURL:url)
        let ids=editor.items.map(\.id)
        editor.select(1);editor.select(3,extending:true);editor.select(0,toggling:true)
        XCTAssertEqual(editor.selectedIndexes,[0,1,2,3]);editor.deleteSelected()
        XCTAssertEqual(editor.sourceIndexes,[4]);XCTAssertTrue(editor.isDirty)
        editor.undo();XCTAssertEqual(editor.items.map(\.id),ids);XCTAssertEqual(editor.selectedIndexes,[0,1,2,3]);XCTAssertFalse(editor.isDirty)
        editor.redo();XCTAssertEqual(editor.sourceIndexes,[4]);XCTAssertFalse(editor.hasSelection)
        editor.selectAll();editor.deleteSelected();XCTAssertEqual(editor.pages.count,1)
    }
    func testBlockMovesAndDragKeepOrderAndSelectionIncludingBoundaryPages()throws {
        let url=try source("Move",widths:[300,300,300,300,300])
        var editor=PDFPageEditorState();editor.reset(document:try PDFEngine.open(url),sourceURL:url)
        editor.select(1);editor.select(2,toggling:true);let chosen=editor.selectedIDs
        editor.moveSelected(to:5)
        XCTAssertEqual(editor.sourceIndexes,[0,3,4,1,2]);XCTAssertEqual(editor.selectedIDs,chosen)
        editor.undo();editor.moveSelected(by:-1)
        XCTAssertEqual(editor.sourceIndexes,[1,2,0,3,4]);XCTAssertEqual(editor.selectedIDs,chosen)
        editor.moveSelected(by:1);XCTAssertEqual(editor.sourceIndexes,[0,1,2,3,4])
        editor.select(0);editor.select(1,toggling:true);editor.moveSelected(by:-1)
        XCTAssertEqual(editor.sourceIndexes,[0,1,2,3,4])
    }
    func testDuplicateRotationAndCropSnapshotsDoNotMutateSourceOrOtherCopies()throws {
        let url=try source("Copies");let before=try Data(contentsOf:url)
        var editor=PDFPageEditorState();editor.reset(document:try PDFEngine.open(url),sourceURL:url)
        editor.select(0);editor.duplicateSelected();editor.select(1);editor.rotateSelected()
        try editor.cropSelected(left:10,right:20,top:30,bottom:40)
        XCTAssertEqual(editor.pages[0].rotation,0);XCTAssertEqual(editor.pages[1].rotation,90)
        XCTAssertEqual(editor.pages[1].bounds(for:.cropBox),CGRect(x:10,y:40,width:270,height:530))
        editor.undo();XCTAssertEqual(editor.pages[1].bounds(for:.cropBox).width,300)
        editor.undo();XCTAssertEqual(editor.pages[1].rotation,0)
        editor.redo();editor.redo();XCTAssertEqual(editor.pages[1].bounds(for:.cropBox).width,270)
        XCTAssertEqual(try Data(contentsOf:url),before)
    }
    func testInterleavedSourcesBlankPageAndCropRoundTripThroughTransaction()throws {
        let first=try source("First"),second=try source("Second")
        let originals=try [first,second].map{try Data(contentsOf:$0)}
        var editor=PDFPageEditorState();editor.reset(document:try PDFEngine.open(first),sourceURL:first)
        editor.select(0);editor.insert(document:try PDFEngine.open(second),sourceURL:second)
        editor.select(1);editor.insertBlank();editor.select(0)
        try editor.cropSelected(left:10,right:20,top:30,bottom:40)
        let output=try export(editor),pdf=try PDFEngine.open(output)
        XCTAssertEqual(pdf.pageCount,5)
        XCTAssertTrue(pdf.page(at:0)!.string!.contains("First page 1"))
        XCTAssertTrue(pdf.page(at:1)!.string!.contains("Second page 1"))
        XCTAssertTrue((pdf.page(at:2)?.string ?? "").trimmingCharacters(in:.whitespacesAndNewlines).isEmpty)
        XCTAssertTrue(pdf.page(at:3)!.string!.contains("Second page 2"))
        XCTAssertTrue(pdf.page(at:4)!.string!.contains("First page 2"))
        XCTAssertEqual(pdf.page(at:0)!.bounds(for:.cropBox),CGRect(x:10,y:40,width:270,height:530))
        for (url,before) in zip([first,second],originals) { XCTAssertEqual(try Data(contentsOf:url),before) }
    }
    func testExtractionUsesOnlySelectedPagesInWorkspaceOrderAndDoesNotChangeState()throws {
        let url=try source("Extract",widths:[300,300,300])
        var editor=PDFPageEditorState();editor.reset(document:try PDFEngine.open(url),sourceURL:url)
        editor.select(2);editor.moveSelected(to:0);editor.select(2,toggling:true)
        let ids=editor.items.map(\.id),selection=editor.selectedIDs
        let pdf=try PDFEngine.open(export(editor,selectedOnly:true))
        XCTAssertEqual(pdf.pageCount,2);XCTAssertTrue(pdf.page(at:0)!.string!.contains("Extract page 3"));XCTAssertTrue(pdf.page(at:1)!.string!.contains("Extract page 2"))
        XCTAssertEqual(editor.items.map(\.id),ids);XCTAssertEqual(editor.selectedIDs,selection)
    }
    func testWidthModesAndUndoUseDisplayedSizesAndKeepSelectableText()throws {
        let url=try source("Widths")
        var editor=PDFPageEditorState();editor.reset(document:try PDFEngine.open(url),sourceURL:url)
        for (mode,target) in [(PDFWidthMode.narrowest,300.0),(.widest,500),(.a4,595.276),(.custom,420)] {
            editor.setWidth(mode,custom:420)
            let pdf=try PDFEngine.open(export(editor))
            for index in 0..<pdf.pageCount { XCTAssertEqual(pdf.page(at:index)!.bounds(for:.mediaBox).width,target,accuracy:0.01) }
            XCTAssertTrue(pdf.string!.contains("Widths page 1"))
        }
        editor.undo();XCTAssertEqual(editor.widthMode,.a4)
        editor.markSaved();XCTAssertFalse(editor.isDirty);editor.select(0);editor.rotateSelected();XCTAssertTrue(editor.isDirty);editor.undo();XCTAssertFalse(editor.isDirty)
    }
    func testUniformWidthUsesRotatedCropSizeAndKeepsEmbeddedFontText()throws {
        let url=try source("Rotated")
        var editor=PDFPageEditorState();editor.reset(document:try PDFEngine.open(url),sourceURL:url)
        editor.select(0);editor.rotateSelected();try editor.cropSelected(left:10,right:20,top:30,bottom:40)
        editor.setWidth(.custom,custom:420)
        let pdf=try PDFEngine.open(export(editor))
        for index in 0..<pdf.pageCount {
            let page=pdf.page(at:index)!,box=page.bounds(for:.cropBox)
            let width=abs(page.rotation)%180==90 ? box.height:box.width
            XCTAssertEqual(width,420,accuracy:0.01)
        }
        XCTAssertEqual(pdf.page(at:0)?.rotation,90)
        XCTAssertTrue(pdf.string!.contains("Rotated page 1"));XCTAssertTrue(pdf.string!.contains("Rotated page 2"))
    }

    func testInvalidCropAndInvalidPlanPublishNothing()throws {
        let url=try source("Invalid")
        var editor=PDFPageEditorState();editor.reset(document:try PDFEngine.open(url),sourceURL:url);editor.selectAll()
        XCTAssertThrowsError(try editor.cropSelected(left:400,right:0,top:0,bottom:0));XCTAssertFalse(editor.canUndo)
        XCTAssertThrowsError(try editor.cropSelected(left:.nan,right:0,top:0,bottom:0))
        var options=OperationOptions();options.outputDirectory=root.appendingPathComponent("Exports")
        options.pdfWorkspacePages=[.init(sourceIndex:4,pageIndex:0)]
        XCTAssertThrowsError(try ConversionEngine.run(.init(inputs:[url],operation:.tool(.organizePDF),options:options)))
        XCTAssertEqual(try FileManager.default.contentsOfDirectory(atPath:options.outputDirectory!.path),[])
    }
    func testNewEditClearsRedoAndResetClearsSelectionHistoryAndSources()throws {
        let url=try source("Reset")
        var editor=PDFPageEditorState();editor.reset(document:try PDFEngine.open(url),sourceURL:url)
        editor.select(0);editor.rotateSelected();editor.undo();editor.insertBlank();XCTAssertFalse(editor.canRedo)
        editor.reset();XCTAssertFalse(editor.canUndo);XCTAssertFalse(editor.canRedo);XCTAssertFalse(editor.hasSelection);XCTAssertTrue(editor.sourceURLs.isEmpty);XCTAssertFalse(editor.isDirty)
    }
}
