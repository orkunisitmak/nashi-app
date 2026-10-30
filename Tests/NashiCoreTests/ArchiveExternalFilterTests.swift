import XCTest
@testable import NashiCore

final class ArchiveExternalFilterTests: XCTestCase {
    func testArchiveBytesNeverLaunchADecompressorFromPATH() throws {
        let fm = FileManager.default
        let base = try OutputFiles.temporaryDirectory(); defer { try? fm.removeItem(at: base) }
        let bin = base.appendingPathComponent("bin", isDirectory: true)
        try fm.createDirectory(at: bin, withIntermediateDirectories: true)
        let marker = base.appendingPathComponent("launched")
        for tool in ["lrzip", "grzip", "zstd"] {
            let script = bin.appendingPathComponent(tool)
            try "#!/bin/sh\necho \(tool) >> '\(marker.path)'\ncat >/dev/null\n".write(to: script, atomically: true, encoding: .utf8)
            try fm.setAttributes([.posixPermissions: 0o755], ofItemAtPath: script.path)
        }
        let oldPath = ProcessInfo.processInfo.environment["PATH"] ?? "/usr/bin:/bin"
        setenv("PATH", bin.path + ":" + oldPath, 1); defer { setenv("PATH", oldPath, 1) }

        let lrzip = Data("LRZI".utf8) + Data([0, 6]) + Data(repeating: 0, count: 18) + Data("payload".utf8)
        let grzip = Data("GRZipII".utf8) + Data([0, 2, 4, 0x3a, 0x29]) + Data("payload".utf8)
        let zstd = Data([0x28, 0xb5, 0x2f, 0xfd, 0x04, 0x58]) + Data(repeating: 0, count: 16)
        for (name, bytes) in [("Download.zip", lrzip), ("Backup.tar", grzip), ("Book.epub", lrzip), ("Packed.tar", zstd), ("download-0", lrzip)] {
            let input = base.appendingPathComponent(name); try bytes.write(to: input)
            XCTAssertThrowsError(try ArchiveCodec.extract(input, into: base.appendingPathComponent(UUID().uuidString), token: .init()), name)
            XCTAssertFalse(fm.fileExists(atPath: marker.path), name)
        }

        let source = base.appendingPathComponent("source", isDirectory: true)
        try fm.createDirectory(at: source, withIntermediateDirectories: true)
        try Data("kept".utf8).write(to: source.appendingPathComponent("a.txt"))
        let gz = base.appendingPathComponent("Archive.gz")
        try ArchiveCodec.create(from: source, at: gz, format: "gz", token: .init())
        let compressedTar = base.appendingPathComponent("Compressed.tar")
        try fm.copyItem(at: gz, to: compressedTar)
        for input in [gz, compressedTar] {
            let output = base.appendingPathComponent("out-" + input.lastPathComponent)
            try ArchiveCodec.extract(input, into: output, token: .init())
            XCTAssertEqual(try Data(contentsOf: output.appendingPathComponent("a.txt")), Data("kept".utf8))
        }
    }
}
