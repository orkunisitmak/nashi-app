import XCTest
@testable import NashiCore

final class FloatShelfDragTests:XCTestCase {
    func testOnlyDraggedFilesThatVanishedAreDropped() throws {
        let folder=FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString,isDirectory:true)
        try FileManager.default.createDirectory(at:folder,withIntermediateDirectories:true);defer { try? FileManager.default.removeItem(at:folder) }
        let moved=folder.appendingPathComponent("moved.txt"),copied=folder.appendingPathComponent("copied.txt"),offline=folder.appendingPathComponent("offline.txt")
        for url in [moved,copied,offline] { try Data("x".utf8).write(to:url) }
        try FileManager.default.removeItem(at:moved);try FileManager.default.removeItem(at:offline)
        let items=[(id:1,url:moved),(id:2,url:copied),(id:3,url:offline)]
        // The drag carried the moved and copied files; the offline one was not part of it.
        XCTAssertEqual(FloatShelfDrag.vanished(items,dragged:[1,2]),[1])
    }
    func testUnreachableItemOutsideTheDragStays() {
        let items=[(id:"a",url:URL(fileURLWithPath:"/Volumes/Backup/a.mov")),(id:"b",url:URL(fileURLWithPath:"/Volumes/Backup/b.mov"))]
        XCTAssertEqual(FloatShelfDrag.vanished(items,dragged:["b"],exists:{_ in false}),["b"])
        XCTAssertEqual(FloatShelfDrag.vanished(items,dragged:[],exists:{_ in false}),[])
    }
}
