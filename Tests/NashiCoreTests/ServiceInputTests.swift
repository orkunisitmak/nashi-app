import XCTest
@testable import NashiCore

final class ServiceInputTests:XCTestCase {
    private var root:URL!

    override func setUpWithError() throws {
        root=FileManager.default.temporaryDirectory.appendingPathComponent("Nashi-ServiceInput-\(UUID().uuidString)",isDirectory:true)
        try FileManager.default.createDirectory(at:root,withIntermediateDirectories:true)
    }
    override func tearDownWithError() throws { try? FileManager.default.removeItem(at:root) }

    private func makeFile(_ name:String) throws -> URL {
        let url=root.appendingPathComponent(name)
        try Data("x".utf8).write(to:url)
        return url
    }

    func testKeepsExistingLocalFilesAndDropsDuplicates() throws {
        let first=try makeFile("a.png"), second=try makeFile("b.pdf")
        let files=ServiceInput.files(from:[first.path,"  ",first.path,second.path,root.appendingPathComponent("missing.png").path])
        XCTAssertEqual(files.map(\.path),[first.standardizedFileURL.path,second.standardizedFileURL.path])
    }

    func testDropsNonFileAndMalformedPaths() throws {
        let file=try makeFile("c.jpg")
        let files=ServiceInput.files(from:["https://example.com/a.png","file:///etc/hosts",String(repeating:"a",count:ServiceInput.maximumPathBytes+1),file.path])
        XCTAssertEqual(files.map(\.path),[file.standardizedFileURL.path])
    }

    func testCapsLargeBatchesInsteadOfStartingUnboundedWork() throws {
        let file=try makeFile("d.txt")
        let paths=Array(repeating:file.path,count:ServiceInput.maximumItems+50)
        XCTAssertEqual(ServiceInput.files(from:paths).count,1)
        let distinct=(0..<ServiceInput.maximumItems+10).map{root.appendingPathComponent("f\($0).txt").path}
        XCTAssertEqual(ServiceInput.files(from:distinct,fileExists:{_ in true}).count,ServiceInput.maximumItems)
    }

    func testRelativePathIsResolvedAgainstWorkingDirectory() throws {
        let file=try makeFile("e.webp")
        XCTAssertEqual(ServiceInput.files(from:[file.path]).first?.lastPathComponent,"e.webp")
        XCTAssertTrue(ServiceInput.files(from:["e.webp"],fileExists:{$0.hasSuffix("/e.webp")}).first?.isFileURL ?? false)
    }
}
