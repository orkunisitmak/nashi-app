import Foundation

/// Nashi writes results beside the original file unless the person picks another
/// folder. A saved path is used only while it still is a directory, so a deleted,
/// unmounted or replaced path cannot receive output by accident.
public enum OutputFolder {
    /// UserDefaults key shared by Settings and the conversion path.
    public static let defaultsKey = "outputFolderPath"

    /// The saved folder, or nil when nothing was chosen or the path is gone.
    public static func resolve(path: String?, fileManager: FileManager = .default) -> URL? {
        guard let path, !path.isEmpty, path.hasPrefix("/") else { return nil }
        var isDirectory: ObjCBool = false
        guard fileManager.fileExists(atPath: path, isDirectory: &isDirectory), isDirectory.boolValue else { return nil }
        return URL(fileURLWithPath: path, isDirectory: true).standardizedFileURL
    }
}
