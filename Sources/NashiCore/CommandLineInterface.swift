import Foundation

public enum NashiCommandLine {
    public static func run(arguments args:[String] = CommandLine.arguments) -> Int32 {
do {
    if args.count==2,args[1]=="catalog" {
        let data=try JSONSerialization.data(withJSONObject:["conversionMatrix":FeatureCatalog.conversionMatrix,"tools":ToolKind.allCases.map(\.rawValue)],options:[.prettyPrinted,.sortedKeys]);print(String(decoding:data,as:UTF8.self));return 0
    }
    if args.count==3,args[1]=="inspect" {
        let input=URL(fileURLWithPath:args[2]),category=FileCategory.detect(input)
        // Categories without a reader would otherwise report a missing path as success.
        guard FileManager.default.fileExists(atPath:input.path) else { throw NashiError.invalidInput("File not found: \(input.lastPathComponent)") }
        var result:[String:Any]=["category":category.rawValue]
        switch category {
        case .image:let cg=try ImageEngine.load(input);result["width"]=cg.width;result["height"]=cg.height
        case .pdf:let pdf=try PDFEngine.open(input);result["pages"]=pdf.pageCount;result["text"]=pdf.string ?? ""
        case .audio,.video:let info=try MediaEngine.inspect(input);result["duration"]=info.duration;result["hasAudio"]=info.hasAudio;result["hasVideo"]=info.hasVideo;result["width"]=info.video?.width;result["height"]=info.video?.height;result["channels"]=info.audio?.channels
        default:break
        }
        print(String(decoding:try JSONSerialization.data(withJSONObject:result,options:.sortedKeys),as:UTF8.self));return 0
    }
    if args.count==2,args[1]=="dependencies" {
        var result:[String:String]=[:];for name in ["ffmpeg","ffprobe","soffice","exiftool"] { result[name]=try Dependencies.executable(name).path };print(String(decoding:try JSONSerialization.data(withJSONObject:result,options:.sortedKeys),as:UTF8.self));return 0
    }
    guard args.count>=4 else { fputs("Usage: nashi-cli convert FORMAT FILE... [--options JSON] [--json]\n       nashi-cli tool TOOL_ID FILE... [--options JSON] [--json]\n       nashi-cli inspect FILE\n       nashi-cli catalog\n       nashi-cli dependencies\n",stderr);return 64 }
    var options=OperationOptions(), paths=Array(args.dropFirst(3))
    let jsonOutput=paths.contains("--json");paths.removeAll{$0=="--json"}
    if let i=paths.firstIndex(of:"--options"),i+1<paths.count {
        // Missing keys keep defaults via a JSON overlay encoded from defaults.
        guard var supplied=try JSONSerialization.jsonObject(with:Data(paths[i+1].utf8)) as? [String:Any] else { throw NashiError.invalidInput("Options must be a JSON object.") }
        // The parser turns a too-large negative number into -infinity, which re-encoding raises on.
        guard JSONSerialization.isValidJSONObject(supplied) else { throw NashiError.invalidInput("Options must contain only finite numbers.") }
        normalizeFileURLs(&supplied)
        var defaults=try JSONSerialization.jsonObject(with:JSONEncoder().encode(options)) as! [String:Any]
        let optionalDefaults:[String:Data]=[
            "imageMetadata":try JSONEncoder().encode(ImageMetadataOptions()),
            "imageFrame":try JSONEncoder().encode(ImageFrameOptions()),
            "imageRedaction":try JSONEncoder().encode(ImageRedactionOptions()),
            "collage":try JSONEncoder().encode(CollageOptions())
        ]
        for (key,data) in optionalDefaults where supplied[key] is [String:Any] { defaults[key]=try JSONSerialization.jsonObject(with:data) }
        options=try JSONDecoder().decode(OperationOptions.self,from:JSONSerialization.data(withJSONObject:mergeDefaults(defaults,with:supplied)));paths.removeSubrange(i...i+1)
        // The decoder drops keys it does not know, so a misspelled option would silently fall back to its default.
        if let key=unknownKey(supplied,in:try JSONSerialization.jsonObject(with:JSONEncoder().encode(options)) as? [String:Any] ?? [:]) { throw NashiError.invalidInput("Unknown option: \(key)") }
    }
    let operation:Operation
    if args[1]=="convert" { operation = .convert(args[2]) }
    else if args[1]=="tool",let kind=ToolKind(rawValue:args[2]) { operation = .tool(kind) }
    else { throw NashiError.invalidInput("Unknown operation.") }
    // Ctrl-C, a closed terminal or a supervisor's SIGTERM cancels the job, so its tools are
    // stopped and its hidden staging folder is removed instead of left in the output folder.
    let interruption=Interruption();defer { interruption.stop() }
    let outputs:[URL]
    do { outputs=try ConversionEngine.run(ConversionRequest(inputs:paths.map{URL(fileURLWithPath:$0)},operation:operation,options:options),token:interruption.token) }
    catch where interruption.signal != 0 { fputs("Interrupted. No files were saved.\n",stderr);return 128+interruption.signal }
    if jsonOutput { print(String(decoding:try JSONEncoder().encode(outputs.map(\.path)),as:UTF8.self)) }
    else { for url in outputs { print(url.path) } }
    return 0
} catch { fputs("\(error.localizedDescription)\n",stderr);return 1 }

    }

    private static func unknownKey(_ supplied:[String:Any],in decoded:[String:Any],prefix:String="")->String? {
        for (key,value) in supplied.sorted(by:{$0.key<$1.key}) where !(value is NSNull) {
            guard let known=decoded[key] else { return prefix+key }
            if let value=value as? [String:Any],let known=known as? [String:Any],let nested=unknownKey(value,in:known,prefix:prefix+key+".") { return nested }
        }
        return nil
    }
    /// Turns SIGINT, SIGTERM and SIGHUP into cancellation for one job. A signal the caller
    /// already ignores (a background job of a script, nohup) stays ignored.
    private final class Interruption:@unchecked Sendable {
        let token=CancellationToken()
        private let lock=NSLock()
        private var received:Int32=0
        private var handled:[(Int32,sigaction,DispatchSourceSignal)]=[]
        init() {
            for value in [SIGINT,SIGTERM,SIGHUP] {
                var current=sigaction();sigaction(value,nil,&current)
                guard unsafeBitCast(current.__sigaction_u.__sa_handler,to:Int.self) != unsafeBitCast(SIG_IGN,to:Int.self) else { continue }
                let source=DispatchSource.makeSignalSource(signal:value,queue:.global())
                source.setEventHandler { [self] in
                    lock.lock();if received==0 { received=value };lock.unlock()
                    token.cancel()
                    // Children that do not poll the token stop too, as when the app quits.
                    ProcessRunner.terminateAll()
                }
                source.resume();Darwin.signal(value,SIG_IGN);handled.append((value,current,source))
            }
        }
        var signal:Int32 { lock.lock();defer { lock.unlock() };return received }
        func stop() { for (value,previous,source) in handled { var previous=previous;sigaction(value,&previous,nil);source.cancel() };handled=[] }
    }

    private static func mergeDefaults(_ defaults:[String:Any],with supplied:[String:Any])->[String:Any] {
        defaults.merging(supplied) { old,new in
            if let old=old as? [String:Any],let new=new as? [String:Any] { return mergeDefaults(old,with:new) }
            return new
        }
    }
    private static func normalizeFileURLs(_ options:inout [String:Any]) {
        func fileURL(_ value:Any?)->Any? {
            // Only file URLs pass through; a relative name such as "Q3:2024" is a path, not a URL scheme.
            guard let value=value as? String,URL(string:value)?.isFileURL != true else { return value }
            return URL(fileURLWithPath:(value as NSString).expandingTildeInPath).absoluteString
        }
        for key in ["outputDirectory","visualizationImage"] { if let value=options[key] { options[key]=fileURL(value) } }
        for key in ["imageFrame","collage"] { if var settings=options[key] as? [String:Any],let value=settings["backgroundImage"] { settings["backgroundImage"]=fileURL(value);options[key]=settings } }
        if var annotations=options["imageAnnotations"] as? [[String:Any]] { for i in annotations.indices { if let value=annotations[i]["imageURL"] { annotations[i]["imageURL"]=fileURL(value) } };options["imageAnnotations"]=annotations }
    }
}
