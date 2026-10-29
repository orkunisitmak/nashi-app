import Foundation

public struct ProcessResult: Sendable {
    public let output: Data
    public let errorOutput: Data
    public var text: String { String(decoding: output, as: UTF8.self) }
}

public enum ProcessRunner {
    @discardableResult
    public static func run(_ executable: URL, _ arguments: [String], directory: URL? = nil, token: CancellationToken = .init(), environment: [String: String] = [:], onOutput: (@Sendable (Data) -> Void)? = nil) throws -> ProcessResult {
        try token.check()
        let process = Process()
        process.executableURL = executable
        // Conversion inputs stay local even if a media container references an
        // external playlist segment. Local pipes/data/encrypted files still work.
        process.arguments = ["ffmpeg","ffprobe"].contains(executable.lastPathComponent) && !arguments.contains("-protocol_whitelist") ? ["-protocol_whitelist","file,pipe,crypto,data"]+arguments:arguments
        process.currentDirectoryURL = directory
        process.environment = ProcessInfo.processInfo.environment.merging(environment) { _, new in new }
        let output = Pipe(), errors = Pipe()
        process.standardOutput = output; process.standardError = errors
        // Drain both streams concurrently: codec diagnostics can exceed a pipe buffer.
        let group = DispatchGroup()
        let captured = Capture()
        group.enter()
        DispatchQueue.global().async {
            defer { group.leave() }
            // availableData returns as soon as bytes arrive; read(upToCount:) can
            // wait for a full buffer and leave a short progress stream stuck at 0%.
            while true {
                let chunk = output.fileHandleForReading.availableData
                guard !chunk.isEmpty else { break }
                captured.output.append(chunk)
                if onOutput != nil && captured.output.count>2_000_000 {
                    captured.output.removeFirst(captured.output.count-2_000_000)
                }
                onOutput?(chunk)
            }
        }
        group.enter()
        DispatchQueue.global().async { captured.errors = errors.fileHandleForReading.readDataToEndOfFile(); group.leave() }
        do { try process.run() } catch {
            try? output.fileHandleForWriting.close(); try? errors.fileHandleForWriting.close()
            group.wait()
            throw error
        }
        running.insert(process); defer { running.remove(process) }
        while process.isRunning {
            if token.isCancelled {
                process.terminate()
                let deadline = Date().addingTimeInterval(2)
                while process.isRunning && Date() < deadline { Thread.sleep(forTimeInterval: 0.02) }
                if process.isRunning { kill(process.processIdentifier, SIGKILL) }
                break
            }
            Thread.sleep(forTimeInterval: 0.03)
        }
        process.waitUntilExit(); group.wait()
        try token.check()
        guard process.terminationStatus == 0 else {
            let diagnostic = String(decoding: captured.errors.suffix(6000), as: UTF8.self).trimmingCharacters(in: .whitespacesAndNewlines)
            throw NashiError.failed(diagnostic.isEmpty ? "\(executable.lastPathComponent) exited with code \(process.terminationStatus)." : diagnostic)
        }
        return ProcessResult(output: captured.output, errorOutput: captured.errors)
    }
    private final class Capture: @unchecked Sendable { var output=Data(); var errors=Data() }

    /// Stops every child still running, for app termination: a cancelled token is
    /// only observed by the polling thread, which does not outlive the app.
    public static func terminateAll() {
        for process in running.snapshot() where process.isRunning { process.terminate() }
    }
    private static let running = Running()
    private final class Running: @unchecked Sendable {
        private let lock = NSLock()
        private var processes: [ObjectIdentifier: Process] = [:]
        func insert(_ process: Process) { lock.lock(); processes[ObjectIdentifier(process)] = process; lock.unlock() }
        func remove(_ process: Process) { lock.lock(); processes[ObjectIdentifier(process)] = nil; lock.unlock() }
        func snapshot() -> [Process] { lock.lock(); defer { lock.unlock() }; return Array(processes.values) }
    }
}

public enum Dependencies {
    public static func executable(_ name: String) throws -> URL {
        let fm = FileManager.default
        let variable = "NASHI_" + name.uppercased()
        var paths: [String] = []
        if let override = ProcessInfo.processInfo.environment[variable] ?? ProcessInfo.processInfo.environment["TANGERINE_"+name.uppercased()] { paths.append(override) }
        if let resource = Bundle.main.resourceURL {
            paths.append(resource.appendingPathComponent("Tools/\(name)").path)
            if name == "exiftool" { paths.append(resource.appendingPathComponent("Tools/ExifTool/exiftool").path) }
            if name == "soffice" { paths.append(resource.appendingPathComponent("Tools/LibreOffice.app/Contents/MacOS/soffice").path) }
        }
        if let external=ExternalToolSetup.executable(name) { paths.append(external.path) }
        #if DEBUG
        let root = URL(fileURLWithPath: #filePath).deletingLastPathComponent().deletingLastPathComponent().deletingLastPathComponent()
        paths.append(root.appendingPathComponent("Tools/\(name)").path)
        if name == "exiftool" { paths.append(root.appendingPathComponent("Tools/ExifTool/exiftool").path) }
        if name == "soffice" { paths.append(root.appendingPathComponent("Tools/LibreOffice.app/Contents/MacOS/soffice").path) }
        #endif
        paths += ["/opt/homebrew/bin/\(name)", "/usr/local/bin/\(name)", "/usr/bin/\(name)"]
        if name == "soffice" { paths.append("/Applications/LibreOffice.app/Contents/MacOS/soffice") }
        guard let found = paths.first(where: { path in
            // Search permission makes a directory "executable" too. In
            // particular, ExifTool and exiftool collide on the default macOS
            // filesystem; select the actual script inside that directory.
            let resolved=URL(fileURLWithPath:path).resolvingSymlinksInPath()
            let values=try? resolved.resourceValues(forKeys:[.isRegularFileKey,.fileSizeKey])
            return fm.isExecutableFile(atPath:resolved.path) && values?.isRegularFile == true && (values?.fileSize ?? 0)>0
        }) else { throw NashiError.missingDependency(name) }
        return URL(fileURLWithPath: found)
    }
}

public enum OutputFiles {
    /// Re-encoding an already optimized file must not increase its size or lose
    /// quality. Call only when output format and requested geometry are unchanged.
    static func retainSmallerOriginal(_ input:URL,output:URL,target:Int64?) throws {
        guard input.pathExtension.lowercased()==output.pathExtension.lowercased() else { return }
        let manager=FileManager.default
        let source=input.resolvingSymlinksInPath()
        let original=(try manager.attributesOfItem(atPath:source.path)[.size] as? NSNumber)?.int64Value ?? 0
        let encoded=(try manager.attributesOfItem(atPath:output.path)[.size] as? NSNumber)?.int64Value ?? 0
        guard original>0,encoded>original,target == nil || original<=target! else { return }
        try manager.removeItem(at:output);try manager.copyItem(at:source,to:output)
    }
    public static func unique(for input: URL, extension ext: String, suffix: String = "", directory: URL? = nil) -> URL {
        let dir = directory ?? input.deletingLastPathComponent()
        let stem = input.deletingPathExtension().lastPathComponent + suffix
        let cleanExt = ext.lowercased().trimmingCharacters(in: CharacterSet(charactersIn: "."))
        var candidate = dir.appendingPathComponent(stem).appendingPathExtension(cleanExt)
        var index = 2
        while FileManager.default.fileExists(atPath: candidate.path) {
            candidate = dir.appendingPathComponent("\(stem) \(index)").appendingPathExtension(cleanExt); index += 1
        }
        return candidate
    }
    public static func temporaryDirectory() throws -> URL {
        let url = FileManager.default.temporaryDirectory.appendingPathComponent("Nashi-\(UUID().uuidString)", isDirectory: true)
        try FileManager.default.createDirectory(at: url, withIntermediateDirectories: true)
        return url
    }
    public static func validate(_ urls: [URL]) throws {
        guard !urls.isEmpty else { throw NashiError.failed("No output was produced.") }
        for url in urls {
            let values = try url.resourceValues(forKeys: [.isDirectoryKey, .fileSizeKey])
            guard values.isDirectory == true || (values.fileSize ?? 0) > 0 else { throw NashiError.failed("An empty output was produced: \(url.lastPathComponent)") }
        }
    }
}
