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
    guard args.count>=4 else { print("Usage: nashi-cli convert FORMAT FILE... [--options JSON]\n       nashi-cli tool TOOL_ID FILE... [--options JSON]\n       nashi-cli catalog");return 64 }
    var options=OperationOptions(), paths=Array(args.dropFirst(3))
    let jsonOutput=paths.contains("--json");paths.removeAll{$0=="--json"}
    if let i=paths.firstIndex(of:"--options"),i+1<paths.count {
        // Missing keys keep defaults via a JSON overlay encoded from defaults.
        guard var supplied=try JSONSerialization.jsonObject(with:Data(paths[i+1].utf8)) as? [String:Any] else { throw NashiError.invalidInput("Options must be a JSON object.") }
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
    }
    let operation:Operation
    if args[1]=="convert" { operation = .convert(args[2]) }
    else if args[1]=="tool",let kind=ToolKind(rawValue:args[2]) { operation = .tool(kind) }
    else { throw NashiError.invalidInput("Unknown operation.") }
    let outputs=try ConversionEngine.run(ConversionRequest(inputs:paths.map{URL(fileURLWithPath:$0)},operation:operation,options:options))
    if jsonOutput { print(String(decoding:try JSONEncoder().encode(outputs.map(\.path)),as:UTF8.self)) }
    else { for url in outputs { print(url.path) } }
    return 0
} catch { fputs("\(error.localizedDescription)\n",stderr);return 1 }

    }

    private static func mergeDefaults(_ defaults:[String:Any],with supplied:[String:Any])->[String:Any] {
        defaults.merging(supplied) { old,new in
            if let old=old as? [String:Any],let new=new as? [String:Any] { return mergeDefaults(old,with:new) }
            return new
        }
    }
    private static func normalizeFileURLs(_ options:inout [String:Any]) {
        func fileURL(_ value:Any?)->Any? {
            guard let value=value as? String,URL(string:value)?.scheme==nil else { return value }
            return URL(fileURLWithPath:(value as NSString).expandingTildeInPath).absoluteString
        }
        for key in ["outputDirectory","visualizationImage"] { if let value=options[key] { options[key]=fileURL(value) } }
        for key in ["imageFrame","collage"] { if var settings=options[key] as? [String:Any],let value=settings["backgroundImage"] { settings["backgroundImage"]=fileURL(value);options[key]=settings } }
        if var annotations=options["imageAnnotations"] as? [[String:Any]] { for i in annotations.indices { if let value=annotations[i]["imageURL"] { annotations[i]["imageURL"]=fileURL(value) } };options["imageAnnotations"]=annotations }
    }
}
