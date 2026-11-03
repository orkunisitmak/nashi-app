import XCTest
import Foundation
@testable import NashiCore

final class ZipEntryNameEncodingTests: XCTestCase {
    private var root: URL!
    override func setUpWithError() throws { root = try OutputFiles.temporaryDirectory() }
    override func tearDownWithError() throws { if let root { try? FileManager.default.removeItem(at: root) } }

    func testNonASCIINamesAreMarkedAsUTF8() throws {
        let source = root.appendingPathComponent("Source", isDirectory: true)
        try FileManager.default.createDirectory(at: source.appendingPathComponent("Belgeler"), withIntermediateDirectories: true)
        try Data("x".utf8).write(to: source.appendingPathComponent("Belgeler/Şişli résumé.txt"))
        let zip = root.appendingPathComponent("Names.zip")
        try ArchiveCodec.create(from: source, at: zip, format: "zip", token: .init())

        // The ZIP specification reads names without general purpose flag
        // bit 11 as CP437, so UTF-8 bytes must carry that flag.
        let bytes = [UInt8](try Data(contentsOf: zip))
        func u16(_ offset: Int) -> Int { Int(bytes[offset]) | Int(bytes[offset + 1]) << 8 }
        var names: [String: Bool] = [:]
        var offset = 0
        while offset + 46 <= bytes.count {
            guard bytes[offset..<offset + 4].elementsEqual([0x50, 0x4b, 0x01, 0x02]) else { offset += 1; continue }
            let length = u16(offset + 28)
            let name = String(decoding: bytes[(offset + 46)..<(offset + 46 + length)], as: UTF8.self).precomposedStringWithCanonicalMapping
            names[name] = u16(offset + 8) & 0x800 != 0
            offset += 46 + length
        }
        XCTAssertEqual(names["Belgeler/Şişli résumé.txt"], true, "\(names)")
        let extracted = root.appendingPathComponent("Extracted", isDirectory: true)
        try ArchiveCodec.extract(zip, into: extracted, token: .init())
        XCTAssertEqual(try FileManager.default.subpathsOfDirectory(atPath: extracted.path).map(\.precomposedStringWithCanonicalMapping).sorted(), ["Belgeler", "Belgeler/Şişli résumé.txt"])    }
}
