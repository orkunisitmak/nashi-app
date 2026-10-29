import AppKit
import NashiCore

enum WebLinkPayload {
    private static let htmlTypes=[NSPasteboard.PasteboardType.html,.init("org.chromium.image-html"),.init("text/html")]
    private static let urlTypes=[NSPasteboard.PasteboardType.URL,.string,.init("text/uri-list")]
    private static let listTypes=[NSPasteboard.PasteboardType("WebURLsWithTitlesPboardType"),.init("dyn.ah62d4rv4gu8zs3pcnzme2641rf4guzdmsv0gn64uqm10c6xenv61a3k")]
    private static let baseTypes=[NSPasteboard.PasteboardType("org.chromium.source-url"),.init("org.chromium.chromium-renderer-initiated-drag")]
    static var pasteboardTypes:[NSPasteboard.PasteboardType] { [.fileURL]+urlTypes+htmlTypes+listTypes }
    // NSItemProvider requires UTI identifiers, unlike NSPasteboard's legacy names.
    private static var providerFormats:[NSPasteboard.PasteboardType] {
        [.fileURL,.URL,.string,.html,.init("org.chromium.image-html")]+listTypes.filter{$0.rawValue.hasPrefix("dyn.")}+baseTypes
    }
    static var providerTypes:[String] { providerFormats.map(\.rawValue) }

    static func urls(from board:NSPasteboard)->[URL] {
        // Finder files win over an incidental icon or HTML representation.
        if board.types?.contains(.fileURL)==true,
           let files=board.readObjects(forClasses:[NSURL.self],options:[.urlReadingFileURLsOnly:true]) as? [URL],!files.isEmpty { return files }
        let result=Representations()
        for type in urlTypes+htmlTypes+listTypes+baseTypes {
            if let value=board.data(forType:type) { result.add(value,type:type.rawValue) }
        }
        // Browsers can put the link and image in separate items.
        for item in (board.pasteboardItems ?? []).prefix(16) {
            for type in urlTypes+htmlTypes+listTypes+baseTypes {
                if let value=item.data(forType:type) { result.add(value,type:type.rawValue) }
            }
        }
        return result.resolved()
    }
    // Main-thread only: a newer drop supersedes loads still pending from an older one.
    private static var providerLoad=0
    static func urls(from providers:[NSItemProvider],completion:@escaping([URL])->Void) {
        let group=DispatchGroup(),result=Representations()
        providerLoad += 1;let load=providerLoad
        for (index,provider) in providers.enumerated() {
            // Browser images can advertise a file promise without supplying a
            // usable local URL. Resolve their web representations as a fallback;
            // real Finder files still take precedence in resolved().
            for type in providerFormats where provider.hasItemConformingToTypeIdentifier(type.rawValue) {
                group.enter()
                provider.loadDataRepresentation(forTypeIdentifier:type.rawValue) { data,_ in
                    if let data { result.add(data,type:type.rawValue,index:index) }
                    group.leave()
                }
            }
        }
        group.notify(queue:.main) { if load==providerLoad { completion(result.resolved()) } }
    }
    static func clipboardLink()->URL? { urls(from:.general).first(where:{!$0.isFileURL}) }

    private final class Representations:@unchecked Sendable {
        private let lock=NSLock()
        private var values:[(Int,String,Data)]=[]
        func add(_ data:Data,type:String,index:Int=0) {
            guard data.count<=131_072 else { return }
            lock.lock();defer{lock.unlock()};values.append((index,type,data))
        }
        func resolved()->[URL] {
            lock.lock();let snapshot=values.sorted{$0.0<$1.0};lock.unlock()
            var files:[URL]=[],urls:[String]=[],html:[String]=[],bases:[String]=[]
            for (_,type,data) in snapshot {
                if listTypes.contains(where:{$0.rawValue==type}),
                   let list=(try? PropertyListSerialization.propertyList(from:data,format:nil)) as? [[String]],let links=list.first { urls += links;continue }
                guard let text=String(data:data,encoding:.utf8) ?? String(data:data,encoding:.utf16) else { continue }
                if type==NSPasteboard.PasteboardType.fileURL.rawValue { if let url=URL(string:text),url.isFileURL { files.append(url) };continue }
                if htmlTypes.contains(where:{$0.rawValue==type}) { html.append(text) }
                else if baseTypes.contains(where:{$0.rawValue==type}) { bases.append(text) }
                else if type=="text/uri-list" { urls += text.components(separatedBy:.newlines).filter{!$0.isEmpty && !$0.hasPrefix("#")} }
                else { urls.append(text) }
            }
            if !files.isEmpty { return files }
            return WebMediaDragPayload.parse(urls:urls,html:html,baseURLs:bases)?.urls ?? []
        }
    }
}
