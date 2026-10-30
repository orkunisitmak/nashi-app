import AppKit
import SwiftUI
import NashiCore
import ServiceManagement
import Carbon
import Combine
import NashiUpdates
import NashiCore
import ApplicationServices

@main
enum NashiApplication {
    @MainActor static func main() {
        if CommandLine.arguments.count>1,["convert","tool","inspect","catalog","dependencies"].contains(CommandLine.arguments[1]) { exit(NashiCommandLine.run()) }
        let app=NSApplication.shared
        if CommandLine.arguments.contains("--ui-audit") {
            do {
                let runner=UIAudit(options:try UIAuditOptions(arguments:CommandLine.arguments))
                app.delegate=runner;app.setActivationPolicy(.accessory)
                withExtendedLifetime(runner){app.run()}
            }catch {fputs(error.localizedDescription+"\n",stderr);exit(64)}
            return
        }
        if let index=CommandLine.arguments.firstIndex(of:"--range-accessibility-smoke"),CommandLine.arguments.count>index+1 {
            let runner=MediaRangeAccessibilitySmoke(output:URL(fileURLWithPath:CommandLine.arguments[index+1],isDirectory:true))
            app.delegate=runner;app.setActivationPolicy(.accessory)
            withExtendedLifetime(runner){app.run()};return
        }
        if let index=CommandLine.arguments.firstIndex(of:"--file-artwork-smoke"),CommandLine.arguments.count>index+1 {
            let runner=FileArtworkSmokeRunner(output:URL(fileURLWithPath:CommandLine.arguments[index+1],isDirectory:true))
            app.delegate=runner;app.setActivationPolicy(.accessory)
            withExtendedLifetime(runner){app.run()};return
        }
        if let index=CommandLine.arguments.firstIndex(of:"--float-smoke"),CommandLine.arguments.count>index+1 {
            let runner=FloatShelfSmoke(output:URL(fileURLWithPath:CommandLine.arguments[index+1],isDirectory:true));app.delegate=runner;app.setActivationPolicy(.accessory)
            withExtendedLifetime(runner){app.run()};return
        }
        if let index=CommandLine.arguments.firstIndex(of:"--tool-setup-smoke"),CommandLine.arguments.count>index+1 {
            let runner=ToolSetupSmoke(output:URL(fileURLWithPath:CommandLine.arguments[index+1],isDirectory:true));app.delegate=runner;app.setActivationPolicy(.accessory)
            withExtendedLifetime(runner){app.run()};return
        }
        if let index=CommandLine.arguments.firstIndex(of:"--localization-smoke"),CommandLine.arguments.count>index+1 {
            let runner=LocalizationSmoke(output:URL(fileURLWithPath:CommandLine.arguments[index+1],isDirectory:true));app.delegate=runner;app.setActivationPolicy(.accessory)
            withExtendedLifetime(runner){app.run()};return
        }
        if CommandLine.arguments.contains("--window-management-status") {
            let status:[String:Any]=["bundleIdentifier":Bundle.main.bundleIdentifier ?? "", "displayName":Bundle.main.object(forInfoDictionaryKey:"CFBundleDisplayName") as? String ?? "", "accessibilityTrusted":AXIsProcessTrusted()]
            if let data=try? JSONSerialization.data(withJSONObject:status,options:[.sortedKeys]),let text=String(data:data,encoding:.utf8) { print(text) }
            return
        }
        if let index=CommandLine.arguments.firstIndex(of:"--window-management-smoke"),CommandLine.arguments.count>index+2,
           let managerPID=Int32(CommandLine.arguments[index+1]) {
            let runner=WindowManagementSmokeDelegate(managerPID:managerPID,output:URL(fileURLWithPath:CommandLine.arguments[index+2],isDirectory:true))
            app.delegate=runner;app.setActivationPolicy(.accessory)
            withExtendedLifetime(runner) { app.run() };return
        }
        if CommandLine.arguments.contains("--window-management-fixture") {
            let fixture=WindowManagementFixtureDelegate();app.delegate=fixture;app.setActivationPolicy(.accessory)
            withExtendedLifetime(fixture) { app.run() };return
        }
        if let index=CommandLine.arguments.firstIndex(of:"--desktop-parity-smoke"),CommandLine.arguments.count>index+1 {
            let runner=DesktopParitySmoke(output:URL(fileURLWithPath:CommandLine.arguments[index+1],isDirectory:true));app.delegate=runner;app.setActivationPolicy(.accessory)
            withExtendedLifetime(runner){app.run()};return
        }
        let delegate=AppDelegate()
        app.delegate=delegate
        app.setActivationPolicy(.accessory)
        withExtendedLifetime(delegate) { app.run() }
    }
}

@MainActor private final class FileArtworkSmokeRunner:NSObject,NSApplicationDelegate {
    let output:URL
    init(output:URL){self.output=output}
    func applicationDidFinishLaunching(_ notification:Notification) {
        Task {
            do {
                let report=try await FileArtworkSmoke.run(output:output)
                print("FILE_ARTWORK PASS \(report["passed"] ?? 0)");exit(0)
            }catch{fputs(error.localizedDescription+"\n",stderr);exit(1)}
        }
    }
}

/// An explicit test-only invocation supplies a disposable, resizable cross-process target.
@MainActor private final class WindowManagementFixtureDelegate:NSObject,NSApplicationDelegate {
    private var window:NSWindow?
    func applicationDidFinishLaunching(_ notification:Notification) {
        let w=NSWindow(contentRect:NSRect(x:220,y:200,width:620,height:420),styleMask:[.titled,.closable,.resizable],backing:.buffered,defer:false)
        let key=CommandLine.arguments.firstIndex(of:"--minimum-window-width")
        let minimum=key.flatMap{$0+1<CommandLine.arguments.count ? Double(CommandLine.arguments[$0+1]):nil} ?? 160
        w.title="Nashi Window Layout Fixture";w.minSize=NSSize(width:min(1200,max(160,minimum)),height:120);w.isReleasedWhenClosed=false
        let label=NSTextField(labelWithString:"Nashi window arrangement test fixture")
        label.font=NSFont.systemFont(ofSize:20,weight:.medium);label.frame=NSRect(x:26,y:300,width:560,height:40)
        w.contentView?.addSubview(label);w.makeKeyAndOrderFront(nil);window=w
    }
    func applicationShouldTerminateAfterLastWindowClosed(_ sender:NSApplication)->Bool { true }
}

@MainActor final class AppDelegate: NSObject, NSApplicationDelegate, NSMenuDelegate {
    let state=AppState()
    let updater=AppUpdater()
    private var updateObservations=Set<AnyCancellable>()
    var statusItem:NSStatusItem!
    var home:NSWindow?
    private var homeKeyboardMonitor:Any?
    var settings:NSWindow?
    var updateConnection:NSWindow?
    private let progressToast=ProgressToastController()
    var toolSetupWorking=false
    var toolSetupWindow:NSWindow?
    private var toolSetupOffered=false
    var downloadWindow:NSWindow?
    lazy var downloadModel=WebDownloadModel(state:state)
    private var downloadHotKey:EventHotKeyRef?
    private var downloadHotKeyHandler:EventHandlerRef?
    private let successToast=SuccessToastController()
    var welcome:NSWindow?
    var monitor:DragMonitor!
    var wheel:WheelController!
    var windowManagement:WindowManagementController!

    func applicationDidFinishLaunching(_ notification:Notification) {
        NashiAppearance.apply()
        UserDefaults.standard.register(defaults:["enabled":true,"reveal":true,"sound":true,"convertKey":"shift","toolsKey":"command","actionsKey":"control","wheelScale":1.0,"windowManagementEnabled":true])
        let mainMenu=NSMenu();let appMenu=NSMenu()
        appMenu.addItem(withTitle:"Home",action:#selector(showHome),keyEquivalent:"1")
        appMenu.addItem(withTitle:"Library",action:#selector(showLibrary),keyEquivalent:"2")
        appMenu.addItem(withTitle:"Find in Library…",action:#selector(findInLibrary),keyEquivalent:"f")
        appMenu.addItem(withTitle:"Choose Files…",action:#selector(chooseFiles),keyEquivalent:"o")
        appMenu.addItem(withTitle:"Download Link…",action:#selector(showDownloadLink),keyEquivalent:"")
        appMenu.addItem(withTitle:"Settings…",action:#selector(showSettings),keyEquivalent:",")
        appMenu.addItem(withTitle:"Welcome…",action:#selector(showWelcome),keyEquivalent:"")
        appMenu.addItem(withTitle:"Check for Updates…",action:#selector(checkForUpdates),keyEquivalent:"u")
        appMenu.addItem(withTitle:"Update Connection…",action:#selector(showUpdateConnection),keyEquivalent:"")
        appMenu.addItem(.separator());appMenu.addItem(withTitle:"Quit Nashi",action:#selector(quit),keyEquivalent:"q")
        for item in appMenu.items { item.target=self;if item.action == #selector(showDownloadLink) { item.keyEquivalent="d";item.keyEquivalentModifierMask=[.control,.shift] } };let appItem=NSMenuItem();appItem.submenu=appMenu;mainMenu.addItem(appItem)
        let edit=NSMenu(title:"Edit")
        for (title,key,selector) in [("Undo","z","undo:"),("Redo","Z","redo:"),("Cut","x","cut:"),("Copy","c","copy:"),("Paste","v","paste:"),("Select All","a","selectAll:")] { edit.addItem(withTitle:title,action:Selector(selector),keyEquivalent:key) }
        let editItem=NSMenuItem(title:"Edit",action:nil,keyEquivalent:"");editItem.submenu=edit;mainMenu.addItem(editItem);NSApp.mainMenu=mainMenu
        let windowMenu=NSMenu(title:"Window");windowMenu.addItem(withTitle:"Close Window",action:#selector(NSWindow.performClose(_:)),keyEquivalent:"w");let windowItem=NSMenuItem(title:"Window",action:nil,keyEquivalent:"");windowItem.submenu=windowMenu;mainMenu.addItem(windowItem);NSApp.windowsMenu=windowMenu
        let menu=NSMenu()
        menu.addItem(withTitle:"Nashi",action:#selector(showHome),keyEquivalent:"")
        menu.addItem(NSMenuItem.separator())
        menu.addItem(withTitle:"Choose Files…",action:#selector(chooseFiles),keyEquivalent:"o")
        menu.addItem(withTitle:"Download Link…",action:#selector(showDownloadLink),keyEquivalent:"")
        menu.addItem(withTitle:"Enable Nashi",action:#selector(toggleEnabled),keyEquivalent:"").state = UserDefaults.standard.bool(forKey:"enabled") ? .on : .off
        menu.addItem(withTitle:"Settings…",action:#selector(showSettings),keyEquivalent:",")
        menu.addItem(withTitle:"Welcome…",action:#selector(showWelcome),keyEquivalent:"")
        menu.addItem(withTitle:"Recent Conversions",action:#selector(showHome),keyEquivalent:"")
        menu.addItem(withTitle:"Check for Updates…",action:#selector(checkForUpdates),keyEquivalent:"")
        menu.addItem(withTitle:"Update Connection…",action:#selector(showUpdateConnection),keyEquivalent:"")
        menu.addItem(NSMenuItem.separator())
        menu.addItem(withTitle:"Quit Nashi",action:#selector(quit),keyEquivalent:"q")
        for item in menu.items { item.target=self }
        statusItem=NSStatusBar.system.statusItem(withLength:NSStatusItem.squareLength)
        statusItem.button?.image=NashiArtwork.menuIcon()
        statusItem.menu=menu;menu.delegate=self
        appMenu.delegate=self
        L10n.localize(mainMenu);L10n.localize(menu)
        NotificationCenter.default.publisher(for:.nashiLanguageChanged).sink { _ in L10n.localize(NSApp.mainMenu);L10n.localize(menu);L10n.updateWindowTitles() }.store(in:&updateObservations)
        state.$working.sink { [weak self] working in guard let self else{return};self.updater.setBusy(working || self.toolSetupWorking) }.store(in:&updateObservations)
        updater.$availableVersion.sink { [weak self] version in
            guard let self else { return }
            let title=version.map { "Update to \($0)…" } ?? "Check for Updates…"
            for updateMenu in [appMenu,menu] { updateMenu.items.first { $0.action == #selector(self.checkForUpdates) } .map { L10n.menuTitle($0,source:title) } }
            self.statusItem.button?.image=NashiArtwork.menuIcon(updateAvailable:version != nil)
        }.store(in:&updateObservations)
        for name in [NSApplication.didResignActiveNotification,NSApplication.didBecomeActiveNotification,NSWindow.didMiniaturizeNotification,NSWindow.willCloseNotification] {
            NotificationCenter.default.publisher(for:name).sink { [weak self] _ in
                DispatchQueue.main.async { self?.showProgress() }
            }.store(in:&updateObservations)
        }
        updater.start()
        NotificationCenter.default.publisher(for:.showUpdateConnection).sink { [weak self] _ in self?.showUpdateConnection() }.store(in:&updateObservations)
        NotificationCenter.default.publisher(for:.showToolSetup).sink { [weak self] _ in self?.showToolSetup() }.store(in:&updateObservations)
        wheel=WheelController(state:state)
        monitor=DragMonitor(onEnd:{ [weak self] in self?.wheel.endDrag() }) { [weak self] urls,mode,point in self?.wheel.show(inputs:urls,mode:mode,at:point) }
        windowManagement=WindowManagementController(fileWheelIsVisible:{ [weak self] in self?.wheel.panel?.isVisible == true })
        windowManagement.onNotice = { message in
            let alert=NSAlert();alert.messageText="Window arrangement";alert.informativeText=message
            alert.addButton(withTitle:"OK");alert.runModal()
        }
        state.onOpenWheel = { [weak self] in self?.wheel.show(inputs:self?.state.inputs ?? [],mode:.tools,at:NSEvent.mouseLocation,keyboard:true) }
        state.onWebDownload = { [weak self] url,mode in self?.showWebDownload(url,mode:mode) }
        state.onWebDrop = { [weak self] urls in self?.wheel.show(inputs:urls,mode:.tools,at:NSEvent.mouseLocation,keyboard:true) }
        registerDownloadShortcut()
        homeKeyboardMonitor=NSEvent.addLocalMonitorForEvents(matching:.keyDown) { [weak self] event in
            guard let self,event.window===self.home,self.home?.isKeyWindow==true else{return event}
            return self.state.handleHomeKey(event,window:self.home!) ? nil:event
        }
        state.onChooseFiles = { [weak self] in self?.chooseFiles() }
        state.onSettings = { [weak self] in self?.showSettings() }
        state.onShowTool = { [weak self] tool in self?.showTool(tool) }
        state.onEPUBOptions = { [weak self] in guard let self else { return };let window=self.makeWindow(title:"Convert to EPUB",size:NSSize(width:530,height:390),view:EPUBOptionsView(state:self.state,inputs:self.state.inputs));self.state.retainToolWindow(window);window.makeKeyAndOrderFront(nil);NSApp.activate(ignoringOtherApps:true) }
        state.onJobStarted = { [weak self] in self?.successToast.dismiss();self?.showProgress() }
        state.onSuccess = { [weak self] notice in self?.progressToast.dismiss();self?.successToast.show(notice) }
        let loginLaunch=NSAppleEventManager.shared().currentAppleEvent?.paramDescriptor(forKeyword:keyAELaunchedAsLogInItem)?.booleanValue ?? false
        if !loginLaunch,!CommandLine.arguments.contains("--background") {
            if UserDefaults.standard.bool(forKey:"hasCompletedWelcome") { showHome() } else { showWelcome() }
        }
        UserDefaults.standard.set(true,forKey:"hasOpened")
        if let index=CommandLine.arguments.firstIndex(of:"--open"), index+1<CommandLine.arguments.count {
            state.inputs=CommandLine.arguments[(index+1)...].map{URL(fileURLWithPath:$0)};showHome()
        }
    }
    @objc func showLibrary() {showHome();state.libraryVisible=true;state.selectedHistoryID=state.history.first?.id}
    @objc func findInLibrary() {showLibrary();state.libraryFocusRequest+=1}
    @objc func showHome() {
        state.libraryVisible=false
        progressToast.dismiss()
        if home == nil { home=makeWindow(title:"Nashi",size:NSSize(width:820,height:780),view:HomeView(state:state,updater:updater)) }
        home?.contentMinSize=NSSize(width:700,height:580)
        home?.makeKeyAndOrderFront(nil);NSApp.activate(ignoringOtherApps:true)
        if !toolSetupOffered, Bundle.main.object(forInfoDictionaryKey:"NashiExternalTools") as? Bool == true, !ExternalToolSetup.status.ready {
            showToolSetup()
        }
    }
    @objc func showToolSetup() {
        toolSetupOffered=true
        if toolSetupWindow == nil {
            let model=ToolSetupModel()
            model.onWorkingChange={ [weak self] working in guard let self else{return};self.toolSetupWorking=working;self.toolSetupWindow?.standardWindowButton(.closeButton)?.isEnabled = !working;self.updater.setBusy(working || self.state.working) }
            toolSetupWindow=makeWindow(title:"Set up Nashi tools",size:NSSize(width:646,height:690),view:ToolSetupView(model:model,onDone:{ [weak self] in
                guard ExternalToolSetup.status.ready else{return}
                UserDefaults.standard.set(true,forKey:"hasCompletedWelcome");self?.toolSetupWindow?.close();self?.showHome()
            }))
        }
        toolSetupWindow?.makeKeyAndOrderFront(nil);NSApp.activate(ignoringOtherApps:true)
    }
    @objc func showDownloadLink() { showWebDownload(WebLinkPayload.clipboardLink(),mode:.video) }
    func showWebDownload(_ url:URL?,mode:WebDownloadMode) {
        if downloadWindow == nil {
            downloadWindow=makeToolWindow(title:"Download from the web",size:NSSize(width:660,height:680),view:WebDownloadView(model:downloadModel,state:state))
            downloadWindow?.contentMinSize=NSSize(width:620,height:520)
            downloadModel.onUseFiles = { [weak self] in guard let self,!self.state.working else { return };self.state.inputs=self.downloadModel.completed;self.downloadWindow?.close();self.showHome() }
        }
        downloadModel.open(url,mode:mode)
        downloadWindow?.makeKeyAndOrderFront(nil);NSApp.activate(ignoringOtherApps:true)
    }
    private func registerDownloadShortcut() {
        var event=EventTypeSpec(eventClass:OSType(kEventClassKeyboard),eventKind:UInt32(kEventHotKeyPressed))
        let context=Unmanaged.passUnretained(self).toOpaque()
        let installed=InstallEventHandler(GetApplicationEventTarget(),{ _,_,context in
            guard let context else { return OSStatus(eventNotHandledErr) }
            let delegate=Unmanaged<AppDelegate>.fromOpaque(context).takeUnretainedValue()
            Task { @MainActor in delegate.showDownloadLink() }
            return noErr
        },1,&event,context,&downloadHotKeyHandler)
        if installed==noErr {
            let identifier=EventHotKeyID(signature:0x54474E44,id:1)
            state.webShortcutAvailable=RegisterEventHotKey(UInt32(kVK_ANSI_D),UInt32(controlKey|shiftKey),identifier,GetApplicationEventTarget(),0,&downloadHotKey)==noErr
        }
        downloadModel.shortcutAvailable=state.webShortcutAvailable
    }
    func applicationWillTerminate(_ notification:Notification) {
        windowManagement?.stop()
        // Previews and file inspection don't block quitting; their ffmpeg/ffprobe must not outlive Nashi.
        ProcessRunner.terminateAll()
        if let downloadHotKey { UnregisterEventHotKey(downloadHotKey) }
        if let downloadHotKeyHandler { RemoveEventHandler(downloadHotKeyHandler) }
    }
    @objc func chooseFiles() {
        guard !state.working else { return }
        NSApp.activate(ignoringOtherApps:true)
        let panel=NSOpenPanel();panel.allowsMultipleSelection=true;panel.canChooseDirectories=true;panel.prompt="Choose Files"
        if panel.runModal() == .OK { state.inputs=panel.urls;showHome() }
    }
    @objc func showSettings() {
        presentSettings(updates:false)
    }
    private func presentSettings(updates:Bool) {
        if settings == nil { settings=makeWindow(title:"Nashi Settings",size:NSSize(width:860,height:700),view:SettingsView(updater:updater,showingUpdates:updates)) }
        settings?.contentMinSize=NSSize(width:790,height:590)
        settings?.makeKeyAndOrderFront(nil);NSApp.activate(ignoringOtherApps:true)
        if updates { NotificationCenter.default.post(name:.showUpdateSettings,object:nil) }
    }
    @objc func showWelcome() {
        if welcome == nil {
            let panel=ToolPanel(contentRect:NSRect(x:0,y:0,width:760,height:520),styleMask:[.borderless,.closable],backing:.buffered,defer:false)
            panel.title="Welcome to Nashi";panel.isReleasedWhenClosed=false;panel.isOpaque=false;panel.backgroundColor = .clear;panel.hasShadow=true;panel.hidesOnDeactivate=false
            let view=WelcomeView(onDone:{ [weak self] in self?.finishWelcome() },onClose:{ [weak self] in self?.finishWelcome() },onMinimize:{ [weak self] in self?.welcome?.orderOut(nil) })
            panel.onPerformClose={ [weak self] in self?.finishWelcome() }
            panel.contentView=NSHostingView(rootView:view.nashiLanguage());panel.center();welcome=panel
        }
        welcome?.makeKeyAndOrderFront(nil);NSApp.activate(ignoringOtherApps:true)
    }
    private func finishWelcome() {
        welcome?.close();welcome=nil
        if ExternalToolSetup.status.ready { UserDefaults.standard.set(true,forKey:"hasCompletedWelcome") }
        showHome()
        if !ExternalToolSetup.status.ready { showToolSetup() }
    }
    @objc func toggleEnabled(_ sender:NSMenuItem) { let value = !UserDefaults.standard.bool(forKey:"enabled");UserDefaults.standard.set(value,forKey:"enabled");sender.state=value ? .on : .off }
    @objc func checkForUpdates() {
        if updater.isConfigured { updater.checkForUpdates() }
        else { showUpdateConnection() }
    }
    @objc func showUpdateConnection() {
        if updateConnection == nil { updateConnection=makeWindow(title:"Nashi Update Connection",size:NSSize(width:620,height:720),view:UpdateConnectionView(updater:updater)) }
        updateConnection?.contentMinSize=NSSize(width:560,height:570)
        updateConnection?.makeKeyAndOrderFront(nil);NSApp.activate(ignoringOtherApps:true)
    }
    func menuNeedsUpdate(_ menu:NSMenu) { menu.items.first(where:{$0.action == #selector(toggleEnabled)})?.state=UserDefaults.standard.bool(forKey:"enabled") ? .on:.off }
    @objc func quit() { NSApp.terminate(nil) }
    func showProgress() {
        guard state.working else { return }
        if NSApp.isActive && (state.toolWindows.contains(where:{$0.isVisible && !$0.isMiniaturized}) || (home?.isVisible == true && home?.isMiniaturized == false) || downloadWindow?.isVisible == true) { progressToast.dismiss();return }
        progressToast.show(state:state)
    }
    func showTool(_ kind:ToolKind) {
        progressToast.dismiss()
        if kind == .readQRCode {
            let window=makeToolWindow(title:kind.title,size:NSSize(width:590,height:620),view:QRCodeReaderView(inputs:state.inputs,onSuccess:{ [weak self] in self?.state.reportSuccess($0) }))
            state.retainToolWindow(window);window.makeKeyAndOrderFront(nil);NSApp.activate(ignoringOtherApps:true);return
        }
        let view=ToolEditor(state:state,tool:kind)
        let preview=[ToolKind.editImage,.annotateImage,.cropImage,.frameImage,.redactImage,.createCollage,.cropVideo,.trimVideo,.changeVideoSpeed,.splitVideo,.videoSnapshots,.redactVideo,.trimAudio,.normalizeAudio,.audioChannels,.redactAudio,.audioToVideo].contains(kind)
        let window=makeToolWindow(title:FeatureCatalog.toolNames[kind] ?? kind.title,size:NSSize(width:preview ? 1000:kind == .organizePDF ? 780:600,height:kind == .compress ? 630:760),view:view)
        state.retainToolWindow(window);window.makeKeyAndOrderFront(nil);NSApp.activate(ignoringOtherApps:true)
    }
    func application(_ application:NSApplication,open urls:[URL]) {
        if let web=urls.first(where:{!$0.isFileURL}),let valid=try? WebDownloadEngine.link(web.absoluteString) { showWebDownload(valid,mode:.video) }
        else { if !state.working { state.inputs=urls };showHome();state.onOpenWheel?() }
    }
    func applicationShouldTerminate(_ sender:NSApplication)->NSApplication.TerminateReply {
        // Sparkle can resume an earlier install without the postponement callback.
        // Never terminate a conversion before it has committed its results.
        (state.working || toolSetupWorking) ? .terminateCancel:.terminateNow
    }
    func applicationShouldHandleReopen(_ sender:NSApplication,hasVisibleWindows flag:Bool)->Bool { showHome();return true }
    func makeWindow<V:View>(title:String,size:NSSize,view:V)->NSWindow {
        let window=NSWindow(contentRect:NSRect(origin:.zero,size:size),styleMask:[.titled,.closable,.miniaturizable,.resizable,.fullSizeContentView],backing:.buffered,defer:false)
        L10n.windowTitle(window,source:title);window.titleVisibility = .hidden;window.titlebarAppearsTransparent=true;window.isReleasedWhenClosed=false;window.contentView=NSHostingView(rootView:view.tint(Color.nashi).nashiLanguage());window.center();fitToScreen(window);return window
    }
    func makeToolWindow<V:View>(title:String,size:NSSize,view:V)->NSWindow {
        let window=ToolPanel(contentRect:NSRect(origin:.zero,size:size),styleMask:[.borderless,.closable,.resizable],backing:.buffered,defer:false)
        L10n.windowTitle(window,source:title);window.isReleasedWhenClosed=false;window.isOpaque=false;window.backgroundColor = .clear;window.hasShadow=true;window.isMovableByWindowBackground=false;window.hidesOnDeactivate=false
        let hosting=NSHostingView(rootView:view.tint(Color.nashi).nashiLanguage());hosting.wantsLayer=true;hosting.layer?.cornerRadius=24;hosting.layer?.masksToBounds=true;window.contentView=hosting;window.center();fitToScreen(window);return window
    }
    private func fitToScreen(_ window:NSWindow) {
        guard let visible=(window.screen ?? NSScreen.main)?.visibleFrame.insetBy(dx:16,dy:16) else { return }
        let size=NSSize(width:min(window.frame.width,visible.width),height:min(window.frame.height,visible.height))
        window.setFrame(NSRect(x:visible.midX-size.width/2,y:visible.midY-size.height/2,width:size.width,height:size.height),display:false)
    }
}
extension AppDelegate:NSMenuItemValidation {
    func validateMenuItem(_ menuItem:NSMenuItem)->Bool {
        if menuItem.action == #selector(checkForUpdates) { return !updater.isConfigured || (updater.canCheckForUpdates && !updater.isBusy) }
        if menuItem.action == #selector(quit) { return !state.working }
        return true
    }
}
final class ToolPanel:NSPanel {
    override var canBecomeKey:Bool { true }
    override var canBecomeMain:Bool { true }
    // A borderless panel has no standard close button for AppKit to click or to validate
    // Close Window against. An owner with its own close flow (Welcome) takes it through that flow.
    var onPerformClose:(()->Void)?
    override func performClose(_ sender:Any?) { if let onPerformClose { onPerformClose() } else { close() } }
    override func validateUserInterfaceItem(_ item:NSValidatedUserInterfaceItem)->Bool {
        item.action == #selector(performClose(_:)) ? styleMask.contains(.closable):super.validateUserInterfaceItem(item)
    }
}
