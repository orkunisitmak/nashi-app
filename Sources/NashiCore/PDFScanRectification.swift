import Foundation
import CoreImage
import PDFKit
import Vision

/// Phone pictures of documents (a bill photographed on a desk) arrive as one
/// image in which the paper is tilted, seen in perspective and surrounded by
/// the table. Before OCR and layout, such pages are cut to the paper and
/// flattened, the way a document scanner app does it: lines become
/// horizontal, ruled tables become axis aligned and the desk never turns into
/// page artwork. Born-digital pages and flatbed scans (paper filling the
/// image) are left exactly as they are.
enum PDFScanRectification {
    /// Long side of a flattened page, in points (A4's long side), so that
    /// recognised text gets ordinary document font sizes.
    static let pageLongSide: CGFloat = 842

    /// A copy of `pdf` in which every photographed page is replaced by its
    /// flattened paper, or nil when no page needed it.
    static func rectified(_ pdf: PDFDocument, token: CancellationToken) throws -> PDFDocument? {
        var flattened: [Int: PDFPage] = [:]
        for index in 0..<pdf.pageCount {
            try token.check()
            guard let page = pdf.page(at: index), let flat = try flatten(page, token: token) else { continue }
            flattened[index] = flat
        }
        guard !flattened.isEmpty else { return nil }
        let output = PDFDocument()
        for index in 0..<pdf.pageCount {
            guard let page = flattened[index] ?? (pdf.page(at: index)?.copy() as? PDFPage) else { continue }
            output.insert(page, at: output.pageCount)
        }
        return output
    }

    /// The page's paper, cut out and straightened, or nil when the page has
    /// real text, no paper is found, or the paper already fills the image.
    static func flatten(_ page: PDFPage, token: CancellationToken) throws -> PDFPage? {
        if let text = page.string, !text.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty { return nil }
        guard let image = try render(page, token: token) else { return nil }
        try token.check()
        guard let quad = try paper(in: image.cgImage), worthFlattening(quad, image: image) else { return nil }
        try token.check()
        guard let flat = perspectiveCorrected(image.cgImage, quad: quad) else { return nil }
        return try pdfPage(flat)
    }

    // MARK: Detection

    struct Quad { var topLeft: CGPoint, topRight: CGPoint, bottomLeft: CGPoint, bottomRight: CGPoint }

    private struct Rendered {
        var cgImage: CGImage
        var pixels: [UInt8]
        var width: Int { cgImage.width }
        var height: Int { cgImage.height }
        /// RGB at a pixel, with y measured from the bottom like Vision.
        func at(_ x: Int, _ y: Int) -> (Int, Int, Int) {
            let cx = min(max(0, x), width - 1), cy = min(max(0, height - 1 - y), height - 1)
            let i = (cy * width + cx) * 4
            return (Int(pixels[i]), Int(pixels[i + 1]), Int(pixels[i + 2]))
        }
    }

    /// The page as an image about 3000 pixels on its long side.
    private static func render(_ page: PDFPage, token: CancellationToken) throws -> Rendered? {
        let bounds = PDFOfficeCodec.visualBounds(page)
        guard bounds.width > 0, bounds.height > 0, let cgPage = page.pageRef else { return nil }
        let scale = max(1, min(4, 3000 / max(bounds.width, bounds.height)))
        let width = Int(ceil(bounds.width * scale)), height = Int(ceil(bounds.height * scale))
        var pixels = [UInt8](repeating: 255, count: width * height * 4)
        let image: CGImage? = pixels.withUnsafeMutableBytes { raw in
            guard let context = CGContext(data: raw.baseAddress, width: width, height: height, bitsPerComponent: 8, bytesPerRow: width * 4, space: CGColorSpaceCreateDeviceRGB(), bitmapInfo: CGImageAlphaInfo.noneSkipLast.rawValue) else { return nil }
            context.setFillColor(CGColor(gray: 1, alpha: 1)); context.fill(CGRect(x: 0, y: 0, width: width, height: height))
            context.scaleBy(x: scale, y: scale)
            context.concatenate(cgPage.getDrawingTransform(.cropBox, rect: bounds, rotate: 0, preserveAspectRatio: true))
            context.drawPDFPage(cgPage)
            return context.makeImage()
        }
        try token.check()
        guard let image else { return nil }
        return Rendered(cgImage: image, pixels: pixels)
    }

    /// Corners of the sheet of paper, in pixels with a bottom-left origin.
    private static func paper(in image: CGImage) throws -> Quad? {
        let segmentation = VNDetectDocumentSegmentationRequest()
        let rectangles = VNDetectRectanglesRequest()
        rectangles.minimumSize = 0.4
        rectangles.minimumConfidence = 0.6
        rectangles.maximumObservations = 1
        rectangles.quadratureTolerance = 30
        rectangles.minimumAspectRatio = 0.3
        try VNImageRequestHandler(cgImage: image).perform([segmentation, rectangles])
        let found = ((segmentation.results ?? []).filter { $0.confidence >= 0.6 } + (rectangles.results ?? [])).first
        guard let found else { return nil }
        let w = CGFloat(image.width), h = CGFloat(image.height)
        func pixel(_ p: CGPoint) -> CGPoint { CGPoint(x: p.x * w, y: p.y * h) }
        return Quad(topLeft: pixel(found.topLeft), topRight: pixel(found.topRight), bottomLeft: pixel(found.bottomLeft), bottomRight: pixel(found.bottomRight))
    }

    static func area(_ q: Quad) -> CGFloat {
        let points = [q.topLeft, q.topRight, q.bottomRight, q.bottomLeft]
        var sum: CGFloat = 0
        for i in 0..<4 { let a = points[i], b = points[(i + 1) % 4]; sum += a.x * b.y - b.x * a.y }
        return abs(sum) / 2
    }

    /// Largest angle, in degrees, between a side of the quad and the image axes.
    static func tilt(_ q: Quad) -> CGFloat {
        func angle(_ a: CGPoint, _ b: CGPoint, vertical: Bool) -> CGFloat {
            let dx = b.x - a.x, dy = b.y - a.y
            return abs(atan2(vertical ? dx : dy, vertical ? dy : dx)) * 180 / .pi
        }
        return max(angle(q.topLeft, q.topRight, vertical: false), angle(q.bottomLeft, q.bottomRight, vertical: false),
                   angle(q.bottomLeft, q.topLeft, vertical: true), angle(q.bottomRight, q.topRight, vertical: true))
    }

    /// Only a sheet that is clearly smaller than the picture, or clearly
    /// tilted, and that differs in colour from what surrounds it (paper on a
    /// desk, not a box printed on the page) is flattened.
    private static func worthFlattening(_ q: Quad, image: Rendered) -> Bool {
        let total = CGFloat(image.width * image.height)
        let share = area(q) / total
        guard share >= 0.2, share < 0.97 else { return false }
        guard share < 0.9 || tilt(q) > 1 else { return false }
        return paperDiffersFromSurroundings(q, image: image)
    }

    private static func paperDiffersFromSurroundings(_ q: Quad, image: Rendered) -> Bool {
        func median(_ samples: [(Int, Int, Int)]) -> (Int, Int, Int)? {
            guard !samples.isEmpty else { return nil }
            return (samples.map(\.0).sorted()[samples.count / 2], samples.map(\.1).sorted()[samples.count / 2], samples.map(\.2).sorted()[samples.count / 2])
        }
        func lerp(_ a: CGPoint, _ b: CGPoint, _ t: CGFloat) -> CGPoint { CGPoint(x: a.x + (b.x - a.x) * t, y: a.y + (b.y - a.y) * t) }
        let centre = CGPoint(x: (q.topLeft.x + q.topRight.x + q.bottomLeft.x + q.bottomRight.x) / 4, y: (q.topLeft.y + q.topRight.y + q.bottomLeft.y + q.bottomRight.y) / 4)
        let sides = [(q.topLeft, q.topRight), (q.topRight, q.bottomRight), (q.bottomRight, q.bottomLeft), (q.bottomLeft, q.topLeft)]
        var inside: [(Int, Int, Int)] = [], outside: [(Int, Int, Int)] = []
        let margin = CGFloat(max(image.width, image.height)) * 0.015
        for (a, b) in sides {
            for step in 1..<40 {
                let p = lerp(a, b, CGFloat(step) / 40)
                let dx = centre.x - p.x, dy = centre.y - p.y, length = max(1, sqrt(dx * dx + dy * dy))
                let ix = p.x + dx / length * margin, iy = p.y + dy / length * margin
                let ox = p.x - dx / length * margin, oy = p.y - dy / length * margin
                inside.append(image.at(Int(ix), Int(iy)))
                // Points outside the picture say nothing about the desk.
                if ox >= 0, oy >= 0, ox < CGFloat(image.width), oy < CGFloat(image.height) { outside.append(image.at(Int(ox), Int(oy))) }
            }
        }
        guard outside.count >= 20, let paper = median(inside), let desk = median(outside) else { return false }
        return abs(paper.0 - desk.0) + abs(paper.1 - desk.1) + abs(paper.2 - desk.2) > 45
    }

    // MARK: Flattening

    private static func perspectiveCorrected(_ image: CGImage, quad: Quad) -> CGImage? {
        guard let filter = CIFilter(name: "CIPerspectiveCorrection") else { return nil }
        filter.setValue(CIImage(cgImage: image), forKey: kCIInputImageKey)
        filter.setValue(CIVector(cgPoint: quad.topLeft), forKey: "inputTopLeft")
        filter.setValue(CIVector(cgPoint: quad.topRight), forKey: "inputTopRight")
        filter.setValue(CIVector(cgPoint: quad.bottomLeft), forKey: "inputBottomLeft")
        filter.setValue(CIVector(cgPoint: quad.bottomRight), forKey: "inputBottomRight")
        guard let output = filter.outputImage, output.extent.width >= 10, output.extent.height >= 10,
              output.extent.width.isFinite, output.extent.height.isFinite else { return nil }
        let context = CIContext(options: [.workingColorSpace: CGColorSpace(name: CGColorSpace.sRGB) as Any])
        return context.createCGImage(output, from: output.extent.integral, format: .RGBA8, colorSpace: CGColorSpace(name: CGColorSpace.sRGB))
    }

    /// A single-image PDF page whose long side is `pageLongSide` points.
    private static func pdfPage(_ image: CGImage) throws -> PDFPage? {
        let w = CGFloat(image.width), h = CGFloat(image.height)
        let scale = pageLongSide / max(w, h)
        var box = CGRect(x: 0, y: 0, width: (w * scale).rounded(), height: (h * scale).rounded())
        let data = NSMutableData()
        guard let consumer = CGDataConsumer(data: data as CFMutableData),
              let context = CGContext(consumer: consumer, mediaBox: &box, nil) else { return nil }
        context.beginPDFPage(nil)
        context.interpolationQuality = .high
        context.draw(image, in: box)
        context.endPDFPage()
        context.closePDF()
        return PDFDocument(data: data as Data)?.page(at: 0)
    }
}
