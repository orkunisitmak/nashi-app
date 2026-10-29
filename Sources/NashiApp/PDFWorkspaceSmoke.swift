import AppKit
import SwiftUI
import PDFKit
import NashiCore

/// Uses only a disposable PDF and this process's views/events. No global event access.
@MainActor final class PDFWorkspaceSmoke:NSObject,NSApplicationDelegate {
    let output:URL
    private var checks:[String]=[]
    private var window:ToolPanel?
    init(output:URL) { self.output=output }
    func applicationDidFinishLaunching(_ notification:Notification) {
        Task {
            var failure:String?
            do { try await run() }catch{failure=error.localizedDescription}
            let report:[String:Any]=["passed":failure == nil,"checks":checks,"error":failure as Any? ?? NSNull()]
            if let data=try? JSONSerialization.data(withJSONObject:report,options:[.prettyPrinted,.sortedKeys]) { try? data.write(to:output.appendingPathComponent("pdf-workspace.json")) }
            window?.shouldClose=nil;window?.close();exit(failure == nil ? 0:1)
        }
    }
    private func check(_ value:Bool,_ label:String)throws {
        guard value else{throw NSError(domain:"PDF workspace smoke",code:1,userInfo:[NSLocalizedDescriptionKey:label])};checks.append(label)
    }
    private func run()async throws {
        try FileManager.default.createDirectory(at:output,withIntermediateDirectories:true)
        let source=output.appendingPathComponent("Source.pdf"),document=PDFDocument()
        for index in 0..<5 { let page=PDFPage();page.setBounds(CGRect(x:0,y:0,width:300+index*20,height:440),for:.mediaBox);document.insert(page,at:index) }
        guard document.write(to:source) else{throw NashiError.failed("Could not create smoke PDF")}
        let original=try Data(contentsOf:source),fixture=Fixture()
        fixture.editor.reset(document:try PDFEngine.open(source),sourceURL:source)
        func event(_ key:UInt16,_ text:String="",flags:NSEvent.ModifierFlags=[])->NSEvent {
            NSEvent.keyEvent(with:.keyDown,location:.zero,modifierFlags:flags,timestamp:0,windowNumber:0,context:nil,characters:text,charactersIgnoringModifiers:text,isARepeat:false,keyCode:key)!
        }
        func route(_ event:NSEvent,responder:NSResponder?=nil)->Bool {
            guard let command=EditorKeyboardCommand.decode(event) else{return false}
            return command.route(event,responder:responder){PDFEditorKeyboardRouter.perform($0,editor:&fixture.editor)} == nil
        }
        fixture.editor.select(1)
        try check(route(event(124,flags:.shift)) && fixture.editor.selectedIndexes == [1,2],"Shift-Right extends the PDF selection")
        try check(route(event(124,flags:.shift)) && fixture.editor.selectedIndexes == [1,2,3],"Repeated Shift-Right extends from the focused page")
        let field=NSTextView();field.isEditable=true
        try check(!route(event(51),responder:field) && fixture.editor.pages.count==5,"Delete in editable text does not remove PDF pages")
        try check(route(event(51)) && fixture.editor.sourceIndexes == [0,4],"Mac Delete removes every selected page in one operation")
        try check(route(event(6,"z",flags:.command)) && fixture.editor.pages.count==5,"Command-Z restores all deleted pages and their selection")
        try check(route(event(6,"z",flags:[.command,.shift])) && fixture.editor.pages.count==2,"Command-Shift-Z repeats the batch deletion")
        try check(route(event(0,"a",flags:.command)) && fixture.editor.selectedIndexes == [0,1],"Command-A selects all PDF pages")
        try check(route(event(117)) && fixture.editor.pages.count==2,"Forward Delete cannot remove the last remaining pages")
        fixture.editor.select(0)
        try check(route(event(117),responder:NSButton()) && fixture.editor.pages.count==1,"Forward Delete works while a toolbar button is focused")
        _=route(event(6,"z",flags:.command));fixture.editor.select(0)
        let panel=ToolPanel(contentRect:NSRect(x:120,y:120,width:780,height:760),styleMask:[.borderless,.closable,.resizable],backing:.buffered,defer:false)
        panel.isReleasedWhenClosed=false
        let suite="Nashi.PDFSmoke."+UUID().uuidString
        let defaults=UserDefaults(suiteName:suite)!
        defer{UserDefaults.standard.removePersistentDomain(forName:suite)}
        let appState=AppState(defaults:defaults);appState.inputs=[source]
        panel.contentView=NSHostingView(rootView:ToolEditor(state:appState,tool:.organizePDF).nashiLanguage().frame(width:780,height:760))
        window=panel;panel.makeKeyAndOrderFront(nil)
        try? await Task.sleep(nanoseconds:300_000_000)
        if let view=panel.contentView,let bitmap=view.bitmapImageRepForCachingDisplay(in:view.bounds) {
            view.cacheDisplay(in:view.bounds,to:bitmap)
            try bitmap.representation(using:.png,properties:[:])?.write(to:output.appendingPathComponent("pdf-workspace.png"))
            try check(bitmap.pixelsWide>0,"Production PDF workspace renders in its own native window")
        } else { try check(false,"Production PDF workspace must render") }
        panel.shouldClose={false};panel.performClose(nil)
        try check(panel.isVisible,"A rejected close request keeps the PDF workspace open")
        panel.shouldClose={true};panel.performClose(nil)
        try check(!panel.isVisible,"An accepted close request closes the PDF workspace")
        try check(try Data(contentsOf:source)==original,"Keyboard and workspace operations leave the source PDF unchanged")
    }
    @MainActor private final class Fixture:ObservableObject { @Published var editor=PDFPageEditorState() }
}
