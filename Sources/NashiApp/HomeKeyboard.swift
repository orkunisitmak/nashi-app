import AppKit
@MainActor extension AppState {
    /// Only the Home window calls this router; editors retain their own native shortcuts.
    func handleHomeKey(_ event:NSEvent,window:NSWindow,pasteboard:NSPasteboard = .general)->Bool {
        let flags=event.modifierFlags.intersection([.command,.control,.option,.shift])
        let editing=(window.firstResponder as? NSTextView)?.isEditable == true
        if flags == .command {
            switch event.charactersIgnoringModifiers {
            case "1":libraryVisible=false;return true
            case "2":libraryVisible=true;return true
            case "f":libraryVisible=true;libraryFocusRequest+=1;return true
            case "o":if !working {onChooseFiles?()};return true
            case "v":if !editing {pasteContent(pasteboard);return true}
            default:break
            }
            if [49,36,76].contains(event.keyCode),!editing,!working,!inputs.isEmpty {onOpenWheel?();return true}
        }
        if !libraryVisible,flags.isEmpty,[36,76].contains(event.keyCode),!editing,!(window.firstResponder is NSButton),!working,!inputs.isEmpty {onOpenWheel?();return true}
        guard libraryVisible,flags.isEmpty else{return false}
        // Down from the Library search transfers focus to the matching results.
        if editing,event.keyCode==125 {window.makeFirstResponder(nil);selectedHistoryID=filteredHistory.first?.id;return true}
        guard !editing,!(window.firstResponder is NSButton) else{return false}
        if [125,126].contains(event.keyCode) {navigateLibrary(event.keyCode==125 ? 1:-1);return true}
        if [49,36,76].contains(event.keyCode),let row=filteredHistory.first(where:{$0.id==selectedHistoryID}) ?? filteredHistory.first {
            if event.keyCode==49 {preview(row)}else if !working {
                // Match the disabled Reuse button: a row whose files are all gone keeps the current selection.
                let available=row.outputs.map{URL(fileURLWithPath:$0)}.filter{FileManager.default.fileExists(atPath:$0.path)}
                if !available.isEmpty {inputs=available;libraryVisible=false}
            };return true
        };return false
    }
}
