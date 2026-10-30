import Foundation

public enum FileCategory: String, Codable, CaseIterable, Sendable {
    case image, video, audio, pdf, document, presentation, spreadsheet, ebook, subtitle, archive, other
    public var title: String {
        switch self {
        case .image: return "Images"
        case .video: return "Video"
        case .audio: return "Audio"
        case .pdf: return "PDF"
        case .document: return "Documents"
        case .presentation: return "Slides"
        case .spreadsheet: return "Spreadsheets"
        case .ebook: return "EPUB"
        case .subtitle: return "Subtitles"
        case .archive: return "Archives"
        case .other: return "Files"
        }
    }
    public static func detect(_ url: URL) -> FileCategory {
        let ext = url.pathExtension.lowercased()
        if ["jpg", "jpeg", "png", "webp", "heic", "heif", "tif", "tiff", "avif", "bmp", "svg"].contains(ext) { return .image }
        if ["mp4", "mov", "mkv", "webm", "avi", "wmv", "m4v", "gif"].contains(ext) { return .video }
        if ["mp3", "m4a", "wav", "flac", "ogg", "opus", "aiff", "aif", "wma", "aac"].contains(ext) { return .audio }
        if ext == "pdf" { return .pdf }
        if ["doc", "docx", "pages", "md", "markdown", "txt", "rtf", "rtfd", "textclipping", "html", "htm", "odt"].contains(ext) { return .document }
        if ["ppt", "pptx", "key"].contains(ext) { return .presentation }
        if ["xlsx", "xls", "ods", "csv", "tsv", "numbers"].contains(ext) { return .spreadsheet }
        if ext == "epub" { return .ebook }
        if ["srt", "vtt"].contains(ext) { return .subtitle }
        if ["zip", "tar", "gz", "gzip", "tgz", "rar"].contains(ext) { return .archive }
        return .other
    }
}

public enum ToolKind: String, Codable, CaseIterable, Sendable {
    case compress, removeMetadata, editImage, annotateImage, frameImage, cropImage
    case normalizeAudio, muteVideo, trimVideo, splitPDF, organizePDF, extractArchive
    case createPDF, mergePDF, cropVideo, changeVideoSpeed, joinVideos, videoSnapshots
    case splitVideo, audioToVideo, trimAudio, audioChannels, createCollage
    case redactImage, redactVideo, redactAudio, readQRCode
    public var title: String {
        switch self {
        case .compress: return "Compress"
        case .removeMetadata: return "Remove Metadata"
        case .editImage: return "Edit Image"
        case .annotateImage: return "Annotate"
        case .frameImage: return "Frame Image"
        case .cropImage: return "Crop Image"
        case .normalizeAudio: return "Normalize Audio"
        case .muteVideo: return "Mute Video"
        case .trimVideo: return "Trim Video"
        case .splitPDF: return "Split PDF"
        case .organizePDF: return "Organize PDF"
        case .extractArchive: return "Extract Archive"
        case .createPDF: return "Create PDF"
        case .mergePDF: return "Merge PDFs"
        case .cropVideo: return "Crop Video"
        case .changeVideoSpeed: return "Change Speed"
        case .joinVideos: return "Join Videos"
        case .videoSnapshots: return "Video Snapshots"
        case .splitVideo: return "Split Video"
        case .audioToVideo: return "Audio to Video"
        case .trimAudio: return "Trim Audio"
        case .audioChannels: return "Audio Channels"
        case .createCollage: return "Create Collage"
        case .redactImage: return "Redact Image"
        case .redactVideo: return "Redact Video"
        case .redactAudio: return "Redact Audio"
        case .readQRCode: return "Read QR Codes"
        }
    }
}

public struct EditRect: Codable, Sendable, Equatable {
    public var x: Double; public var y: Double; public var width: Double; public var height: Double
    public init(x: Double, y: Double, width: Double, height: Double) { self.x=x; self.y=y; self.width=width; self.height=height }
}
public struct Annotation: Codable, Sendable {
    public var rect: EditRect
    public var text: String
    public var color: String
    public var kind: String
    public init(rect: EditRect, text: String = "", color: String = "#FF830A", kind: String = "text") { self.rect=rect; self.text=text; self.color=color; self.kind=kind }
}
public struct TimeRange: Codable, Sendable, Equatable {
    public var start: Double
    public var end: Double
    public init(start: Double, end: Double) { self.start=start; self.end=end }
}
public struct VideoRedaction: Codable, Sendable {
    public var rect: EditRect
    public var start: Double
    public var end: Double?
    public init(rect: EditRect, start: Double = 0, end: Double? = nil) { self.rect=rect;self.start=start;self.end=end }
}

public struct OperationOptions: Codable, Sendable {
    public var quality: Double = 0.8
    public var audioBitrate: Int = 256
    public var audioSampleDepth: String = "matchSource"
    public var videoMaxHeight: Int = 0
    public var gifFrameRate: Int = 15
    public var gifMaxWidth: Int = 960
    public var preserveMetadata: Bool = true
    public var compressionPreset: String = "balanced"
    public var targetBytes: Int64? = nil
    /// Percentage reduction: 20 means at most 80% of the original bytes.
    public var percentage: Double? = nil
    public var width: Int? = nil
    public var height: Int? = nil
    public var crop: EditRect? = nil
    public var rotation: Int = 0
    public var brightness: Double = 0
    public var contrast: Double = 1
    public var saturation: Double = 1
    public var flipHorizontal: Bool = false
    public var start: Double = 0
    public var end: Double? = nil
    public var speed: Double = 1
    public var channels: Int = 2
    public var targetLoudness: Double = -16
    public var loudnessRange: Double = 11
    public var truePeak: Double = -1.5
    public var leftVolume: Double = 1
    public var rightVolume: Double = 1
    public var splitPoints: [Double] = []
    public var equalClips: Int? = nil
    /// Zero-based segment indices after sorting the split points, before any removals.
    public var discardedClips: [Int] = []
    public var joinSplitClips: Bool = false
    public var audioRedactions: [TimeRange] = []
    public var trimSilence: Bool = false
    public var silenceThresholdDb: Double = -50
    public var visualizationStyle: String = "waveform"
    public var visualizationImage: URL? = nil
    public var videoRedactions: [VideoRedaction] = []
    public var splitInterval: Double = 30
    public var snapshotTimes: [Double] = [0]
    public var pageRanges: String = ""
    public var pagesPerFile: Int = 1
    public var pdfPageOrder: [Int] = []
    public var pdfPageRotations: [Int: Int] = [:]
    public var pdfUniformWidth: Bool = false
    public var pdfWorkspacePages: [PDFWorkspacePage] = []
    public var pdfExtractSelection: Bool = false
    public var pdfWidthMode: PDFWidthMode = .original
    public var pdfCustomWidth: Double = 595.276
    public var epubLayout: String = "reflowable"
    public var pdfOfficeLayout: String = "editable"
    public var dpi: Int = 300
    public var password: String = ""
    public var metadata: [String: String] = [:]
    public var metadataRemoveAll: Bool = false
    public var annotations: [Annotation] = []
    public var imageAdjustments: ImageAdjustments = .init()
    public var imageFrame: ImageFrameOptions? = nil
    public var collage: CollageOptions? = nil
    public var imageAnnotations: [ImageAnnotation] = []
    public var imageRedaction: ImageRedactionOptions? = nil
    public var imageMetadata: ImageMetadataOptions? = nil
    public var redactions: [EditRect] = []
    public var redactionMode: String = "blur"
    public var backgroundColor: String = "#FFFFFF"
    public var padding: Int = 32
    public var cornerRadius: Int = 16
    public var collageColumns: Int = 2
    public var audioMode: String = "bleep"
    public var outputDirectory: URL? = nil
    public var revealInFinder: Bool = true
    public init() {}
}

public enum Operation: Sendable {
    case convert(String)
    case tool(ToolKind)
    public var title: String { switch self { case .convert(let f): return "Convert to \(f.uppercased())"; case .tool(let t): return t.title } }
}

public struct ConversionRequest: Sendable {
    public var inputs: [URL]
    public var operation: Operation
    public var options: OperationOptions
    public init(inputs: [URL], operation: Operation, options: OperationOptions = .init()) { self.inputs=inputs; self.operation=operation; self.options=options }
}

public enum NashiError: LocalizedError {
    case invalidInput(String), unsupported(String), failed(String), cancelled, missingDependency(String)
    public var errorDescription: String? {
        switch self {
        case .invalidInput(let s), .unsupported(let s), .failed(let s): return s
        case .cancelled: return "The operation was cancelled."
        case .missingDependency(let s): return "Required local component is missing: \(s)"
        }
    }
}

/// A fraction in 0...1, or OperationProgress.indeterminate when the current
/// stage has no measurable total. Labels describe real work, not a timer estimate.
public typealias ProgressHandler = @Sendable (Double, String) -> Void

public final class CancellationToken: @unchecked Sendable {
    private let lock = NSLock()
    private var cancelled = false
    public init() {}
    public func cancel() { lock.lock(); cancelled=true; lock.unlock() }
    public var isCancelled: Bool { lock.lock(); defer { lock.unlock() }; return cancelled }
    public func check() throws { if isCancelled { throw NashiError.cancelled } }
}
