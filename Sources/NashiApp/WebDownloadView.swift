import AppKit
import SwiftUI
import NashiCore

@MainActor final class WebDownloadModel:ObservableObject {
    @Published var linkText=""
    @Published var info:WebMediaInfo?
    @Published var mode:WebDownloadMode = .video
    @Published var maximumHeight=0
    @Published var inspecting=false
    @Published var downloading=false
    @Published var error:String?
    @Published var completed:[URL]=[]
    @Published var selectedPhotos=Set<Int>()
    @Published var shortcutAvailable=false
    @Published var destination:URL
    let state:AppState
    var onUseFiles:(()->Void)?
    private var inspection:Task<Void,Never>?
    private var inspectionToken=CancellationToken()
    private var revision=UUID()
    private var pendingMode:WebDownloadMode?
    init(state:AppState) {
        self.state=state
        destination=UserDefaults.standard.string(forKey:"webDownloadDestination").map{URL(fileURLWithPath:$0,isDirectory:true)} ?? FileManager.default.urls(for:.downloadsDirectory,in:.userDomainMask)[0]
    }
    func open(_ url:URL?,mode:WebDownloadMode) {
        guard !downloading,let url else { return }
        pendingMode=mode;linkText=url.absoluteString;inspect()
    }
    func editedLink() {
        guard !downloading else { return }
        inspection?.cancel();inspectionToken.cancel();revision=UUID();inspecting=false;info=nil;error=nil;completed=[];pendingMode=nil
    }
    func pasteLink() {
        guard !downloading else { return }
        guard let url=WebLinkPayload.clipboardLink() else { error="Copy a video share link or an image address first.";return }
        pendingMode=nil;linkText=url.absoluteString;inspect()
    }
    func inspect() {
        guard !downloading else { return }
        inspection?.cancel();inspectionToken.cancel();inspectionToken=CancellationToken();revision=UUID()
        let current=revision,token=inspectionToken
        info=nil;completed=[];error=nil;inspecting=false
        let url:URL
        do { url=try WebDownloadEngine.link(linkText) } catch { self.error=error.localizedDescription;return }
        inspecting=true
        inspection=Task { [weak self] in
            do {
                let found=try await WebDownloadEngine.inspect(url,token:token)
                guard let self,self.revision==current,!Task.isCancelled else { return }
                self.info=found;self.maximumHeight=0;self.selectedPhotos=Set(found.photos.map(\.id))
                if found.kind == .image { self.linkText=found.url.absoluteString }
                self.mode=found.modes.contains(self.pendingMode ?? .video) ? (self.pendingMode ?? .video):(found.modes.first ?? .original)
                self.pendingMode=nil;self.inspecting=false
            } catch {
                guard let self,self.revision==current,!token.isCancelled,!Task.isCancelled else { return }
                self.error=error.localizedDescription;self.inspecting=false
            }
        }
    }
    func cancelInspection() { inspection?.cancel();inspectionToken.cancel();revision=UUID();inspecting=false }
    func configurationChanged() { guard !downloading else { return };completed=[];error=nil }
    func selectPhotos(_ ids:Set<Int>) { guard !downloading else { return };selectedPhotos=ids;configurationChanged() }
    func chooseDestination() {
        guard !downloading else { return }
        let panel=NSOpenPanel();panel.canChooseFiles=false;panel.canChooseDirectories=true;panel.canCreateDirectories=true;panel.directoryURL=destination;panel.prompt="Save Downloads Here"
        if panel.runModal() == .OK,let url=panel.url { do { try FolderAccessPreferences.add([url]);configurationChanged();destination=url;UserDefaults.standard.set(url.path,forKey:"webDownloadDestination") } catch { self.error=error.localizedDescription } }
    }
    func download() {
        guard let info,!downloading,!state.working else { return }
        let mode=mode,maximum=maximumHeight>0 ? maximumHeight:nil,folder=destination
        let selection=info.photos.isEmpty ? nil:selectedPhotos
        downloading=true;error=nil;completed=[]
        state.beginJob("Starting download…")
        let token=state.token
        Task { [state] in
            defer { downloading=false;state.finishJob() }
            do {
                let files=try await WebDownloadEngine.download(info,mode:mode,maximumHeight:maximum,destination:folder,selectedPhotos:selection,token:token) { [weak state] fraction,label in
                    Task { @MainActor in state?.receiveProgress(fraction,label:label,token:token) }
                }
                state.stopProgressUpdates();state.outputs=files;state.inputs=files;state.progress=1;completed=files
                state.recordWebDownload(files,site:info.url.host ?? "Web")
            } catch {
                state.stopProgressUpdates();self.error=token.isCancelled ? "Download cancelled. No partial files were kept.":error.localizedDescription
                if !token.isCancelled { state.error=error.localizedDescription }
            }
        }
    }
}

struct WebDownloadView:View {
    @ObservedObject var model:WebDownloadModel
    @ObservedObject var state:AppState
    var body:some View {
        VStack(alignment:.leading,spacing:0) {
            HStack(spacing:14) {
                WindowCloseButton().frame(width:24,height:24)
                ZStack {
                    WindowDragRegion()
                    HStack(spacing:12) {
                        Image(systemName:"arrow.down.circle.fill").font(.system(size:26)).foregroundStyle(Color.nashi)
                        VStack(alignment:.leading,spacing:4) { Text("Download from the web").font(.system(size:21,weight:.bold,design:.rounded));Text("A link. A file. Yours to work with.").font(.caption).foregroundStyle(.secondary) }
                        Spacer()
                    }.allowsHitTesting(false)
                }.frame(height:50)
            }.padding(22)
            Divider()
            ScrollView {
                VStack(alignment:.leading,spacing:20) {
                    HStack(spacing:10) {
                        TextField("Paste a video share link or image address",text:Binding(get:{model.linkText},set:{ if model.linkText != $0 { model.linkText=$0;model.editedLink() } })).textFieldStyle(.roundedBorder).onSubmit { if model.info != nil { model.download() } else { model.inspect() } }.disabled(model.downloading).accessibilityLabel("Media link")
                        Button { model.pasteLink() } label:{Label("Paste",systemImage:"doc.on.clipboard")}.disabled(model.downloading)
                    }
                    if model.inspecting {
                        HStack { ProgressView().controlSize(.small);Text("Finding the media…").font(.callout);Spacer();Button("Cancel") { model.cancelInspection() } }
                    }
                    if let info=model.info {
                        HStack(spacing:16) {
                            Image(systemName:info.kind == .image ? "photo":info.kind == .audio ? "waveform":"play.rectangle").font(.system(size:30)).foregroundStyle(Color.nashi).frame(width:66,height:66).background(NashiPalette.sand,in:RoundedRectangle(cornerRadius:17))
                            VStack(alignment:.leading,spacing:6) {
                                Text(info.title).font(.system(size:16,weight:.semibold,design:.rounded)).lineLimit(3).textSelection(.enabled)
                                Text(details(info)).font(.caption).foregroundStyle(.secondary)
                            };Spacer(minLength:0)
                        }.padding(17).nashiCard()
                        if !info.photos.isEmpty {
                            VStack(alignment:.leading,spacing:12) {
                                HStack {
                                    Text("Select photos").font(.headline);Spacer()
                                    Button("All") { model.selectPhotos(Set(info.photos.map(\.id))) }
                                    Button("None") { model.selectPhotos([]) }
                                }
                                if info.skippedVideoCount>0 { Text("\(info.skippedVideoCount) video items are not included. Only photos will be saved.").font(.caption).foregroundStyle(.secondary) }
                                Text("Saved in post order in a new Instagram folder.").font(.caption).foregroundStyle(.secondary)
                                ForEach(info.photos) { photo in
                                    Toggle(isOn:Binding(get:{model.selectedPhotos.contains(photo.id)},set:{ selected in
                                        var ids=model.selectedPhotos;if selected { ids.insert(photo.id) } else { ids.remove(photo.id) };model.selectPhotos(ids)
                                    })) {
                                        HStack(spacing:12) {
                                            AsyncImage(url:photo.url) { image in image.resizable().scaledToFit() } placeholder:{Image(systemName:"photo").foregroundStyle(.secondary)}
                                                .frame(width:88,height:76).background(NashiPalette.sand,in:RoundedRectangle(cornerRadius:8))
                                            VStack(alignment:.leading,spacing:4) {
                                                Text("Photo \(photo.id)").font(.callout.weight(.semibold))
                                                if let width=photo.width,let height=photo.height { Text("\(width) × \(height)").font(.caption).foregroundStyle(.secondary) }
                                            };Spacer()
                                        }
                                    }.toggleStyle(.checkbox)
                                }
                            }.padding(17).nashiCard().disabled(model.downloading)
                        }
                        if info.modes.count>1 {
                            Picker("Save as",selection:$model.mode) { ForEach(info.modes,id:\.self) { LText($0.title).tag($0) } }.pickerStyle(.segmented).disabled(model.downloading)
                        }
                        if model.mode == .video,(info.heights.max() ?? 0)>720 {
                            Picker("Video quality",selection:$model.maximumHeight) {
                                Text("Best available").tag(0)
                                if (info.heights.max() ?? 0)>1080 { Text("Up to 1080p").tag(1080) }
                                if (info.heights.max() ?? 0)>720 { Text("Up to 720p").tag(720) }
                            }.disabled(model.downloading)
                        }
                    }
                    HStack(spacing:10) {
                        Image(systemName:"folder").foregroundStyle(.secondary)
                        VStack(alignment:.leading,spacing:3) { Text("SAVE TO").font(.system(size:9,weight:.bold,design:.rounded)).foregroundStyle(.secondary);Text((model.destination.path as NSString).abbreviatingWithTildeInPath).font(.caption).lineLimit(2).textSelection(.enabled) }
                        Spacer();Button("Change…") { model.chooseDestination() }.disabled(model.downloading)
                    }.padding(15).nashiCard()
                    if model.downloading {
                        VStack(alignment:.leading,spacing:9) { HStack { LText(state.progressLabel).font(.caption);Spacer();if state.progressIsDeterminate { Text("\(Int(state.progress*100))%").font(.caption.monospacedDigit()) } };JobProgressBar(state:state) }
                    }
                    if !model.completed.isEmpty { Label("Ready on your Mac",systemImage:"checkmark.circle.fill").foregroundStyle(NashiPalette.forest);Text(model.completed.map(\.lastPathComponent).joined(separator:"\n")).font(.caption).textSelection(.enabled) }
                    if let error=model.error { Label(error,systemImage:"exclamationmark.circle").font(.callout).foregroundStyle(.orange).fixedSize(horizontal:false,vertical:true) }
                    if state.working,!model.downloading { Text("Another task is running. Your link is ready when it finishes.").font(.caption).foregroundStyle(.secondary) }
                    VStack(alignment:.leading,spacing:7) {
                        LText(model.shortcutAvailable ? "Copy a link → ⌃⇧D, or drag it with ⌘.":"Copy a link, then choose Download Link from the Nashi menu.").font(.caption)
                        Text("Instagram photo posts open a photo selection. For videos, use the share link; for other photos, drag the image or use Copy Image Address. Public media only; login-required and protected items may be unavailable.").font(.caption).foregroundStyle(.secondary).fixedSize(horizontal:false,vertical:true)
                    }
                }.padding(24)
            }
            Divider()
            HStack {
                if model.downloading { Button("Cancel download") { state.token.cancel() } }
                else if !model.completed.isEmpty { Button("Show in Finder") { NSWorkspace.shared.activateFileViewerSelecting(model.completed) } }
                Spacer()
                if !model.completed.isEmpty { Button("Use in Nashi") { model.onUseFiles?() }.buttonStyle(NashiButtonStyle(.primary)) }
                else if let info=model.info { Button(!info.photos.isEmpty ? "Download \(model.selectedPhotos.count) \(model.selectedPhotos.count==1 ? "photo":"photos")":model.mode == .audioMP3 ? "Download MP3":info.kind == .image ? "Download image":"Download") { model.download() }.buttonStyle(NashiButtonStyle(.primary)).disabled(model.downloading || state.working || (!info.photos.isEmpty && model.selectedPhotos.isEmpty)) }
                else { Button("Check link") { model.inspect() }.buttonStyle(NashiButtonStyle(.primary)).disabled(model.linkText.isEmpty || model.inspecting) }
            }.padding(20)
        }.onChange(of:model.mode) { _ in model.configurationChanged() }.onChange(of:model.maximumHeight) { _ in model.configurationChanged() }.frame(minWidth:620,minHeight:520).background(NashiPalette.canvas).tint(.nashi).buttonStyle(NashiButtonStyle(.secondary,compact:true))
    }
    func details(_ info:WebMediaInfo)->String {
        var details=[info.site,info.kind.rawValue.capitalized]
        if !info.photos.isEmpty { details.append("\(info.photos.count) \(info.photos.count==1 ? "photo":"photos")") }
        if let width=info.imageWidth,let height=info.imageHeight { details.append("\(width) × \(height)") }
        if let duration=info.duration { details.append("\(Int(duration)/60):\(String(format:"%02d",Int(duration)%60))") }
        if let height=info.heights.max() { details.append("\(height)p") }
        if let bytes=info.byteCount { details.append(ByteCountFormatter.string(fromByteCount:bytes,countStyle:.file)) }
        return details.joined(separator:" · ")
    }
}
