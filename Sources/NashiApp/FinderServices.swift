import AppKit
import NashiCore

/// Finder → Services entry point. The service hands files straight to the same
/// path as a double-clicked document, so no shell, AppleScript or URL handler is
/// involved and the pasteboard payload never becomes a command line.
enum FinderServices {
    /// Must match `NSMessage` in Resources/Info.plist.
    static let message = "nashiOpenFiles"

    static func register(provider: AnyObject) {
        NSApp.servicesProvider = provider
        NSUpdateDynamicServices()
    }

    /// Reads the file names a Services request carries. `NSFilenamesPboardType`
    /// covers every file type on macOS 13+; file URLs are the fallback for
    /// providers that send only `public.file-url`.
    static func paths(from pasteboard: NSPasteboard) -> [String] {
        let legacy = NSPasteboard.PasteboardType("NSFilenamesPboardType")
        if let names = pasteboard.propertyList(forType: legacy) as? [String], !names.isEmpty {
            return names
        }
        let options: [NSPasteboard.ReadingOptionKey: Any] = [.urlReadingFileURLsOnly: true]
        guard let urls = pasteboard.readObjects(forClasses: [NSURL.self], options: options) as? [URL] else { return [] }
        return urls.filter(\.isFileURL).map(\.path)
    }
}
