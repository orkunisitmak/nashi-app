import Foundation

/// A quick action the projects wheel offers for a dragged developer folder.
public struct ProjectLaunch: Equatable, Sendable {
    public enum Kind: Equatable, Sendable {
        /// Opens the folder with the app that has this bundle identifier.
        case openWith(bundleIdentifier: String)
        /// Runs these commands one after another in a new terminal window inside the folder.
        case terminal(commands: [[String]])
    }
    public let title: String
    public let symbol: String
    public let kind: Kind
}

/// Detects what kind of project a folder holds and how to start it.
/// It only reads marker files; nothing in the folder is executed here.
public enum ProjectLauncher {
    public static let visualStudioCode = "com.microsoft.VSCode"
    private static let manifestByteLimit = 1 << 20

    /// The single folder the projects wheel works on; files, links and multiple items have none.
    public static func projectFolder(in urls: [URL]) -> URL? {
        guard urls.count == 1, let url = urls.first, url.isFileURL,
              (try? url.resourceValues(forKeys: [.isDirectoryKey]).isDirectory) == true else { return nil }
        return url
    }

    /// Editor and assistant actions first, then the run commands the folder's manifests support.
    public static func launches(for folder: URL, isInstalled: (String) -> Bool) -> [ProjectLaunch] {
        var items: [ProjectLaunch] = []
        if isInstalled(visualStudioCode) {
            items.append(ProjectLaunch(title: "VS Code", symbol: "chevron.left.forwardslash.chevron.right",
                                       kind: .openWith(bundleIdentifier: visualStudioCode)))
        }
        items.append(ProjectLaunch(title: "Claude Code", symbol: "sparkles", kind: .terminal(commands: [["claude"]])))
        return items + runCommands(for: folder)
    }

    static func runCommands(for folder: URL) -> [ProjectLaunch] {
        func has(_ name: String) -> Bool { FileManager.default.fileExists(atPath: folder.appendingPathComponent(name).path) }
        func run(_ command: [String], after setup: [[String]] = []) -> ProjectLaunch {
            ProjectLaunch(title: command.joined(separator: " "), symbol: "play.fill", kind: .terminal(commands: setup + [command]))
        }
        var items: [ProjectLaunch] = []
        if let package = packageManifest(in: folder) {
            let manager = packageManager(for: package, in: folder)
            // A fresh clone has no dependencies yet; install them before the first run.
            let setup = has("node_modules") ? [] : [[manager, "install"]]
            for script in ["dev", "start"] where package.scripts.contains(script) {
                items.append(run([manager, "run", script], after: setup))
            }
        }
        if let pubspec = text(of: folder.appendingPathComponent("pubspec.yaml")) {
            items.append(run(pubspec.contains("sdk: flutter") ? ["flutter", "run"] : ["dart", "run"]))
        }
        if has("Cargo.toml") { items.append(run(["cargo", "run"])) }
        if has("go.mod") { items.append(run(["go", "run", "."])) }
        if has("Package.swift") { items.append(run(["swift", "run"])) }
        return items
    }

    struct PackageManifest {
        var scripts: Set<String>
        var packageManager: String?
    }

    static func packageManifest(in folder: URL) -> PackageManifest? {
        guard let source = text(of: folder.appendingPathComponent("package.json")),
              let json = (try? JSONSerialization.jsonObject(with: Data(source.utf8))) as? [String: Any] else { return nil }
        let scripts = (json["scripts"] as? [String: Any]).map { Set($0.keys) } ?? []
        return PackageManifest(scripts: scripts, packageManager: json["packageManager"] as? String)
    }

    /// Corepack's `packageManager` field wins; otherwise the lock file tells which tool the project uses.
    static func packageManager(for manifest: PackageManifest, in folder: URL) -> String {
        let known = ["npm", "pnpm", "yarn", "bun"]
        if let declared = manifest.packageManager?.split(separator: "@").first.map(String.init), known.contains(declared) {
            return declared
        }
        let lockFiles = [("pnpm-lock.yaml", "pnpm"), ("yarn.lock", "yarn"), ("bun.lockb", "bun"), ("bun.lock", "bun")]
        return lockFiles.first { FileManager.default.fileExists(atPath: folder.appendingPathComponent($0.0).path) }?.1 ?? "npm"
    }

    private static func text(of file: URL) -> String? {
        guard let size = (try? file.resourceValues(forKeys: [.fileSizeKey]))?.fileSize, size <= manifestByteLimit,
              let data = try? Data(contentsOf: file) else { return nil }
        return String(data: data, encoding: .utf8)
    }

    /// A `.command` script for a terminal app. It deletes itself, enters the folder, runs the
    /// commands in the user's login shell (so PATH from nvm, Flutter or Homebrew applies) and then
    /// leaves that shell open in the folder. Every word is single-quoted, so folder names and
    /// arguments are never interpreted by a shell.
    public static func terminalScript(commands: [[String]], in folder: URL) -> String {
        let line = commands.map { $0.map(shellQuoted).joined(separator: " ") }.joined(separator: " && ")
        return """
        #!/bin/sh
        rm -f -- "$0"
        cd -- \(shellQuoted(folder.path)) || exit 1
        trap : INT
        "${SHELL:-/bin/zsh}" -lic \(shellQuoted(line))
        exec "${SHELL:-/bin/zsh}" -l

        """
    }

    public static func shellQuoted(_ word: String) -> String {
        "'" + word.replacingOccurrences(of: "'", with: "'\\''") + "'"
    }
}
