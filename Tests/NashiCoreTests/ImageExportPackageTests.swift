import XCTest
import ImageIO
import CoreGraphics
@testable import NashiCore

final class ImageExportPackageTests: XCTestCase {
    private var root: URL!
    override func setUpWithError() throws {
        root = FileManager.default.temporaryDirectory.appendingPathComponent("Nashi-PackageTests-" + UUID().uuidString)
        try FileManager.default.createDirectory(at: root, withIntermediateDirectories: true)
    }
    override func tearDownWithError() throws { try FileManager.default.removeItem(at: root) }

    private func fixture(_ name: String = "İstanbul 🍐.png", width: Int = 80, height: Int = 40, transparent: Bool = false, orientation: Int = 1) throws -> URL {
        let url = root.appendingPathComponent(name)
        try FileManager.default.createDirectory(at: url.deletingLastPathComponent(), withIntermediateDirectories: true)
        let context = try XCTUnwrap(CGContext(data: nil, width: width, height: height, bitsPerComponent: 8, bytesPerRow: 0,
            space: CGColorSpace(name: CGColorSpace.sRGB)!, bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue))
        context.setFillColor(CGColor(red: 1, green: 0, blue: 0, alpha: transparent ? 0.5 : 1))
        context.fill(CGRect(x: 0, y: 0, width: width / 2, height: height))
        let destination = try XCTUnwrap(CGImageDestinationCreateWithURL(url as CFURL, "public.png" as CFString, 1, nil))
        CGImageDestinationAddImage(destination, try XCTUnwrap(context.makeImage()), [
            kCGImagePropertyOrientation: orientation,
            kCGImagePropertyTIFFDictionary: [kCGImagePropertyTIFFArtist: "Private fixture author"],
            kCGImagePropertyGPSDictionary: [kCGImagePropertyGPSLatitude: 41.0, kCGImagePropertyGPSLatitudeRef: "N"]
        ] as CFDictionary)
        XCTAssertTrue(CGImageDestinationFinalize(destination)); return url
    }
    private func run(_ inputs: [URL], package: ImageExportPackage, token: CancellationToken = .init(), progress: @escaping ProgressHandler = { _, _ in }) throws -> [URL] {
        var options = OperationOptions(); options.outputDirectory = root.appendingPathComponent("Exports")
        return try ConversionEngine.run(.init(inputs: inputs, operation: .imageExportPackage(package), options: options), token: token, progress: progress)
    }
    private var package: ImageExportPackage {
        .init(name: "Product set", variants: [.init(format: "png", maxDimension: 30, subfolder: "web"), .init(format: "jpg", maxDimension: 10, subfolder: "thumbnail"), .init(format: "webp", maxDimension: 0, subfolder: "archive")])
    }
    private func assertNoExports(file: StaticString = #filePath, line: UInt = #line) throws {
        let output = root.appendingPathComponent("Exports")
        if FileManager.default.fileExists(atPath: output.path) {
            XCTAssertEqual(try FileManager.default.contentsOfDirectory(atPath: output.path), [], file: file, line: line)
        }
    }

    func testBatchWritesRealFormatsDimensionsAndLeavesOriginalsUnchanged() throws {
        let wide = try fixture(), tall = try fixture("Second.png", width: 20, height: 60)
        let originals = try [wide, tall].map { try Data(contentsOf: $0) }
        let outputs = try run([wide, tall], package: package)
        XCTAssertEqual(outputs.count, 6)
        let expected = [(30,15), (10,5), (80,40), (10,30), (3,10), (20,60)]
        for (index, output) in outputs.enumerated() {
            let image = try ImageEngine.load(output)
            XCTAssertEqual(image.width, expected[index].0); XCTAssertEqual(image.height, expected[index].1)
            XCTAssertEqual(output.pathComponents.suffix(3).first, "Product set")
            let source = try XCTUnwrap(CGImageSourceCreateWithURL(output as CFURL, nil))
            let type = CGImageSourceGetType(source) as String?
            XCTAssertEqual(type, ["public.png", "public.jpeg", "org.webmproject.webp"][index % 3])
            let properties = CGImageSourceCopyPropertiesAtIndex(source, 0, nil) as? [CFString: Any]
            XCTAssertNil(properties?[kCGImagePropertyGPSDictionary])
            XCTAssertFalse(String(describing: properties).contains("Private fixture author"))
        }
        XCTAssertEqual(try [wide, tall].map { try Data(contentsOf: $0) }, originals)
    }

    func testNoUpscalingAndPortraitOrientation() throws {
        let input = try fixture(width: 80, height: 40, orientation: 6)
        let spec = ImageExportPackage(name: "Portrait", variants: [.init(format: "png", maxDimension: 1000, subfolder: "original"), .init(format: "png", maxDimension: 40, subfolder: "small")])
        let outputs = try run([input], package: spec)
        let original = try ImageEngine.load(outputs[0]), small = try ImageEngine.load(outputs[1])
        XCTAssertEqual(original.width, 40); XCTAssertEqual(original.height, 80)
        XCTAssertEqual(small.width, 20); XCTAssertEqual(small.height, 40)
    }

    func testTransparentPNGAndWebPKeepAlphaAndJPEGFlattensToWhite() throws {
        let input = try fixture(transparent: true), outputs = try run([input], package: package)
        for index in [0,2] {
            let image = try ImageEngine.load(outputs[index]), context = try XCTUnwrap(CGContext(data: nil, width: image.width, height: image.height, bitsPerComponent: 8, bytesPerRow: image.width * 4, space: CGColorSpace(name: CGColorSpace.sRGB)!, bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue))
            context.draw(image, in: CGRect(x: 0, y: 0, width: image.width, height: image.height))
            let data = try XCTUnwrap(context.data).assumingMemoryBound(to: UInt8.self)
            XCTAssertLessThan(data[3], 200); XCTAssertEqual(data[(image.width - 1) * 4 + 3], 0)
        }
        let jpg = try ImageEngine.load(outputs[1]), context = try XCTUnwrap(CGContext(data: nil, width: jpg.width, height: jpg.height, bitsPerComponent: 8, bytesPerRow: jpg.width * 4, space: CGColorSpace(name: CGColorSpace.sRGB)!, bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue))
        context.draw(jpg, in: CGRect(x: 0, y: 0, width: jpg.width, height: jpg.height))
        let data = try XCTUnwrap(context.data).assumingMemoryBound(to: UInt8.self), offset = (jpg.width - 1) * 4
        for channel in 0..<3 { XCTAssertGreaterThan(data[offset + channel], 240) }; XCTAssertEqual(data[offset + 3], 255)
    }

    func testPreviewMatchesOutputsWithDuplicateBasenamesAndRepeatedInputs() throws {
        let a = try fixture("one/Photo.png"), b = try fixture("two/photo.png")
        let inputs = [a, b, a], plan = try package.plan(inputs: inputs), outputs = try run(inputs, package: package)
        XCTAssertEqual(plan.count, 9)
        XCTAssertEqual(Set(plan.map { $0.relativePath.lowercased() }).count, 9)
        XCTAssertTrue(plan[3].relativePath.contains("photo 2")); XCTAssertTrue(plan[6].relativePath.contains("Photo 3"))
        for (entry, output) in zip(plan, outputs) { XCTAssertTrue(output.path.hasSuffix("Product set/" + entry.relativePath)) }
    }

    func testExistingPackageFolderAndFilesAreNeverReplaced() throws {
        let input = try fixture(), first = try run([input], package: package)
        let data = try first.map { try Data(contentsOf: $0) }, second = try run([input], package: package)
        XCTAssertTrue(second.allSatisfy { $0.path.contains("/Product set 2/") })
        XCTAssertEqual(try first.map { try Data(contentsOf: $0) }, data)
        XCTAssertTrue(try FileManager.default.contentsOfDirectory(atPath: root.appendingPathComponent("Exports").path).allSatisfy { !$0.hasPrefix(".Nashi-") })
    }

    func testCancellationAfterFirstVariantRollsBackTheWholePackage() throws {
        let input = try fixture(), token = CancellationToken()
        XCTAssertThrowsError(try run([input], package: package, token: token) { fraction, _ in if fraction > 0 { token.cancel() } }) { error in
            guard case NashiError.cancelled = error else { return XCTFail("Expected cancellation, got \(error)") }
        }
        XCTAssertTrue(token.isCancelled)
        try assertNoExports(); XCTAssertTrue(FileManager.default.fileExists(atPath: input.path))
    }

    func testCancellationBeforePublicationRollsBackAllVariants() throws {
        let input = try fixture(), token = CancellationToken()
        XCTAssertThrowsError(try run([input], package: package, token: token) { _, label in if label == "Saving your files…" { token.cancel() } }) { error in
            guard case NashiError.cancelled = error else { return XCTFail("Expected cancellation, got \(error)") }
        }
        XCTAssertTrue(token.isCancelled)
        try assertNoExports()
    }

    func testCorruptLaterInputRollsBackEarlierOutputs() throws {
        let input = try fixture(), bad = root.appendingPathComponent("Broken.png")
        try Data("not an image".utf8).write(to: bad)
        XCTAssertThrowsError(try run([input, bad], package: package)); try assertNoExports()
    }

    func testRejectsUnsafeAndAmbiguousFoldersAndInvalidOptions() throws {
        let input = try fixture()
        for name in ["../escape", "a/b", "a\\b", ".hidden", "CON", "COM1.txt", "NUL", "file:stream", "trailing.", " name", "a\n", String(repeating: "é", count: 41)] {
            XCTAssertThrowsError(try run([input], package: .init(name: name, variants: package.variants)), name)
            var spec = package; spec.variants[0].subfolder = name
            XCTAssertThrowsError(try run([input], package: spec), name)
        }
        var duplicate = package; duplicate.variants[1].subfolder = "WEB"
        XCTAssertThrowsError(try duplicate.validate())
        for edge in [-1, 16385] { var spec = package; spec.variants[0].maxDimension = edge; XCTAssertThrowsError(try spec.validate()) }
        for format in ["pdf", "../png", "JPEG"] { var spec = package; spec.variants[0].format = format; XCTAssertThrowsError(try spec.validate()) }
        XCTAssertThrowsError(try ImageExportPackage(name: "Empty", variants: []).validate())
        XCTAssertThrowsError(try package.plan(inputs: []))
        XCTAssertThrowsError(try package.plan(inputs: [root.appendingPathComponent("a.gif")]))
        XCTAssertThrowsError(try package.plan(inputs: Array(repeating: input, count: 1001)))
        try assertNoExports()
    }

    func testPreviewSanitizesLongUnicodeAndReservedFilenames() throws {
        let inputs = [String(repeating: "猫🍐", count: 50) + ".png", "CON.jpg", "a:b.png"].map { root.appendingPathComponent($0) }
        let plan = try package.plan(inputs: inputs)
        XCTAssertTrue(plan.allSatisfy { URL(fileURLWithPath: $0.relativePath).lastPathComponent.decomposedStringWithCanonicalMapping.utf8.count < 255 })
        XCTAssertTrue(plan[3].relativePath.hasSuffix("Image.png")); XCTAssertTrue(plan[6].relativePath.hasSuffix("a_b.png"))
        XCTAssertEqual(package.variants[0].dimensions(width: 1, height: 10000).width, 1)
    }

    func testSavedPackagesRoundTripReplaceAndRecoverOtherValidItems() throws {
        let suite = "Nashi.PackageTest." + UUID().uuidString, defaults = try XCTUnwrap(UserDefaults(suiteName: suite))
        defer { defaults.removePersistentDomain(forName: suite) }
        let store = ImageExportPackageStore(defaults: defaults)
        try store.save(package); XCTAssertEqual(ImageExportPackageStore(defaults: defaults).packages, [package])
        var changed = package; changed.name = "PRODUCT SET"; changed.variants[0].maxDimension = 900
        try store.save(changed); XCTAssertEqual(store.packages, [changed])
        let valid = try JSONSerialization.jsonObject(with: JSONEncoder().encode(package))
        defaults.set(try JSONSerialization.data(withJSONObject: [valid, ["name": "Broken", "variants": []], NSNull(), 12]), forKey: "imageExportPackages.v1")
        XCTAssertEqual(store.packages, [package])
        try store.remove(named: "product set"); XCTAssertTrue(store.packages.isEmpty)
    }

    func testSavedPackageLimitAllowsUpdatingAnExistingName() throws {
        let suite = "Nashi.PackageTest." + UUID().uuidString, defaults = try XCTUnwrap(UserDefaults(suiteName: suite))
        defer { defaults.removePersistentDomain(forName: suite) }
        let store = ImageExportPackageStore(defaults: defaults)
        for index in 0..<50 { var spec = package; spec.name = "Set \(index)"; try store.save(spec) }
        XCTAssertThrowsError(try store.save(package))
        var spec = package; spec.name = "Set 0"; try store.save(spec)
        XCTAssertEqual(store.packages.count, 50)
    }
}
