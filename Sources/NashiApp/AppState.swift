import AppKit
import SwiftUI
import NashiCore

struct JobRecord:Codable,Identifiable {
    var id=UUID()
    var title:String
    var inputs:[String]
    var outputs:[String]
    var date=Date()
}

@MainActor final class AppState:NSObject,ObservableObject {
    @Published var inputs:[URL]=[]
    @Published var working=false
    @Published var progress=0.0
    @Published var progressIsDeterminate=false
    private var acceptsProgress=false
    @Published var progressLabel=""
    @Published var error:String?
    @Published var outputs:[URL]=[]
    @Published var activeTool:ToolKind?
    @Published var activeInputs:[URL]=[]
    @Published var history:[JobRecord]=[]
    @Published var libraryVisible=false
    @Published var libraryQuery=""
    @Published var selectedHistoryID:UUID?
    @Published var libraryFocusRequest=0
    let filePreview=FilePreview()
    var filteredHistory:[JobRecord] {history.filter{libraryQuery.isEmpty || AppLocalization.matches(([$0.title,AppLocalization.text($0.title,language:historyDefaults.string(forKey:"appLanguage") ?? "en")]+$0.outputs.map{URL(fileURLWithPath:$0).lastPathComponent}).joined(separator:" "),query:libraryQuery,language:historyDefaults.string(forKey:"appLanguage") ?? "en")}}
    func preview(_ job:JobRecord) {filePreview.show(job.outputs.map{URL(fileURLWithPath:$0)}.filter{FileManager.default.fileExists(atPath:$0.path)})}
    func navigateLibrary(_ direction:Int) {let rows=filteredHistory;guard !rows.isEmpty else{return};let old=rows.firstIndex{$0.id==selectedHistoryID};selectedHistoryID=rows[min(rows.count-1,max(0,(old ?? (direction>0 ? -1:1))+direction))].id}
    func pasteContent(_ pasteboard:NSPasteboard = .general) {
        guard !working else{return}
        do {let result=try ClipboardImport.read(pasteboard)
            if let link=result.link {onWebDownload?(link,.video);return}
            inputs=result.urls;outputs=[];error=nil;libraryVisible=false
            history.insert(JobRecord(title:result.label,inputs:[],outputs:result.urls.map(\.path)),at:0);history=Array(history.prefix(50))
            if let data=try? JSONEncoder().encode(history) {historyDefaults.set(data,forKey:"history")}
        }catch{self.error=error.localizedDescription}
    }
    var token=CancellationToken()
    var onOpenWheel:(()->Void)?
    var onChooseFiles:(()->Void)?
    var onSettings:(()->Void)?
    var onWebDownload:((URL?,WebDownloadMode)->Void)?
    var onWebDrop:(([URL])->Void)?
    @Published var webShortcutAvailable=false
    var onShowTool:((ToolKind)->Void)?
    var onEPUBOptions:(()->Void)?
    var onJobStarted:(()->Void)?
    var onSuccess:((SuccessNotice)->Void)?
    var toolWindows:[NSWindow]=[]
    var floatShelves:[FloatShelfController]=[]
    private var windowObservers:[ObjectIdentifier:NSObjectProtocol]=[:]
    private var sharingService:NSSharingService?

    private let historyDefaults:UserDefaults
    init(defaults:UserDefaults = .standard) {
        historyDefaults=defaults;super.init()
        // Decode records one by one so a single unreadable entry cannot erase the whole Library.
        if let data=historyDefaults.data(forKey:"history"),let items=try? JSONDecoder().decode([StoredJobRecord].self,from:data) { history=items.compactMap(\.record) }
    }
    private struct StoredJobRecord:Decodable {
        let record:JobRecord?
        init(from decoder:Decoder) throws { record=try? JobRecord(from:decoder) }
    }
    func retainToolWindow(_ window:NSWindow) {
        toolWindows.append(window);let id=ObjectIdentifier(window)
        windowObservers[id]=NotificationCenter.default.addObserver(forName:NSWindow.willCloseNotification,object:window,queue:.main) { [weak self] _ in Task { @MainActor in guard let self else { return };self.toolWindows.removeAll{ObjectIdentifier($0)==id};if let token=self.windowObservers.removeValue(forKey:id) { NotificationCenter.default.removeObserver(token) } } }
    }
    func select(_ operation:NashiCore.Operation,options:NashiCore.OperationOptions? = nil,immediate:Bool=false) {
        var settings=options ?? OperationOptions()
        if options == nil,case .convert(let format)=operation { settings=FormatPreferences.shared.options(for:format) }
        if case .convert("epub")=operation,inputs.allSatisfy({FileCategory.detect($0) == .pdf}),!immediate { onEPUBOptions?();return }
        if case .tool(.removeMetadata)=operation,inputs.count>1 { execute(operation,options:settings);return }
        if case .tool(let kind)=operation,!immediate,!([.muteVideo,.extractArchive,.createPDF,.splitPDF].contains(kind)) {
            onShowTool?(kind);return
        }
        execute(operation,options:settings)
    }
    func execute(_ operation:NashiCore.Operation,options:NashiCore.OperationOptions = .init(),inputs:[URL]?=nil) {
        guard !working else { return }
        let selected=inputs ?? self.inputs
        beginJob(operation.title)
        let cancel=token
        activeInputs=selected;if case .tool(let tool)=operation { activeTool=tool } else { activeTool=nil }
        var options=options;options.revealInFinder=UserDefaults.standard.bool(forKey:"reveal")
        let request=ConversionRequest(inputs:selected,operation:operation,options:options)
        Task { [self] in
            // A deferred update may restart the app as soon as working becomes false.
            // Keep the job active until its outputs and recent history are committed.
            defer { finishJob() }
            do {
                let result=try await Task.detached(priority:.userInitiated) {
                    try withGrantedFolderAccess(request.inputs + [request.options.outputDirectory].compactMap{$0}) {
                        try ConversionEngine.run(request,token:cancel) { [weak self] value,label in
                            Task { @MainActor in self?.receiveProgress(value,label:label,token:cancel) }
                        }
                    }
                }.value
                stopProgressUpdates();outputs=result;progress=1
                let record=JobRecord(title:operation.title,inputs:selected.map(\.path),outputs:result.map(\.path))
                history.insert(record,at:0);history=Array(history.prefix(50))
                if let data=try? JSONEncoder().encode(history) { historyDefaults.set(data,forKey:"history") }
                onSuccess?(.filesReady(result))
                if options.revealInFinder { NSWorkspace.shared.activateFileViewerSelecting(result) }
            } catch { stopProgressUpdates();if !cancel.isCancelled { self.error=error.localizedDescription } }
        }
    }
    func beginJob(_ label:String) {
        token=CancellationToken();acceptsProgress=true
        error=nil;outputs=[];progress=0;progressIsDeterminate=false;progressLabel=label
        activeTool=nil;activeInputs=[];working=true
        onJobStarted?()
    }
    func receiveProgress(_ value:Double,label:String,token:CancellationToken) {
        guard working,acceptsProgress,self.token === token,!token.isCancelled else { return }
        progressIsDeterminate=value.isFinite && value>=0
        if progressIsDeterminate { progress=min(0.99,max(0,value)) }
        progressLabel=label
    }
    func stopProgressUpdates() { acceptsProgress=false }
    func finishJob() { stopProgressUpdates();working=false }

    func perform(_ action:FileAction,urls:[URL]?=nil) {
        let urls=urls ?? inputs
        guard !urls.isEmpty else { return }
        switch action {
        case .copyPath:
            NSPasteboard.general.clearContents()
            if NSPasteboard.general.setString(urls.map(\.path).joined(separator:"\n"),forType:.string) { reportSuccess(.action("File paths copied.",files:urls)) }
            else { error="Could not copy the selected file paths." }
        case .copy:
            NSPasteboard.general.clearContents()
            if NSPasteboard.general.writeObjects(urls as [NSURL]) { reportSuccess(.action("Files copied.",files:urls)) }
            else { error="Could not copy the selected files." }
        case .airDrop:
            if let service=NSSharingService(named:.sendViaAirDrop),service.canPerform(withItems:urls) { sharingService=service;service.delegate=self;service.perform(withItems:urls) }
            else { error="AirDrop is unavailable on this Mac." }
        case .move:
            let panel=NSOpenPanel();panel.canChooseFiles=false;panel.canChooseDirectories=true;panel.canCreateDirectories=true;panel.prompt="Move Here"
            NSApp.activate(ignoringOtherApps:true)
            if panel.runModal() == .OK,let directory=panel.url {
                var moved:[URL:URL]=[:]
                do { for source in urls {
                    var target=directory.appendingPathComponent(source.lastPathComponent)
                    if target.standardizedFileURL == source.standardizedFileURL || sameFolder(source.deletingLastPathComponent(),directory) { continue }
                    // Same names as OutputFiles.unique, but attributesOfItem does not follow links, so a dangling link also takes a name.
                    var index=2
                    while (try? FileManager.default.attributesOfItem(atPath:target.path)) != nil { target=directory.appendingPathComponent("\(source.deletingPathExtension().lastPathComponent) \(index)").appendingPathExtension(source.pathExtension.lowercased());index+=1 }
                    try FileManager.default.moveItem(at:source,to:target);moved[source]=target
                };reportSuccess(.action("Files moved.",files:urls.map{moved[$0] ?? $0})) } catch { self.error=error.localizedDescription }
                inputs=inputs.map{moved[$0] ?? $0}
                // Keep Library records pointing at files Nashi itself moved.
                let movedPaths=Dictionary(moved.map{($0.key.path,$0.value.path)},uniquingKeysWith:{first,_ in first})
                if !movedPaths.isEmpty {
                    history=history.map{var record=$0;record.outputs=record.outputs.map{movedPaths[$0] ?? $0};return record}
                    if let data=try? JSONEncoder().encode(history) { historyDefaults.set(data,forKey:"history") }
                }
            }
        case .terminal:
            let directory=((try? urls[0].resourceValues(forKeys:[.isDirectoryKey]).isDirectory) ?? false) ? urls[0] : urls[0].deletingLastPathComponent()
            // Terminal's Services integration opens the chosen folder without shell interpolation.
            let preferred=UserDefaults.standard.string(forKey:"terminalApp") ?? "Terminal"
            let app=preferred=="iTerm2" ? NSWorkspace.shared.urlForApplication(withBundleIdentifier:"com.googlecode.iterm2"):URL(fileURLWithPath:"/System/Applications/Utilities/Terminal.app")
            if let app {
                NSWorkspace.shared.open([directory],withApplicationAt:app,configuration:NSWorkspace.OpenConfiguration()) { [weak self] _,error in
                    Task { @MainActor in
                        if let error { self?.error=error.localizedDescription }
                        else { self?.reportSuccess(.action("Opened in \(preferred).",files:[directory])) }
                    }
                }
            }
            else { error="\(preferred) is not installed." }
        case .float:
            if UserDefaults.standard.string(forKey:"floatWheelBehavior")=="addToExisting",let existing=floatShelves.reversed().first(where:{$0.window?.isVisible == true}) {
                existing.append(urls);existing.showWindow(nil);reportSuccess(.action("Added to Float.",files:urls));return
            }
            let shelf=FloatShelfController(urls:urls)
            shelf.onClose={ [weak self,weak shelf] in guard let shelf else { return };self?.floatShelves.removeAll{$0 === shelf} }
            floatShelves.append(shelf);shelf.showWindow(nil);reportSuccess(.action("Added to Float.",files:urls))
        }
    }
    func reportSuccess(_ notice:SuccessNotice) { error=nil;onSuccess?(notice) }
    func recordWebDownload(_ files:[URL],site:String) {
        history.insert(JobRecord(title:"Download · \(site)",inputs:files.map(\.path),outputs:files.map(\.path)),at:0)
        history=Array(history.prefix(50))
        if let data=try? JSONEncoder().encode(history) { historyDefaults.set(data,forKey:"history") }
        reportSuccess(.filesReady(files))
    }
}

// Compares folder identity, so a symlinked path or a missing trailing slash still matches.
private func sameFolder(_ a:URL,_ b:URL)->Bool {
    guard let x=try? a.resolvingSymlinksInPath().resourceValues(forKeys:[.fileResourceIdentifierKey]).fileResourceIdentifier,
          let y=try? b.resolvingSymlinksInPath().resourceValues(forKeys:[.fileResourceIdentifierKey]).fileResourceIdentifier else { return false }
    return x.isEqual(y)
}

private func withGrantedFolderAccess<T>(_ urls:[URL],_ action:() throws -> T) throws -> T {
    try FolderAccessPreferences.withAccess(to:urls,action)
}

extension AppState:NSSharingServiceDelegate {
    nonisolated func sharingService(_ sharingService:NSSharingService,didShareItems items:[Any]) {
        let files=items.compactMap{$0 as? URL}
        Task { @MainActor [weak self] in self?.sharingService=nil;self?.reportSuccess(.action("Shared with AirDrop.",files:files)) }
    }
    nonisolated func sharingService(_ sharingService:NSSharingService,didFailToShareItems items:[Any],error:Error) {
        let description=(error as NSError).code == NSUserCancelledError ? nil:error.localizedDescription
        Task { @MainActor [weak self] in self?.error=description;self?.sharingService=nil }
    }
}

enum FileAction:String,CaseIterable {
    case airDrop,float,copy,move,copyPath,terminal
    var title:String { switch self { case .airDrop:return "AirDrop";case .float:return "Float";case .copy:return "Copy";case .move:return "Move";case .copyPath:return "Copy Path";case .terminal:return "Terminal" } }
    var symbol:String { switch self { case .airDrop:return "dot.radiowaves.left.and.right";case .float:return "rectangle.on.rectangle";case .copy:return "doc.on.doc";case .move:return "folder";case .copyPath:return "link";case .terminal:return "terminal" } }
}
