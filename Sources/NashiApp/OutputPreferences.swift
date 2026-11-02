import AppKit
import NashiCore

/// Shared state for "Save copies" in Settings › General and Settings › Folders.
@MainActor enum OutputPreferences {
    static var savedPath:String { UserDefaults.standard.string(forKey:OutputFolder.defaultsKey) ?? "" }
    static var folder:URL? { OutputFolder.resolve(path:savedPath.isEmpty ? nil:savedPath) }
    /// True when a folder was chosen but has since been deleted or unmounted.
    static var isMissing:Bool { !savedPath.isEmpty && folder == nil }

    static func set(_ url:URL) throws {
        // Record the grant as well, so the conversion can open a security scope
        // when macOS hands the task to another process.
        try FolderAccessPreferences.add([url])
        UserDefaults.standard.set(url.path,forKey:OutputFolder.defaultsKey)
    }

    static func clear() { UserDefaults.standard.removeObject(forKey:OutputFolder.defaultsKey) }

    /// Asks once for a folder and saves it. Returns false when cancelled.
    static func choose() -> Bool {
        let panel=NSOpenPanel()
        panel.title=L10n.text("Save Copies To")
        panel.prompt=L10n.text("Save Here")
        panel.message=L10n.text("New files are written here instead of beside the originals.")
        panel.canChooseFiles=false;panel.canChooseDirectories=true;panel.canCreateDirectories=true;panel.allowsMultipleSelection=false
        panel.directoryURL=folder ?? FileManager.default.urls(for:.downloadsDirectory,in:.userDomainMask)[0]
        guard panel.runModal() == .OK,let url=panel.url else { return false }
        do { try set(url);return true } catch { return false }
    }
}
