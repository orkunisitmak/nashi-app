import XCTest
import Foundation
@testable import NashiCore

final class ZipLegacyEntryNameTests: XCTestCase {
    private var root: URL!
    override func setUpWithError() throws { root = try OutputFiles.temporaryDirectory() }
    override func tearDownWithError() throws { if let root { try? FileManager.default.removeItem(at: root) } }

    /// Windows Explorer and older ZIP tools store names in the DOS code page
    /// without the UTF-8 flag; "résumé.txt" is 72 82 73 75 6D 82 in CP437.
    func testNamesWithoutTheUTF8FlagAreExtracted() throws {
        let input = root.appendingPathComponent("Windows.zip")
        try zip(name: [0x72, 0x82, 0x73, 0x75, 0x6d, 0x82, 0x2e, 0x74, 0x78, 0x74]).write(to: input)
        let output = root.appendingPathComponent("Extracted", isDirectory: true)
        try ArchiveCodec.extract(input, into: output, token: .init())
        let names = try FileManager.default.contentsOfDirectory(atPath: output.path).map(\.precomposedStringWithCanonicalMapping)
        XCTAssertEqual(names, ["résumé.txt"])
        XCTAssertEqual(try Data(contentsOf: output.appendingPathComponent(names[0])), Data("hello".utf8))
    }

    /// TAR names are not in a DOS code page, so an undecodable one is still rejected rather than guessed.
    func testTarNamesAreNotDecodedAsDOSCodePage() throws {
        let input = root.appendingPathComponent("Legacy.tar")
        try tar(name: [0x72, 0x82, 0x73, 0x75, 0x6d, 0x82, 0x2e, 0x74, 0x78, 0x74]).write(to: input)
        XCTAssertThrowsError(try ArchiveCodec.extract(input, into: root.appendingPathComponent("Extracted TAR", isDirectory: true), token: .init()))
    }

    private func tar(name: [UInt8]) -> Data {
        func field(_ text: String, _ length: Int) -> [UInt8] { Array(text.utf8) + [UInt8](repeating: 0, count: length - text.utf8.count) }
        var header: [UInt8] = name + [UInt8](repeating: 0, count: 100 - name.count)
        header += field("0000644", 8) + field("0000000", 8) + field("0000000", 8)
        header += field("00000000005", 12) + field("00000000000", 12)
        header += [UInt8](repeating: 0x20, count: 8)
        header += Array("0".utf8) + [UInt8](repeating: 0, count: 100)
        header += field("ustar", 6) + Array("00".utf8)
        header += [UInt8](repeating: 0, count: 512 - header.count)
        let checksum = header.reduce(0) { $0 + Int($1) }
        let digits: [UInt8] = Array(String(format: "%06o", checksum).utf8) + [0, 0x20]
        header.replaceSubrange(148..<156, with: digits)
        var body: [UInt8] = Array("hello".utf8)
        body += [UInt8](repeating: 0, count: 512 - body.count)
        return Data(header + body + [UInt8](repeating: 0, count: 1024))
    }

    private func zip(name: [UInt8]) -> Data {
        func u16(_ value: Int) -> [UInt8] { [UInt8(value & 0xff), UInt8(value >> 8 & 0xff)] }
        func u32(_ value: UInt32) -> [UInt8] { u16(Int(value & 0xffff)) + u16(Int(value >> 16)) }
        let content = Array("hello".utf8), crc: UInt32 = 0x3610a686
        // Version 2.0, no flags (so no UTF-8 name), stored, DOS time and date.
        // Built in small typed steps: one long concatenation is too slow for the type checker on CI's Swift.
        var common: [UInt8] = u16(20) + u16(0) + u16(0)
        common += u16(0) + u16(0x21) + u32(crc)
        common += u32(UInt32(content.count)) + u32(UInt32(content.count))
        common += u16(name.count) + u16(0)
        var local: [UInt8] = [0x50, 0x4b, 0x03, 0x04]
        local += common + name + content
        var central: [UInt8] = [0x50, 0x4b, 0x01, 0x02]
        central += u16(20) + common
        central += u16(0) + u16(0) + u16(0)
        central += u32(0) + u32(0) + name
        var end: [UInt8] = [0x50, 0x4b, 0x05, 0x06]
        end += u16(0) + u16(0) + u16(1) + u16(1)
        end += u32(UInt32(central.count)) + u32(UInt32(local.count)) + u16(0)
        return Data(local + central + end)
    }
}
