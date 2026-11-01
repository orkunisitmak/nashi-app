import Foundation

/// Neutral values leave the corresponding image adjustment unchanged.
/// Tonal/color amounts use -1...1, exposure uses EV and gamma uses its actual exponent.
public struct ImageAdjustments: Codable, Sendable {
    public var exposure: Double = 0
    public var brightness: Double = 0
    public var contrast: Double = 1
    public var highlights: Double = 0
    public var shadows: Double = 0
    public var whites: Double = 0
    public var blacks: Double = 0
    public var gamma: Double = 1
    public var temperature: Double = 0
    public var tint: Double = 0
    public var saturation: Double = 1
    public var vibrance: Double = 0
    public var hue: Double = 0
    public var clarity: Double = 0
    public var texture: Double = 0
    public var fade: Double = 0
    public var blur: Double = 0
    public var sharpen: Double = 0
    public var dehaze: Double = 0
    public var grain: Double = 0
    public var noiseReduction: Double = 0
    public var vignette: Double = 0
    public init() {}
}

public struct ImagePoint: Codable, Sendable, Equatable {
    public var x: Double
    public var y: Double
    public init(x: Double, y: Double) { self.x = x; self.y = y }
}

public enum ImageBackdrop: String, Codable, CaseIterable, Sendable { case solid, gradient, image }
public struct ImageFrameOptions: Codable, Sendable {
    public var backdrop: ImageBackdrop = .solid
    public var backgroundColor: String = "#FFFFFF"
    public var gradientColors: [String] = ["#FF6B79", "#FFD85C", "#895CE0"]
    public var gradientAngle: Double = 45
    public var backgroundImage: URL? = nil
    public var backgroundBlur: Double = 0
    public var padding: Int = 160
    public var cornerRadius: Double = 24
    public var shadowRadius: Double = 24
    public var shadowOpacity: Double = 0.25
    public var aspectRatio: Double? = nil
    public var width: Int? = nil
    public var height: Int? = nil
    /// Normalized image position. (0,0) is top left, (1,1) bottom right.
    public var position: ImagePoint = .init(x: 0.5, y: 0.5)
    public var imageScale: Double = 1
    public init() {}
}

public enum CollageLayout: String, Codable, CaseIterable, Sendable { case grid, row, column, featured }
public enum ImageFit: String, Codable, CaseIterable, Sendable { case fit, fill }
public struct CollageOptions: Codable, Sendable {
    public var layout: CollageLayout = .grid
    public var columns: Int = 2
    public var width: Int = 2048
    public var height: Int? = nil
    public var aspectRatio: Double = 1
    public var spacing: Int = 12
    public var outerPadding: Int = 16
    public var cornerRadius: Double = 0
    public var backgroundColor: String = "#FFFFFF"
    public var backgroundImage: URL? = nil
    public var imageFit: ImageFit = .fill
    /// An empty list retains selection order; a nonempty list must be a permutation.
    public var imageOrder: [Int] = []
    public init() {}
}

public enum ImageAnnotationKind: String, Codable, CaseIterable, Sendable {
    case text, arrow, line, rectangle, ellipse, freehand, highlight, number, callout, magnifier, image
}
public struct ImageAnnotation: Codable, Sendable {
    public var kind: ImageAnnotationKind
    /// All image-edit coordinates are pixels measured from the image's top left.
    public var rect: EditRect
    public var points: [ImagePoint] = []
    public var text: String = ""
    public var color: String = "#FF5200"
    public var fillColor: String? = nil
    public var opacity: Double = 1
    public var strokeWidth: Double = 3
    public var fontSize: Double = 24
    public var fontFamily: String = "system"
    public var number: Int = 1
    public var imageURL: URL? = nil
    public var sourceRect: EditRect? = nil
    public var magnification: Double = 2
    public init(kind: ImageAnnotationKind, rect: EditRect) { self.kind = kind; self.rect = rect }
    /// Omitted keys keep the defaults above. CLI options overlay defaults on
    /// nested objects, but each annotation in the array is decoded as given.
    public init(from decoder: Decoder) throws {
        let values = try decoder.container(keyedBy: CodingKeys.self)
        kind = try values.decode(ImageAnnotationKind.self, forKey: .kind)
        rect = try values.decode(EditRect.self, forKey: .rect)
        points = try values.decodeIfPresent([ImagePoint].self, forKey: .points) ?? points
        text = try values.decodeIfPresent(String.self, forKey: .text) ?? text
        color = try values.decodeIfPresent(String.self, forKey: .color) ?? color
        fillColor = try values.decodeIfPresent(String.self, forKey: .fillColor)
        opacity = try values.decodeIfPresent(Double.self, forKey: .opacity) ?? opacity
        strokeWidth = try values.decodeIfPresent(Double.self, forKey: .strokeWidth) ?? strokeWidth
        fontSize = try values.decodeIfPresent(Double.self, forKey: .fontSize) ?? fontSize
        fontFamily = try values.decodeIfPresent(String.self, forKey: .fontFamily) ?? fontFamily
        number = try values.decodeIfPresent(Int.self, forKey: .number) ?? number
        imageURL = try values.decodeIfPresent(URL.self, forKey: .imageURL)
        sourceRect = try values.decodeIfPresent(EditRect.self, forKey: .sourceRect)
        magnification = try values.decodeIfPresent(Double.self, forKey: .magnification) ?? magnification
    }
}

public enum ImageRedactionStyle: String, Codable, CaseIterable, Sendable { case solid, blur, pixelate }
public struct ImageRedactionOptions: Codable, Sendable {
    public var style: ImageRedactionStyle = .solid
    public var color: String = "#000000"
    public var blurRadius: Double = 24
    public var pixelSize: Double = 16
    public var detectFaces: Bool = false
    public var detectText: Bool = false
    public var detectionPadding: Double = 0.02
    public init() {}
}

public struct DetectedImageRegion: Codable, Sendable {
    public enum Kind: String, Codable, Sendable { case face, text }
    public var kind: Kind
    public var rect: EditRect
    public var confidence: Double
    public var text: String?
    public init(kind: Kind, rect: EditRect, confidence: Double, text: String? = nil) {
        self.kind = kind; self.rect = rect; self.confidence = confidence; self.text = text
    }
}

public struct ImageMetadataOptions: Codable, Sendable {
    /// Paths correspond to ImageEngine.metadata(), for example "{TIFF}.Artist".
    public var updates: [String: String] = [:]
    public var removals: [String] = []
    public var removeAll: Bool = false
    public init() {}
}
