import XCTest
import CoreGraphics
@testable import NashiCore

final class ExportTransactionTests:XCTestCase {
    private var root:URL!
    override func setUpWithError() throws { root=try OutputFiles.temporaryDirectory() }
    override func tearDownWithError() throws { try? FileManager.default.removeItem(at:root) }

    func testOnlyReportedOutputsPublishAndNestedFolderKeepsItsContentsAndCollisionSafeName() throws {
        let destination=root.appendingPathComponent("Exports",isDirectory:true)
        let existing=destination.appendingPathComponent("Pages",isDirectory:true)
        try FileManager.default.createDirectory(at:existing,withIntermediateDirectories:true)
        let old=existing.appendingPathComponent("Existing.txt")
        try Data("previous output".utf8).write(to:old)
        let transaction=ExportTransaction();defer { transaction.cleanUp() }
        let area=try transaction.makeArea(destination:destination)
        let folder=area.directory.appendingPathComponent("Pages",isDirectory:true)
        try FileManager.default.createDirectory(at:folder,withIntermediateDirectories:true)
        let page=folder.appendingPathComponent("Page 001.png")
        try Data("a rendered page".utf8).write(to:page)
        try Data("private intermediate".utf8).write(to:area.directory.appendingPathComponent("metadata-backup.txt"))
        try transaction.record([page],in:area)
        let outputs=try transaction.publish(token:.init())
        XCTAssertEqual(outputs.map(\.lastPathComponent),["Page 001.png"])
        XCTAssertEqual(outputs.first?.deletingLastPathComponent().lastPathComponent,"Pages 2")
        XCTAssertEqual(try Data(contentsOf:outputs[0]),Data("a rendered page".utf8))
        XCTAssertEqual(try Data(contentsOf:old),Data("previous output".utf8))
        XCTAssertFalse(FileManager.default.fileExists(atPath:destination.appendingPathComponent("metadata-backup.txt").path))
    }

    func testLaterPublishFailureRollsBackEarlierExportsButKeepsExistingFiles() throws {
        let destination=root.appendingPathComponent("Exports",isDirectory:true)
        try FileManager.default.createDirectory(at:destination,withIntermediateDirectories:true)
        let existing=destination.appendingPathComponent("Report.txt")
        try Data("previous output".utf8).write(to:existing)
        let transaction=ExportTransaction();defer { transaction.cleanUp() }
        for index in 0..<2 {
            let area=try transaction.makeArea(destination:destination)
            let output=area.directory.appendingPathComponent("Report.txt")
            try Data("new report".utf8).write(to:output)
            try transaction.record([output],in:area)
            if index==1 { try FileManager.default.removeItem(at:output) }
        }
        XCTAssertThrowsError(try transaction.publish(token:.init()))
        transaction.cleanUp()
        XCTAssertEqual(try Data(contentsOf:existing),Data("previous output".utf8))
        XCTAssertEqual(try FileManager.default.contentsOfDirectory(at:destination,includingPropertiesForKeys:nil).map(\.lastPathComponent),["Report.txt"])
    }

    func testDanglingLinksInDestinationKeepTheirNamesAndDoNotBlockPublishing() throws {
        let destination=root.appendingPathComponent("Exports",isDirectory:true)
        try FileManager.default.createDirectory(at:destination,withIntermediateDirectories:true)
        for name in ["Report.txt","Pages"] {
            try FileManager.default.createSymbolicLink(atPath:destination.appendingPathComponent(name).path,withDestinationPath:"missing-\(name)")
        }
        let transaction=ExportTransaction();defer { transaction.cleanUp() }
        let area=try transaction.makeArea(destination:destination)
        let report=area.directory.appendingPathComponent("Report.txt"),folder=area.directory.appendingPathComponent("Pages",isDirectory:true)
        try FileManager.default.createDirectory(at:folder,withIntermediateDirectories:true)
        let page=folder.appendingPathComponent("Page 001.txt")
        try Data("new report".utf8).write(to:report);try Data("a page".utf8).write(to:page)
        try transaction.record([report,page],in:area)
        let outputs=try transaction.publish(token:.init())
        XCTAssertEqual(outputs.map{$0.path.replacingOccurrences(of:destination.path+"/",with:"")},["Report 2.txt","Pages 2/Page 001.txt"])
        for name in ["Report.txt","Pages"] {
            XCTAssertEqual(try FileManager.default.destinationOfSymbolicLink(atPath:destination.appendingPathComponent(name).path),"missing-\(name)")
        }
    }

    func testOptimizedSymlinkInputsProduceIndependentRegularFilesForCompressionAndMetadataRemoval() throws {
        let source=root.appendingPathComponent("Source.png")
        let context=try XCTUnwrap(CGContext(data:nil,width:8,height:8,bitsPerComponent:8,bytesPerRow:32,space:CGColorSpaceCreateDeviceRGB(),bitmapInfo:CGImageAlphaInfo.premultipliedLast.rawValue))
        context.setFillColor(CGColor(red:1,green:0,blue:0,alpha:1));context.fill(CGRect(x:0,y:0,width:8,height:8))
        try ImageEngine.write(XCTUnwrap(context.makeImage()),to:source)
        let before=try Data(contentsOf:source),link=root.appendingPathComponent("Linked.png")
        try FileManager.default.createSymbolicLink(at:link,withDestinationURL:source)
        for tool in [ToolKind.compress,.removeMetadata] {
            var options=OperationOptions();options.outputDirectory=root.appendingPathComponent(tool.rawValue,isDirectory:true);options.metadataRemoveAll=true
            let outputs=try ConversionEngine.run(.init(inputs:[link],operation:.tool(tool),options:options))
            let output=try XCTUnwrap(outputs.first)
            XCTAssertFalse(try output.resourceValues(forKeys:[.isSymbolicLinkKey]).isSymbolicLink ?? true)
            XCTAssertEqual(try ImageEngine.load(output).width,8)
            XCTAssertEqual(try Data(contentsOf:source),before)
            XCTAssertTrue(try link.resourceValues(forKeys:[.isSymbolicLinkKey]).isSymbolicLink ?? false)
        }
    }

    func testMetadataEditorReadRejectsCorruptInputAndCancellation() throws {
        let input=root.appendingPathComponent("Broken.png")
        try Data("not an image".utf8).write(to:input)
        XCTAssertThrowsError(try ImageEngine.readMetadata(input))
        let token=CancellationToken();token.cancel()
        XCTAssertThrowsError(try ImageEngine.readMetadata(input,token:token)) { error in
            guard case NashiError.cancelled=error else { return XCTFail("Expected cancellation, got \(error)") }
        }
    }
}
