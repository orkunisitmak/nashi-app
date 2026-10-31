import XCTest
@testable import NashiCore

/// bzip2 CRCs and xz check fields must be honoured, as `bzip2 -t` and `xz -t` do,
/// even when the damage sits after the tar end marker, and the expansion limit
/// must cover the whole decoded stream, not only the bytes written to disk.
final class CompressedStreamIntegrityTests: XCTestCase {
    private let fm = FileManager.default
    // a.txt (6,800 bytes) in a USTAR tarball zero-padded to 256 KiB (as a large
    // tar blocking factor produces), compressed with bzip2 -9 and xz -6 -C crc64.
    private let bz = "QlpoOTFBWSZTWfCU7MoAC9zfgNgQQAF/4AABCAhj5R5gCAhAAt2F0rA2BhgmBMBDRk0wMqf/6lQap//6qSiNAwEDAzVUQ2/1VJ+pMQYIYAKSpRMJ6m1DQ0GgzSe3+7nCiq7OtSVXU/va8Oq66Sq148fDw+nX6932+7QAAAAAAAAAAAAAAAAAAAAAAAAAAAAAABrx22RdBF/BFwIv19ubu3hjnxz58+fbz+Pf3qqrr06CTSWe/H1uayZyWBLAlgSwJWw4zjJsmybr3CbJsmybJsmybJsmybjU2pt3ZONTElC2BbAthjGWMaMYVVVVLYFsNTamy5aca8sONTam1Nqbly5cuOOOAA3KrfMrMyqqq5HQAAKqqqqqt8w5gAAtqq6ZlbVTMqsHUAAVW2+ZVVVVRzAABVVXGZVW1Va1rWta0AGGZlVVVXTflyqjm67gAFVVVVVWZgBVVVyyAAAAAIePy2yS4vLhyklkvQRfkRdQi+gi8hF6iL0EXqIusRdgi9BFwIvMIvPoIugi4EXHt7/ARcCLp2iLmIvbcCLCLxEqv+LuSKcKEh4SnZlA"
    private let xz = "/Td6WFoAAATm1rRGBMCWA4CAECEBFgAAAAAAAP+cmXHj//8Bjl0AMIuKh8QO8pek+HU9xant2Zm69tIncBPmn4xHoD/d+hum6/tlZf8U2+W2vLz/gp4+zSh6DomD5WO1M4UONvLxqjfzmBXCcM203fr2Nfy0tiSEykdzLE5gHYQP275tbEtDJmjmlTlvN8JcrykKxpv9liTxxle7/wTbsrCLQkLmHPAsLQWZfuRYFDX4Z65aBrhWSd/oPyWAk07O3IgtRH9Wa5ILb7WR0xt/jxNLR1zY6o0QF2iZLF9L26lgQ/ANkXphlTn2MD+4hkgRN8/6/TOhISQ7gRsM38wUl/e/vEuIG9xhuw1M2zd3qsCayi0N59gNs28zSQLxUKs8KzwXdsWG8k4HpA7jHNKHIlG3PS7ds0HSZERPGS1063D/PVn9F6UBu8pZvFPIhq17ouVO2SZwcJ4LUhsR8SAc6Alw+PKGcvEF06cOhs+O2iDINowJFfZMiut8NKoPYpExXWzI8eaV9o4W7ki39L5fDuHhmEVGuYyQdG/H8wgaXSJIHwT49ZE2MQWlsO5vwXA0Akqs4AAAAABkeg051UjKWAABsgOAgBAAmaNZlbHEZ/sCAAAAAARZWg=="

    private func extract(_ data: Data, named name: String, in dir: URL, limit: Int64 = 20 * 1024 * 1024 * 1024) throws -> URL? {
        let caseDir = dir.appendingPathComponent(UUID().uuidString, isDirectory: true)
        try fm.createDirectory(at: caseDir, withIntermediateDirectories: true)
        let input = caseDir.appendingPathComponent(name)
        try data.write(to: input)
        let root = caseDir.appendingPathComponent("out")
        do { try ArchiveCodec.extract(input, into: root, token: .init(), limits: .init(maximumEntries: 100_000, maximumBytes: limit)) } catch {
            XCTAssertFalse(fm.fileExists(atPath: root.path), "\(name): a rejected stream must not leave output")
            return nil
        }
        return root
    }

    func testDamagedBzip2AndXzStreamsAreRejected() throws {
        let dir = fm.temporaryDirectory.appendingPathComponent(UUID().uuidString, isDirectory: true)
        try fm.createDirectory(at: dir, withIntermediateDirectories: true)
        defer { try? fm.removeItem(at: dir) }
        let original = Data((0..<200).map { String(format: "Nashi integrity fixture line %04d\n", $0) }.joined().utf8)
        let bz = try XCTUnwrap(Data(base64Encoded: bz)), xz = try XCTUnwrap(Data(base64Encoded: xz))
        let backward = xz.suffix(8).prefix(4).reversed().reduce(0) { $0 << 8 | Int($1) }
        let check = xz.count - 12 - (backward + 1) * 4 - 8
        var bzBlock = bz; bzBlock[10] ^= 0x01
        var bzStream = bz; bzStream[bz.count - 2] ^= 0x01
        var xzCheck = xz; xzCheck[check] ^= 0x01
        for name in ["bundle.tar", "bundle.tgz", "download-0"] {
            for (base, damaged) in [(bz, [bzBlock, bzStream, bz.prefix(bz.count - 4)]), (xz, [xzCheck, xz.prefix(xz.count - 4)])] {
                let root = try XCTUnwrap(try extract(base, named: name, in: dir), name)
                XCTAssertEqual(try Data(contentsOf: root.appendingPathComponent("a.txt")), original, name)
                for data in damaged { XCTAssertNil(try extract(data, named: name, in: dir), "\(name): damaged stream was accepted") }
            }
        }
    }

    func testSkippedBzip2DataCountsTowardTheExpansionLimit() throws {
        let dir = fm.temporaryDirectory.appendingPathComponent(UUID().uuidString, isDirectory: true)
        try fm.createDirectory(at: dir, withIntermediateDirectories: true)
        defer { try? fm.removeItem(at: dir) }
        func header(_ name: String, size: Int) -> Data {
            var h = [UInt8](repeating: 0, count: 512)
            func put(_ s: String, _ at: Int) { for (i, b) in s.utf8.enumerated() { h[at + i] = b } }
            put(name, 0); put("0000644\0", 100); put(String(format: "%011o\0", size), 124); put("14524770400\0", 136)
            put("        ", 148); put("0", 156); put("ustar\0", 257); put("00", 263)
            put(String(format: "%06o\0 ", h.reduce(0) { $0 + Int($1) }), 148)
            return Data(h)
        }
        // An entry named "." is skipped, not written, yet libarchive must still decode it.
        let skipped = 4 * 1024 * 1024, text = Data("visible\n".utf8)
        let tar = header(".", size: skipped) + Data(count: skipped) + header("a.txt", size: text.count) + text + Data(count: 504 + 1024)
        let plain = dir.appendingPathComponent("plain.tar"); try tar.write(to: plain)
        let bz = try ProcessRunner.run(URL(fileURLWithPath: "/usr/bin/bzip2"), ["-c", plain.path]).output
        XCTAssertNil(try extract(bz, named: "bundle.tar", in: dir, limit: 1024 * 1024), "skipped data escaped the expansion limit")
        let root = try XCTUnwrap(try extract(bz, named: "bundle.tar", in: dir, limit: Int64(tar.count)))
        XCTAssertEqual(try Data(contentsOf: root.appendingPathComponent("a.txt")), text)
    }
}
