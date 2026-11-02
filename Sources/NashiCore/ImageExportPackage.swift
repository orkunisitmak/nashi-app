import Foundation

public struct ImageExportVariant: Codable, Equatable, Sendable {
    public var format: String
    /// Longest edge in pixels. Zero keeps the source size; smaller images are never enlarged.
    public var maxDimension: Int
    public var subfolder: String

    public init(format: String, maxDimension: Int, subfolder: String) {
        self.format = format; self.maxDimension = maxDimension; self.subfolder = subfolder
    }

    public func dimensions(width: Int, height: Int) -> (width: Int, height: Int) {
        guard maxDimension > 0, max(width, height) > maxDimension else { return (width, height) }
        let scale = Double(maxDimension) / Double(max(width, height))
        return (max(1, Int((Double(width) * scale).rounded())), max(1, Int((Double(height) * scale).rounded())))
    }
}

public struct ImageExportOutput: Equatable, Sendable {
    public let inputIndex: Int
    public let variantIndex: Int
    /// Relative to the new package folder, including the variant's subfolder.
    public let relativePath: String
}

public struct ImageExportPackage: Codable, Equatable, Sendable {
    public var name: String
    public var variants: [ImageExportVariant]
    public static let formats = ["webp", "jpg", "png"]
    public static let web = ImageExportPackage(name: "Web package", variants: [
        .init(format: "webp", maxDimension: 1600, subfolder: "web"),
        .init(format: "jpg", maxDimension: 400, subfolder: "thumbnail"),
        .init(format: "jpg", maxDimension: 0, subfolder: "archive")
    ])

    public init(name: String, variants: [ImageExportVariant]) { self.name = name; self.variants = variants }

    public func validate() throws {
        guard Self.validFolderName(name) else { throw NashiError.invalidInput("Use a short package name without paths or reserved names.") }
        guard (1...12).contains(variants.count) else { throw NashiError.invalidInput("Add between 1 and 12 outputs to the package.") }
        var folders = Set<String>()
        for variant in variants {
            guard Self.formats.contains(variant.format) else { throw NashiError.invalidInput("Package outputs must be PNG, JPG or WebP.") }
            guard (0...16384).contains(variant.maxDimension) else { throw NashiError.invalidInput("Use a longest edge from 1 to 16384 pixels, or 0 for original size.") }
            guard Self.validFolderName(variant.subfolder) else { throw NashiError.invalidInput("Use short subfolder names without paths or reserved names.") }
            guard folders.insert(Self.nameKey(variant.subfolder)).inserted else { throw NashiError.invalidInput("Give each output a different subfolder name.") }
        }
    }

    public func plan(inputs: [URL]) throws -> [ImageExportOutput] {
        try validate()
        guard (1...1000).contains(inputs.count), inputs.allSatisfy({ $0.isFileURL && FileCategory.detect($0) == .image }) else {
            throw NashiError.invalidInput("Choose between 1 and 1000 still images for the package.")
        }
        var used = Set<String>(), result: [ImageExportOutput] = []
        for (index, input) in inputs.enumerated() {
            let stem = Self.outputStem(input.deletingPathExtension().lastPathComponent)
            for (variantIndex, variant) in variants.enumerated() {
                var number = 1, relative: String
                repeat {
                    relative = variant.subfolder + "/" + stem + (number == 1 ? "" : " \(number)") + "." + variant.format
                    number += 1
                } while !used.insert(Self.nameKey(relative)).inserted
                result.append(.init(inputIndex: index, variantIndex: variantIndex, relativePath: relative))
            }
        }
        return result
    }

    static func nameKey(_ value: String) -> String { value.precomposedStringWithCanonicalMapping.lowercased() }

    private static let reserved = Set(["CON", "PRN", "AUX", "NUL"] + (1...9).flatMap { ["COM\($0)", "LPT\($0)"] })
    private static func validFolderName(_ value: String) -> Bool {
        !value.isEmpty && value.decomposedStringWithCanonicalMapping.utf8.count <= 120 &&
        value == value.trimmingCharacters(in: .whitespacesAndNewlines) && !value.hasPrefix(".") && !value.hasSuffix(".") &&
        !value.unicodeScalars.contains { $0.value < 32 || $0.value == 127 || "/\\:*?\"<>|".unicodeScalars.contains($0) } &&
        !reserved.contains(String(value.split(separator: ".", omittingEmptySubsequences: false)[0]).uppercased())
    }

    private static func outputStem(_ value: String) -> String {
        var stem = String(String.UnicodeScalarView(value.precomposedStringWithCanonicalMapping.unicodeScalars.map {
            $0.value < 32 || $0.value == 127 || "/\\:*?\"<>|".unicodeScalars.contains($0) ? UnicodeScalar(95)! : $0
        }))
        while stem.decomposedStringWithCanonicalMapping.utf8.count > 120 { stem.removeLast() }
        stem = stem.trimmingCharacters(in: CharacterSet(charactersIn: ". "))
        return validFolderName(stem) ? stem : "Image"
    }
}

/// Each named package is independent: one unreadable saved item does not discard the others.
public final class ImageExportPackageStore {
    private let defaults: UserDefaults
    private let key = "imageExportPackages.v1"
    public init(defaults: UserDefaults = .standard) { self.defaults = defaults }
    public var packages: [ImageExportPackage] {
        guard let data = defaults.data(forKey: key), data.count <= 1_000_000,
              let items = try? JSONDecoder().decode([StoredPackage].self, from: data) else { return [] }
        return Array(items.compactMap(\.value).prefix(50))
    }
    public func save(_ package: ImageExportPackage) throws {
        try package.validate()
        var current = packages.filter { ImageExportPackage.nameKey($0.name) != ImageExportPackage.nameKey(package.name) }
        guard current.count < 50 else { throw NashiError.invalidInput("You can save up to 50 export packages.") }
        current.append(package)
        defaults.set(try JSONEncoder().encode(current), forKey: key)
    }
    public func remove(named name: String) throws {
        defaults.set(try JSONEncoder().encode(packages.filter { ImageExportPackage.nameKey($0.name) != ImageExportPackage.nameKey(name) }), forKey: key)
    }
    private struct StoredPackage: Decodable {
        let value: ImageExportPackage?
        init(from decoder: Decoder) throws {
            do { let package = try ImageExportPackage(from: decoder); try package.validate(); value = package }
            catch { value = nil }
        }
    }
}
