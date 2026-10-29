import AppKit
import SwiftUI

/// ToolPanel.close also covers the custom close button and Command-W.
struct PDFEditorCloseGuard:NSViewRepresentable {
    let enabled:Bool
    let hasChanges:Bool
    final class GuardView:NSView {
        var enabled=false
        var hasChanges=false
        weak var panel:ToolPanel?
        override func viewDidMoveToWindow() {
            super.viewDidMoveToWindow()
            panel?.shouldClose=nil
            panel=window as? ToolPanel
            panel?.shouldClose={ [weak self] in
                guard let self,self.enabled,self.hasChanges else{return true}
                let alert=NSAlert();alert.messageText=L10n.text("Discard PDF changes?")
                alert.informativeText=L10n.text("Your organized PDF has not been saved. Your original files are unchanged.")
                alert.addButton(withTitle:L10n.text("Keep editing"));alert.addButton(withTitle:L10n.text("Discard changes"))
                return alert.runModal() == .alertSecondButtonReturn
            }
        }
    }
    func makeNSView(context:Context)->GuardView { let view=GuardView();view.enabled=enabled;view.hasChanges=hasChanges;return view }
    func updateNSView(_ view:GuardView,context:Context) { view.enabled=enabled;view.hasChanges=hasChanges }
    static func dismantleNSView(_ view:GuardView,coordinator:()) { view.panel?.shouldClose=nil }
}
