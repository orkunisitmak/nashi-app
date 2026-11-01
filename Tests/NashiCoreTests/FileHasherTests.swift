import XCTest
@testable import NashiCore

final class FileHasherTests: XCTestCase {
    private let sha256OfABC = "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad"
    private let sha512OfABC = "ddaf35a193617abacc417349ae20413112e6fa4e89a97ea20a9eeee64b55d39a2192992a274fc1a836ba3c23a3feebbd454d4423643ce80e2a9ac94fa54ca49f"
    private let md5OfABC = "900150983cd24fb0d6963f7d28e17f72"

    func testDigestsMatchPublishedVectors() throws {
        let root = try OutputFiles.temporaryDirectory()
        defer { try? FileManager.default.removeItem(at: root) }
        let file = root.appendingPathComponent("checksum ' sample.txt")
        try Data("abc".utf8).write(to: file)
        XCTAssertEqual(try FileHasher.digest(of: file), sha256OfABC)
        XCTAssertEqual(try FileHasher.digest(of: file, algorithm: .sha256), sha256OfABC)
        XCTAssertEqual(try FileHasher.digest(of: file, algorithm: .sha512), sha512OfABC)
        XCTAssertEqual(try FileHasher.digest(of: file, algorithm: .md5), md5OfABC)
    }

    func testMissingFileAndDirectoryAreRejected() throws {
        let root = try OutputFiles.temporaryDirectory()
        defer { try? FileManager.default.removeItem(at: root) }
        XCTAssertThrowsError(try FileHasher.digest(of: root.appendingPathComponent("absent.bin")))
        XCTAssertThrowsError(try FileHasher.digest(of: root))
    }

    func testChecksumLineParsingKeepsSpacesAndBinaryMarker() {
        XCTAssertEqual(FileHasher.parseChecksumLine("\(sha256OfABC)  a b/c.txt")?.path, "a b/c.txt")
        XCTAssertEqual(FileHasher.parseChecksumLine("\(sha256OfABC) *photo 1.jpg")?.path, "photo 1.jpg")
        XCTAssertEqual(FileHasher.parseChecksumLine("\(md5OfABC)  legacy.bin")?.digest, md5OfABC)
        XCTAssertNil(FileHasher.parseChecksumLine(""))
        XCTAssertNil(FileHasher.parseChecksumLine("# comment"))
        XCTAssertNil(FileHasher.parseChecksumLine("not-a-digest  file.txt"))
        XCTAssertNil(FileHasher.parseChecksumLine(sha256OfABC))
    }

    func testHashCommandPrintsDigestAndJSON() throws {
        let root = try OutputFiles.temporaryDirectory()
        defer { try? FileManager.default.removeItem(at: root) }
        let file = root.appendingPathComponent("payload.txt")
        try Data("abc".utf8).write(to: file)
        XCTAssertEqual(NashiCommandLine.run(arguments: ["nashi-cli", "hash", file.path]), 0)
        XCTAssertEqual(NashiCommandLine.run(arguments: ["nashi-cli", "hash", file.path, "--json"]), 0)
        XCTAssertEqual(NashiCommandLine.run(arguments: ["nashi-cli", "hash", file.path, "--algorithm", "md5"]), 0)
        XCTAssertNotEqual(NashiCommandLine.run(arguments: ["nashi-cli", "hash", file.path, "--algorithm", "sha1"]), 0)
        XCTAssertNotEqual(NashiCommandLine.run(arguments: ["nashi-cli", "hash"]), 0)
    }

    func testVerifyAcceptsMatchingChecksumsAndRejectsMismatches() throws {
        let root = try OutputFiles.temporaryDirectory()
        defer { try? FileManager.default.removeItem(at: root) }
        let file = root.appendingPathComponent("payload.txt")
        try Data("abc".utf8).write(to: file)
        let good = root.appendingPathComponent("good.sha256")
        try Data("\(sha256OfABC)  payload.txt\n".utf8).write(to: good)
        XCTAssertEqual(NashiCommandLine.run(arguments: ["nashi-cli", "hash", "--verify", good.path]), 0)

        let bad = root.appendingPathComponent("bad.sha256")
        try Data("\(sha256OfABC)  payload.txt\n\(sha256OfABC)  missing.txt\n".utf8).write(to: bad)
        XCTAssertEqual(NashiCommandLine.run(arguments: ["nashi-cli", "hash", "--verify", bad.path]), 1)

        let empty = root.appendingPathComponent("empty.sha256")
        try Data("# nothing to check\n".utf8).write(to: empty)
        XCTAssertNotEqual(NashiCommandLine.run(arguments: ["nashi-cli", "hash", "--verify", empty.path]), 0)
    }
}
