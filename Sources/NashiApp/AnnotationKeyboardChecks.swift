import AppKit

@MainActor enum AnnotationKeyboardChecks {
    static func run() throws -> [String] {
        var checks:[String]=[]
        for (flags,expected) in [(NSEvent.ModifierFlags.command,EditorKeyboardCommand.undo),([.command,.shift],.redo)] {
            guard let event=NSEvent.keyEvent(with:.keyDown,location:.zero,modifierFlags:flags,timestamp:0,windowNumber:0,context:nil,characters:"z",charactersIgnoringModifiers:"z",isARepeat:false,keyCode:6),EditorKeyboardCommand.decode(event)==expected else { throw UIAuditError("Annotation undo/redo shortcut did not decode") }
            var invoked=false
            let text=NSTextView();text.isEditable=true
            guard expected.route(event,responder:text,perform:{_ in invoked=true;return true}) === event,!invoked else { throw UIAuditError("Annotation shortcut intercepted text editing") }
            guard expected.route(event,responder:nil,perform:{$0==expected}) == nil else { throw UIAuditError("Annotation shortcut did not reach the editor") }
            checks.append(expected == .undo ? "Annotation Undo routes and preserves text input":"Annotation Redo routes and preserves text input")
        }
        return checks
    }
}
