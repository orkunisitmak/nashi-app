import SwiftUI
import AppKit
import NashiCore

@MainActor final class QRCodeReaderModel:ObservableObject {
    @Published var report=QRCodeScanReport()
    @Published var busy=false
    @Published var progress=0.0
    @Published var label=""
    @Published var password=""
    @Published var copied:Set<UUID>=[]
    @Published var copiedAll=false
    @Published var error:String?
    let inputs:[URL]
    let onSuccess:(SuccessNotice)->Void
    var token=CancellationToken()
    init(inputs:[URL],onSuccess:@escaping(SuccessNotice)->Void) { self.inputs=inputs;self.onSuccess=onSuccess }
    func scan() {
        guard !busy else { return }
        busy=true;progress=0;error=nil;copied=[];copiedAll=false;report=QRCodeScanReport();token=CancellationToken()
        let source=inputs,password=password,cancel=token,model=self
        Task {
            do {
                let scanned=try await Task.detached(priority:.userInitiated) {
                    try QRCodeEngine.scan(source,password:password,token:cancel) { amount,message in Task { @MainActor in model.progress=amount;model.label=message } }
                }.value
                // A Cancel or window close can land after the engine's last check.
                try cancel.check()
                report=scanned
                if report.errors.isEmpty { onSuccess(SuccessNotice(title:"Scan complete.",detail:"\(report.results.count) QR code\(report.results.count == 1 ? "":"s") found.")) }
            } catch { self.error=error.localizedDescription }
            busy=false
        }
    }
    func copy(_ result:QRCodeMatch) {
        NSPasteboard.general.clearContents()
        if NSPasteboard.general.setString(result.text,forType:.string) { copied=[result.id];copiedAll=false;onSuccess(SuccessNotice(title:"QR code copied.",detail:"Ready to paste.")) }
        else { error="Could not copy the QR code." }
    }
    func copyAll() {
        NSPasteboard.general.clearContents()
        if NSPasteboard.general.setString(report.results.map(\.text).joined(separator:"\n"),forType:.string) { copied=[];copiedAll=true;onSuccess(SuccessNotice(title:"QR codes copied.",detail:"\(report.results.count) ready to paste.")) }
        else { error="Could not copy the QR codes." }
    }
    func saveReport() {
        let panel=NSSavePanel();panel.nameFieldStringValue="QR Codes.json";panel.allowedContentTypes=[.json]
        if panel.runModal() == .OK,let url=panel.url {
            do { let encoder=JSONEncoder();encoder.outputFormatting=[.prettyPrinted,.sortedKeys];try encoder.encode(report).write(to:url,options:.atomic);onSuccess(.action("Report saved.",files:[url])) } catch { self.error=error.localizedDescription }
        }
    }
}

struct QRCodeReaderView:View {
    @StateObject private var model:QRCodeReaderModel
    @MainActor init(inputs:[URL],onSuccess:@escaping(SuccessNotice)->Void) { _model=StateObject(wrappedValue:QRCodeReaderModel(inputs:inputs,onSuccess:onSuccess)) }
    var body:some View {
        VStack(spacing:0) {
            HStack(spacing:13) {
                WindowCloseButton().frame(width:26,height:26)
                ZStack { WindowDragRegion();HStack(spacing:12) { Image(systemName:"qrcode.viewfinder").font(.system(size:23)).foregroundStyle(Color.nashi);VStack(alignment:.leading,spacing:4) { Text("Read QR Codes").font(.system(size:21,weight:.bold,design:.rounded));Text("A little code, decoded.").font(.system(size:11)).foregroundStyle(.secondary) };Spacer() }.allowsHitTesting(false) }
            }.padding(20)
            Divider()
            if model.busy {
                HStack(spacing:12) { ProgressView().controlSize(.small);VStack(alignment:.leading,spacing:4) { Text("Looking for little squares…").font(.system(size:12,weight:.semibold,design:.rounded));Text(model.label).font(.caption).foregroundStyle(.secondary).lineLimit(1) };Spacer();Button("Cancel") { model.token.cancel() }.buttonStyle(NashiButtonStyle(.secondary,compact:true)) }.padding(20)
            }
            ScrollView {
                VStack(alignment:.leading,spacing:14) {
                    if model.report.results.isEmpty,!model.busy {
                        VStack(spacing:13) { Image(systemName:"qrcode.viewfinder").font(.system(size:44,weight:.light)).foregroundStyle(NashiPalette.forest);Text("No QR codes this time.").font(.system(size:19,weight:.bold,design:.rounded));Text("Try a clearer image or another PDF.").font(.system(size:12)).foregroundStyle(.secondary) }.frame(maxWidth:.infinity).padding(.vertical,45)
                    }
                    ForEach(model.report.results) { result in
                        VStack(alignment:.leading,spacing:14) {
                            HStack { Text(result.sourceName).font(.system(size:11,weight:.semibold)).foregroundStyle(.secondary).lineLimit(1);if let page=result.page { NashiPill(title:"Page \(page)") };Spacer();Button { model.copy(result) } label:{Label(model.copied.contains(result.id) ? "Copied":"Copy",systemImage:model.copied.contains(result.id) ? "checkmark":"doc.on.doc")}.buttonStyle(NashiButtonStyle(.secondary,compact:true)) }
                            Text(result.text).font(.system(size:13)).textSelection(.enabled).frame(maxWidth:.infinity,alignment:.leading)
                        }.padding(18).nashiCard(radius:18)
                    }
                    ForEach(model.report.errors,id:\.self) { Text($0).font(.caption).foregroundStyle(.orange) }
                    if let error=model.error { LText(error).font(.caption).foregroundStyle(.orange) }
                }.padding(20)
            }
            Divider()
            if model.inputs.contains(where:{FileCategory.detect($0) == .pdf}) { SecureField("PDF password (if needed)",text:$model.password).textFieldStyle(.roundedBorder).padding(.horizontal,20).padding(.top,15) }
            HStack { Button("Scan again") { model.scan() }.disabled(model.busy).buttonStyle(NashiButtonStyle(.quiet,compact:true));Spacer();NashiPill(title:"\(model.report.results.count) found");Button("Save report…") { model.saveReport() }.disabled(model.busy).buttonStyle(NashiButtonStyle(.secondary,compact:true));Button(model.copiedAll ? "Copied all":"Copy all") { model.copyAll() }.disabled(model.report.results.isEmpty).buttonStyle(NashiButtonStyle(.primary,compact:true)) }.padding(20)
        }.frame(minWidth:530,minHeight:440).background(NashiPalette.canvas).tint(.nashi).nashiAnimation(value:model.busy).nashiAnimation(value:model.copiedAll).onAppear { model.scan() }.onDisappear { model.token.cancel() }
    }
}
