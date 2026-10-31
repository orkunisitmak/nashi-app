import XCTest
import Foundation
@testable import NashiCore

/// The Move file action publishes into a folder other apps also write to.
final class OutputFilesMoveTests:XCTestCase {
    private var root:URL!
    override func setUpWithError() throws { root=try OutputFiles.temporaryDirectory() }
    override func tearDownWithError() throws { if let root { try? FileManager.default.removeItem(at:root) } }

    private func source(_ name:String,_ text:String) throws -> URL {
        let folder=root.appendingPathComponent(UUID().uuidString,isDirectory:true)
        try FileManager.default.createDirectory(at:folder,withIntermediateDirectories:true)
        let url=folder.appendingPathComponent(name);try Data(text.utf8).write(to:url);return url
    }

    func testMoveNeverReplacesANameTakenAfterItWasChosen() throws {
        let destination=root.appendingPathComponent("Destination",isDirectory:true)
        try FileManager.default.createDirectory(at:destination,withIntermediateDirectories:true)
        let foreign=destination.appendingPathComponent("Report.txt")
        let ours=try source("Report.txt","ours")
        // Another app saves under the chosen name right before the move.
        var chosen:[String]=[]
        let moved=try OutputFiles.move(ours,into:destination) { url in
            chosen.append(url.lastPathComponent)
            if chosen.count==1 { try? Data("another app".utf8).write(to:url) }
        }
        XCTAssertEqual(chosen,["Report.txt","Report 2.txt"])
        XCTAssertEqual(moved.lastPathComponent,"Report 2.txt")
        XCTAssertEqual(try Data(contentsOf:foreign),Data("another app".utf8))
        XCTAssertEqual(try Data(contentsOf:moved),Data("ours".utf8))
        XCTAssertFalse(FileManager.default.fileExists(atPath:ours.path))
    }

    func testMoveKeepsNamesExistingItemsAndDanglingLinks() throws {
        let destination=root.appendingPathComponent("Destination",isDirectory:true)
        try FileManager.default.createDirectory(at:destination,withIntermediateDirectories:true)
        XCTAssertEqual(try OutputFiles.move(try source("Photo.JPG","a"),into:destination).lastPathComponent,"Photo.JPG")
        try FileManager.default.createSymbolicLink(atPath:destination.appendingPathComponent("Photo 2.jpg").path,withDestinationPath:"/nonexistent/F1740")
        let third=try OutputFiles.move(try source("Photo.JPG","b"),into:destination)
        XCTAssertEqual(third.lastPathComponent,"Photo 3.jpg")
        XCTAssertEqual(try Data(contentsOf:third),Data("b".utf8))
        XCTAssertEqual(try Data(contentsOf:destination.appendingPathComponent("Photo.JPG")),Data("a".utf8))
        // Folders move too, and an existing folder of the same name is kept.
        let folder=root.appendingPathComponent("Album",isDirectory:true)
        try FileManager.default.createDirectory(at:folder,withIntermediateDirectories:true)
        try FileManager.default.createDirectory(at:destination.appendingPathComponent("Album"),withIntermediateDirectories:true)
        XCTAssertEqual(try OutputFiles.move(folder,into:destination).lastPathComponent,"Album 2")
    }

    func testMoveReportsAMissingSourceWithoutCreatingAnything() throws {
        let destination=root.appendingPathComponent("Destination",isDirectory:true)
        try FileManager.default.createDirectory(at:destination,withIntermediateDirectories:true)
        XCTAssertThrowsError(try OutputFiles.move(root.appendingPathComponent("Missing.txt"),into:destination))
        XCTAssertEqual(try FileManager.default.contentsOfDirectory(atPath:destination.path),[])
    }
}
