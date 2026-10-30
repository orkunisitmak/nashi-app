import XCTest
@testable import NashiCore

final class ToolSetupTests:XCTestCase {
    func testEmptyExecutableCannotReportReady() throws {
        try temporary { root in
            let name="nashi_setup_fixture_"+UUID().uuidString.replacingOccurrences(of:"-",with:""),file=root.appendingPathComponent("tool")
            let variable="NASHI_"+name.uppercased()
            try Data().write(to:file);try FileManager.default.setAttributes([.posixPermissions:0o755],ofItemAtPath:file.path)
            setenv(variable,file.path,1);defer{unsetenv(variable)}
            XCTAssertThrowsError(try Dependencies.executable(name))
            try Data("#!/bin/sh\nexit 0\n".utf8).write(to:file)
            XCTAssertEqual(try Dependencies.executable(name),file)
        }
    }
    func testReadinessRequiresEveryMediaExecutableAndOffice() {
        let names=["ffmpeg","ffprobe","yt-dlp","deno","soffice"]
        for missing in names {
            let status=ToolSetupStatus { $0 == missing ? nil:URL(fileURLWithPath:"/fixture/"+$0) }
            XCTAssertFalse(status.ready)
            XCTAssertEqual(status.missing,missing == "soffice" ? [.office]:[.media])
        }
        XCTAssertEqual(ToolSetupStatus(resolve:{_ in nil}).missing,[.media,.office])
        XCTAssertTrue(ToolSetupStatus(resolve:{URL(fileURLWithPath:"/already-installed/"+$0)}).ready)
    }
    func testRepairPublishesCompleteDirectoryAndKeepsHealthyInstall() throws {
        try temporary { root in
            let target=root.appendingPathComponent("tools"),stage=root.appendingPathComponent("stage")
            try FileManager.default.createDirectory(at:target,withIntermediateDirectories:false)
            try Data("old".utf8).write(to:target.appendingPathComponent("partial"))
            try completeStage(stage,"new")
            try ToolInstallation.publish(stage,to:target,complete:isComplete)
            XCTAssertEqual(try String(contentsOf:target.appendingPathComponent("ready"),encoding:.utf8),"new")
            XCTAssertFalse(FileManager.default.fileExists(atPath:target.appendingPathComponent("partial").path))
            let second=root.appendingPathComponent("second");try completeStage(second,"other")
            try ToolInstallation.publish(second,to:target,complete:isComplete)
            XCTAssertEqual(try String(contentsOf:target.appendingPathComponent("ready"),encoding:.utf8),"new")
            XCTAssertTrue(FileManager.default.fileExists(atPath:second.path))
        }
    }
    func testIncompleteDownloadDoesNotReplaceExistingTools() throws {
        try temporary { root in
            let target=root.appendingPathComponent("tools"),stage=root.appendingPathComponent("stage")
            try completeStage(target,"old")
            try FileManager.default.createDirectory(at:stage,withIntermediateDirectories:false)
            XCTAssertThrowsError(try ToolInstallation.publish(stage,to:target,complete:isComplete))
            XCTAssertEqual(try String(contentsOf:target.appendingPathComponent("ready"),encoding:.utf8),"old")
        }
    }
    func testFailedPublishRollsBackPartialInstallation() throws {
        try temporary { root in
            let target=root.appendingPathComponent("tools"),stage=root.appendingPathComponent("stage")
            try FileManager.default.createDirectory(at:target,withIntermediateDirectories:false)
            try Data("keep".utf8).write(to:target.appendingPathComponent("partial"));try completeStage(stage,"new")
            XCTAssertThrowsError(try ToolInstallation.publish(stage,to:target) { path in
                if path==stage {try? FileManager.default.removeItem(at:stage);return true};return false
            })
            XCTAssertEqual(try String(contentsOf:target.appendingPathComponent("partial"),encoding:.utf8),"keep")
        }
    }
    func testRepairReplacesDanglingToolDirectoryLink() throws {
        try temporary { root in
            let target=root.appendingPathComponent("tools"),stage=root.appendingPathComponent("stage")
            try FileManager.default.createSymbolicLink(atPath:target.path,withDestinationPath:root.appendingPathComponent("unmounted/tools").path)
            try completeStage(stage,"new")
            try ToolInstallation.publish(stage,to:target,complete:isComplete)
            XCTAssertEqual(try String(contentsOf:target.appendingPathComponent("ready"),encoding:.utf8),"new")
            XCTAssertEqual(try FileManager.default.contentsOfDirectory(atPath:root.path).filter{$0.hasPrefix(".previous-")},[])
        }
    }
    private func isComplete(_ directory:URL)->Bool { FileManager.default.fileExists(atPath:directory.appendingPathComponent("ready").path) }
    private func completeStage(_ path:URL,_ content:String)throws {
        try FileManager.default.createDirectory(at:path,withIntermediateDirectories:false)
        try Data(content.utf8).write(to:path.appendingPathComponent("ready"))
    }
    private func temporary(_ run:(URL)throws->Void)throws {
        let root=FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        try FileManager.default.createDirectory(at:root,withIntermediateDirectories:false)
        defer { try? FileManager.default.removeItem(at:root) };try run(root)
    }
}
