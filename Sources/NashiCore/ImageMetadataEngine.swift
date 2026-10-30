import Foundation
import ImageIO
import CryptoKit

/// Metadata-only writes on an owned copy. The public ExifTool dependency is
/// acquired independently; no original Nashi bridge or assets are used.
public enum ImageMetadataEngine {
    private static let formats:Set<String>=["jpg","jpeg","png","heic","heif","tif","tiff","webp","gif","avif"]
    private static let appearanceTags:Set<String>=["Orientation","ImageWidth","ImageHeight","ExifImageWidth","ExifImageHeight","ColorSpace","Gamma","WhitePoint","PrimaryChromaticities","YCbCrCoefficients","YCbCrPositioning","BitsPerSample","SamplesPerPixel","Compression","PhotometricInterpretation","PlanarConfiguration","StripOffsets","StripByteCounts","RowsPerStrip","TileOffsets","TileByteCounts","YCbCrSubSampling","AnimationIterations","LoopCount","FrameCount","Duration"]

    public static func fields(_ input:URL,token:CancellationToken = .init()) throws -> [String:String] {
        let raw=try read(input,token:token)
        var result:[String:String]=[:]
        for (tag,value) in raw {
            guard tag != "SourceFile",let colon=tag.firstIndex(of:":") else { continue }
            let group=String(tag[..<colon])
            guard !["File","System","ExifTool","Composite"].contains(group) else { continue }
            result[displayKey(tag)]=displayValue(value)
        }
        if let packet=try? run(["-b","-XMP",input.path],token:token).output,let custom=try? XMPPacketEditor.fields(packet) {
            for (key,value) in custom where !result.keys.contains(where:{$0.caseInsensitiveCompare(key) == .orderedSame}) { result[key]=value }
        }
        return result
    }

    public static func isWritable(_ displayKey:String)->Bool {
        guard let tag=try? tagName(displayKey),let colon=tag.lastIndex(of:":") else { return false }
        // ExifTool matches group and tag names case-insensitively and accepts several group prefixes.
        let groups=tag[..<colon].split(separator:":").map{$0.lowercased()},name=tag[tag.index(after:colon)...].lowercased()
        let blocked=["File","System","ExifTool","Composite","ICC_Profile","JFIF","PNG","GIF","RIFF","VP8","VP8L"].map{$0.lowercased()}
        guard !groups.contains(where:blocked.contains),!appearanceTags.contains(where:{$0.lowercased()==name}) else { return false }
        return true
    }

    public static func copyMetadata(from source:URL,to output:URL,token:CancellationToken) throws {
        guard formats.contains(source.pathExtension.lowercased()),formats.contains(output.pathExtension.lowercased()) else { return }
        let hash=try encodedImageHash(output,token:token)
        let sourceMetadata=try read(source,token:token)
        // Pixels are already transformed into display orientation by ImageEngine.
        // Disable ExifTool's inverse print conversion: bare "1" can match the
        // human-readable "Rotate 180" description and write numeric value 3.
        _ = try run(["-overwrite_original","-TagsFromFile",source.path,"-EXIF:all","-XMP","-IPTC:all","--Orientation","-Orientation#=1",output.path],token:token)
        // XMP is copied as a packet, so excluding its individual orientation
        // tag above cannot remove the old value. Some readers prefer XMP over
        // EXIF. Packet transfer takes precedence over individual XMP writes in
        // the same command, so normalize existing XMP in a subsequent write.
        if sourceMetadata["XMP-tiff:Orientation"] != nil {
            _ = try run(["-overwrite_original","-XMP-tiff:Orientation#=1",output.path],token:token)
        }
        guard try encodedImageHash(output,token:token)==hash else { throw NashiError.failed("Metadata transfer changed encoded image data.") }
        let written=try read(output,token:token)
        for key in ["IFD0:Orientation","XMP-tiff:Orientation"] where written[key] != nil {
            guard (written[key] as? NSNumber)?.intValue == 1 else { throw NashiError.failed("Metadata transfer did not preserve the image's display orientation. No output was saved.") }
        }
    }

    public static func edit(_ input:URL,changes:ImageMetadataOptions,options:OperationOptions,token:CancellationToken) throws -> URL {
        try token.check()
        guard formats.contains(input.pathExtension.lowercased()) else { throw NashiError.unsupported("Metadata-only editing is unavailable for this image format.") }
        let original=try fingerprint(input,token:token)
        let sourceHash=SHA256.hash(data:try Data(contentsOf:input))
        let directory=try OutputFiles.temporaryDirectory()
        defer { try? FileManager.default.removeItem(at:directory) }
        let staged=directory.appendingPathComponent("image").appendingPathExtension(input.pathExtension)
        try FileManager.default.copyItem(at:input.resolvingSymlinksInPath(),to:staged)
        var arguments=["-P","-overwrite_original"]
        var containerChanges=changes.removeAll
        var xmpUpdates:[String:String]=[:],xmpRemovals:[String]=[]
        if changes.removeAll {
            // Color interpretation and display orientation are part of appearance.
            // ExifTool edits container metadata without decoding encoded samples.
            arguments += ["-all=","--ICC_Profile:all","-TagsFromFile","@","-ColorSpaceTags","-Orientation#"]
            if ["tif","tiff"].contains(input.pathExtension.lowercased()) {
                arguments.append("-CommonIFD0=")
                let raw=try read(input,token:token)
                for name in ["DocumentName","HostComputer","ImageID"] where raw["IFD0:"+name] != nil { arguments.append("-IFD0:\(name)=") }
            }
        }
        for key in changes.removals.sorted() {
            guard isWritable(key) else { throw NashiError.invalidInput("\(key) is structural or read-only metadata.") }
            if XMPPacketEditor.isXMP(key) { xmpRemovals.append(key);continue }
            containerChanges=true
            arguments.append("-\(try tagName(key))=")
        }
        for (key,value) in changes.updates.sorted(by:{$0.key<$1.key}) {
            guard isWritable(key) else { throw NashiError.invalidInput("\(key) is structural or read-only metadata.") }
            if XMPPacketEditor.isXMP(key) { xmpUpdates[key]=value;continue }
            containerChanges=true
            let tag=try tagName(key)
            if ["GPS:GPSLatitude","GPS:GPSLongitude"].contains(tag),let coordinate=Double(value) {
                arguments.append("-\(tag)=\(abs(coordinate))")
                arguments.append("-\(tag)Ref=\(tag.hasSuffix("Latitude") ? (coordinate<0 ? "S":"N"):(coordinate<0 ? "W":"E"))")
            } else { arguments.append("-\(tag)=\(value)") }
        }
        if containerChanges {
            arguments.append(staged.path)
            _ = try run(arguments,token:token)
        }
        if !xmpUpdates.isEmpty || !xmpRemovals.isEmpty {
            let originalPacket=try run(["-b","-XMP",staged.path],token:token).output
            let updated=try XMPPacketEditor.edit(originalPacket,updates:xmpUpdates,removals:xmpRemovals)
            let packet=directory.appendingPathComponent("metadata.xmp");try updated.write(to:packet)
            _ = try run(["-overwrite_original","-XMP<="+packet.path,staged.path],token:token)
        }
        let after=try fingerprint(staged,token:token)
        guard original==after else { throw NashiError.failed("Metadata changes would alter encoded image data or its appearance. No output was saved.") }
        let written=try fields(staged,token:token)
        for (key,value) in changes.updates {
            let expectedKey=displayKey(try tagName(key))
            let actual=written.first(where:{$0.key.caseInsensitiveCompare(expectedKey) == .orderedSame})?.value
            let tag=try tagName(key),expected=(["GPS:GPSLatitude","GPS:GPSLongitude"].contains(tag) ? Double(value).map{String(abs($0))}:nil) ?? value
            guard equivalent(actual,expected) else { throw NashiError.failed("The metadata field \(key) was not written. No output was saved.") }
        }
        for key in changes.removals {
            let expectedKey=displayKey(try tagName(key))
            guard !written.keys.contains(where:{$0.caseInsensitiveCompare(expectedKey) == .orderedSame}) else { throw NashiError.failed("The metadata field \(key) could not be removed.") }
        }
        guard sourceHash==SHA256.hash(data:try Data(contentsOf:input)) else { throw NashiError.failed("The source changed during this operation. No output was saved.") }
        try token.check()
        let output=OutputFiles.unique(for:input,extension:input.pathExtension,suffix:changes.removeAll ? " - Clean":" - Metadata",directory:options.outputDirectory)
        try FileManager.default.moveItem(at:staged,to:output)
        return output
    }

    /// Public for independent verification. ImageDataHash covers encoded samples;
    /// frame timing, orientation, ICC bytes and dimensions are checked separately.
    public static func encodedImageHash(_ input:URL,token:CancellationToken = .init()) throws -> String {
        if input.pathExtension.lowercased()=="gif" { return try gifImageHash(Data(contentsOf:input)) }
        let result=try run(["-j","-s","-api","RequestAll=3","-api","ImageHashType=SHA256","-ImageDataHash",input.path],token:token)
        let rows=try JSONSerialization.jsonObject(with:result.output) as? [[String:Any]]
        guard let hash=rows?.first?["ImageDataHash"] as? String,!hash.isEmpty else { throw NashiError.failed("The encoded image data cannot be verified for this format.") }
        return hash
    }

    private static func gifImageHash(_ data:Data) throws -> String {
        let bytes=[UInt8](data)
        guard bytes.count>=13,String(bytes:bytes[0..<6],encoding:.ascii)?.hasPrefix("GIF8")==true else { throw NashiError.invalidInput("Invalid GIF header.") }
        var position=13
        if bytes[10]&0x80 != 0 { position += 3*(1 << (Int(bytes[10]&7)+1)) }
        guard position<=bytes.count else { throw NashiError.invalidInput("Invalid GIF color table.") }
        var imageData=Data(bytes[0..<position])
        func skipBlocks(_ start:Int) throws -> Int {
            var p=start
            while p<bytes.count { let size=Int(bytes[p]);p += 1;if size==0 { return p };guard size<=bytes.count-p else { break };p += size }
            throw NashiError.invalidInput("Truncated GIF block.")
        }
        while position<bytes.count {
            let start=position
            switch bytes[position] {
            case 0x2c:
                guard position+10<=bytes.count else { throw NashiError.invalidInput("Truncated GIF frame.") }
                let packed=bytes[position+9];position += 10
                if packed&0x80 != 0 { position += 3*(1 << (Int(packed&7)+1)) }
                guard position<bytes.count else { throw NashiError.invalidInput("Truncated GIF image data.") }
                position=try skipBlocks(position+1)
                imageData.append(contentsOf:bytes[start..<position])
            case 0x21:
                guard position+2<bytes.count else { throw NashiError.invalidInput("Truncated GIF extension.") }
                let label=bytes[position+1];position=try skipBlocks(position+2)
                if label==0xf9 || label==0x01 { imageData.append(contentsOf:bytes[start..<position]) }
            case 0x3b:
                imageData.append(0x3b)
                return SHA256.hash(data:imageData).map{String(format:"%02x",$0)}.joined()
            default: throw NashiError.invalidInput("Invalid GIF block type.")
            }
        }
        throw NashiError.invalidInput("GIF trailer is missing.")
    }

    private struct Fingerprint:Equatable {
        var imageHash:String
        var profile:Data
        var frames:[[String]]
    }
    private static func fingerprint(_ input:URL,token:CancellationToken) throws -> Fingerprint {
        let hash=try encodedImageHash(input,token:token)
        let profile=try run(["-b","-ICC_Profile",input.path],token:token).output
        guard let source=CGImageSourceCreateWithURL(input as CFURL,nil) else { throw NashiError.invalidInput("Could not read image structure.") }
        var frames:[[String]]=[]
        let global=(CGImageSourceCopyProperties(source,nil) as? [String:Any]) ?? [:]
        let globalGIF=global[kCGImagePropertyGIFDictionary as String] as? [String:Any]
        for i in 0..<CGImageSourceGetCount(source) {
            let props=(CGImageSourceCopyPropertiesAtIndex(source,i,nil) as? [String:Any]) ?? [:]
            let gif=props[kCGImagePropertyGIFDictionary as String] as? [String:Any]
            frames.append([displayValue(props[kCGImagePropertyPixelWidth as String] ?? 0),displayValue(props[kCGImagePropertyPixelHeight as String] ?? 0),displayValue(props[kCGImagePropertyOrientation as String] ?? 1),displayValue(gif?[kCGImagePropertyGIFDelayTime as String] ?? 0),displayValue(gif?[kCGImagePropertyGIFUnclampedDelayTime as String] ?? 0),displayValue(globalGIF?[kCGImagePropertyGIFLoopCount as String] ?? -1)])
        }
        return Fingerprint(imageHash:hash,profile:profile,frames:frames)
    }

    private static func read(_ input:URL,token:CancellationToken) throws -> [String:Any] {
        let result=try run(["-j","-G1","-s","-n","-a",input.path],token:token)
        guard let rows=try JSONSerialization.jsonObject(with:result.output) as? [[String:Any]],let first=rows.first else { throw NashiError.failed("Could not inspect image metadata.") }
        return first
    }
    private static func run(_ args:[String],token:CancellationToken) throws -> ProcessResult {
        var paths:[URL]=[]
        if let override=ProcessInfo.processInfo.environment["NASHI_EXIFTOOL"] { paths.append(URL(fileURLWithPath:override)) }
        if let resource=Bundle.main.resourceURL { paths.append(resource.appendingPathComponent("Tools/ExifTool/exiftool")) }
        #if DEBUG
        let root=URL(fileURLWithPath:#filePath).deletingLastPathComponent().deletingLastPathComponent().deletingLastPathComponent()
        paths.append(root.appendingPathComponent("Tools/ExifTool/exiftool"))
        #endif
        guard let script=paths.first(where:{FileManager.default.fileExists(atPath:$0.path)}) else { throw NashiError.missingDependency("ExifTool; run scripts/setup-exiftool.sh") }
        // Empty config disables executable user configuration. Values remain argv,
        // never shell code, and the source is only read or copied.
        return try ProcessRunner.run(URL(fileURLWithPath:"/usr/bin/perl"),[script.path,"-config",""]+args,token:token)
    }
    private static func displayKey(_ tag:String)->String {
        guard let colon=tag.firstIndex(of:":") else { return tag }
        let group=String(tag[..<colon]);var name=String(tag[tag.index(after:colon)...])
        if group=="IFD0",name=="ModifyDate" { name="DateTime" }
        let mapped=["IFD0":"TIFF","IFD1":"IFD1","ExifIFD":"Exif","GPS":"GPS"][group] ?? group
        if group=="GPS",name.hasPrefix("GPS") { name=String(name.dropFirst(3)) }
        return "{\(mapped)}.\(name)"
    }
    private static func tagName(_ key:String) throws -> String {
        let aliases=["artist":"IFD0:Artist","author":"IFD0:Artist","copyright":"IFD0:Copyright","description":"IFD0:ImageDescription","title":"IFD0:ImageDescription","comment":"ExifIFD:UserComment","software":"IFD0:Software","make":"IFD0:Make","model":"IFD0:Model","latitude":"GPS:GPSLatitude","longitude":"GPS:GPSLongitude","date":"ExifIFD:DateTimeOriginal"]
        if let tag=aliases[key.lowercased()] { return tag }
        var tag=key
        if key.hasPrefix("{"),let close=key.range(of:"}.") {
            let group=String(key[key.index(after:key.startIndex)..<close.lowerBound]),name=String(key[close.upperBound...])
            let mapped=["TIFF":"IFD0","Exif":"ExifIFD","GPS":"GPS"][group] ?? group
            let field=group=="TIFF" && name=="DateTime" ? "ModifyDate":name
            tag="\(mapped):\(group=="GPS" && !field.hasPrefix("GPS") ? "GPS":"")\(field)"
        }
        guard tag.range(of:"^[A-Za-z0-9_][A-Za-z0-9_:-]*:[A-Za-z0-9_]+$",options:.regularExpression) != nil else { throw NashiError.invalidInput("Invalid metadata field: \(key)") }
        return tag
    }
    private static func displayValue(_ value:Any)->String {
        if let value=value as? String { return value }
        if let value=value as? NSNumber { return value.stringValue }
        if JSONSerialization.isValidJSONObject(value),let data=try? JSONSerialization.data(withJSONObject:value,options:[.sortedKeys]),let text=String(data:data,encoding:.utf8) { return text }
        return String(describing:value)
    }
    private static func equivalent(_ actual:String?,_ expected:String)->Bool {
        if actual==expected { return true }
        if let actual,let a=try? JSONSerialization.jsonObject(with:Data(actual.utf8)),let b=try? JSONSerialization.jsonObject(with:Data(expected.utf8)),let a=a as? NSObject,let b=b as? NSObject,a==b { return true }
        if let actual,let list=(try? JSONSerialization.jsonObject(with:Data(actual.utf8))) as? [String],list==[expected] { return true }
        if let actual,let a=Double(actual),let b=Double(expected) { return abs(a-b)<0.0000001 }
        return expected.isEmpty && actual==nil
    }
}
