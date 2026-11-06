import XCTest
import Foundation
@testable import NashiCore

final class CompoundArchiveNamingTests: XCTestCase {
    private var root: URL!
    override func setUpWithError() throws { root = try OutputFiles.temporaryDirectory() }
    override func tearDownWithError() throws { if let root { try? FileManager.default.removeItem(at: root) } }

    private func archive(_ name: String, format: String) throws -> URL {
        let source = root.appendingPathComponent("src-\(UUID().uuidString)", isDirectory: true)
        try FileManager.default.createDirectory(at: source, withIntermediateDirectories: true)
        try Data("payload".utf8).write(to: source.appendingPathComponent("a.txt"))
        let output = root.appendingPathComponent(name)
        try ArchiveCodec.create(from: source, at: output, format: format, token: .init())
        try FileManager.default.removeItem(at: source)
        return output
    }
    private func convert(_ input: URL, _ format: String) throws -> String {
        try XCTUnwrap(DocumentEngine.convert(input, to: format, options: .init(), token: .init()).first).lastPathComponent
    }

    func testTarGzOutputsAreNamedAfterTheBareStem() throws {
        let input = try archive("Project.tar.gz", format: "gz")
        XCTAssertEqual(try convert(input, "tar"), "Project.tar")
        XCTAssertEqual(try convert(input, "zip"), "Project.zip")
        XCTAssertEqual(try convert(input, "tar"), "Project 2.tar")
        let folder = try XCTUnwrap(DocumentEngine.extractArchive(input, options: .init(), token: .init()).first)
        XCTAssertEqual(folder.lastPathComponent, "Project Extracted")
        XCTAssertEqual(try convert(try archive("Release.TAR.GZ", format: "gz"), "zip"), "Release.zip")
        XCTAssertEqual(try convert(try archive("v1.2.tar.gz", format: "gz"), "zip"), "v1.2.zip")
    }

    func testOtherDottedArchiveNamesKeepTheirStem() throws {
        XCTAssertEqual(try convert(try archive("v1.2.zip", format: "zip"), "tar"), "v1.2.tar")
        XCTAssertEqual(try convert(try archive("Bundle.tgz", format: "gz"), "zip"), "Bundle.zip")
    }
}
