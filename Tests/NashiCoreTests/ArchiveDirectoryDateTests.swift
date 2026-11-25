import XCTest
@testable import NashiCore

final class ArchiveDirectoryDateTests: XCTestCase {
    private struct Entry {
        let name: String
        var type: UInt8 = 48
        var target = ""
        var mtime = 0
        var data = Data()
    }

    private func archive(_ entries: [Entry], in directory: URL) throws -> URL {
        var bytes = Data()
        for entry in entries {
            var header = Data(repeating: 0, count: 512)
            func put(_ value: String, at offset: Int, length: Int) {
                header.replaceSubrange(offset..<(offset + min(length, value.utf8.count)), with: value.utf8.prefix(length))
            }
            put(entry.name, at: 0, length: 100); put(entry.type == 53 ? "0000755\0" : "0000644\0", at: 100, length: 8)
            put("0000000\0", at: 108, length: 8); put("0000000\0", at: 116, length: 8)
            put(String(format: "%011o", entry.data.count) + "\0", at: 124, length: 12)
            put(String(format: "%011o", entry.mtime) + "\0", at: 136, length: 12); put("        ", at: 148, length: 8)
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

    private func modificationDate(_ url: URL) throws -> Date? {
        try FileManager.default.attributesOfItem(atPath: url.path)[.modificationDate] as? Date
    }

    func testExtractedFoldersKeepTheirStoredDateAfterTheirContentsAreWritten() throws {
        let base = try OutputFiles.temporaryDirectory(); defer { try? FileManager.default.removeItem(at: base) }
        let outer = 1_000_000_000, inner = 1_100_000_000, file = 1_200_000_000
        let input = try archive([
            .init(name: "folder/", type: 53, mtime: outer),
            .init(name: "folder/file.txt", mtime: file, data: Data("Content".utf8)),
            .init(name: "folder/inner/", type: 53, mtime: inner),
            .init(name: "folder/inner/file.txt", mtime: file, data: Data("Nested".utf8)),
            .init(name: "folder/inner/link", type: 50, target: "file.txt", mtime: file)
        ], in: base)
        let root = base.appendingPathComponent("extract", isDirectory: true)
        try ArchiveCodec.extract(input, into: root, token: .init())
        let folder = root.appendingPathComponent("folder", isDirectory: true)
        XCTAssertEqual(try modificationDate(folder), Date(timeIntervalSince1970: TimeInterval(outer)))
        XCTAssertEqual(try modificationDate(folder.appendingPathComponent("inner", isDirectory: true)), Date(timeIntervalSince1970: TimeInterval(inner)))
        XCTAssertEqual(try modificationDate(folder.appendingPathComponent("file.txt")), Date(timeIntervalSince1970: TimeInterval(file)))
    }
}
