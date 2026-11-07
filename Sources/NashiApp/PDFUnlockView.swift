import SwiftUI
import PDFKit
import NashiCore

@MainActor final class PDFUnlockModel:ObservableObject {
    @Published var password=""
    @Published private(set) var needsPassword=false
    func open(_ input:URL)throws->PDFDocument {
        do { let document=try PDFEngine.open(input,password:password);needsPassword=false;return document }
        catch { needsPassword=PDFDocument(url:input)?.isLocked == true;throw error }
    }
    func reset() { password="";needsPassword=false }
}

struct PDFUnlockView:View {
    @ObservedObject var model:PDFUnlockModel
    let busy:Bool
    let onUnlock:()->Void
    var body:some View {
        VStack(alignment:.leading,spacing:12) {
            LText("This PDF is password protected. Enter its password.").font(.callout)
            SecureField("PDF password",text:$model.password).onSubmit(onUnlock).disabled(busy)
            Button(action:onUnlock) { LText("Unlock PDF") }.disabled(busy || model.password.isEmpty)
        }
    }
}
