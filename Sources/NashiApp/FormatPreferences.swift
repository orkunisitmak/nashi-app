import Foundation
import Combine
import AppKit
import NashiCore

enum OutputQuality: String, Codable, CaseIterable, Identifiable {
    case smaller, balanced, higher
    var id:String { rawValue }
    var title:String { switch self { case .smaller:return "Smaller File";case .balanced:return "Balanced";case .higher:return "Higher Quality" } }
    var value:Double { switch self { case .smaller:return 0.45;case .balanced:return 0.8;case .higher:return 0.95 } }
}

struct OutputFormatProfile: Codable, Equatable {
    var quality:OutputQuality = .balanced
    var preserveMetadata=true
    var audioBitrate=256
    var audioSampleDepth="matchSource"
    var videoMaxHeight=0
    var gifFrameRate=15
    var gifMaxWidth=960
    var compressionPreset="balanced"
    var epubLayout="reflowable"
    var pdfOfficeLayout="editable"
    init(format:String="") { if format.lowercased()=="mov" { quality = .higher } }
    private enum CodingKeys:String,CodingKey { case quality,preserveMetadata,audioBitrate,audioSampleDepth,videoMaxHeight,gifFrameRate,gifMaxWidth,compressionPreset,epubLayout,pdfOfficeLayout }
    init(from decoder:Decoder) throws {
        self.init();let c=try decoder.container(keyedBy:CodingKeys.self)
        quality=try c.decodeIfPresent(OutputQuality.self,forKey:.quality) ?? .balanced
        preserveMetadata=try c.decodeIfPresent(Bool.self,forKey:.preserveMetadata) ?? true
        audioBitrate=try c.decodeIfPresent(Int.self,forKey:.audioBitrate) ?? 256
        audioSampleDepth=try c.decodeIfPresent(String.self,forKey:.audioSampleDepth) ?? "matchSource"
        videoMaxHeight=try c.decodeIfPresent(Int.self,forKey:.videoMaxHeight) ?? 0
        gifFrameRate=try c.decodeIfPresent(Int.self,forKey:.gifFrameRate) ?? 15
        gifMaxWidth=try c.decodeIfPresent(Int.self,forKey:.gifMaxWidth) ?? 960
        compressionPreset=try c.decodeIfPresent(String.self,forKey:.compressionPreset) ?? "balanced"
        epubLayout=try c.decodeIfPresent(String.self,forKey:.epubLayout) ?? "reflowable"
        pdfOfficeLayout=try c.decodeIfPresent(String.self,forKey:.pdfOfficeLayout) ?? "editable"
    }
}

@MainActor final class FormatPreferences:ObservableObject {
    static let shared=FormatPreferences()
    private let defaults:UserDefaults
    @Published private(set) var revision=0
    init(defaults:UserDefaults = .standard) { self.defaults=defaults }
    static func normalize(_ format:String)->String {
        let f=format.lowercased()
        return ["jpeg":"jpg","tif":"tiff","heif":"heic","aif":"aiff","gzip":"gz","markdown":"md"][f] ?? f
    }
    private func key(_ format:String)->String { "conversionProfile."+Self.normalize(format) }
    func profile(for format:String)->OutputFormatProfile {
        guard let data=defaults.data(forKey:key(format)),let value=try? JSONDecoder().decode(OutputFormatProfile.self,from:data) else { return .init(format:Self.normalize(format)) }
        return value
    }
    func save(_ profile:OutputFormatProfile,for format:String) {
        guard let data=try? JSONEncoder().encode(profile) else { return }
        defaults.set(data,forKey:key(format));revision += 1
    }
    func restoreDefaults(for format:String) { defaults.removeObject(forKey:key(format));revision += 1 }
    func options(for format:String)->OperationOptions {
        let profile=profile(for:format)
        var options=OperationOptions()
        options.quality=profile.quality.value
        options.preserveMetadata=profile.preserveMetadata
        options.audioBitrate=profile.audioBitrate
        options.audioSampleDepth=profile.audioSampleDepth
        options.videoMaxHeight=profile.videoMaxHeight
        options.gifFrameRate=profile.gifFrameRate
        options.gifMaxWidth=profile.gifMaxWidth
        options.compressionPreset=profile.compressionPreset
        options.epubLayout=profile.epubLayout
        options.pdfOfficeLayout=profile.pdfOfficeLayout
        return options
    }
    var imageCompressionStrength:String { defaults.string(forKey:"defaultImageCompressionStrength") ?? "balanced" }
    var imageCompressionSize:String { defaults.string(forKey:"defaultImageCompressionSize") ?? "original" }
    var videoCompressionStrength:String { defaults.string(forKey:"defaultVideoCompressionStrength") ?? "balanced" }
    func setCompression(_ value:String,key:String) { defaults.set(value,forKey:key);revision += 1 }
    func restoreCompressionDefaults(video:Bool) {
        for key in video ? ["defaultVideoCompressionStrength"] : ["defaultImageCompressionStrength","defaultImageCompressionSize"] { defaults.removeObject(forKey:key) }
        revision += 1
    }
}

struct FormatPreferenceItem:Identifiable,Hashable {
    let id:String
    let group:String
    var title:String { switch id { case "gz":return "GZIP";case "md":return "Markdown";case "textclipping":return "Text Clipping";default:return id.uppercased() } }
    var isInputOnly:Bool { ["svg","ppt","xls","rtf","rtfd","textclipping","rar"].contains(id) }
    var isOutputOnly:Bool { FeatureCatalog.conversionMatrix[id] == nil }
    var compatibilityLabel:String { isInputOnly ? "Input-only format.":isOutputOnly ? "Output-only format.":"Input and output format." }
    var symbol:String { switch group { case "Images":return "photo";case "Audio":return "waveform";case "Video":return "film";case "Archives":return "archivebox";case "Spreadsheets":return "tablecells";case "Subtitles":return "captions.bubble";default:return "doc.text" } }
    static let groups:[(String,[String])]=[
        ("Images",["jpg","png","webp","heic","tiff","svg","avif","bmp"]),
        ("Documents",["pdf","docx","pages","ppt","pptx","txt","md","rtf","rtfd","textclipping","html","epub"]),
        ("Spreadsheets",["xlsx","xls","ods","csv","tsv"]),
        ("Audio",["mp3","m4a","wav","flac","ogg","opus","aiff","wma"]),
        ("Video",["mp4","mov","mkv","webm","avi","wmv","gif"]),
        ("Archives",["zip","tar","gz","rar"]),
        ("Subtitles",["srt","vtt"])
    ]
    static let all:[FormatPreferenceItem]=groups.flatMap { group,formats in formats.map{.init(id:$0,group:group)} }
}

struct AllowedFolder:Codable,Identifiable {
    let path:String
    let bookmark:Data
    var id:String { path }
}

enum FolderAccessPreferences {
    private static let key="folderAccessBookmarks"
    static func grants(defaults:UserDefaults = .standard)->[AllowedFolder] {
        guard let data=defaults.data(forKey:key) else { return [] }
        return (try? JSONDecoder().decode([AllowedFolder].self,from:data)) ?? []
    }
    static func resolved(_ grant:AllowedFolder)->URL? {
        var stale=false
        return try? URL(resolvingBookmarkData:grant.bookmark,options:[.withSecurityScope,.withoutUI],relativeTo:nil,bookmarkDataIsStale:&stale)
    }
    static func add(_ urls:[URL],defaults:UserDefaults = .standard) throws {
        var current=grants(defaults:defaults)
        for url in urls {
            let canonical=url.standardizedFileURL
            guard !current.contains(where:{$0.path==canonical.path}) else { continue }
            let data=try canonical.bookmarkData(options:.withSecurityScope,includingResourceValuesForKeys:nil,relativeTo:nil)
            current.append(.init(path:canonical.path,bookmark:data))
        }
        defaults.set(try JSONEncoder().encode(current),forKey:key)
    }
    static func remove(_ grant:AllowedFolder,defaults:UserDefaults = .standard) {
        let remaining=grants(defaults:defaults).filter{$0.id != grant.id}
        if let data=try? JSONEncoder().encode(remaining) { defaults.set(data,forKey:key) }
    }
    /// The parent folder's bookmark covers all of its descendants.
    /// Each covering grant is opened once for the whole batch, so stack depth does not grow with the file count.
    static func withAccess<T>(to outputs:[URL],defaults:UserDefaults = .standard,_ action:() throws -> T) rethrows -> T {
        let paths=outputs.map{$0.standardizedFileURL.path}
        let folders=grants(defaults:defaults).compactMap(resolved).filter { url in
            let parent=url.standardizedFileURL.path
            return paths.contains { path in path==parent || path.hasPrefix(parent=="/" ? "/":parent+"/") }
        }
        let active=folders.filter{$0.startAccessingSecurityScopedResource()}
        defer { for url in active { url.stopAccessingSecurityScopedResource() } }
        return try action()
    }
}
