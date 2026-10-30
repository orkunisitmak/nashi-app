import AppKit
import SwiftUI
import NashiCore
import UniformTypeIdentifiers
import NashiUpdates

struct HomeView:View {
    @ObservedObject var state:AppState
    @ObservedObject var updater:AppUpdater
    @AppStorage("convertKey") private var convertKey="shift"
    @AppStorage("toolsKey") private var toolsKey="command"
    @AppStorage("actionsKey") private var actionsKey="control"
    @FocusState private var librarySearchFocused:Bool
    @State private var targeted=false
    @State private var historySearch=""
    @State private var allHistory=false
    @State private var toolsReady=ExternalToolSetup.status.ready
    private var formats:[String] { FeatureCatalog.formats(for:state.inputs) }
    private var tools:[ToolKind] { FeatureCatalog.tools(for:state.inputs) }
    private var recent:[JobRecord] {
        let filtered=state.history.filter { historySearch.isEmpty || AppLocalization.matches(([$0.title,AppLocalization.text($0.title,language:L10n.language)]+$0.outputs.map{URL(fileURLWithPath:$0).lastPathComponent}).joined(separator:" "),query:historySearch,language:L10n.language) }
        return Array(filtered.prefix(allHistory || !historySearch.isEmpty ? 50:3))
    }
    var body:some View {
        VStack(spacing:0) {
            HStack(spacing:10) {
                NashiMark(size:40)
                Text("Nashi").font(.system(size:19,weight:.bold,design:.rounded)).tracking(-0.5)
                Spacer()
                if Bundle.main.object(forInfoDictionaryKey:"NashiExternalTools") as? Bool == true {
                    Button(toolsReady ? "Tools ready":"Finish setup") {NotificationCenter.default.post(name:.showToolSetup,object:nil)}
                        .buttonStyle(NashiButtonStyle(.secondary,compact:true))
                }
                Button("Home") {state.libraryVisible=false}.buttonStyle(NashiButtonStyle(state.libraryVisible ? .quiet:.secondary,compact:true))
                Button("Library") {state.libraryVisible=true}.buttonStyle(NashiButtonStyle(state.libraryVisible ? .secondary:.quiet,compact:true))
                NashiPill(title:"On your Mac",symbol:"lock.fill")
                Button { state.onSettings?() } label:{Image(systemName:"slider.horizontal.3").font(.system(size:15))}
                    .buttonStyle(NashiButtonStyle(.quiet,compact:true)).help("Settings").accessibilityLabel("Settings")
            }.padding(.horizontal,28).padding(.top,19).padding(.bottom,15)
                .onReceive(NotificationCenter.default.publisher(for:.toolSetupChanged)) { _ in toolsReady=ExternalToolSetup.status.ready }
                .onReceive(NotificationCenter.default.publisher(for:NSApplication.didBecomeActiveNotification)) { _ in toolsReady=ExternalToolSetup.status.ready }
            if state.libraryVisible { libraryPage } else { ScrollView {
                VStack(alignment:.leading,spacing:22) {
                    if let version=updater.availableVersion {
                        UpdateBanner(updater:updater,version:version).transition(.opacity.combined(with:.move(edge:.top)))
                    }
                    if updater.repository != nil, let updateError=updater.lastError {
                        HStack(spacing:12) {
                            Label(updateError,systemImage:"exclamationmark.circle").font(.caption).foregroundStyle(.secondary).fixedSize(horizontal:false,vertical:true)
                            Spacer(minLength:8)
                            Button("Update Connection…") { NotificationCenter.default.post(name:.showUpdateConnection,object:nil) }
                                .buttonStyle(NashiButtonStyle(.secondary,compact:true))
                        }.padding(16).nashiCard()
                    }
                    HStack(alignment:.bottom) {
                        VStack(alignment:.leading,spacing:6) {
                            LText(state.inputs.isEmpty ? "Your files. Their next form.":"Let’s make something of it.")
                                .font(.system(size:30,weight:.bold,design:.rounded)).tracking(-0.9)
                            Text("Convert, fine-tune, and send them on their way.").font(.system(size:13)).foregroundStyle(.secondary)
                        }
                        Spacer()
                    }.padding(.top,8).nashiEntrance()
                    dropZone.nashiEntrance(delay:0.04)
                    Button { state.onWebDownload?(WebLinkPayload.clipboardLink(),.video) } label:{
                        HStack(spacing:12) {
                            Image(systemName:"arrow.down.circle.fill").font(.system(size:25)).foregroundStyle(Color.nashi)
                            VStack(alignment:.leading,spacing:4) { Text("Download a link").font(.system(size:14,weight:.bold,design:.rounded));Text("Video, audio, or an image. Straight into your tools.").font(.caption).foregroundStyle(.secondary) }
                            Spacer();if state.webShortcutAvailable { Text("⌃⇧D").font(.system(size:13,weight:.semibold)).foregroundStyle(.secondary) }
                        }.padding(18).frame(maxWidth:.infinity,alignment:.leading).nashiCard()
                    }.buttonStyle(.plain).accessibilityLabel("Download a link")
                    if !state.inputs.isEmpty {
                        selectionActions.transition(.opacity.combined(with:.move(edge:.top)))
                    }
                    if state.working || state.error != nil || !state.outputs.isEmpty { JobProgressView(state:state) }
                    if !state.history.isEmpty { historySection.nashiEntrance(delay:0.08) }
                    HStack(spacing:6) {
                        Image(systemName:"leaf")
                        Text("A little less file fuss. A little more flow.")
                        Spacer()
                        Text("Made for your Mac").foregroundStyle(.tertiary)
                    }.font(.system(size:10,weight:.medium,design:.rounded)).foregroundStyle(.secondary).padding(.top,1)
                }.padding(.horizontal,28).padding(.bottom,24)
            }
        }
        }.frame(minWidth:700,minHeight:580).background(NashiPalette.canvas).tint(.nashi)
            .nashiAnimation(value:state.inputs.isEmpty).nashiAnimation(value:state.working)
            .nashiAnimation(value:updater.availableVersion)
            .onChange(of:state.libraryFocusRequest) { _ in librarySearchFocused=true }
            .onChange(of:state.inputs) { _ in if !state.working { state.outputs=[];state.error=nil } }
    }
    private var libraryPage:some View {
        ScrollViewReader { scroll in
            ScrollView {
                VStack(alignment:.leading,spacing:16) {
                    Text("Your Library").font(.system(size:30,weight:.bold,design:.rounded))
                    Text("Freshly made, pasted, and ready for another round.").font(.callout).foregroundStyle(.secondary)
                    TextField("Find a file or tool · ⌘F",text:$state.libraryQuery).textFieldStyle(.roundedBorder).focused($librarySearchFocused)
                    if let error=state.error {HStack{Label(error,systemImage:"exclamationmark.circle").font(.caption).foregroundStyle(.orange);Spacer();Button("Dismiss"){state.error=nil}.buttonStyle(NashiButtonStyle(.quiet,compact:true))}}
                    if state.filteredHistory.isEmpty {LText(state.history.isEmpty ? "Nothing here yet. Paste something or make your first fresh copy.":"No matches. Try a file name or tool.").foregroundStyle(.secondary).padding(20)}
                    ForEach(state.filteredHistory) {job in
                        RecentFileRow(job:job,canReuse:!state.working,onPreview:{state.preview(job)},onReuse:{urls in guard !state.working else{return};state.inputs=urls;state.libraryVisible=false})
                            .nashiCard(radius:16).overlay(RoundedRectangle(cornerRadius:16).strokeBorder(state.selectedHistoryID==job.id ? Color.nashi:Color.clear,lineWidth:1.5))
                            .contentShape(Rectangle()).onTapGesture {state.selectedHistoryID=job.id;librarySearchFocused=false}.id(job.id)
                    }
                    Text("↑/↓ choose · Space preview · Return use in Nashi").font(.caption).foregroundStyle(.secondary)
                }.padding(28)
            }.onChange(of:state.selectedHistoryID) { id in if let id {scroll.scrollTo(id)} }
        }
    }
    private var dropZone:some View {
        VStack(alignment:.leading,spacing:18) {
            HStack(spacing:22) {
                VStack(alignment:.leading,spacing:12) {
                    NashiPill(title:targeted ? "A perfect landing spot":state.inputs.isEmpty ? "START HERE":"READY WHEN YOU ARE",symbol:targeted ? "arrow.down":"sparkle",color:.nashi)
                    selectionTitle
                        .font(.system(size:23,weight:.bold,design:.rounded)).tracking(-0.5).lineLimit(2).textSelection(.enabled)
                    selectionDescription
                        .font(.system(size:12)).foregroundStyle(.secondary).lineLimit(2)
                    HStack(spacing:9) {
                        Button { state.onChooseFiles?() } label:{Label(state.inputs.isEmpty ? "Choose files":"Add or change files",systemImage:"plus")}
                            .buttonStyle(NashiButtonStyle(state.inputs.isEmpty ? .primary:.secondary)).disabled(state.working)
                        Button("Paste") {state.pasteContent()}.buttonStyle(NashiButtonStyle(.secondary)).help("⌘V · Paste files, text, an image or link").disabled(state.working)
                        if !state.inputs.isEmpty {
                            Button { state.onOpenWheel?() } label:{Label("Open wheel",systemImage:"circle.hexagongrid")}
                                .buttonStyle(NashiButtonStyle(.primary)).disabled(state.working)
                        }
                    }.padding(.top,3)
                }.frame(maxWidth:.infinity,alignment:.leading)
                FileGarden(targeted:targeted,selected:!state.inputs.isEmpty).frame(width:166,height:150).accessibilityHidden(true)
            }
            if !state.inputs.isEmpty {
                HStack(spacing:8) {
                    ForEach(Array(state.inputs.prefix(3)),id:\.self) { url in
                        HStack(spacing:6) {
                            AsyncFileArtwork(url:url).frame(width:17,height:17)
                            Text(url.lastPathComponent).font(.system(size:10,weight:.medium)).lineLimit(1)
                            Button { state.inputs.removeAll{$0==url} } label:{Image(systemName:"xmark").font(.system(size:8,weight:.bold))}
                                .buttonStyle(.plain).help("Remove \(url.lastPathComponent) from selection").accessibilityLabel("Remove \(url.lastPathComponent)").disabled(state.working)
                        }.padding(.horizontal,9).padding(.vertical,7).background(NashiPalette.canvas,in:RoundedRectangle(cornerRadius:9))
                    }
                    if state.inputs.count>3 { Text("+\(state.inputs.count-3)").font(.caption).foregroundStyle(.secondary) }
                    Spacer(minLength:0)
                    Button("Clear") { state.inputs=[] }.buttonStyle(NashiButtonStyle(.quiet,compact:true)).disabled(state.working)
                }
            }
            HStack(spacing:8) {
                shortcut(convertKey,"Convert");shortcut(toolsKey,"Tools");shortcut(actionsKey,"Actions")
                Spacer()
                Text("Hold a key while dragging in Finder").font(.system(size:10)).foregroundStyle(.secondary)
            }.padding(.top,2)
        }.padding(24).background(NashiPalette.surface,in:RoundedRectangle(cornerRadius:26,style:.continuous))
            .overlay(RoundedRectangle(cornerRadius:26,style:.continuous).strokeBorder(targeted ? Color.nashi:NashiPalette.border,style:StrokeStyle(lineWidth:targeted ? 2:1,dash:targeted ? [7,5]:[])))
            .shadow(color:Color.nashi.opacity(targeted ? 0.10:0),radius:20,y:6)
            .scaleEffect(targeted && NashiMotion.enabled ? 1.008:1).nashiAnimation(value:targeted)
            .onDrop(of:WebLinkPayload.providerTypes,isTargeted:$targeted,perform:receiveDrop)
    }
    /// Copy goes through the catalog; file names and folders stay verbatim.
    @ViewBuilder private var selectionTitle:some View {
        if targeted { LText("Drop it like it’s… a file.") }
        else if state.inputs.isEmpty { LText("Drop something good.") }
        else if state.inputs.count==1 { Text(verbatim:state.inputs[0].lastPathComponent) }
        else { LText("\(state.inputs.count) files, ready to go.") }
    }
    @ViewBuilder private var selectionDescription:some View {
        if state.inputs.isEmpty { LText("Images, videos, documents, and all the little things in between.") }
        else if state.inputs.count==1 { Text(verbatim:state.inputs[0].deletingLastPathComponent().abbreviatingWithTildeInPath) }
        else { LText("Keep them together, or give each one a fresh start.") }
    }
    private func shortcut(_ key:String,_ title:String)->some View {
        HStack(spacing:5) { Text(ModifierChord.legacy(key)?.symbols ?? key).font(.system(size:12,weight:.semibold));LText(title).font(.system(size:10,weight:.medium)) }
            .foregroundStyle(NashiPalette.forest).padding(.horizontal,9).padding(.vertical,6).background(NashiPalette.sage.opacity(0.65),in:RoundedRectangle(cornerRadius:8))
    }
    private var selectionActions:some View {
        VStack(alignment:.leading,spacing:12) {
            HStack(spacing:12) {
                Button { HomeActionMenu.show(formats.map{format in (format=="gz" ? "GZIP":format.uppercased(),{state.select(.convert(format))})}) } label:{HomeActionTile(title:"Convert",subtitle:formats.isEmpty ? "No output formats":"\(formats.count) ways to change it",symbol:"arrow.triangle.2.circlepath",color:.nashi)}
                    .buttonStyle(.plain).disabled(formats.isEmpty || state.working)
                Button { HomeActionMenu.show(tools.map{tool in (tool.title,{state.select(.tool(tool))})}) } label:{HomeActionTile(title:"Fine-tune",subtitle:tools.isEmpty ? "No tools for this selection":"Edit, compress, and more",symbol:"slider.horizontal.3",color:NashiPalette.forest)}
                    .buttonStyle(.plain).disabled(tools.isEmpty || state.working)
                Button { HomeActionMenu.show(FileAction.allCases.map{action in (action.title,{state.perform(action)})}) } label:{HomeActionTile(title:"Move & share",subtitle:"Send it on its way",symbol:"paperplane",color:Color(red:0.34,green:0.47,blue:0.67))}
                    .buttonStyle(.plain).disabled(state.working)
            }
            if !formats.isEmpty {
                HStack(spacing:7) {
                    Text("QUICK PICKS").font(.system(size:9,weight:.bold,design:.rounded)).tracking(0.8).foregroundStyle(.secondary)
                    ForEach(Array(formats.prefix(4)),id:\.self) { format in
                        Button { state.select(.convert(format)) } label:{HStack(spacing:5) { Text(format=="gz" ? "GZIP":format.uppercased());Image(systemName:"arrow.up.right").font(.system(size:8,weight:.bold)) }}
                            .buttonStyle(NashiButtonStyle(.secondary,compact:true)).disabled(state.working)
                    }
                    Spacer()
                }
            }
        }
    }
    private var historySection:some View {
        VStack(alignment:.leading,spacing:14) {
            HStack {
                Text("Freshly made").font(.system(size:16,weight:.bold,design:.rounded))
                NashiPill(title:"\(state.history.count)")
                Spacer()
                HStack(spacing:6) { Image(systemName:"magnifyingglass").foregroundStyle(.secondary);TextField("Find a recent file",text:$historySearch).textFieldStyle(.plain).font(.system(size:11)) }
                    .padding(8).frame(width:185).background(NashiPalette.inset,in:RoundedRectangle(cornerRadius:10))
            }
            VStack(spacing:3) {
                if recent.isEmpty { Text("No matches. Try a file name or tool.").font(.callout).foregroundStyle(.secondary).padding(20).frame(maxWidth:.infinity) }
                ForEach(recent) { job in
                    RecentFileRow(job:job,canReuse:!state.working,onPreview:{state.preview(job)},onReuse:{ urls in
                        guard !state.working else { return }
                        state.inputs=urls
                    })
                }
            }.padding(7).nashiCard()
            if state.history.count>3 && historySearch.isEmpty {
                Button(allHistory ? "Show less":"See all recent files") { allHistory.toggle() }.buttonStyle(NashiButtonStyle(.quiet,compact:true))
            }
        }.nashiAnimation(value:allHistory)
    }
    private func receiveDrop(_ providers:[NSItemProvider])->Bool {
        guard !state.working else { return false }
        WebLinkPayload.urls(from:providers) { urls in
            guard !state.working else { return }
            if urls.first?.isFileURL == false { state.onWebDrop?(urls) }
            else if !urls.isEmpty { state.inputs=urls }
        }
        return !providers.isEmpty
    }
}

private struct HomeActionTile:View {
    let title:String;let subtitle:String;let symbol:String;let color:Color
    @State private var hovered=false
    var body:some View {
        HStack(spacing:11) {
            Image(systemName:symbol).font(.system(size:17,weight:.medium)).foregroundStyle(color).frame(width:38,height:38).background(color.opacity(0.10),in:RoundedRectangle(cornerRadius:12))
            VStack(alignment:.leading,spacing:4) { LText(title).font(.system(size:13,weight:.bold,design:.rounded));LText(subtitle).font(.system(size:10)).foregroundStyle(.secondary).lineLimit(1) }
            Spacer(minLength:1)
            Image(systemName:"chevron.down").font(.system(size:9,weight:.semibold)).foregroundStyle(.tertiary)
        }.padding(15).frame(maxWidth:.infinity,alignment:.leading).nashiCard(radius:18)
            .overlay(RoundedRectangle(cornerRadius:18).strokeBorder(color.opacity(hovered ? 0.35:0),lineWidth:1))
            .offset(y:hovered && NashiMotion.enabled ? -2:0).onHover{hovered=$0}.nashiAnimation(value:hovered)
    }
}
private struct RecentFileRow:View {
    let job:JobRecord
    let canReuse:Bool
    let onPreview:()->Void
    let onReuse:([URL])->Void
    @State private var hovered=false
    private var urls:[URL] { job.outputs.map{URL(fileURLWithPath:$0)} }
    private var available:[URL] { urls.filter{FileManager.default.fileExists(atPath:$0.path)} }
    var body:some View {
        HStack(spacing:12) {
            if let first=urls.first { AsyncFileArtwork(url:first).frame(width:29,height:29).padding(5).background(NashiPalette.inset,in:RoundedRectangle(cornerRadius:11)) }
            VStack(alignment:.leading,spacing:4) {
                Text(urls.first?.lastPathComponent ?? job.title).font(.system(size:12,weight:.semibold)).lineLimit(1)
                HStack(spacing:3) { if available.isEmpty { Text("Moved or removed") } else { LText(job.title);Text("· "+job.date.formatted(.relative(presentation:.named))) } }.font(.system(size:10)).foregroundStyle(.secondary).lineLimit(1)
            }
            Spacer()
            if urls.count>1 { Text("\(urls.count) files").font(.caption2).foregroundStyle(.secondary) }
            Button(action:onPreview) {Image(systemName:"eye").font(.system(size:13))}.buttonStyle(NashiButtonStyle(.quiet,compact:true)).disabled(available.isEmpty).help("Preview").accessibilityLabel("Preview \(urls.first?.lastPathComponent ?? "result")")
            Button { onReuse(available) } label:{Image(systemName:"arrow.up.left").font(.system(size:12,weight:.medium))}
                .buttonStyle(NashiButtonStyle(.quiet,compact:true)).disabled(available.isEmpty || !canReuse)
                .help(canReuse ? "Use these files again":"Available after the current task finishes")
                .accessibilityLabel("Use \(urls.first?.lastPathComponent ?? "result") again")
            Button { NSWorkspace.shared.activateFileViewerSelecting(available) } label:{Image(systemName:"folder").font(.system(size:13))}
                .buttonStyle(NashiButtonStyle(.quiet,compact:true)).disabled(available.isEmpty).help("Show in Finder").accessibilityLabel("Show \(urls.first?.lastPathComponent ?? "result") in Finder")
        }.padding(.horizontal,10).padding(.vertical,8).background(hovered ? NashiPalette.inset.opacity(0.75):Color.clear,in:RoundedRectangle(cornerRadius:14))
            .onHover{hovered=$0}.nashiAnimation(value:hovered)
    }
}
private struct FileGarden:View {
    let targeted:Bool;let selected:Bool
    var body:some View {
        ZStack {
            Circle().fill(NashiPalette.sand).frame(width:144,height:144)
            card("PDF","doc.text.fill",NashiPalette.forest).rotationEffect(.degrees(-16)).offset(x:-34,y:12)
            card("PNG","photo.fill",.nashi).rotationEffect(.degrees(targeted ? 13:7)).offset(x:31,y:-2)
            NashiMark(size:65,happy:selected || targeted).rotationEffect(.degrees(targeted ? -8:0)).offset(x:0,y:42)
        }.nashiAnimation(value:targeted).nashiAnimation(value:selected)
    }
    func card(_ title:String,_ symbol:String,_ color:Color)->some View {
        VStack(alignment:.leading,spacing:10) {
            Image(systemName:symbol).font(.system(size:23)).foregroundStyle(color)
            LText(title).font(.system(size:10,weight:.bold,design:.rounded)).foregroundStyle(color)
            Capsule().fill(color.opacity(0.18)).frame(width:37,height:4)
        }.padding(13).frame(width:77,height:103,alignment:.leading).background(NashiPalette.surface,in:RoundedRectangle(cornerRadius:15)).overlay(RoundedRectangle(cornerRadius:15).strokeBorder(color.opacity(0.12)))
            .shadow(color:.black.opacity(0.07),radius:12,y:5)
    }
}
private extension URL { var abbreviatingWithTildeInPath:String { (path as NSString).abbreviatingWithTildeInPath } }

struct JobProgressView:View {
    @ObservedObject var state:AppState
    var body:some View {
        VStack(spacing:10) {
            if state.working {
                VStack(alignment:.leading,spacing:12) {
                    HStack(spacing:10) {
                        ProgressView().controlSize(.small)
                        VStack(alignment:.leading,spacing:4) { Text("A fresh file is on its way.").font(.system(size:13,weight:.semibold,design:.rounded));LText(state.progressLabel).font(.system(size:11)).foregroundStyle(.secondary).lineLimit(1) }
                        Spacer()
                        if state.progressIsDeterminate { Text("\(Int(state.progress*100))%").font(.system(size:12,weight:.semibold,design:.rounded)).monospacedDigit().foregroundStyle(.secondary) }
                        Button("Cancel") { state.token.cancel() }.buttonStyle(NashiButtonStyle(.secondary,compact:true))
                    }
                    JobProgressBar(state:state)
                }.padding(17).nashiCard(radius:18).transition(.opacity.combined(with:.move(edge:.top)))
            }
            if let error=state.error {
                HStack(alignment:.top,spacing:10) {
                    Image(systemName:"exclamationmark.circle.fill").foregroundStyle(.orange)
                    VStack(alignment:.leading,spacing:4) { Text("Let’s try that again.").font(.system(size:12,weight:.semibold,design:.rounded));LText(error).font(.system(size:11)).foregroundStyle(.secondary).lineLimit(4).textSelection(.enabled) }
                    Spacer();Button { state.error=nil } label:{Image(systemName:"xmark")}.buttonStyle(NashiButtonStyle(.quiet,compact:true)).accessibilityLabel("Dismiss error")
                }.padding(15).background(Color.orange.opacity(0.07)).background(NashiPalette.canvas).clipShape(RoundedRectangle(cornerRadius:17)).transition(.opacity)
            }
        }.nashiAnimation(value:state.working).nashiAnimation(value:state.outputs.count).nashiAnimation(value:state.error != nil)
    }
}

/// Keep the complete tile as the button label; NSPopUpButton otherwise flattens it to an icon/title.
@MainActor private enum HomeActionMenu {
    private final class Action:NSObject {
        let run:()->Void
        init(_ run:@escaping()->Void){self.run=run}
        @objc func invoke(){run()}
    }
    static func show(_ choices:[(String,()->Void)]) {
        let menu=NSMenu();let handlers=choices.map{Action($0.1)}
        for (index,choice) in choices.enumerated(){let item=NSMenuItem(title:choice.0,action:#selector(Action.invoke),keyEquivalent:"");item.target=handlers[index];menu.addItem(item)}
        withExtendedLifetime(handlers){menu.popUp(positioning:nil,at:NSEvent.mouseLocation,in:nil)}
    }
}
