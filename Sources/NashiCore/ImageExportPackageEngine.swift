import Foundation
import CoreGraphics

enum ImageExportPackageEngine {
    /// ConversionEngine owns the transaction. Returning nested files publishes their
    /// single package root together, and a failure removes the entire staged tree.
    static func stage(inputs: [URL], package: ImageExportPackage, directory: URL, token: CancellationToken, progress: ProgressHandler) throws -> [URL] {
        let plan = try package.plan(inputs: inputs)
        let root = directory.appendingPathComponent(package.name, isDirectory: true)
        var outputs: [URL] = []
        for (inputIndex, input) in inputs.enumerated() {
            try token.check()
            let image = try ImageEngine.load(input)
            for variantIndex in package.variants.indices {
                try token.check()
                let variant = package.variants[variantIndex]
                let entry = plan[inputIndex * package.variants.count + variantIndex]
                let output = root.appendingPathComponent(entry.relativePath)
                try FileManager.default.createDirectory(at: output.deletingLastPathComponent(), withIntermediateDirectories: true)
                let size = variant.dimensions(width: image.width, height: image.height)
                var resized = size.width == image.width && size.height == image.height ? image : try ImageEngine.resized(image, width: size.width, height: size.height)
                if variant.format == "jpg" { resized = try opaque(resized) }
                // Fresh pixels only. Source GPS, comments and document tags are not copied.
                try ImageEngine.write(resized, to: output, quality: 0.85, token: token)
                outputs.append(output)
                progress(Double(outputs.count) / Double(plan.count), "Preparing package outputs…")
                try token.check()
            }
        }
        return outputs
    }

    private static func opaque(_ image: CGImage) throws -> CGImage {
        guard let context = CGContext(data: nil, width: image.width, height: image.height, bitsPerComponent: 8, bytesPerRow: 0,
                                      space: CGColorSpace(name: CGColorSpace.sRGB)!, bitmapInfo: CGImageAlphaInfo.noneSkipLast.rawValue) else {
            throw NashiError.failed("Could not prepare the package image.")
        }
        let bounds = CGRect(x: 0, y: 0, width: image.width, height: image.height)
        context.setFillColor(CGColor(gray: 1, alpha: 1)); context.fill(bounds); context.draw(image, in: bounds)
        guard let output = context.makeImage() else { throw NashiError.failed("Could not prepare the package image.") }
        return output
    }
}
