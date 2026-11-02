import SwiftUI
import AppKit
import NashiCore

struct QRCodeCreatorView:View {
    @ObservedObject var state:AppState
    @State private var text:String
    @State private var size=512
    @State private var correction="M"
    @State private var image:CGImage?
    @State private var imageRevision:String?
    @State private var error:String?
    init(state:AppState,initialText:String="") { self.state=state;_text=State(initialValue:initialText) }
    private var revision:String { "\(size):\(correction):"+text }
    private var hasCurrentPreview:Bool { image != nil && imageRevision==revision }
    var body:some View {
        VStack(alignment:.leading,spacing:18) {
            HStack { WindowCloseButton().frame(width:26,height:26);LText("Create QR Code").font(.title2.bold());Spacer() }
            LText("Text or URL").font(.headline)
            TextEditor(text:$text).font(.body).frame(height:90).overlay(RoundedRectangle(cornerRadius:8).stroke(NashiPalette.border))
            HStack {
                Picker("Image size",selection:$size) { ForEach([256,512,1024],id:\.self) { Text("\($0) px").tag($0) } }
                Picker("Error correction",selection:$correction) { ForEach(["L","M","Q","H"],id:\.self) { Text($0).tag($0) } }
            }
            ZStack {
                Color.white
                if let image,hasCurrentPreview { Image(decorative:image,scale:1).renderingMode(.original).interpolation(.none).resizable().scaledToFit().padding(8) }
                else { Image(systemName:"qrcode").font(.system(size:80)).foregroundStyle(.gray) }
            }.frame(width:260,height:260).frame(maxWidth:.infinity).uiAuditProbe("qr.preview")
            if let error { LText(error).font(.caption).foregroundStyle(.orange) }
            HStack { LText("Created locally. No upload.").font(.caption).foregroundStyle(.secondary);Spacer();Button(action:save) { LText("Save PNG…") }.buttonStyle(NashiButtonStyle(.primary)).disabled(!hasCurrentPreview || state.working) }
        }.padding(24).frame(width:620).background(NashiPalette.canvas).tint(.nashi).textFieldStyle(.roundedBorder)
            .uiAuditStatus(hasCurrentPreview ? "ready":error.map{"failed: "+$0} ?? "loading")
            .task(id:revision) {
                let currentRevision=revision
                image=nil;imageRevision=nil;error=nil
                guard !text.trimmingCharacters(in:.whitespacesAndNewlines).isEmpty else { return }
                let value=text,pixels=size,level=correction
                do {
                    try await Task.sleep(nanoseconds:120_000_000);try Task.checkCancellation()
                    let generated=try await Task.detached { try QRCodeGenerator.image(text:value,size:pixels,correction:level) }.value
                    try Task.checkCancellation();image=generated;imageRevision=currentRevision
                } catch is CancellationError {} catch { if !Task.isCancelled { self.error=error.localizedDescription } }
            }
    }
    private func save() {
        guard !state.working,hasCurrentPreview else { return }
        let panel=NSSavePanel();panel.nameFieldStringValue="QR Code.png";panel.allowedContentTypes=[.png]
        guard panel.runModal() == .OK,let url=panel.url else { return }
        do { let output=try QRCodeGenerator.export(text:text,size:size,correction:correction,directory:url.deletingLastPathComponent(),filename:url.lastPathComponent);state.recordQRCode(output) }
        catch { self.error=error.localizedDescription }
    }
}

extension AppState {
    func showQRCodeCreator() {
        guard !working else { return }
        let window=ToolPanel(contentRect:NSRect(x:0,y:0,width:668,height:650),styleMask:[.borderless,.closable],backing:.buffered,defer:false)
        window.isReleasedWhenClosed=false;window.contentView=NSHostingView(rootView:QRCodeCreatorView(state:self).nashiLanguage());window.center();retainToolWindow(window);window.makeKeyAndOrderFront(nil);NSApp.activate(ignoringOtherApps:true)
    }
}
