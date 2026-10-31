import Foundation

/// A failed or cancelled job must not leave unreported partial exports. Each
/// destination gets a private sibling staging directory, allowing publication
/// by same-volume renames without replacing an existing file.
final class ExportTransaction {
    final class Area {
        let directory:URL
        let destination:URL
        var outputs:[URL]=[]
        init(directory:URL,destination:URL) { self.directory=directory;self.destination=destination }
    }

    private let manager=FileManager.default
    private var areas:[Area]=[]
    /// Output folders this transaction had to create; cleanUp removes them again
    /// if they are still empty, so a failed export leaves no folders behind.
    private var createdFolders:[URL]=[]

    func makeArea(destination:URL) throws -> Area {
        var folder=destination.standardizedFileURL
        while folder.path != "/",(try? manager.attributesOfItem(atPath:folder.path))==nil {
            createdFolders.append(folder);folder=folder.deletingLastPathComponent()
        }
        try manager.createDirectory(at:destination,withIntermediateDirectories:true)
        let directory=destination.appendingPathComponent(".Nashi-\(UUID().uuidString)",isDirectory:true)
        try manager.createDirectory(at:directory,withIntermediateDirectories:false,attributes:[.posixPermissions:0o700])
        let area=Area(directory:directory,destination:destination)
        areas.append(area)
        return area
    }

    func record(_ outputs:[URL],in area:Area) throws {
        try OutputFiles.validate(outputs)
        let prefix=area.directory.standardizedFileURL.resolvingSymlinksInPath().path+"/"
        guard outputs.allSatisfy({$0.standardizedFileURL.resolvingSymlinksInPath().path.hasPrefix(prefix)}) else {
            throw NashiError.failed("An export could not be staged safely. Your originals were not changed.")
        }
        area.outputs=outputs
    }

    /// Test seam: runs after a destination name is chosen, just before the move.
    var willMove:((URL)->Void)?

    /// Moves `source` to `destination` only if that name is still free. moveItem
    /// checks first and then renames, so a name another writer takes in between
    /// is replaced. RENAME_EXCL checks and renames in one step; false means the
    /// name was taken and the caller picks the next one.
    private func claim(_ source:URL,as destination:URL) throws -> Bool {
        willMove?(destination)
        let code=source.withUnsafeFileSystemRepresentation { from in
            destination.withUnsafeFileSystemRepresentation { to in renamex_np(from!,to!,UInt32(RENAME_EXCL))==0 ? 0:errno }
        }
        if code==0 { return true }
        if code==EEXIST { return false }
        // Volumes without RENAME_EXCL (exFAT returns ENOTSUP) keep the previous
        // behavior, which also reports other errors as before.
        try manager.moveItem(at:source,to:destination)
        return true
    }

    func publish(token:CancellationToken) throws -> [URL] {
        var published:[URL]=[],outputs:[URL]=[]
        var complete=false
        defer { if !complete { for url in published.reversed() { try? manager.removeItem(at:url) } } }
        for area in areas {
            try token.check()
            let prefix=area.directory.standardizedFileURL.resolvingSymlinksInPath().path+"/"
            let relativeOutputs=area.outputs.map { output in
                let path=output.standardizedFileURL.resolvingSymlinksInPath().path
                return String(path.dropFirst(prefix.count)).split(separator:"/",omittingEmptySubsequences:true).map(String.init)
            }
            let roots=Set(relativeOutputs.compactMap(\.first))
            let children=try manager.contentsOfDirectory(at:area.directory,includingPropertiesForKeys:[.isDirectoryKey]).filter { roots.contains($0.lastPathComponent) }.sorted{$0.lastPathComponent<$1.lastPathComponent}
            guard Set(children.map(\.lastPathComponent))==roots else { throw NashiError.failed("A completed export could not be located.") }
            var destinations:[String:URL]=[:]
            for child in children {
                try token.check()
                var destination=uniqueDestination(for:child,in:area.destination)
                while try !claim(child,as:destination) { destination=uniqueDestination(for:child,in:area.destination) }
                published.append(destination)
                destinations[child.lastPathComponent]=destination
            }
            for components in relativeOutputs {
                guard let first=components.first,let root=destinations[first] else {
                    throw NashiError.failed("A completed export could not be located.")
                }
                outputs.append(components.dropFirst().reduce(root){$0.appendingPathComponent(String($1))})
            }
        }
        try token.check()
        try OutputFiles.validate(outputs)
        complete=true
        return outputs
    }

    private func uniqueDestination(for source:URL,in parent:URL) -> URL {
        // lstat-based: a dangling link still occupies its name and moveItem refuses it.
        let occupied={ (url:URL) in (try? self.manager.attributesOfItem(atPath:url.path)) != nil }
        let isFile=(try? source.resourceValues(forKeys:[.isDirectoryKey]).isDirectory) != true
        let stem=isFile ? source.deletingPathExtension().lastPathComponent:source.lastPathComponent
        let ext=isFile ? source.pathExtension.lowercased():""
        var destination:URL,number=1
        repeat {
            let tail=(number==1 ? "":" \(number)")+(ext.isEmpty ? "":".\(ext)")
            // macOS limits a name to 255 UTF-16 units of its decomposed form. A
            // staged name at that limit is shortened so " 2" still fits.
            var base=stem
            while !base.isEmpty,(base+tail).decomposedStringWithCanonicalMapping.utf16.count>255 { base.removeLast() }
            destination=parent.appendingPathComponent(base+tail,isDirectory:!isFile);number += 1
        } while occupied(destination)
        return destination
    }

    func cleanUp() {
        for area in areas { try? manager.removeItem(at:area.directory) }
        // rmdir removes only empty folders: published outputs and anything
        // another writer put there keep their folder.
        for folder in createdFolders.sorted(by:{$0.path.count>$1.path.count}) { _=rmdir(folder.path) }
    }
}
