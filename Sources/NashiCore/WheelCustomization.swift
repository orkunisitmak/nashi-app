import Foundation

/// A stand-in drag payload for the Settings wheel preview; the paths never exist on disk.
public struct WheelSample:Identifiable,Hashable,Sendable {
    public let id:String
    public let extensions:[String]
    public var title:String { let name=extensions[0]=="gz" ? "GZIP":extensions[0].uppercased();return extensions.count>1 ? "\(extensions.count) × \(name)":name }
    public var inputs:[URL] { extensions.enumerated().map{URL(fileURLWithPath:"/Nashi Wheel Sample/sample-\($0.offset).\($0.element)")} }
    init(_ extensions:String...) { self.extensions=extensions;id=extensions.joined(separator:"+") }
}

/// Which wheel items people can hide, and how the Settings preview shows each of them.
public enum WheelCustomization {
    public static let convertTargets:[String]={ let targets=Set(FeatureCatalog.conversionMatrix.values.joined());return FeatureCatalog.formatGroups.flatMap(\.1).filter(targets.contains) }()
    public static let formatGroups:[(String,[String])]=FeatureCatalog.formatGroups.map{($0.0,$0.1.filter(convertTargets.contains))}.filter{!$0.1.isEmpty}
    public static let toolGroups:[(String,[ToolKind])]=[
        ("General",[.compress,.removeMetadata,.readQRCode]),
        ("Images",[.editImage,.annotateImage,.frameImage,.cropImage,.redactImage,.createCollage]),
        ("Video",[.trimVideo,.cropVideo,.muteVideo,.changeVideoSpeed,.splitVideo,.joinVideos,.videoSnapshots,.redactVideo]),
        ("Audio",[.trimAudio,.normalizeAudio,.audioChannels,.audioToVideo,.redactAudio]),
        ("Documents",[.createPDF,.mergePDF,.splitPDF,.organizePDF]),
        ("Archives",[.extractArchive])
    ]
    // Together these samples show every target and tool at least once (WheelCustomizationTests).
    public static let convertSamples=[WheelSample("png"),WheelSample("mp3"),WheelSample("mp4"),WheelSample("gif"),WheelSample("docx"),WheelSample("pdf"),WheelSample("xlsx"),WheelSample("srt"),WheelSample("vtt"),WheelSample("zip"),WheelSample("tar")]
    public static let toolSamples=[WheelSample("png"),WheelSample("mp3"),WheelSample("mp4"),WheelSample("pdf"),WheelSample("zip"),WheelSample("png","png"),WheelSample("mp4","mp4"),WheelSample("pdf","pdf")]
    /// Drops hidden ids and keeps order. `keepOne` returns everything instead of an empty wheel.
    public static func visible<T>(_ items:[T],hidden:Set<String>,id:(T)->String,keepOne:Bool=false)->[T] {
        let shown=items.filter{!hidden.contains(id($0))}
        return keepOne && shown.isEmpty ? items:shown
    }
    public static func firstConvertSample(showing format:String)->WheelSample? { convertSamples.first{FeatureCatalog.formats(for:$0.inputs).contains(format)} }
    public static func firstToolSample(showing tool:ToolKind)->WheelSample? { toolSamples.first{FeatureCatalog.tools(for:$0.inputs).contains(tool)} }
}
