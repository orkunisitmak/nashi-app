import Foundation
import CoreGraphics
import ImageIO

/// Traces a raster image into a black-and-white vector SVG (for laser engraving/cutting and plotters).
/// shortcut: fixed 50% luminance threshold and pixel-edge tracing smoothed by Douglas-Peucker, no Bezier fitting; add a threshold option or potrace-style curves if users need smoother photo traces.
public enum ImageVectorizer {
    public static func convert(_ input: URL, options: OperationOptions, token: CancellationToken) throws -> [URL] {
        try token.check()
        let image = try ImageEngine.load(input)
        let dpi = CGImageSourceCreateWithURL(input as CFURL, nil)
            .flatMap { CGImageSourceCopyPropertiesAtIndex($0, 0, nil) as? [CFString: Any] }
            .flatMap { $0[kCGImagePropertyDPIWidth] as? Double }
            .flatMap { $0.isFinite && $0 > 0 ? $0 : nil } ?? 96
        let svg = try self.svg(image, dpi: dpi, token: token)
        let output = OutputFiles.unique(for: input, extension: "svg", directory: options.outputDirectory)
        do { try svg.write(to: output, atomically: true, encoding: .utf8) } catch { try? FileManager.default.removeItem(at: output); throw error }
        return [output]
    }

    public static func svg(_ image: CGImage, dpi: Double = 96, token: CancellationToken = .init()) throws -> String {
        let w = image.width, h = image.height
        guard w > 0, h > 0, let context = CGContext(data: nil, width: w, height: h, bitsPerComponent: 8, bytesPerRow: w * 4,
                                                    space: CGColorSpace(name: CGColorSpace.sRGB)!,
                                                    bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue),
              let data = context.data else { throw NashiError.invalidInput("This image could not be vectorized.") }
        // Transparent areas become white so logos on transparent backgrounds trace cleanly.
        context.setFillColor(red: 1, green: 1, blue: 1, alpha: 1)
        context.fill(CGRect(x: 0, y: 0, width: w, height: h))
        context.draw(image, in: CGRect(x: 0, y: 0, width: w, height: h))
        let pixels = data.bindMemory(to: UInt8.self, capacity: w * h * 4)
        var black = [Bool](repeating: false, count: w * h)
        for i in 0..<(w * h) {
            let luma = 0.299 * Double(pixels[i*4]) + 0.587 * Double(pixels[i*4+1]) + 0.114 * Double(pixels[i*4+2])
            black[i] = luma < 128
        }
        try token.check()

        // Directed boundary edges between black and white pixels; vertex id = y*(w+1)+x.
        func isBlack(_ x: Int, _ y: Int) -> Bool { x >= 0 && y >= 0 && x < w && y < h && black[y*w+x] }
        let stride = w + 1
        var next: [Int: [Int]] = [:]
        func add(_ a: (Int, Int), _ b: (Int, Int)) { next[a.1*stride+a.0, default: []].append(b.1*stride+b.0) }
        for y in 0..<h {
            for x in 0..<w where black[y*w+x] {
                if !isBlack(x, y-1) { add((x, y), (x+1, y)) }
                if !isBlack(x+1, y) { add((x+1, y), (x+1, y+1)) }
                if !isBlack(x, y+1) { add((x+1, y+1), (x, y+1)) }
                if !isBlack(x-1, y) { add((x, y+1), (x, y)) }
            }
        }

        // Every vertex has equal in/out degree, so each walk closes. Even-odd fill makes the loop split at touching corners irrelevant.
        var path = ""
        while let start = next.first?.key {
            try token.check()
            var loop: [(Double, Double)] = []
            var v = start
            repeat {
                loop.append((Double(v % stride), Double(v / stride)))
                var outs = next[v]!; let n = outs.removeLast()
                next[v] = outs.isEmpty ? nil : outs
                v = n
            } while v != start
            let points = simplify(loop, epsilon: 0.75)
            guard points.count >= 3 else { continue }
            path += "M" + points.map { fmt($0.0) + " " + fmt($0.1) }.joined(separator: "L") + "Z"
        }

        let mm = { (px: Int) in fmt(Double(px) / dpi * 25.4) }
        return """
        <?xml version="1.0" encoding="UTF-8"?>
        <svg xmlns="http://www.w3.org/2000/svg" width="\(mm(w))mm" height="\(mm(h))mm" viewBox="0 0 \(w) \(h)">
        <path fill="#000000" fill-rule="evenodd" d="\(path)"/>
        </svg>

        """
    }

    private static func fmt(_ v: Double) -> String {
        let s = String(format: "%.2f", v)
        return s.contains(".") ? String(s.reversed().drop { $0 == "0" }.drop { $0 == "." }.reversed()) : s
    }

    /// Douglas-Peucker on a closed loop: straightens pixel staircases into diagonal lines while keeping corners.
    static func simplify(_ loop: [(Double, Double)], epsilon: Double) -> [(Double, Double)] {
        guard loop.count > 4 else { return loop }
        let far = (1..<loop.count).max { dist2(loop[$0], loop[0]) < dist2(loop[$1], loop[0]) }!
        let a = dp(Array(loop[0...far]), epsilon), b = dp(Array(loop[far...]) + [loop[0]], epsilon)
        return Array(a.dropLast()) + Array(b.dropLast())
    }

    private static func dp(_ pts: [(Double, Double)], _ eps: Double) -> [(Double, Double)] {
        guard pts.count > 2 else { return pts }
        let a = pts[0], b = pts[pts.count-1]
        let dx = b.0 - a.0, dy = b.1 - a.1, len = (dx*dx + dy*dy).squareRoot()
        var best = 0.0, index = 0
        for i in 1..<(pts.count-1) {
            let d = len == 0 ? dist2(pts[i], a).squareRoot() : abs(dy*pts[i].0 - dx*pts[i].1 + b.0*a.1 - b.1*a.0) / len
            if d > best { best = d; index = i }
        }
        guard best > eps else { return [a, b] }
        return Array(dp(Array(pts[0...index]), eps).dropLast()) + dp(Array(pts[index...]), eps)
    }

    private static func dist2(_ p: (Double, Double), _ q: (Double, Double)) -> Double { (p.0-q.0)*(p.0-q.0) + (p.1-q.1)*(p.1-q.1) }
}
