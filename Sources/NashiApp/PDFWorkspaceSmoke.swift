import AppKit
import SwiftUI
import PDFKit
import NashiCore
import NashiUpdates

/// Uses only a disposable PDF and this process's views/events. No global event access.
@MainActor final class PDFWorkspaceSmoke:NSObject,NSApplicationDelegate {
    let output:URL
    private var checks:[String]=[]
    private var window:ToolPanel?
    init(output:URL) { self.output=output }
    /// Runs disposable own-window checks and writes a machine-readable report before terminating the QA process.
    func applicationDidFinishLaunching(_ notification:Notification) {
        Task {
            var failure:String?
            do { try await run() }catch{failure=error.localizedDescription}
            let report:[String:Any]=["passed":failure == nil,"checks":checks,"error":failure as Any? ?? NSNull()]
            if let data=try? JSONSerialization.data(withJSONObject:report,options:[.prettyPrinted,.sortedKeys]) { try? data.write(to:output.appendingPathComponent("pdf-workspace.json")) }
            window?.shouldClose=nil;window?.close();exit(failure == nil ? 0:1)
        }
    }
    /// Records a named invariant or fails the smoke run with the corresponding diagnostic.
    private func check(_ value:Bool,_ label:String)throws {
        guard value else{throw NSError(domain:"PDF workspace smoke",code:1,userInfo:[NSLocalizedDescriptionKey:label])};checks.append(label)
    }
    /// Exercises production shortcut routing, text-field protection, batch history and native panel close interception.
    private func run()async throws {
        try FileManager.default.createDirectory(at:output,withIntermediateDirectories:true)
        let source=output.appendingPathComponent("Source.pdf"),document=PDFDocument()
        for index in 0..<5 { let page=PDFPage();page.setBounds(CGRect(x:0,y:0,width:300+index*20,height:440),for:.mediaBox);document.insert(page,at:index) }
        guard document.write(to:source) else{throw NashiError.failed("Could not create smoke PDF")}
        let original=try Data(contentsOf:source),fixture=Fixture()
        fixture.editor.reset(document:try PDFEngine.open(source),sourceURL:source)
        /// Builds a local synthetic key event without posting global keyboard input.
        func event(_ key:UInt16,_ text:String="",flags:NSEvent.ModifierFlags=[])->NSEvent {
            NSEvent.keyEvent(with:.keyDown,location:.zero,modifierFlags:flags,timestamp:0,windowNumber:0,context:nil,characters:text,charactersIgnoringModifiers:text,isARepeat:false,keyCode:key)!
        }
        /// Replays one event through the same decoder and PDF command router used by the production editor.
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
        let closeItem=NSMenuItem(title:"Close Window",action:#selector(NSWindow.performClose(_:)),keyEquivalent:"w")
        try check(panel.validateUserInterfaceItem(closeItem),"Close Window remains enabled for a closable borderless panel")
        var ownerCloseRequests=0
        panel.onPerformClose={ownerCloseRequests+=1};panel.performClose(nil)
        try check(ownerCloseRequests==1 && panel.isVisible,"Close Window respects the owner's custom close flow")
        panel.onPerformClose=nil
        panel.shouldClose={false};panel.performClose(nil)
        try check(panel.isVisible,"A rejected close request keeps the PDF workspace open")
        let delegate=AppDelegate(state:appState,updater:AppUpdater(credentials:NoCredentials(),defaults:defaults,info:[:]))
        appState.toolWindows=[panel]
        try check(delegate.applicationShouldTerminate(NSApp) == .terminateCancel && panel.isVisible,"Application exit consults the PDF discard guard and respects Keep editing")
        panel.shouldClose={true}
        try check(delegate.applicationShouldTerminate(NSApp) == .terminateNow && panel.isVisible,"Accepting PDF discard allows application exit without prematurely closing the editor")
        var closeRequests=0
        panel.shouldClose={closeRequests+=1;return true}
        appState.working=true
        try check(delegate.applicationShouldTerminate(NSApp) == .terminateCancel && closeRequests==0,"Active conversion blocks application exit before consulting PDF editors")
        appState.working=false;delegate.toolSetupWorking=true
        try check(delegate.applicationShouldTerminate(NSApp) == .terminateCancel && closeRequests==0,"Active tool setup blocks application exit before consulting PDF editors")
        delegate.toolSetupWorking=false;appState.toolWindows=[]
        panel.shouldClose={true};panel.performClose(nil)
        try check(!panel.isVisible,"An accepted close request closes the PDF workspace")
        try check(try Data(contentsOf:source)==original,"Keyboard and workspace operations leave the source PDF unchanged")
    }
    @MainActor private final class Fixture:ObservableObject { @Published var editor=PDFPageEditorState() }
    /// An accidental updater credential request fails locally without touching a Keychain or broker.
    private struct NoCredentials:UpdateCredentialStore {
        func token(for repository:GitHubRepository)throws->String {throw UpdateCredentialError.missing}
        func save(_ token:String,for repository:GitHubRepository)throws {throw UpdateCredentialError.missing}
        func remove(for repository:GitHubRepository)throws {throw UpdateCredentialError.missing}
    }
}
