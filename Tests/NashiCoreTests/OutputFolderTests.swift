import XCTest
@testable import NashiCore

final class OutputFolderTests:XCTestCase {
    private var root:URL!
    override func setUpWithError() throws {
        root=FileManager.default.temporaryDirectory.appendingPathComponent("nashi-output-folder-\(UUID().uuidString)",isDirectory:true)
        try FileManager.default.createDirectory(at:root,withIntermediateDirectories:true)
    }
    override func tearDownWithError() throws { try? FileManager.default.removeItem(at:root) }

    func testExistingFolderIsUsed() {
        XCTAssertEqual(OutputFolder.resolve(path:root.path)?.path,root.standardizedFileURL.path)
    }

    func testMissingFileOrNilPathMeansBesideTheOriginal() throws {
        XCTAssertNil(OutputFolder.resolve(path:nil))
        XCTAssertNil(OutputFolder.resolve(path:""))
        XCTAssertNil(OutputFolder.resolve(path:root.appendingPathComponent("Missing").path))
    }

    func testRelativePathsAreIgnored() {
        XCTAssertNil(OutputFolder.resolve(path:"Downloads"))
        XCTAssertNil(OutputFolder.resolve(path:"~/Downloads"))
    }

    func testAFileCannotBeAnOutputFolder() throws {
        let file=root.appendingPathComponent("note.txt")
        try Data("x".utf8).write(to:file)
        XCTAssertNil(OutputFolder.resolve(path:file.path))
    }

    func testTrailingSlashAndParentComponentsAreNormalised() throws {
        let nested=root.appendingPathComponent("One/Two",isDirectory:true)
        try FileManager.default.createDirectory(at:nested,withIntermediateDirectories:true)
        let stored=root.path+"/One/../One/Two/"
        XCTAssertEqual(OutputFolder.resolve(path:stored)?.path,nested.standardizedFileURL.path)
    }

    func testAReplacedFolderIsNotReusedThroughASymlinkToAFile() throws {
        let file=root.appendingPathComponent("target.txt")
        try Data("x".utf8).write(to:file)
        let link=root.appendingPathComponent("Shortcut")
        try FileManager.default.createSymbolicLink(at:link,withDestinationURL:file)
        XCTAssertNil(OutputFolder.resolve(path:link.path))
    }
}
