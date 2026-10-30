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

    func makeArea(destination:URL) throws -> Area {
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
                let destination=uniqueDestination(for:child,in:area.destination)
                // moveItem refuses an existing destination, including a file
                // created by another process after our name check.
                try manager.moveItem(at:child,to:destination)
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
        if (try? source.resourceValues(forKeys:[.isDirectoryKey]).isDirectory) != true {
            let stem=source.deletingPathExtension().lastPathComponent,ext=source.pathExtension.lowercased()
            var destination=OutputFiles.unique(for:parent.appendingPathComponent(source.lastPathComponent),extension:source.pathExtension,directory:parent)
            var number=2
            while occupied(destination) {
                destination=parent.appendingPathComponent("\(stem) \(number)").appendingPathExtension(ext);number += 1
            }
            return destination
        }
        var destination=parent.appendingPathComponent(source.lastPathComponent,isDirectory:true),number=2
        while occupied(destination) {
            destination=parent.appendingPathComponent("\(source.lastPathComponent) \(number)",isDirectory:true);number += 1
        }
        return destination
    }

    func cleanUp() { for area in areas { try? manager.removeItem(at:area.directory) } }
}
