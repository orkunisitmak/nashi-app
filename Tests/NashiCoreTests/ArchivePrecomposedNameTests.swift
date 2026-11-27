import XCTest
import Foundation
@testable import NashiCore

final class ArchivePrecomposedNameTests: XCTestCase {
    private var root: URL!
    override func setUpWithError() throws { root = try OutputFiles.temporaryDirectory() }
    override func tearDownWithError() throws { if let root { try? FileManager.default.removeItem(at: root) } }

    private func contains(_ data: Data, _ text: String) -> Bool { data.range(of: Data(text.utf8)) != nil }

    /// Every entry name of a ZIP as stored bytes, with its UTF-8 flag (general purpose bit 11),
    /// from the local headers and from the central directory.
    private func zipNames(_ data: Data) -> (local: [[UInt8]: Bool], central: [[UInt8]: Bool]) {
        let bytes = [UInt8](data)
        func u16(_ offset: Int) -> Int { Int(bytes[offset]) | Int(bytes[offset + 1]) << 8 }
        var local: [[UInt8]: Bool] = [:], central: [[UInt8]: Bool] = [:]
        var offset = 0
        while offset + 46 <= bytes.count {
            if bytes[offset..<offset + 4].elementsEqual([0x50, 0x4b, 0x03, 0x04]) {
                local[Array(bytes[(offset + 30)..<(offset + 30 + u16(offset + 26))])] = u16(offset + 6) & 0x800 != 0
                offset += 30 + u16(offset + 26)
            } else if bytes[offset..<offset + 4].elementsEqual([0x50, 0x4b, 0x01, 0x02]) {
                central[Array(bytes[(offset + 46)..<(offset + 46 + u16(offset + 28))])] = u16(offset + 8) & 0x800 != 0
                offset += 46 + u16(offset + 28)
            } else { offset += 1 }
        }
        return (local, central)
    }

    func testCreatedArchivesStorePrecomposedNames() throws {
        let source = root.appendingPathComponent("source", isDirectory: true)
        let folder = source.appendingPathComponent("\u{00DC}ber", isDirectory: true)
        try FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true)
        // macOS stores these names decomposed: "e" + U+0301, Hangul as jamo.
        try Data("a".utf8).write(to: folder.appendingPathComponent("caf\u{00E9}.txt"))
        try Data("b".utf8).write(to: source.appendingPathComponent("\u{D55C}\u{AE00}.txt"))
        // The file system leaves CJK compatibility ideographs alone; so must the archive,
        // without keeping the folder above such a file decomposed.
        try Data("c".utf8).write(to: source.appendingPathComponent("\u{F900}.txt"))
        try Data("d".utf8).write(to: folder.appendingPathComponent("\u{F900}.txt"))
        try FileManager.default.createSymbolicLink(atPath: source.appendingPathComponent("link").path, withDestinationPath: "\u{00DC}ber/caf\u{00E9}.txt")
        try FileManager.default.createSymbolicLink(atPath: source.appendingPathComponent("link2").path, withDestinationPath: "\u{00DC}ber/\u{F900}.txt")
        let names = ["\u{00DC}ber/", "\u{00DC}ber/caf\u{00E9}.txt", "\u{00DC}ber/\u{F900}.txt", "\u{D55C}\u{AE00}.txt", "\u{F900}.txt", "link", "link2"]
        for format in ["zip", "tar"] {
            let output = root.appendingPathComponent("names.\(format)")
            try ArchiveCodec.create(from: source, at: output, format: format, token: .init())
            let data = try Data(contentsOf: output)
            for name in names { XCTAssertTrue(contains(data, name), "\(format) lacks \(name)") }
            // The folder entry, the files in it and the link targets share one folder spelling.
            XCTAssertFalse(contains(data, "U\u{0308}ber"), format)
            XCTAssertFalse(contains(data, "cafe\u{0301}"), format)
            XCTAssertFalse(contains(data, "\u{1112}\u{1161}\u{11AB}"), format)
            XCTAssertFalse(contains(data, "\u{8C48}"), format)
            if format == "zip" {
                // Without the UTF-8 flag other systems read these bytes as CP437.
                let stored = zipNames(data)
                for headers in [stored.local, stored.central] {
                    XCTAssertEqual(Set(headers.keys), Set(names.map { Array($0.utf8) }))
                    for (name, flagged) in headers where name.contains(where: { $0 >= 0x80 }) {
                        XCTAssertTrue(flagged, String(decoding: name, as: UTF8.self))
                    }
                }
            }
            let extracted = root.appendingPathComponent("extracted-\(format)", isDirectory: true)
            try ArchiveCodec.extract(output, into: extracted, token: .init())
            let system = root.appendingPathComponent("system-\(format)", isDirectory: true)
            try FileManager.default.createDirectory(at: system, withIntermediateDirectories: true)
            if format == "zip" {
                _ = try ProcessRunner.run(URL(fileURLWithPath: "/usr/bin/unzip"), ["-q", output.path, "-d", system.path])
            } else {
                _ = try ProcessRunner.run(URL(fileURLWithPath: "/usr/bin/tar"), ["-xf", output.path, "-C", system.path])
            }
            for folder in [extracted, system] {
                // One folder, not a precomposed and a decomposed one side by side.
                XCTAssertEqual(try FileManager.default.subpathsOfDirectory(atPath: folder.path).count, 7, "\(format) \(folder.lastPathComponent)")
                XCTAssertEqual(try String(contentsOf: folder.appendingPathComponent("link"), encoding: .utf8), "a")
                XCTAssertEqual(try String(contentsOf: folder.appendingPathComponent("link2"), encoding: .utf8), "d")
                XCTAssertEqual(try String(contentsOf: folder.appendingPathComponent("\u{D55C}\u{AE00}.txt"), encoding: .utf8), "b")
                XCTAssertEqual(try String(contentsOf: folder.appendingPathComponent("\u{F900}.txt"), encoding: .utf8), "c")
            }
        }
    }
}
