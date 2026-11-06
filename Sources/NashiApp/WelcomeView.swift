import AppKit
import SwiftUI
import ServiceManagement
import NashiCore

struct WelcomeView:View {
    let onDone:()->Void
    let onClose:()->Void
    let onMinimize:()->Void
    @State private var page=0
    @State private var folderAllowed=false
    @State private var folderError:String?
    @State private var launchAtLogin=false
    @State private var launchNeedsApproval=false
    @State private var launchUnavailable=false
    @State private var launchError:String?
    private let names=["Hello","Convert","Fine-tune","Move & share","Folders","Ready"]
    private var toolsReady:Bool { ExternalToolSetup.status.ready }
    private var titles:[String] { ["Nice files.\nNo fuss.","A new form.\nIn a tap.","Little tweaks.\nBig difference.","Keep things\nmoving.","Right where\nyou want it.",toolsReady ? "You’re all set.\nMake something good.":"One last step.\nSet up your tools."] }
    var body:some View {
        VStack(spacing:0) {
            HStack(spacing:12) {
                Button(action:onClose) { Image(systemName:"xmark").font(.system(size:11,weight:.semibold)) }.buttonStyle(NashiButtonStyle(.quiet,compact:true)).accessibilityLabel("Close welcome").keyboardShortcut(.cancelAction)
                ZStack { WindowDragRegion();Text("A FRESH START").font(.system(size:10,weight:.bold,design:.rounded)).tracking(1.5).foregroundStyle(.secondary).allowsHitTesting(false) }
                Button("Skip tour",action:onDone).buttonStyle(NashiButtonStyle(.quiet,compact:true))
            }.padding(.horizontal,22).padding(.top,15)
            HStack(spacing:28) {
                ZStack {
                    Circle().fill(page==4 ? NashiPalette.sage:NashiPalette.sand).frame(width:280,height:280)
                    if page==0 || page==5 { NashiMark(size:170,happy:page==5).rotationEffect(.degrees(page==5 ? -6:0)) }
                    else if page==4 { Image(systemName:folderAllowed ? "folder.badge.checkmark":"folder.fill").font(.system(size:112,weight:.light)).foregroundStyle(NashiPalette.forest).nashiAnimation(value:folderAllowed) }
                    else { WelcomeWheelPreview(mode:page==1 ? .convert:page==2 ? .tools:.actions,items:page==1 ? converterItems:page==2 ? toolItems:actionItems).frame(width:300,height:300).allowsHitTesting(false).accessibilityHidden(true) }
                }.frame(width:310,height:330).id("art-\(page)").transition(.opacity)
                VStack(alignment:.leading,spacing:17) {
                    NashiPill(title:names[page].uppercased(),symbol:page==5 ? "checkmark":"sparkle",color:page==5 ? NashiPalette.forest:.nashi)
                    LText(titles[page]).font(.system(size:33,weight:.bold,design:.rounded)).tracking(-1).fixedSize(horizontal:false,vertical:true)
                    detail
                }.frame(width:310,alignment:.leading).id("copy-\(page)").transition(.opacity)
            }.frame(maxWidth:.infinity,maxHeight:.infinity)
            HStack(spacing:10) {
                HStack(spacing:6) { ForEach(0..<6,id:\.self) { index in Button { page=index } label:{ Capsule().fill(page==index ? Color.nashi:Color.secondary.opacity(0.22)).frame(width:page==index ? 23:6,height:6).frame(height:24) }.buttonStyle(.plain).accessibilityLabel("Step \(index+1) of 6: \(names[index])").accessibilityAddTraits(page==index ? .isSelected:[]) } }
                Text("\(page+1) / 6").font(.system(size:10,design:.rounded)).monospacedDigit().foregroundStyle(.secondary)
                Spacer()
                if page>0 { Button("Back") { page-=1 }.buttonStyle(NashiButtonStyle(.quiet)) }
                Button(action:page==5 ? onDone:next) { Label(page==5 ? (toolsReady ? "Let’s go":"Set up tools"):"Continue",systemImage:"arrow.right") }.buttonStyle(NashiButtonStyle(.primary)).keyboardShortcut(.defaultAction)
            }.padding(.horizontal,32).padding(.bottom,26)
        }.frame(width:760,height:520).background(NashiPalette.canvas).clipShape(RoundedRectangle(cornerRadius:28,style:.continuous))
            .overlay(RoundedRectangle(cornerRadius:28).strokeBorder(NashiPalette.border))
            .tint(.nashi).nashiAnimation(value:page)
            .onAppear { folderAllowed = !FolderAccessPreferences.grants().isEmpty;readLaunchStatus() }
            .onChange(of:page) { if $0==5 { readLaunchStatus() } }
    }
    @ViewBuilder private var detail:some View {
        switch page {
        case 0:
            Text("A happier home for all the little file jobs. Convert, edit, and share — right on your Mac.").font(.system(size:13)).foregroundStyle(.secondary)
            Label("Your files stay local",systemImage:"lock.shield").font(.system(size:11,weight:.medium)).foregroundStyle(NashiPalette.forest)
        case 1,2,3:
            Text(page==1 ? "Drag a file in Finder, tap Shift, and drop onto its next format.":page==2 ? "Tap Command while dragging to crop, compress, annotate, and give your file a little love.":"Tap Control while dragging to copy, move, share, or carry files in a floating shelf.").font(.system(size:13)).foregroundStyle(.secondary)
            HStack(spacing:9) { Text(page==1 ? "⇧":page==2 ? "⌘":"⌃").font(.system(size:22,weight:.semibold));Text(page==1 ? "Shift to convert":page==2 ? "Command for tools":"Control for actions").font(.system(size:12,weight:.semibold,design:.rounded)) }.padding(13).background(NashiPalette.surface,in:RoundedRectangle(cornerRadius:13))
            Text("You can also choose files from the main window.").font(.system(size:11)).foregroundStyle(.secondary)
        case 4:
            Text("Pick a folder you work in. Fresh copies are saved beside the originals by default.").font(.system(size:13)).foregroundStyle(.secondary)
            Button(action:chooseFolder) { Label(folderAllowed ? "Folder access ready":"Choose a folder",systemImage:folderAllowed ? "checkmark":"folder.badge.plus") }.buttonStyle(NashiButtonStyle())
            Text("Optional. You can change this in Settings.").font(.system(size:11)).foregroundStyle(.secondary)
            if let folderError { Text(folderError).font(.caption).foregroundStyle(.orange).lineLimit(2) }
        default:
            Text(toolsReady ? "Your tools are ready. Let’s put that next file to good use.":"Next, download the missing tools for media and Office files. Tools already on your computer do not need another download.").font(.system(size:13)).foregroundStyle(.secondary)
            Toggle("Ready when you log in",isOn:Binding(get:{launchAtLogin},set:updateLaunch)).toggleStyle(.switch).font(.system(size:12)).disabled(launchUnavailable)
            if launchNeedsApproval { Button("Open Login Items") { SMAppService.openSystemSettingsLoginItems() }.buttonStyle(NashiButtonStyle(.secondary,compact:true)) }
            if launchUnavailable { Text("Launch at login is unavailable for this copy.").font(.caption).foregroundStyle(.secondary) }
            if let launchError { Text(launchError).font(.caption).foregroundStyle(.orange).lineLimit(2) }
        }
    }
    private var converterItems: [WheelItem] {
        ["PNG", "WEBP", "HEIC", "TIFF", "AVIF", "BMP", "PDF"].map { WheelItem(title: $0) }
    }
    private var toolItems: [WheelItem] {
        [WheelItem(title: "COMPRESS", symbol: "arrow.down.right.and.arrow.up.left"),
         WheelItem(title: "METADATA", symbol: "tag"),
         WheelItem(title: "EDIT", symbol: "slider.horizontal.3"),
         WheelItem(title: "ANNOTATE", symbol: "pencil.and.outline"),
         WheelItem(title: "ADD BG", symbol: "photo.artframe"),
         WheelItem(title: "CROP", symbol: "crop"),
         WheelItem(title: "REDACT", symbol: "eye.slash")]
    }
    private var actionItems: [WheelItem] {
        [WheelItem(title: "AIRDROP", symbol: "dot.radiowaves.left.and.right"),
         WheelItem(title: "FLOAT", symbol: "rectangle.on.rectangle"),
         WheelItem(title: "COPY", symbol: "doc.on.doc"),
         WheelItem(title: "MOVE", symbol: "folder"),
         WheelItem(title: "COPY PATH", symbol: "link"),
         WheelItem(title: "TERMINAL", symbol: "terminal")]
    }

    private func next() { page = min(5, page + 1) }

    private func chooseFolder() {
        let picker = NSOpenPanel()
        picker.title = L10n.text("Allow Home Folder Access")
        picker.message = L10n.text("Choose your home folder or a folder where you save files.")
        picker.prompt = L10n.text("Allow Access")
        picker.directoryURL = FileManager.default.homeDirectoryForCurrentUser
        picker.canChooseFiles = false
        picker.canChooseDirectories = true
        picker.canCreateDirectories = false
        picker.allowsMultipleSelection = false
        guard picker.runModal() == .OK, let url = picker.url else { return }
        do {
            try FolderAccessPreferences.add([url])
            folderAllowed = true
            folderError = nil
        } catch {
            folderError = error.localizedDescription
        }
    }

    private func readLaunchStatus() {
        let status = SMAppService.mainApp.status
        launchAtLogin = status == .enabled || status == .requiresApproval
        launchNeedsApproval = status == .requiresApproval
        launchUnavailable = status == .notFound
    }

    private func updateLaunch(_ value: Bool) {
        do {
            if value { try SMAppService.mainApp.register() }
            else { try SMAppService.mainApp.unregister() }
            launchError = nil
        } catch {
            launchError = error.localizedDescription
        }
        readLaunchStatus()
    }
}

private struct WelcomeWheelPreview: NSViewRepresentable {
    let mode: WheelMode
    let items: [WheelItem]
    func makeNSView(context: Context) -> WheelView {
        let view = WheelView(frame: NSRect(x: 0, y: 0, width: 300, height: 300))
        view.setAccessibilityElement(false)
        return view
    }
    func updateNSView(_ view: WheelView, context: Context) {
        view.mode = mode
        view.items = items
        view.inputs = []
        view.onSelect = nil
        view.onCancel = nil
        view.onDragInputs = nil
        view.needsDisplay = true
    }
}
