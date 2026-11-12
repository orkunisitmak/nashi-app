import Foundation
import AppKit
@testable import NashiCore

private final class DownloadProgressEvents:@unchecked Sendable {
    let lock=NSLock();private var values:[(Double,String)]=[]
    func append(_ value:Double,_ label:String) { lock.lock();values.append((value,label));lock.unlock() }
    var snapshot:[(Double,String)] { lock.lock();defer { lock.unlock() };return values }
}

@main struct VerifyWebDownload {
    /// Exercises downloads against a single loopback fixture, including export and cancellation.
    static func main() async throws {
        let root=URL(fileURLWithPath:CommandLine.arguments.count>1 ? CommandLine.arguments[1]:"build/web-download-050",isDirectory:true)
        let base=CommandLine.arguments.count>2 ? CommandLine.arguments[2]:"http://127.0.0.1:18749"
        let fixtureURL=URL(string:base)!
        let checkAddress:PublicMediaAddress.Check={ url in
            guard url.scheme=="http",url.host=="127.0.0.1",url.port==fixtureURL.port else { throw PublicMediaAddress.invalidAddress }
        }
        let destination=root.appendingPathComponent("EngineExports",isDirectory:true)
        try FileManager.default.createDirectory(at:destination,withIntermediateDirectories:true)
        var checks=0
        func check(_ condition:Bool,_ message:String) { precondition(condition,message);checks += 1 }
        let image=try await WebDownloadEngine.inspect(URL(string:base+"/Picture.png")!,checkAddress:checkAddress)
        check(image.direct && image.kind == .image,"Direct image detection")
        let one=try await WebDownloadEngine.download(image,mode:.original,maximumHeight:nil,destination:destination,checkAddress:checkAddress,progress:{_,_ in})
        let two=try await WebDownloadEngine.download(image,mode:.original,maximumHeight:nil,destination:destination,checkAddress:checkAddress,progress:{_,_ in})
        check(one != two,"Existing download overwritten")
        let bytes=try Data(contentsOf:root.appendingPathComponent("Server/Picture.png"))
        check(try Data(contentsOf:one[0])==bytes,"Image changed")
        check(try Data(contentsOf:two[0])==bytes,"Duplicate image changed")
        let video=try await WebDownloadEngine.inspect(URL(string:base+"/Clip.mp4")!,checkAddress:checkAddress)
        check(video.direct && video.kind == .video,"Direct video detection")
        let audio=try await WebDownloadEngine.download(video,mode:.audioMP3,maximumHeight:nil,destination:destination,checkAddress:checkAddress,progress:{_,_ in})
        let media=try MediaEngine.inspect(audio[0])
        check(audio[0].pathExtension=="mp3" && media.duration>2.8 && media.duration<3.2,"MP3 conversion failed")
        let page=try await WebDownloadEngine.inspect(URL(string:base+"/index.html")!,checkAddress:checkAddress,runExtractor:WebDownloadProcess.run)
        check(!page.direct && page.kind == .video,"HTML extraction failed")
        let site=try await WebDownloadEngine.download(page,mode:.video,maximumHeight:720,destination:destination,checkAddress:checkAddress,runExtractor:WebDownloadProcess.run,progress:{_,_ in})
        check(try MediaEngine.inspect(site[0]).duration>2.8,"Extracted download failed")
        let capped=try await WebDownloadEngine.download(page,mode:.video,maximumHeight:120,destination:destination,checkAddress:checkAddress,runExtractor:WebDownloadProcess.run,progress:{_,_ in})
        let cappedMedia=try MediaEngine.inspect(capped[0])
        check(cappedMedia.video?.height==120,"Unknown source dimensions bypassed the height limit")
        check(cappedMedia.hasAudio && cappedMedia.duration>2.8,"Resizing lost audio or duration")
        let before=Set(try FileManager.default.contentsOfDirectory(atPath:destination.path))
        let slow=try await WebDownloadEngine.inspect(URL(string:base+"/Slow.mp4")!,checkAddress:checkAddress)
        let token=CancellationToken(),start=Date(),events=DownloadProgressEvents()
        let task=Task { try await WebDownloadEngine.download(slow,mode:.original,maximumHeight:nil,destination:destination,token:token,checkAddress:checkAddress,progress:{ events.append($0,$1) }) }
        try await Task.sleep(nanoseconds:1_200_000_000);token.cancel()
        do { _=try await task.value;preconditionFailure("Cancelled download succeeded") } catch { check(Date().timeIntervalSince(start)<3,"Cancel did not stop transfer") }
        check(Set(try FileManager.default.contentsOfDirectory(atPath:destination.path))==before,"Cancellation left partial files")
        check(events.snapshot.contains{$0.0>0 && $0.0<1 && $0.1.hasPrefix("Downloading ·")},"Known byte totals never emitted live progress")
        let unknown=try await WebDownloadEngine.inspect(URL(string:base+"/UnknownSlow.mp4")!,checkAddress:checkAddress)
        let unknownToken=CancellationToken(),unknownEvents=DownloadProgressEvents()
        let unknownTask=Task { try await WebDownloadEngine.download(unknown,mode:.original,maximumHeight:nil,destination:destination,token:unknownToken,checkAddress:checkAddress,progress:{ unknownEvents.append($0,$1) }) }
        try await Task.sleep(nanoseconds:1_200_000_000);unknownToken.cancel()
        do { _=try await unknownTask.value;preconditionFailure("Unknown-length cancellation succeeded") } catch { }
        check(unknownEvents.snapshot.contains{$0.0==OperationProgress.indeterminate && $0.1.hasPrefix("Downloading ·")},"Unknown total did not report live byte counts")
        check(!unknownEvents.snapshot.contains{$0.0>=0},"Unknown transfer invented a percentage")
        check(Set(try FileManager.default.contentsOfDirectory(atPath:destination.path))==before,"Unknown-length cancellation left partial files")
        print("Web download integration: \(checks) checks passed")
    }
}
