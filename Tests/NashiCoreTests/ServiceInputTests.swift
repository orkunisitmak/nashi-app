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
        let files=try ServiceInput.files(from:[first.path,"  ",first.path,second.path,root.appendingPathComponent("missing.png").path])
        XCTAssertEqual(files.map(\.path),[first.standardizedFileURL.path,second.standardizedFileURL.path])
    }

    func testDropsNonFileAndMalformedPaths() throws {
        let file=try makeFile("c.jpg")
        let files=try ServiceInput.files(from:["https://example.com/a.png","file:///etc/hosts",String(repeating:"a",count:ServiceInput.maximumPathBytes+1),file.path])
        XCTAssertEqual(files.map(\.path),[file.standardizedFileURL.path])
    }

    func testDropsDirectories() throws {
        let folder=root.appendingPathComponent("Selected Folder",isDirectory:true)
        try FileManager.default.createDirectory(at:folder,withIntermediateDirectories:true)
        let file=try makeFile("inside.png")
        let files=try ServiceInput.files(from:[folder.path,file.path])
        XCTAssertEqual(files.map(\.path),[file.standardizedFileURL.path])
    }

    func testRejectsASelectionOverTheLimitInsteadOfTruncatingIt() {
        let paths=(0...ServiceInput.maximumItems).map{"/tmp/nashi-\($0).png"}
        XCTAssertThrowsError(try ServiceInput.files(from:paths,fileExists:{_ in true})) { error in
            XCTAssertEqual((error as? NashiError)?.errorDescription,ServiceInput.tooManyFilesMessage)
        }
    }

    func testExactlyTheLimitIsAccepted() throws {
        let paths=(0..<ServiceInput.maximumItems).map{"/tmp/nashi-\($0).png"}
        XCTAssertEqual(try ServiceInput.files(from:paths,fileExists:{_ in true}).count,ServiceInput.maximumItems)
    }

    func testDuplicatesDoNotCountTowardTheLimit() throws {
        let file=try makeFile("d.txt")
        let paths=Array(repeating:file.path,count:ServiceInput.maximumItems+50)
        XCTAssertEqual(try ServiceInput.files(from:paths).count,1)
    }

    func testRejectsPasteboardsWithTooManyCandidates() {
        let paths=Array(repeating:"",count:ServiceInput.maximumCandidates+1)
        XCTAssertThrowsError(try ServiceInput.files(from:paths,fileExists:{_ in true})) { error in
            XCTAssertEqual((error as? NashiError)?.errorDescription,ServiceInput.tooManyFilesMessage)
        }
    }

    func testRelativePathIsResolvedAgainstWorkingDirectory() throws {
        let file=try makeFile("e.webp")
        XCTAssertEqual(try ServiceInput.files(from:[file.path]).first?.lastPathComponent,"e.webp")
        let expected=URL(fileURLWithPath:FileManager.default.currentDirectoryPath)
            .appendingPathComponent("e.webp").standardizedFileURL
        XCTAssertEqual(try ServiceInput.files(from:["e.webp"],fileExists:{$0.hasSuffix("/e.webp")}).first?.path,expected.path)
    }
}
