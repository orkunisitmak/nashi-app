import Foundation
import Darwin

/// Streaming local archive I/O. Entries are materialized only inside a newly
/// created staging directory; links are deferred until regular files are done.
enum ArchiveCodec {
    private static let api = ArchiveAPI()
    private struct DeferredLink { let destination: URL; let target: String; let hard: Bool }

    struct ExtractionLimits {
        var maximumEntries=100_000
        var maximumBytes:Int64=20*1024*1024*1024
    }
    static func extract(_ input: URL, into root: URL, password: String = "", token: CancellationToken, limits:ExtractionLimits=ExtractionLimits()) throws {
        let a = try api.get()
        guard let reader = a.readNew() else { throw NashiError.failed("Could not initialize the archive reader.") }
        defer { _ = a.readFree(reader) }
        // Only in-process decompressors: other filters launch programs from PATH.
        for filter in [a.readSupportGzip, a.readSupportBzip2, a.readSupportXz, a.readSupportLzma, a.readSupportCompress] {
            try a.check(filter(reader), handle: reader)
        }
        let gzip = ["gz", "gzip"].contains(input.pathExtension.lowercased())
        if gzip {
            let prefix = try Data(contentsOf: input, options: .mappedIfSafe).prefix(2)
            guard prefix.elementsEqual([0x1f, 0x8b]) else { throw NashiError.invalidInput("This file is not a GZIP stream.") }
            // A .gz holds a tarball or one file; never sniff the payload as ZIP, mtree, etc.
            try a.check(a.readSupportTar(reader), handle: reader)
            try a.check(a.readSupportRaw(reader), handle: reader)
            try a.check(a.readSupportEmpty(reader), handle: reader)
        } else {
            try a.check(a.readSupportFormats(reader), handle: reader)
        }
        if !password.isEmpty { try a.check(password.withCString { a.readPassphrase(reader, $0) }, handle: reader) }
        try a.check(input.path.withCString { a.readOpen(reader, $0, 128 * 1024) }, handle: reader)
        let fm = FileManager.default
        try fm.createDirectory(at: root, withIntermediateDirectories: true)
        var links: [DeferredLink] = []
        var entries = 0
        var expandedBytes:Int64=0
        var buffer = [UInt8](repeating: 0, count: 128 * 1024)
        while true {
            try token.check()
            var entry: OpaquePointer?
            let status = a.readNext(reader, &entry)
            if status == 1 { break }
            try a.check(status, handle: reader)
            guard let entry, let rawName = a.entryPath(entry) else { throw NashiError.failed("The archive contains an unnamed entry.") }
            var name = String(cString: rawName)
            if a.format(reader) == 0x90000 {
                // A gzip stream can hold one ordinary file instead of a tar.
                name = input.deletingPathExtension().lastPathComponent
                if ["tgz", "gzip"].contains(input.pathExtension.lowercased()) && name.isEmpty { name = "data" }
            }
            entries += 1
            guard entries<=limits.maximumEntries else {throw NashiError.invalidInput("The archive has too many entries (100,000 limit).")}
            let destination = try safeURL(name, inside: root)
            if destination.standardizedFileURL == root.standardizedFileURL {
                _ = a.readSkip(reader)
                continue
            }
            try validateDestination(destination, root: root)
            if let link = a.entryHardlink(entry) {
                _ = try safeURL(String(cString: link), inside: root)
                links.append(.init(destination: destination, target: String(cString: link), hard: true))
                _ = a.readSkip(reader)
                continue
            }
            if let link = a.entrySymlink(entry) {
                let target = String(cString: link)
                try validateSymbolicTarget(target, at: destination, root: root)
                links.append(.init(destination: destination, target: target, hard: false))
                _ = a.readSkip(reader)
                continue
            }
            let kind = a.entryFiletype(entry) & 0o170000
            if kind == 0o040000 {
                try fm.createDirectory(at: destination, withIntermediateDirectories: true)
                continue
            }
            guard kind == 0o100000 || kind == 0 else {
                throw NashiError.invalidInput("The archive contains an unsupported special file: \(name)")
            }
            try fm.createDirectory(at: destination.deletingLastPathComponent(), withIntermediateDirectories: true)
            guard !fm.fileExists(atPath: destination.path) else {
                throw NashiError.invalidInput("The archive contains duplicate file paths: \(name)")
            }
            guard fm.createFile(atPath: destination.path, contents: nil) else { throw NashiError.failed("Could not create \(name).") }
            let file = try FileHandle(forWritingTo: destination)
            do {
                while true {
                    try token.check()
                    let count = buffer.withUnsafeMutableBytes { a.readData(reader, $0.baseAddress, $0.count) }
                    if count == 0 { break }
                    guard count > 0 else { throw a.failure(reader) }
                    guard Int64(count)<=limits.maximumBytes-expandedBytes else {throw NashiError.invalidInput("The archive expands beyond the 20 GB safety limit.")}
                    expandedBytes += Int64(count)
                    try file.write(contentsOf: Data(buffer[0..<count]))
                }
                try file.close()
            } catch { try? file.close(); throw error }
            let mode = a.entryPerm(entry) & 0o777
            if mode != 0 { try? fm.setAttributes([.posixPermissions: NSNumber(value: mode)], ofItemAtPath: destination.path) }
            let mtime = a.entryMtime(entry)
            if mtime > 0 { try? fm.setAttributes([.modificationDate: Date(timeIntervalSince1970: TimeInterval(mtime))], ofItemAtPath: destination.path) }
        }
        guard entries > 0 else {
            // Empty ZIP/TAR archives are valid and extract to an empty folder.
            if a.format(reader) == 0 { throw NashiError.invalidInput("This file is not a readable archive.") }
            // A gzip of an empty file restores that empty file, not an empty folder.
            if gzip && a.format(reader) == 0x60000 {
                let destination = try safeURL(input.deletingPathExtension().lastPathComponent, inside: root)
                guard destination.standardizedFileURL != root.standardizedFileURL else { return }
                try validateDestination(destination, root: root)
                guard fm.createFile(atPath: destination.path, contents: nil) else { throw NashiError.failed("Could not create \(destination.lastPathComponent).") }
            }
            return
        }
        for link in links.filter(\.hard) {
            try token.check()
            try validateDestination(link.destination, root: root)
            let target = try safeURL(link.target, inside: root)
            try validateDestination(target, root: root)
            let attributes = try fm.attributesOfItem(atPath: target.path)
            guard attributes[.type] as? FileAttributeType == .typeRegular else {
                throw NashiError.invalidInput("An archive hard link does not point to a regular file.")
            }
            try fm.createDirectory(at: link.destination.deletingLastPathComponent(), withIntermediateDirectories: true)
            try fm.linkItem(at: target, to: link.destination)
        }
        for link in links.filter({ !$0.hard }) {
            try token.check()
            // Deferred entries must be checked against the filesystem as it
            // exists now, before creating directories or the link itself.
            try validateDestination(link.destination, root: root)
            try validateSymbolicTarget(link.target, at: link.destination, root: root)
            try fm.createDirectory(at: link.destination.deletingLastPathComponent(), withIntermediateDirectories: true)
            try fm.createSymbolicLink(atPath: link.destination.path, withDestinationPath: link.target)
        }
        // A chain of individually relative links must also stay inside the root.
        for link in links where !link.hard {
            guard contains(link.destination.resolvingSymlinksInPath(), root: root) else {
                throw NashiError.invalidInput("An archive link points outside the extraction folder.")
            }
        }
    }

    static func create(from root: URL, at output: URL, format: String, compressionLevel: Int = 6, token: CancellationToken) throws {
        let a = try api.get()
        guard let writer = a.writeNew() else { throw NashiError.failed("Could not initialize the archive writer.") }
        defer { _ = a.writeFree(writer) }
        switch format {
        case "zip": try a.check(a.writeZIP(writer), handle: writer)
        case "tar": try a.check(a.writeTar(writer), handle: writer)
        case "gz", "gzip", "tgz":
            try a.check(a.writeTar(writer), handle: writer)
            try a.check(a.writeGzip(writer), handle: writer)
        default: throw NashiError.unsupported("Cannot create \(format.uppercased()) archives.")
        }
        if ["zip","gz","gzip","tgz"].contains(format) {
            let level = max(0,min(9,compressionLevel))
            let setting = format == "zip" ? (level == 0 ? "zip:compression=store" : "zip:compression-level=\(level)") : "gzip:compression-level=\(level)"
            try a.check(setting.withCString { a.writeOptions(writer,$0) }, handle:writer)
        }
        try a.check(output.path.withCString { a.writeOpen(writer, $0) }, handle: writer)
        let fm = FileManager.default
        // URL enumeration can return /private/var paths while URL canonicalizing
        // deliberately presents /var. Ask for relative names instead of slicing
        // absolute paths with potentially different macOS aliases.
        guard let enumerator = fm.enumerator(atPath: root.path) else {
            throw NashiError.failed("Could not read the extracted archive.")
        }
        let children = enumerator.compactMap { $0 as? String }.sorted()
        let bufferSize = 128 * 1024
        for name in children {
            try token.check()
            let item = root.appendingPathComponent(name)
            _ = try safeURL(name, inside: root)
            let attributes = try fm.attributesOfItem(atPath: item.path)
            guard let entry = a.entryNew() else { throw NashiError.failed("Could not create an archive entry.") }
            defer { a.entryFree(entry) }
            name.withCString { a.entrySetPath(entry, $0) }
            a.entrySetPerm(entry, (attributes[.posixPermissions] as? NSNumber)?.uint32Value ?? 0o644)
            a.entrySetMtime(entry, Int64((attributes[.modificationDate] as? Date ?? Date()).timeIntervalSince1970), 0)
            let type = attributes[.type] as? FileAttributeType
            switch type {
            case .typeDirectory:
                a.entrySetType(entry, 0o040000); a.entrySetSize(entry, 0)
            case .typeSymbolicLink:
                let target = try fm.destinationOfSymbolicLink(atPath: item.path)
                try validateSymbolicTarget(target, at: item, root: root)
                a.entrySetType(entry, 0o120000); a.entrySetSize(entry, 0)
                target.withCString { a.entrySetSymlink(entry, $0) }
            case .typeRegular:
                a.entrySetType(entry, 0o100000)
                a.entrySetSize(entry, (attributes[.size] as? NSNumber)?.int64Value ?? 0)
            default: throw NashiError.invalidInput("Cannot archive the special file \(name).")
            }
            try a.check(a.writeHeader(writer, entry), handle: writer)
            if type == .typeRegular {
                let file = try FileHandle(forReadingFrom: item)
                defer { try? file.close() }
                while let data = try file.read(upToCount: bufferSize), !data.isEmpty {
                    try token.check()
                    try data.withUnsafeBytes { bytes in
                        var offset = 0
                        while offset < bytes.count {
                            let written = a.writeData(writer, bytes.baseAddress?.advanced(by: offset), bytes.count - offset)
                            guard written > 0 else { throw a.failure(writer) }
                            offset += written
                        }
                    }
                }
            }
            try a.check(a.writeFinish(writer), handle: writer)
        }
        try a.check(a.writeClose(writer), handle: writer)
    }

    static func safeURL(_ path: String, inside root: URL) throws -> URL {
        let normalized = path.replacingOccurrences(of: "\\", with: "/")
        guard !normalized.hasPrefix("/"), !normalized.contains("\0"),
              normalized.range(of: "^[A-Za-z]:", options: .regularExpression) == nil else {
            throw NashiError.invalidInput("The archive contains an absolute path.")
        }
        let parts = normalized.split(separator: "/", omittingEmptySubsequences: true)
        guard !parts.contains("..") else { throw NashiError.invalidInput("The archive contains a path outside its extraction folder.") }
        let relative = parts.filter { $0 != "." }.joined(separator: "/")
        let url = root.appendingPathComponent(relative).standardizedFileURL
        guard contains(url, root: root) else { throw NashiError.invalidInput("An archive path leaves its extraction folder.") }
        return url
    }

    private static func contains(_ url: URL, root: URL) -> Bool {
        let base = root.resolvingSymlinksInPath().standardizedFileURL.path
        let path = url.resolvingSymlinksInPath().standardizedFileURL.path
        return path == base || path.hasPrefix(base + "/")
    }
    private static func validateDestination(_ destination: URL, root: URL) throws {
        let base=root.standardizedFileURL
        let prefix=base.path+"/",path=destination.standardizedFileURL.path
        guard path.hasPrefix(prefix) else { throw NashiError.invalidInput("An archive path leaves its extraction folder.") }
        var item=base
        // lstat does not follow a link, including a dangling one. Only inspect
        // components below the trusted staging root, preserving macOS aliases
        // such as /var without permitting archive-created directory aliases.
        for component in path.dropFirst(prefix.count).split(separator:"/") {
            item.appendPathComponent(String(component))
            var attributes=stat()
            if lstat(item.path,&attributes) != 0 {
                let code=errno
                if code==ENOENT { return }
                throw POSIXError(POSIXErrorCode(rawValue:code) ?? .EIO)
            }
            guard attributes.st_mode & S_IFMT != S_IFLNK else {
                throw NashiError.invalidInput("An archive entry cannot be created through a symbolic link.")
            }
        }
    }
    private static func validateSymbolicTarget(_ target: String, at destination: URL, root: URL) throws {
        guard !target.hasPrefix("/"), !target.contains("\\"), target.range(of: "^[A-Za-z]:", options: .regularExpression) == nil,
              contains(destination.deletingLastPathComponent().appendingPathComponent(target).standardizedFileURL, root: root) else {
            throw NashiError.invalidInput("An archive link points outside the extraction folder.")
        }
    }

    /// libarchive ships with macOS and supports ZIP, TAR, gzip and RAR without
    /// running a shell or trusting a textual archive listing.
    private final class ArchiveAPI: @unchecked Sendable {
        typealias New = @convention(c) () -> OpaquePointer?
        typealias Call = @convention(c) (OpaquePointer?) -> Int32
        typealias OpenRead = @convention(c) (OpaquePointer?, UnsafePointer<CChar>?, Int) -> Int32
        typealias OpenWrite = @convention(c) (OpaquePointer?, UnsafePointer<CChar>?) -> Int32
        typealias Next = @convention(c) (OpaquePointer?, UnsafeMutablePointer<OpaquePointer?>?) -> Int32
        typealias Read = @convention(c) (OpaquePointer?, UnsafeMutableRawPointer?, Int) -> Int
        typealias Write = @convention(c) (OpaquePointer?, UnsafeRawPointer?, Int) -> Int
        typealias StringGet = @convention(c) (OpaquePointer?) -> UnsafePointer<CChar>?
        typealias UIntGet = @convention(c) (OpaquePointer?) -> UInt32
        typealias TimeGet = @convention(c) (OpaquePointer?) -> Int64
        typealias Header = @convention(c) (OpaquePointer?, OpaquePointer?) -> Int32
        typealias SetString = @convention(c) (OpaquePointer?, UnsafePointer<CChar>?) -> Void
        typealias SetUInt = @convention(c) (OpaquePointer?, UInt32) -> Void
        typealias SetInt64 = @convention(c) (OpaquePointer?, Int64) -> Void
        typealias SetTime = @convention(c) (OpaquePointer?, Int64, Int) -> Void
        typealias FreeEntry = @convention(c) (OpaquePointer?) -> Void
        let library: UnsafeMutableRawPointer?
        init() { library = dlopen("/usr/lib/libarchive.2.dylib", RTLD_NOW | RTLD_LOCAL) }
        func get() throws -> ArchiveAPI { guard library != nil else { throw NashiError.missingDependency("macOS libarchive") }; return self }
        private func symbol<T>(_ name: String, _ type: T.Type) -> T {
            // All symbols below are part of libarchive's stable public ABI.
            guard let pointer = dlsym(library, name) else { preconditionFailure("Missing libarchive symbol: \(name)") }
            return unsafeBitCast(pointer, to: type)
        }
        var readNew: New { symbol("archive_read_new", New.self) }
        var readSupportGzip: Call { symbol("archive_read_support_filter_gzip", Call.self) }
        var readSupportBzip2: Call { symbol("archive_read_support_filter_bzip2", Call.self) }
        var readSupportXz: Call { symbol("archive_read_support_filter_xz", Call.self) }
        var readSupportLzma: Call { symbol("archive_read_support_filter_lzma", Call.self) }
        var readSupportCompress: Call { symbol("archive_read_support_filter_compress", Call.self) }
        var readSupportFormats: Call { symbol("archive_read_support_format_all", Call.self) }
        var readSupportRaw: Call { symbol("archive_read_support_format_raw", Call.self) }
        var readSupportTar: Call { symbol("archive_read_support_format_tar", Call.self) }
        var readSupportEmpty: Call { symbol("archive_read_support_format_empty", Call.self) }
        var readPassphrase: OpenWrite { symbol("archive_read_add_passphrase", OpenWrite.self) }
        var readOpen: OpenRead { symbol("archive_read_open_filename", OpenRead.self) }
        var readNext: Next { symbol("archive_read_next_header", Next.self) }
        var readData: Read { symbol("archive_read_data", Read.self) }
        var readSkip: Call { symbol("archive_read_data_skip", Call.self) }
        var readFree: Call { symbol("archive_read_free", Call.self) }
        var format: Call { symbol("archive_format", Call.self) }
        var errorString: StringGet { symbol("archive_error_string", StringGet.self) }
        var entryPath: StringGet { symbol("archive_entry_pathname_utf8", StringGet.self) }
        var entrySymlink: StringGet { symbol("archive_entry_symlink_utf8", StringGet.self) }
        var entryHardlink: StringGet { symbol("archive_entry_hardlink_utf8", StringGet.self) }
        var entryFiletype: UIntGet { symbol("archive_entry_filetype", UIntGet.self) }
        var entryPerm: UIntGet { symbol("archive_entry_perm", UIntGet.self) }
        var entryMtime: TimeGet { symbol("archive_entry_mtime", TimeGet.self) }
        var writeNew: New { symbol("archive_write_new", New.self) }
        var writeZIP: Call { symbol("archive_write_set_format_zip", Call.self) }
        var writeTar: Call { symbol("archive_write_set_format_pax_restricted", Call.self) }
        var writeGzip: Call { symbol("archive_write_add_filter_gzip", Call.self) }
        var writeOptions: OpenWrite { symbol("archive_write_set_options", OpenWrite.self) }
        var writeOpen: OpenWrite { symbol("archive_write_open_filename", OpenWrite.self) }
        var writeHeader: Header { symbol("archive_write_header", Header.self) }
        var writeData: Write { symbol("archive_write_data", Write.self) }
        var writeFinish: Call { symbol("archive_write_finish_entry", Call.self) }
        var writeClose: Call { symbol("archive_write_close", Call.self) }
        var writeFree: Call { symbol("archive_write_free", Call.self) }
        var entryNew: New { symbol("archive_entry_new", New.self) }
        var entryFree: FreeEntry { symbol("archive_entry_free", FreeEntry.self) }
        var entrySetPath: SetString { symbol("archive_entry_set_pathname_utf8", SetString.self) }
        var entrySetSymlink: SetString { symbol("archive_entry_set_symlink_utf8", SetString.self) }
        var entrySetPerm: SetUInt { symbol("archive_entry_set_perm", SetUInt.self) }
        var entrySetType: SetUInt { symbol("archive_entry_set_filetype", SetUInt.self) }
        var entrySetSize: SetInt64 { symbol("archive_entry_set_size", SetInt64.self) }
        var entrySetMtime: SetTime { symbol("archive_entry_set_mtime", SetTime.self) }
        func check(_ status: Int32, handle: OpaquePointer?) throws {
            guard status >= -20 else { throw failure(handle) }
        }
        func failure(_ handle: OpaquePointer?) -> NashiError {
            let message = errorString(handle).map { String(cString: $0) } ?? "The archive could not be read or written."
            return .failed(message)
        }
    }
}
