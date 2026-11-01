import Foundation

/// One browser card can carry two different resources. Keep its media page and
/// image together instead of silently replacing a video with its poster.
public struct WebMediaDragPayload:Equatable,Sendable {
    public let primaryURL:URL
    public let videoURL:URL?
    public let imageURL:URL?
    public var isInstagramPhotoPost:Bool { Self.isInstagramPhotoPost(primaryURL) }
    public static func isInstagramPhotoPost(_ url:URL)->Bool {
        guard let page=videoPage(url) else { return false }
        return page.host=="www.instagram.com" && page.path.hasPrefix("/p/")
    }
    public var urls:[URL] { if let imageURL,imageURL != primaryURL { return [primaryURL,imageURL] };return [primaryURL] }

    public static func parse(urls:[String],html:[String]=[],baseURLs:[String]=[])->Self? {
        let explicit=unique(urls.prefix(32).compactMap{try? WebDownloadEngine.link($0)})
        let bases=baseURLs.prefix(8).compactMap{try? WebDownloadEngine.link($0)}
        var anchors:[URL]=[],images:[URL]=[],videos:[URL]=[]
        for markup in html.prefix(8) where markup.utf8.count<=131_072 {
            let base=attributes(markup,tag:"base",attribute:"href").compactMap{try? WebDownloadEngine.link(decode($0))}.first ?? bases.first
            func resolve(_ value:String)->URL? {
                // Browsers strip spaces around URL attributes; Foundation would encode them into a relative path.
                let value=decode(value).trimmingCharacters(in:.whitespacesAndNewlines)
                guard let url=URL(string:value,relativeTo:base)?.absoluteURL else { return nil }
                return try? WebDownloadEngine.link(url.absoluteString)
            }
            anchors += attributes(markup,tag:"a",attribute:"href").compactMap(resolve)
            images += attributes(markup,tag:"img",attribute:"src").compactMap(resolve)
            images += attributes(markup,tag:"video",attribute:"poster").compactMap(resolve)
            videos += attributes(markup,tag:"video",attribute:"src").compactMap(resolve)
        }
        let candidates=unique(explicit+anchors)
        let pages=unique(candidates.compactMap(videoPage))
        // A multi-card selection must not silently download an arbitrary video.
        guard pages.count<=1 else { return nil }
        let image=unique(images+explicit.filter(isImage)).first
        if let page=pages.first {
            if isInstagramPhotoPost(page) { return .init(primaryURL:page,videoURL:nil,imageURL:image) }
            // Instagram reel posters are not separate photo downloads.
            if page.host=="www.instagram.com" { return .init(primaryURL:page,videoURL:page,imageURL:nil) }
            return .init(primaryURL:page,videoURL:page,imageURL:image ?? youtubeThumbnail(page))
        }
        if let image,let page=youtubePageFromThumbnail(image) {
            return .init(primaryURL:page,videoURL:page,imageURL:image)
        }
        if let video=(videos+candidates.filter(isVideo)).first {
            return .init(primaryURL:video,videoURL:video,imageURL:image)
        }
        if let image { return .init(primaryURL:image,videoURL:nil,imageURL:image) }
        guard candidates.count==1,let url=candidates.first else { return nil }
        return .init(primaryURL:url,videoURL:nil,imageURL:nil)
    }

    private static func unique(_ urls:[URL])->[URL] {
        var seen=Set<URL>();return urls.filter{seen.insert($0).inserted}
    }
    private static func isImage(_ url:URL)->Bool {
        WebDownloadEngine.directKind(mime:nil,extension:url.pathExtension) == .image || youtubePageFromThumbnail(url) != nil
    }
    private static func isVideo(_ url:URL)->Bool { WebDownloadEngine.directKind(mime:nil,extension:url.pathExtension) == .video }
    private static func youtubeID(_ url:URL)->String? {
        let host=url.host?.lowercased() ?? "",parts=url.path.split(separator:"/").map(String.init)
        var id:String?
        if host=="youtu.be" { id=parts.first }
        else if ["youtube.com","www.youtube.com","m.youtube.com","music.youtube.com"].contains(host) {
            if parts.first=="watch" { id=URLComponents(url:url,resolvingAgainstBaseURL:false)?.queryItems?.first{$0.name=="v"}?.value }
            else if ["shorts","embed","live"].contains(parts.first ?? ""),parts.count>=2 { id=parts[1] }
        }
        return validID(id,length:11...11)
    }
    private static func validID(_ id:String?,length:ClosedRange<Int>)->String? {
        guard let id,length.contains(id.utf8.count),id.utf8.allSatisfy({(65...90).contains($0) || (97...122).contains($0) || (48...57).contains($0) || $0==45 || $0==95}) else { return nil };return id
    }
    private static func youtubePage(_ id:String)->URL { URL(string:"https://www.youtube.com/watch?v=\(id)")! }
    private static func youtubePageFromThumbnail(_ url:URL)->URL? {
        let host=url.host?.lowercased() ?? "",parts=url.path.split(separator:"/").map(String.init)
        guard host=="img.youtube.com" || host=="ytimg.com" || host.hasSuffix(".ytimg.com"),parts.count>=3,
              ["vi","vi_webp","an_webp"].contains(parts[0]),let id=validID(parts[1],length:11...11) else { return nil }
        return youtubePage(id)
    }
    private static func youtubeThumbnail(_ page:URL)->URL? {
        youtubeID(page).flatMap{URL(string:"https://i.ytimg.com/vi/\($0)/hqdefault.jpg")}
    }
    private static func videoPage(_ url:URL)->URL? {
        guard url.port==nil || url.port==443 || url.port==80 else { return nil }
        if let id=youtubeID(url) { return youtubePage(id) }
        let host=url.host?.lowercased() ?? "",parts=url.path.split(separator:"/").map(String.init)
        guard ["instagram.com","www.instagram.com","m.instagram.com"].contains(host) else { return nil }
        // Profile grids include the username before /reel/; share URLs omit it.
        let index=parts.firstIndex(where:{["reel","reels","p","tv"].contains($0)})
        guard let index,index<=1,parts.count==index+2,let id=validID(parts[index+1],length:5...64) else { return nil }
        let kind=parts[index]=="reels" ? "reel":parts[index]
        return URL(string:"https://www.instagram.com/\(kind)/\(id)/")
    }
    private static func attributes(_ html:String,tag:String,attribute:String)->[String] {
        // Only extract text attributes; never instantiate an HTML view or fetch
        // subresources from the untrusted drag fragment.
        let pattern="<"+tag+"\\b[^>]*?\\s"+attribute+"\\s*=\\s*(?:\"([^\"]*)\"|'([^']*)'|([^\\s>]+))"
        guard let regex=try? NSRegularExpression(pattern:pattern,options:[.caseInsensitive,.dotMatchesLineSeparators]) else { return [] }
        return regex.matches(in:html,range:NSRange(html.startIndex...,in:html)).prefix(32).compactMap { match in
            (1...3).compactMap{Range(match.range(at:$0),in:html).map{String(html[$0])}}.first
        }
    }
    private static func decode(_ text:String)->String {
        // One pass over the original text, so an escaped entity ("&amp;lt;") stays literal ("&lt;").
        guard let regex=try? NSRegularExpression(pattern:"&(?:(amp|quot|lt|gt)|#([xX][0-9a-fA-F]+|[0-9]+));") else { return text }
        let named=["amp":"&","quot":"\"","lt":"<","gt":">"]
        var result=text
        for match in regex.matches(in:text,range:NSRange(text.startIndex...,in:text)).reversed() {
            guard let full=Range(match.range,in:result) else { continue }
            if let range=Range(match.range(at:1),in:result),let value=named[String(result[range])] { result.replaceSubrange(full,with:value);continue }
            guard let range=Range(match.range(at:2),in:result) else { continue }
            let code=String(result[range]),hex=code.lowercased().hasPrefix("x")
            if let value=UInt32(hex ? String(code.dropFirst()):code,radix:hex ? 16:10),let scalar=UnicodeScalar(value) { result.replaceSubrange(full,with:String(scalar)) }
        }
        return result
    }
}
