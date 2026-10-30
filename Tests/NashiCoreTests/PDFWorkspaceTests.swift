import XCTest
import PDFKit
import AppKit
import CoreText
@testable import NashiCore

final class PDFWorkspaceTests:XCTestCase {
    private var root:URL!
    override func setUpWithError() throws { root=try OutputFiles.temporaryDirectory() }
    override func tearDownWithError() throws { try? FileManager.default.removeItem(at:root) }
    /// Creates searchable synthetic pages with distinct text and widths, without external or personal fixtures.
    private func source(_ name:String,widths:[Double]=[300,500],fontSize:CGFloat=18)throws->URL {
        let url=root.appendingPathComponent(name+".pdf")
        let context=CGContext(consumer:CGDataConsumer(url:url as CFURL)!,mediaBox:nil,nil)!
        for (index,width) in widths.enumerated() {
            var box=CGRect(x:0,y:0,width:width,height:600)
            context.beginPDFPage([kCGPDFContextMediaBox:Data(bytes:&box,count:MemoryLayout<CGRect>.size)] as CFDictionary)
            let line=CTLineCreateWithAttributedString(NSAttributedString(string:"\(name) page \(index+1)",attributes:[.font:NSFont.systemFont(ofSize:fontSize)]))
            context.textPosition=CGPoint(x:40,y:100);CTLineDraw(line,context);context.endPDFPage()
        }
        context.closePDF();return url
    }
    /// Exercises the public transactional conversion path using the current workspace plan and width settings.
    private func export(_ editor:PDFPageEditorState,selectedOnly:Bool=false)throws->URL {
        var options=OperationOptions();options.outputDirectory=root
        options.pdfWorkspacePages=editor.exportPlan(selectedOnly:selectedOnly);options.pdfWidthMode=editor.widthMode;options.pdfCustomWidth=editor.customWidth;options.pdfExtractSelection=selectedOnly
        return try ConversionEngine.run(.init(inputs:editor.sourceURLs.compactMap{$0},operation:.tool(.organizePDF),options:options))[0]
    }
    /// Detached immutable snapshots must render real source content, with the same crop area as the workspace.
    func testDetachedPageThumbnailRetainsVisibleSourceContentAndCropAspectRatio()throws {
        let url=try source("Thumbnail",widths:[300],fontSize:72)
        var editor=PDFPageEditorState()
        try autoreleasepool { editor.reset(document:try PDFEngine.open(url),sourceURL:url) }
        editor.select(0);try editor.cropSelected(left:10,right:20,top:30,bottom:40)
        let image=editor.items[0].thumbnail(of:NSSize(width:110,height:140))
        let cgImage=try XCTUnwrap(image.cgImage(forProposedRect:nil,context:nil,hints:nil))
        let bitmap=NSBitmapImageRep(cgImage:cgImage)
        var inkPixels=0
        for y in 0..<bitmap.pixelsHigh {for x in 0..<bitmap.pixelsWide {
            if let color=bitmap.colorAt(x:x,y:y)?.usingColorSpace(.deviceRGB),color.alphaComponent>0.5,
               min(color.redComponent,color.greenComponent,color.blueComponent)<0.5 {inkPixels+=1}
        }}
        XCTAssertGreaterThan(inkPixels,10,"Source text must remain visible in the page card")
        XCTAssertEqual(Double(bitmap.pixelsWide)/Double(bitmap.pixelsHigh),270.0/530.0,accuracy:0.03)
    }
    /// Undoing an import must remove unused source dependencies; redo restores their matching page indices.
    func testUndoingPDFInsertionRestoresSourcesAndCanExportAfterRemovedImportDisappears()throws {
        let first=try source("Retained"),second=try source("Removed import")
        let before=try Data(contentsOf:first)
        var editor=PDFPageEditorState();editor.reset(document:try PDFEngine.open(first),sourceURL:first)
        editor.insert(document:try PDFEngine.open(second),sourceURL:second)
        editor.undo();XCTAssertEqual(editor.sourceURLs,[first]);XCTAssertFalse(editor.isDirty)
        editor.redo();XCTAssertEqual(editor.sourceURLs,[first,second]);XCTAssertEqual(editor.items.last?.sourceIndex,1)
        editor.undo();try FileManager.default.removeItem(at:second)
        let pdf=try PDFEngine.open(export(editor))
        XCTAssertEqual(pdf.pageCount,2);XCTAssertTrue(pdf.string!.contains("Retained page 1"))
        XCTAssertEqual(try Data(contentsOf:first),before)
    }
    /// Verifies batch deletion, selection restoration, saved state, redo and the minimum-one-page invariant.
    func testMultiSelectionDeleteUndoAndRedoPreservePageInstancesAndSelection()throws {
        let url=try source("Original",widths:[300,300,300,300,300])
        var editor=PDFPageEditorState();editor.reset(document:try PDFEngine.open(url),sourceURL:url)
        let ids=editor.items.map(\.id)
        editor.select(0);editor.select(4,toggling:true);XCTAssertEqual(editor.selectedIndexes,[0,4])
        editor.select(0,toggling:true);XCTAssertEqual(editor.selectedIndexes,[4])
        editor.select(4,toggling:true);XCTAssertFalse(editor.hasSelection);XCTAssertFalse(editor.isDirty)
        editor.select(1);editor.select(3,extending:true);editor.select(0,toggling:true)
        XCTAssertEqual(editor.selectedIndexes,[0,1,2,3]);editor.deleteSelected()
        XCTAssertEqual(editor.sourceIndexes,[4]);XCTAssertTrue(editor.isDirty)
        editor.undo();XCTAssertEqual(editor.items.map(\.id),ids);XCTAssertEqual(editor.selectedIndexes,[0,1,2,3]);XCTAssertFalse(editor.isDirty)
        editor.redo();XCTAssertEqual(editor.sourceIndexes,[4]);XCTAssertFalse(editor.hasSelection)
        editor.selectAll();editor.deleteSelected();XCTAssertEqual(editor.pages.count,1)
    }
    /// Checks stable block order and selected identities for drag insertion and boundary-constrained moves.
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
    /// Ensures rotation/crop history never mutates another page instance or the original source file.
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
    /// Round-trips interleaved sources, crop coordinates and blank pages through the preserving writer.
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
    /// Exports only the selected pages in workspace order without changing the working selection or page instances.
    func testExtractionUsesOnlySelectedPagesInWorkspaceOrderAndDoesNotChangeState()throws {
        let url=try source("Extract",widths:[300,300,300])
        var editor=PDFPageEditorState();editor.reset(document:try PDFEngine.open(url),sourceURL:url)
        editor.select(2);editor.moveSelected(to:0);editor.select(2,toggling:true)
        let ids=editor.items.map(\.id),selection=editor.selectedIDs
        let pdf=try PDFEngine.open(export(editor,selectedOnly:true))
        XCTAssertEqual(pdf.pageCount,2);XCTAssertTrue(pdf.page(at:0)!.string!.contains("Extract page 3"));XCTAssertTrue(pdf.page(at:1)!.string!.contains("Extract page 2"))
        XCTAssertEqual(editor.items.map(\.id),ids);XCTAssertEqual(editor.selectedIDs,selection)
    }
    /// Checks every width mode, selectable text, history restoration and the saved-content fingerprint.
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
    /// Checks displayed width after rotation/cropping while retaining embedded-font text and page rotation.
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

    /// Owner-encrypted PDFs that open with an empty user password retain the editor's complete page plan.
    func testOwnerEncryptedWorkspaceKeepsOrderCropRotationBlankPagesAndOriginal()throws {
        let plain=try source("Owner encrypted"),encrypted=root.appendingPathComponent("Owner encrypted.pdf")
        XCTAssertTrue(try PDFEngine.open(plain).write(to:encrypted,withOptions:[.ownerPasswordOption:"synthetic-owner",.userPasswordOption:""]))
        let opened=try PDFEngine.open(encrypted),before=try Data(contentsOf:encrypted)
        XCTAssertTrue(opened.isEncrypted);XCTAssertFalse(opened.isLocked)
        var editor=PDFPageEditorState();editor.reset(document:opened,sourceURL:encrypted)
        editor.select(1);editor.moveSelected(to:0);editor.rotateSelected()
        try editor.cropSelected(left:10,right:20,top:30,bottom:40)
        editor.insertBlank()
        let output=try export(editor),pdf=try PDFEngine.open(output)
        XCTAssertEqual(pdf.pageCount,3)
        XCTAssertTrue(pdf.page(at:0)!.string!.contains("Owner encrypted page 2"))
        XCTAssertEqual(pdf.page(at:0)?.rotation,90)
        XCTAssertEqual(pdf.page(at:0)!.bounds(for:.cropBox),CGRect(x:10,y:40,width:470,height:530))
        XCTAssertTrue((pdf.page(at:1)?.string ?? "").trimmingCharacters(in:.whitespacesAndNewlines).isEmpty)
        XCTAssertEqual(pdf.page(at:1)!.bounds(for:.mediaBox).size,CGSize(width:470,height:530))
        XCTAssertTrue(pdf.page(at:2)!.string!.contains("Owner encrypted page 1"))
        XCTAssertEqual(try Data(contentsOf:encrypted),before)
        // The decrypted source still uses the content-stream scaler, including displayed rotation.
        editor.setWidth(.custom,custom:420)
        let scaled=try PDFEngine.open(export(editor))
        for index in 0..<scaled.pageCount {
            let page=scaled.page(at:index)!,box=page.bounds(for:.cropBox)
            XCTAssertEqual(abs(page.rotation)%180==90 ? box.height:box.width,420,accuracy:0.01)
        }
        XCTAssertTrue(scaled.string!.contains("Owner encrypted page 2"))
        XCTAssertEqual(try Data(contentsOf:encrypted),before)
    }

    /// Core callers can supply a user password; a wrong password must publish no copy.
    func testWorkspaceUsesSuppliedPasswordAndRejectsWrongPasswordTransactionally()throws {
        let plain=try source("Password"),encrypted=root.appendingPathComponent("Password protected.pdf")
        XCTAssertTrue(try PDFEngine.open(plain).write(to:encrypted,withOptions:[.ownerPasswordOption:"synthetic-owner",.userPasswordOption:"synthetic-password"]))
        let before=try Data(contentsOf:encrypted)
        var options=OperationOptions();options.outputDirectory=root.appendingPathComponent("Password exports")
        options.pdfWorkspacePages=[.init(sourceIndex:0,pageIndex:1,rotation:180)]
        options.password="incorrect"
        XCTAssertThrowsError(try ConversionEngine.run(.init(inputs:[encrypted],operation:.tool(.organizePDF),options:options)))
        XCTAssertEqual(try FileManager.default.contentsOfDirectory(atPath:options.outputDirectory!.path),[])
        options.password="synthetic-password"
        let output=try ConversionEngine.run(.init(inputs:[encrypted],operation:.tool(.organizePDF),options:options))[0]
        let pdf=try PDFEngine.open(output)
        XCTAssertEqual(pdf.pageCount,1);XCTAssertEqual(pdf.page(at:0)?.rotation,180)
        XCTAssertTrue(pdf.string!.contains("Password page 2"));XCTAssertEqual(try Data(contentsOf:encrypted),before)
    }

    /// Cancels after all workspace outputs are staged, proving the publication boundary keeps user files intact.
    func testCancelledWorkspaceBeforePublicationKeepsOriginalAndExistingExports()throws {
        let plain=try source("Cancelled workspace"),encrypted=root.appendingPathComponent("Cancelled encrypted.pdf")
        XCTAssertTrue(try PDFEngine.open(plain).write(to:encrypted,withOptions:[.ownerPasswordOption:"synthetic-owner",.userPasswordOption:""]))
        let before=try Data(contentsOf:encrypted),destination=root.appendingPathComponent("Cancelled exports")
        try FileManager.default.createDirectory(at:destination,withIntermediateDirectories:true)
        let sentinel=destination.appendingPathComponent("Keep.txt"),sentinelBytes=Data("existing user export".utf8)
        try sentinelBytes.write(to:sentinel)
        var editor=PDFPageEditorState();editor.reset(document:try PDFEngine.open(encrypted),sourceURL:encrypted)
        editor.select(0);editor.insertBlank()
        var options=OperationOptions();options.outputDirectory=destination;options.pdfWorkspacePages=editor.exportPlan()
        let token=CancellationToken(),publicationReached=CancellationToken()
        XCTAssertThrowsError(try ConversionEngine.run(.init(inputs:[encrypted],operation:.tool(.organizePDF),options:options),token:token) { _,label in
            if label == "Saving your files…" { publicationReached.cancel();token.cancel() }
        }) { error in XCTAssertEqual(error.localizedDescription,NashiError.cancelled.localizedDescription) }
        XCTAssertTrue(publicationReached.isCancelled)
        XCTAssertEqual(try FileManager.default.contentsOfDirectory(atPath:destination.path),["Keep.txt"])
        XCTAssertEqual(try Data(contentsOf:sentinel),sentinelBytes)
        XCTAssertEqual(try Data(contentsOf:encrypted),before)
    }

    /// Rejects nonfinite/empty crops and invalid source references without history changes or published outputs.
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
    /// Checks redo invalidation after branching history and reset cleanup of selection, sources and dirty state.
    func testNewEditClearsRedoAndResetClearsSelectionHistoryAndSources()throws {
        let url=try source("Reset")
        var editor=PDFPageEditorState();editor.reset(document:try PDFEngine.open(url),sourceURL:url)
        editor.select(0);editor.rotateSelected();editor.undo();editor.insertBlank();XCTAssertFalse(editor.canRedo)
        editor.reset();XCTAssertFalse(editor.canUndo);XCTAssertFalse(editor.canRedo);XCTAssertFalse(editor.hasSelection);XCTAssertTrue(editor.sourceURLs.isEmpty);XCTAssertFalse(editor.isDirty)
    }
}
