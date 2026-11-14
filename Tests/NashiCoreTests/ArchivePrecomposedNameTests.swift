import XCTest
import Foundation
@testable import NashiCore

final class ArchivePrecomposedNameTests: XCTestCase {
    private var root: URL!
    override func setUpWithError() throws { root = try OutputFiles.temporaryDirectory() }
    override func tearDownWithError() throws { if let root { try? FileManager.default.removeItem(at: root) } }

    private func contains(_ data: Data, _ text: String) -> Bool { data.range(of: Data(text.utf8)) != nil }

    func testCreatedArchivesStorePrecomposedNames() throws {
        let source = root.appendingPathComponent("source", isDirectory: true)
        let folder = source.appendingPathComponent("\u{00DC}ber", isDirectory: true)
        try FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true)
        // macOS stores these names decomposed: "e" + U+0301, Hangul as jamo.
        try Data("a".utf8).write(to: folder.appendingPathComponent("caf\u{00E9}.txt"))
        try Data("b".utf8).write(to: source.appendingPathComponent("\u{D55C}\u{AE00}.txt"))
        // The file system leaves CJK compatibility ideographs alone; so must the archive.
        try Data("c".utf8).write(to: source.appendingPathComponent("\u{F900}.txt"))
        try FileManager.default.createSymbolicLink(atPath: source.appendingPathComponent("link").path, withDestinationPath: "\u{00DC}ber/caf\u{00E9}.txt")
        for format in ["zip", "tar"] {
            let output = root.appendingPathComponent("names.\(format)")
            try ArchiveCodec.create(from: source, at: output, format: format, token: .init())
            let data = try Data(contentsOf: output)
            for name in ["\u{00DC}ber/caf\u{00E9}.txt", "\u{D55C}\u{AE00}.txt", "\u{F900}.txt"] {
                XCTAssertTrue(contains(data, name), "\(format) lacks \(name)")
            }
            XCTAssertFalse(contains(data, "cafe\u{0301}"), format)
            XCTAssertFalse(contains(data, "\u{1112}\u{1161}\u{11AB}"), format)
            XCTAssertFalse(contains(data, "\u{8C48}"), format)
            let extracted = root.appendingPathComponent("extracted-\(format)", isDirectory: true)
            try ArchiveCodec.extract(output, into: extracted, token: .init())
            XCTAssertEqual(try String(contentsOf: extracted.appendingPathComponent("link"), encoding: .utf8), "a")
        }
    }
}
