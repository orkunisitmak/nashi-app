import AppKit
import NashiCore

/// Persist pasted content outside the application bundle, so updates keep it intact.
@MainActor enum ClipboardImport {
    struct Result { var urls:[URL]=[];var link:URL?;var label:String }
    static func read(_ pasteboard:NSPasteboard = .general,directory:URL?=nil)throws->Result {
        if let urls=pasteboard.readObjects(forClasses:[NSURL.self],options:[.urlReadingFileURLsOnly:true]) as? [URL],!urls.isEmpty {
            var seen=Set<URL>();let existing=urls.filter{FileManager.default.fileExists(atPath:$0.path) && seen.insert($0).inserted}
            guard !existing.isEmpty else { throw ImportError.unavailable };return Result(urls:existing,label:"Pasted files")
        }
        let folder=directory ?? FileManager.default.urls(for:.applicationSupportDirectory,in:.userDomainMask)[0].appendingPathComponent("Nashi/Imports",isDirectory:true)
        func destination(_ ext:String)throws->URL {try FileManager.default.createDirectory(at:folder,withIntermediateDirectories:true);let date=DateFormatter();date.dateFormat="yyyy-MM-dd HH-mm-ss";return folder.appendingPathComponent("Clipboard \(date.string(from:Date()))-\(UUID().uuidString.prefix(6)).\(ext)")}
        let pasted=NSImage(pasteboard:pasteboard)
        // Reject by declared size before tiffRepresentation decodes the whole bitmap.
        if let pasted,pasted.representations.contains(where:{Int64($0.pixelsWide)*Int64($0.pixelsHigh)>100_000_000}) { throw ImportError.imageTooLarge }
        if let image=pasted,let tiff=image.tiffRepresentation,let rep=NSBitmapImageRep(data:tiff) {
            guard rep.pixelsWide>0,rep.pixelsHigh>0,Int64(rep.pixelsWide)*Int64(rep.pixelsHigh)<=100_000_000,let png=rep.representation(using:.png,properties:[:]) else { throw ImportError.imageTooLarge }
            let url=try destination("png");try png.write(to:url,options:.atomic);return Result(urls:[url],label:"Pasted image")
        }
        guard let text=pasteboard.string(forType:.string),!text.trimmingCharacters(in:.whitespacesAndNewlines).isEmpty else {throw ImportError.empty}
        let trimmed=text.trimmingCharacters(in:.whitespacesAndNewlines)
        if let url=URL(string:trimmed),["http","https"].contains(url.scheme?.lowercased() ?? ""),url.host != nil,!trimmed.contains(where:{$0.isWhitespace}) {return Result(link:url,label:"Copied link")}
        guard text.utf8.count<=16*1024*1024 else {throw ImportError.textTooLarge}
        let url=try destination("txt");try Data(text.utf8).write(to:url,options:.atomic);return Result(urls:[url],label:"Pasted text")
    }
    enum ImportError:LocalizedError {
        case empty,unavailable,imageTooLarge,textTooLarge
        var errorDescription:String? {switch self {case .empty:return "Copy a file, some text, an image or a public media link first.";case .unavailable:return "Those files are no longer available.";case .imageTooLarge:return "This image is too large to paste. Choose the image file instead.";case .textTooLarge:return "This text is too large to paste. Choose a text file instead."}}
    }
}
