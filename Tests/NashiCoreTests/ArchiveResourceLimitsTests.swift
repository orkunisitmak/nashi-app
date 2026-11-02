import XCTest
@testable import NashiCore
final class ArchiveResourceLimitsTests:XCTestCase {
    func testEntryAndExpandedByteLimits() throws {
        let manager=FileManager.default
        let root=manager.temporaryDirectory.appendingPathComponent(UUID().uuidString,isDirectory:true)
        try manager.createDirectory(at:root,withIntermediateDirectories:false)
        defer{try? manager.removeItem(at:root)}
        let source=root.appendingPathComponent("source",isDirectory:true)
        try manager.createDirectory(at:source,withIntermediateDirectories:false)
        try Data(repeating:65,count:100).write(to:source.appendingPathComponent("data.txt"))
        try Data([1]).write(to:source.appendingPathComponent("another.txt"))
        let archive=root.appendingPathComponent("test.zip")
        try ArchiveCodec.create(from:source,at:archive,format:"zip",token:.init())
        XCTAssertThrowsError(try ArchiveCodec.extract(archive,into:root.appendingPathComponent("bytes"),token:.init(),limits:.init(maximumEntries:10,maximumBytes:50)))
        XCTAssertThrowsError(try ArchiveCodec.extract(archive,into:root.appendingPathComponent("entries"),token:.init(),limits:.init(maximumEntries:1,maximumBytes:1000)))
        try ArchiveCodec.extract(archive,into:root.appendingPathComponent("valid"),token:.init())
        XCTAssertEqual(try Data(contentsOf:root.appendingPathComponent("valid/data.txt")).count,100)
    }
    func testHardLinksCountTowardExpandedByteLimit() throws {
        let manager=FileManager.default
        let root=manager.temporaryDirectory.appendingPathComponent(UUID().uuidString,isDirectory:true)
        try manager.createDirectory(at:root,withIntermediateDirectories:false)
        defer{try? manager.removeItem(at:root)}
        let source=root.appendingPathComponent("source",isDirectory:true)
        try manager.createDirectory(at:source,withIntermediateDirectories:false)
        try Data(repeating:65,count:100).write(to:source.appendingPathComponent("data.txt"))
        for index in 1...3 { try manager.linkItem(at:source.appendingPathComponent("data.txt"),to:source.appendingPathComponent("link\(index).txt")) }
        let archive=root.appendingPathComponent("links.tar")
        _=try ProcessRunner.run(URL(fileURLWithPath:"/usr/bin/tar"),["-cf",archive.path,"-C",source.path,"data.txt","link1.txt","link2.txt","link3.txt"])
        // Conversion writes 400 bytes for these entries, so a 399-byte limit must refuse them.
        XCTAssertThrowsError(try ArchiveCodec.extract(archive,into:root.appendingPathComponent("over"),token:.init(),limits:.init(maximumEntries:10,maximumBytes:399)))
        let valid=root.appendingPathComponent("valid",isDirectory:true)
        try ArchiveCodec.extract(archive,into:valid,token:.init(),limits:.init(maximumEntries:10,maximumBytes:400))
        XCTAssertEqual(try Data(contentsOf:valid.appendingPathComponent("link3.txt")).count,100)
    }
}
