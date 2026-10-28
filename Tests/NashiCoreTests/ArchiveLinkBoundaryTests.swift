import XCTest
@testable import NashiCore

final class ArchiveLinkBoundaryTests: XCTestCase {
    private struct Entry {
        let name: String
        var type: UInt8 = 48
        var target = ""
        var data = Data()
    }

    private func archive(_ entries: [Entry], in directory: URL) throws -> URL {
        var bytes = Data()
        for entry in entries {
            var header = Data(repeating: 0, count: 512)
            func put(_ value: String, at offset: Int, length: Int) {
                header.replaceSubrange(offset..<(offset + min(length, value.utf8.count)), with: value.utf8.prefix(length))
            }
            put(entry.name, at: 0, length: 100); put("0000644\0", at: 100, length: 8)
            put("0000000\0", at: 108, length: 8); put("0000000\0", at: 116, length: 8)
            put(String(format: "%011o", entry.data.count) + "\0", at: 124, length: 12)
            put("00000000000\0", at: 136, length: 12); put("        ", at: 148, length: 8)
            header[156] = entry.type; put(entry.target, at: 157, length: 100)
            put("ustar\0", at: 257, length: 6); put("00", at: 263, length: 2)
            put(String(format: "%06o", header.reduce(0) { $0 + Int($1) }) + "\0 ", at: 148, length: 8)
            bytes += header + entry.data + Data(repeating: 0, count: (512 - entry.data.count % 512) % 512)
        }
        bytes += Data(repeating: 0, count: 1024)
        let input = directory.appendingPathComponent(UUID().uuidString + ".tar")
        try bytes.write(to: input)
        return input
    }

    func testLeafSymbolicLinksForwardChainsAndHardLinksRemainReadable() throws {
        let base = try OutputFiles.temporaryDirectory(); defer { try? FileManager.default.removeItem(at: base) }
        let content = Data("Safe local content".utf8)
        let input = try archive([
            .init(name: "data/file.txt", data: content),
            .init(name: "first", type: 50, target: "second"),
            .init(name: "second", type: 50, target: "data/file.txt"),
            .init(name: "links/nested", type: 50, target: "../data/file.txt"),
            .init(name: "hard", type: 49, target: "data/file.txt")
        ], in: base)
        let root = base.appendingPathComponent("extract", isDirectory: true)
        try ArchiveCodec.extract(input, into: root, token: .init())
        for name in ["first", "second", "hard", "links/nested", "data/file.txt"] {
            XCTAssertEqual(try Data(contentsOf: root.appendingPathComponent(name)), content)
        }
        XCTAssertEqual(try FileManager.default.destinationOfSymbolicLink(atPath: root.appendingPathComponent("first").path), "second")
    }

    func testDeferredEntryCannotCreateChildrenThroughAnInRootDirectoryAlias() throws {
        let base = try OutputFiles.temporaryDirectory(); defer { try? FileManager.default.removeItem(at: base) }
        let input = try archive([
            .init(name: "data/file.txt", data: Data("Retained fixture".utf8)),
            .init(name: "alias", type: 50, target: "data"),
            .init(name: "alias/new/child", type: 50, target: "file.txt")
        ], in: base)
        let root = base.appendingPathComponent("extract", isDirectory: true)
        XCTAssertThrowsError(try ArchiveCodec.extract(input, into: root, token: .init()))
        XCTAssertFalse(FileManager.default.fileExists(atPath: root.appendingPathComponent("data/new").path))
    }

    func testPreexistingInRootDirectoryAliasesAreNotUsedForAnyEntryType() throws {
        let base = try OutputFiles.temporaryDirectory(); defer { try? FileManager.default.removeItem(at: base) }
        for type: UInt8 in [48, 49, 50, 53] {
            let root = base.appendingPathComponent("extract-\(type)", isDirectory: true)
            let real = root.appendingPathComponent("real", isDirectory: true)
            try FileManager.default.createDirectory(at: real, withIntermediateDirectories: true)
            try FileManager.default.createSymbolicLink(atPath: root.appendingPathComponent("alias").path, withDestinationPath: "real")
            let input = try archive([.init(name: "alias/item", type: type, target: type == 49 ? "real/keep.txt" : "keep.txt", data: type == 48 ? Data("New".utf8) : Data())], in: base)
            let keep = real.appendingPathComponent("keep.txt"), content = Data("Original".utf8)
            try content.write(to: keep)
            XCTAssertThrowsError(try ArchiveCodec.extract(input, into: root, token: .init()))
            XCTAssertEqual(try FileManager.default.contentsOfDirectory(atPath: real.path), ["keep.txt"])
            XCTAssertEqual(try Data(contentsOf: keep), content)
        }
    }

    func testDanglingSymbolicDestinationIsRejectedBeforeCreatingItsTarget() throws {
        let base = try OutputFiles.temporaryDirectory(); defer { try? FileManager.default.removeItem(at: base) }
        let root = base.appendingPathComponent("extract", isDirectory: true)
        try FileManager.default.createDirectory(at: root, withIntermediateDirectories: true)
        let link = root.appendingPathComponent("item.txt")
        try FileManager.default.createSymbolicLink(atPath: link.path, withDestinationPath: "missing.txt")
        let input = try archive([.init(name: "item.txt", data: Data("New".utf8))], in: base)
        XCTAssertThrowsError(try ArchiveCodec.extract(input, into: root, token: .init()))
        XCTAssertFalse(FileManager.default.fileExists(atPath: root.appendingPathComponent("missing.txt").path))
        XCTAssertEqual(try FileManager.default.destinationOfSymbolicLink(atPath: link.path), "missing.txt")
    }

    func testRefusedArchiveBatchKeepsSourcesPreviousExportsAndRemovesStaging() throws {
        let base = try OutputFiles.temporaryDirectory(); defer { try? FileManager.default.removeItem(at: base) }
        let first = try archive([.init(name: "file.txt", data: Data("First".utf8))], in: base)
        let second = try archive([
            .init(name: "data/file.txt", data: Data("Second".utf8)),
            .init(name: "alias", type: 50, target: "data"),
            .init(name: "alias/new/child", type: 50, target: "file.txt")
        ], in: base)
        let firstBytes = try Data(contentsOf: first), secondBytes = try Data(contentsOf: second)
        let exports = base.appendingPathComponent("Exports", isDirectory: true)
        try FileManager.default.createDirectory(at: exports, withIntermediateDirectories: true)
        let previous = exports.appendingPathComponent("keep.txt"), content = Data("Previous".utf8)
        try content.write(to: previous)
        var options = OperationOptions(); options.outputDirectory = exports
        XCTAssertThrowsError(try ConversionEngine.run(.init(inputs: [first, second], operation: .tool(.extractArchive), options: options)))
        XCTAssertEqual(try FileManager.default.contentsOfDirectory(atPath: exports.path), ["keep.txt"])
        XCTAssertEqual(try Data(contentsOf: previous), content)
        XCTAssertEqual(try Data(contentsOf: first), firstBytes)
        XCTAssertEqual(try Data(contentsOf: second), secondBytes)
    }
}
