import Foundation
import PDFKit
import AppKit

public enum PDFEngine {
    public static func open(_ input: URL, password: String = "") throws -> PDFDocument {
        guard let pdf=PDFDocument(url:input) else { throw NashiError.invalidInput("This PDF could not be opened.") }
        if pdf.isLocked && !pdf.unlock(withPassword:password) { throw NashiError.invalidInput("This PDF is password protected. Enter its password.") }
        guard pdf.pageCount>0 else { throw NashiError.invalidInput("The PDF contains no pages.") };return pdf
    }
    public static func create(inputs: [URL], options: OperationOptions, token: CancellationToken, progress: ProgressHandler = {_,_ in}) throws -> [URL] {
        guard let first=inputs.first else { throw NashiError.invalidInput("Choose images to create a PDF.") }
        let pdf=PDFDocument()
        for (index,input) in inputs.enumerated() { progress(Double(index)/Double(inputs.count),"Adding image \(index+1) of \(inputs.count)…");try token.check(); let cg=try ImageEngine.load(input); if let page=PDFPage(image:NSImage(cgImage:cg,size:NSSize(width:cg.width,height:cg.height))) { pdf.insert(page,at:pdf.pageCount) } }
        let output=OutputFiles.unique(for:first,extension:"pdf",directory:options.outputDirectory)
        progress(OperationProgress.indeterminate,"Writing PDF…")
        guard pdf.write(to:output) else { throw NashiError.failed("Could not create PDF.") };return [output]
    }
    public static func render(_ input: URL, to format:String, options:OperationOptions, token:CancellationToken, progress: ProgressHandler = {_,_ in}) throws -> [URL] {
        let pdf=try open(input,password:options.password)
        let folder=OutputFiles.unique(for:input,extension:"",suffix:" - Pages",directory:options.outputDirectory)
        try FileManager.default.createDirectory(at:folder,withIntermediateDirectories:true)
        var outputs:[URL]=[]
        do {
            for index in 0..<pdf.pageCount {
                try token.check();guard let page=pdf.page(at:index),let ref=page.pageRef else { throw NashiError.failed("A PDF page could not be read.") }
                let bounds=PDFOfficeCodec.visualBounds(page),scale=Double(max(36,min(1200,options.dpi)))/72
                let scaledWidth=bounds.width*scale,scaledHeight=bounds.height*scale
                guard scaledWidth.isFinite,scaledHeight.isFinite,scaledWidth>0,scaledHeight>0,scaledWidth<=32000,scaledHeight<=32000 else { throw NashiError.failed("PDF page is too large to render at this DPI.") }
                let w=max(1,Int(scaledWidth.rounded())),h=max(1,Int(scaledHeight.rounded()))
                guard w<=32000,h<=32000,let ctx=CGContext(data:nil,width:w,height:h,bitsPerComponent:8,bytesPerRow:0,space:CGColorSpaceCreateDeviceRGB(),bitmapInfo:CGImageAlphaInfo.premultipliedLast.rawValue) else { throw NashiError.failed("PDF page is too large to render at this DPI.") }
                ctx.setFillColor(CGColor(gray:1,alpha:1));ctx.fill(CGRect(x:0,y:0,width:w,height:h));ctx.scaleBy(x:scale,y:scale)
                ctx.concatenate(ref.getDrawingTransform(.cropBox,rect:bounds,rotate:Int32(page.rotation)-ref.rotationAngle,preserveAspectRatio:true))
                ctx.drawPDFPage(ref)
                for annotation in page.annotations { annotation.draw(with:.mediaBox,in:ctx) }
                try token.check()
                guard let image=ctx.makeImage() else { throw NashiError.failed("A PDF page could not be rendered.") }
                let output=folder.appendingPathComponent(String(format:"Page %03d.%@",index+1,format));try ImageEngine.write(image,to:output,quality:options.quality);outputs.append(output)
                progress(Double(index+1)/Double(pdf.pageCount),"Rendered page \(index+1) of \(pdf.pageCount)")
            }
        } catch { try? FileManager.default.removeItem(at:folder);throw error }
        return outputs
    }
    public static func pageOrder(_ ranges:String, count:Int) throws -> [Int] {
        if ranges.trimmingCharacters(in:.whitespacesAndNewlines).isEmpty { return Array(0..<count) }
        var result:[Int]=[]
        for part in ranges.split(separator:",") {
            let pieces=part.trimmingCharacters(in:.whitespaces).split(separator:"-")
            if pieces.count==1,let n=Int(pieces[0]),n>0,n<=count { result.append(n-1) }
            else if pieces.count==2,let a=Int(pieces[0]),let b=Int(pieces[1]),a>0,b>0,a<=count,b<=count {
                result += stride(from:a,through:b,by:a<=b ? 1 : -1).map { $0-1 }
            } else { throw NashiError.invalidInput("Invalid page range: \(part). Use numbers between 1 and \(count).") }
        }
        guard !result.isEmpty else { throw NashiError.invalidInput("Select at least one page.") };return result
    }
    public static func tool(_ kind:ToolKind, inputs:[URL], options:OperationOptions, token:CancellationToken, progress: ProgressHandler = {_,_ in}) throws -> [URL] {
        guard let first=inputs.first else { throw NashiError.invalidInput("Choose PDF files.") }
        if kind == .mergePDF {
            let selections=try inputs.map { input -> [Int] in try token.check();return Array(0..<(try open(input,password:options.password)).pageCount) }
            let bytes=try pageEditedData(inputs,selections:selections,password:options.password,token:token)
            let output=OutputFiles.unique(for:first,extension:"pdf",suffix:" - Merged",directory:options.outputDirectory)
            try bytes.write(to:output,options:.atomic);return [output]
        }
        var outputs:[URL]=[]
        for input in inputs {
            let pdf=try open(input,password:options.password)
            switch kind {
            case .splitPDF:
                let order=try pageOrder(options.pageRanges,count:pdf.pageCount),chunk=max(1,options.pagesPerFile)
                let folder=OutputFiles.unique(for:input,extension:"",suffix:" - Pages",directory:options.outputDirectory)
                try FileManager.default.createDirectory(at:folder,withIntermediateDirectories:true)
                var saved=false;defer { if !saved { try? FileManager.default.removeItem(at:folder) } }
                for start in stride(from:0,to:order.count,by:chunk) {
                    try token.check()
                    let selection=Array(order[start..<min(start+chunk,order.count)])
                    let bytes=try pageEditedData([input],selections:[selection],password:options.password,token:token)
                    let output=folder.appendingPathComponent(String(format:"Pages %03d-%03d.pdf",start+1,min(start+chunk,order.count)))
                    try bytes.write(to:output,options:.atomic);outputs.append(output)
                    progress(Double(min(start+chunk,order.count))/Double(order.count),"Saved \(min(start+chunk,order.count)) of \(order.count) pages")
                }
                saved=true
            case .organizePDF:
                let order=options.pdfPageOrder.isEmpty ? try pageOrder(options.pageRanges,count:pdf.pageCount):options.pdfPageOrder
                var rotations:[Int:Int]=[:]
                for (index,n) in order.enumerated() { try token.check();guard n>=0,n<pdf.pageCount,let page=pdf.page(at:n) else { throw NashiError.invalidInput("A page index is outside the PDF.") };rotations[index]=options.pdfPageRotations[index] ?? ((page.rotation%360)+(options.rotation%360))%360 }
                let bytes=try pageEditedData([input],selections:[order],rotations:rotations,password:options.password,token:token)
                let output=OutputFiles.unique(for:input,extension:"pdf",suffix:" - Organized",directory:options.outputDirectory)
                if options.pdfUniformWidth {
                    guard !requiresPreservingWrite(pdf) else { throw NashiError.unsupported("Uniform page width cannot safely preserve this PDF's form fields, attachments, links or other document features. Turn off Uniform width and try again. No copy was exported.") }
                    guard let organized=PDFDocument(data:bytes),try uniformWidth(organized,token:token).write(to:output) else { throw NashiError.failed("Could not organize PDF.") }
                } else { try bytes.write(to:output,options:.atomic) }
                outputs.append(output)
            case .removeMetadata:
                // Mutating documentAttributes alone leaves the catalog's old XMP
                // packet in the saved PDF. A fresh document copies page content
                // without that catalog, then restores navigation and annotations.
                let cleaned=try withoutSourceMetadata(pdf,clearAnnotationMetadata:options.metadataRemoveAll || options.metadata.isEmpty,token:token)
                cleaned.documentAttributes=options.metadataRemoveAll ? [:]:Dictionary(uniqueKeysWithValues:options.metadata.map{(AnyHashable($0.key),$0.value as Any)})
                let output=OutputFiles.unique(for:input,extension:"pdf",suffix:" - Clean",directory:options.outputDirectory)
                guard cleaned.write(to:output) else { throw NashiError.failed("Could not save PDF.") };outputs.append(output)
            case .compress:
                let output=OutputFiles.unique(for:input,extension:"pdf",suffix:" - Compressed",directory:options.outputDirectory)
                let original=Int64((try input.resourceValues(forKeys:[.fileSizeKey])).fileSize ?? 0)
                if let bytes=options.targetBytes,bytes<=0 { throw NashiError.invalidInput("Target file size must be positive.") }
                if let percent=options.percentage,(!percent.isFinite || percent<=0 || percent>=100) { throw NashiError.invalidInput("Percentage reduction must be between 0 and 100.") }
                let target=options.targetBytes ?? options.percentage.map{Int64(Double(original)*(1-$0/100))}
                if !(pdf.string ?? "").trimmingCharacters(in:.whitespacesAndNewlines).isEmpty || requiresPreservingWrite(pdf) {
                    try token.check()
                    var settings:[PDFDocumentWriteOption:Any]=[:]
                    if #available(macOS 13.4,*) { settings=[.saveImagesAsJPEGOption:true,.optimizeImagesForScreenOption:true] }
                    guard pdf.write(to:output,withOptions:settings) else { throw NashiError.failed("Could not compress PDF.") }
                    let bytes=Int64(try output.resourceValues(forKeys:[.fileSizeKey]).fileSize ?? 0)
                    if let target,bytes>target {
                        try? FileManager.default.removeItem(at:output)
                        throw NashiError.failed("That PDF size cannot be reached while keeping selectable text, links, and annotations. Choose a larger target.")
                    }
                    try OutputFiles.retainSmallerOriginal(input,output:output,target:target)
                    outputs.append(output);continue
                }
                var dpi=144.0,quality=options.quality
                var complete=false,wrote=false
                defer { if wrote && !complete { try? FileManager.default.removeItem(at:output) } }
                for attempt in 0..<14 {
                    try token.check();let compressed=PDFDocument()
                    for n in 0..<pdf.pageCount {
                        try token.check();progress(Double(n)/Double(pdf.pageCount),"Compressing PDF · attempt \(attempt+1) · page \(n+1) of \(pdf.pageCount)")
                        guard let page=pdf.page(at:n) else { continue };let bounds=page.bounds(for:.cropBox)
                        // The thumbnail is drawn upright, so size it and the new page by the displayed
                        // (rotated) size, and give the image that size in points rather than pixels.
                        let shown=abs(page.rotation)%180==90 ? CGSize(width:bounds.height,height:bounds.width):bounds.size
                        let thumb=page.thumbnail(of:NSSize(width:shown.width*dpi/72,height:shown.height*dpi/72),for:.cropBox)
                        guard let cg=thumb.cgImage(forProposedRect:nil,context:nil,hints:nil) else { continue }
                        let bitmap=NSBitmapImageRep(cgImage:cg)
                        guard let data=bitmap.representation(using:.jpeg,properties:[.compressionFactor:quality]),let img=NSImage(data:data) else { continue }
                        img.size=shown
                        guard let result=PDFPage(image:img) else { continue }
                        result.setBounds(CGRect(origin:.zero,size:shown),for:.mediaBox);compressed.insert(result,at:compressed.pageCount)
                    }
                    wrote=true;guard compressed.write(to:output) else { throw NashiError.failed("Could not compress PDF.") }
                    let bytes=(try FileManager.default.attributesOfItem(atPath:output.path)[.size] as? NSNumber)?.int64Value ?? 0
                    if target == nil || bytes<=target! { complete=true;break };dpi=max(36,dpi*0.8);quality=max(0.05,quality*0.75)
                }
                guard complete else { try? FileManager.default.removeItem(at:output);throw NashiError.failed("The requested PDF size could not be reached.") }
                try OutputFiles.retainSmallerOriginal(input,output:output,target:target);outputs.append(output)
            default: throw NashiError.unsupported("Unsupported PDF tool.")
            }
        }
        return outputs
    }

    private static func pageEditedData(_ inputs:[URL],selections:[[Int]],rotations:[Int:Int]=[:],password:String,token:CancellationToken)throws->Data {
        let documents=try inputs.map { try open($0,password:password) }
        guard documents.contains(where:{$0.isEncrypted}) else {
            return try PDFPreservingCodec.transform(inputs,selections:selections,rotations:rotations,token:token)
        }
        // PDFKit can unlock a PDF for ordinary page copying. Its complete write
        // still emits encrypted bytes, which the object-graph parser cannot read.
        // Keep that established path for simple PDFs, and refuse rich encrypted
        // documents instead of exporting orphan form widgets or losing attachments.
        guard !documents.contains(where:requiresPreservingWrite) else {
            throw NashiError.unsupported("This password-protected PDF contains document features that this page operation cannot safely preserve. Use an unencrypted copy for form fields, attachments or links. No copy was exported.")
        }
        let result=PDFDocument()
        for (source,selection) in zip(documents,selections) {
            for index in selection {
                try token.check()
                guard index>=0,index<source.pageCount,let page=source.page(at:index)?.copy() as? PDFPage else { throw NashiError.invalidInput("A selected PDF page is outside the document.") }
                if let rotation=rotations[result.pageCount] {
                    guard rotation%90==0 else { throw NashiError.invalidInput("PDF rotations must be whole quarter turns.") }
                    page.rotation=rotation%360
                }
                result.insert(page,at:result.pageCount)
            }
        }
        guard let bytes=result.dataRepresentation() else { throw NashiError.failed("Could not save the unlocked PDF pages.") }
        return bytes
    }

    private static func requiresPreservingWrite(_ document:PDFDocument)->Bool {
        if document.outlineRoot != nil || (0..<document.pageCount).contains(where:{ !(document.page(at:$0)?.annotations.isEmpty ?? true) }) { return true }
        guard let catalog=document.documentRef?.catalog else { return false }
        for key in ["AcroForm","Names","AF","Collection","OCProperties","StructTreeRoot","MarkInfo","Dests","PageLabels","OpenAction","AA","Perms","Metadata"] {
            var object:CGPDFObjectRef?
            if CGPDFDictionaryGetObject(catalog,key,&object) { return true }
        }
        return (0..<document.pageCount).contains { index in
            guard let dictionary=document.page(at:index)?.pageRef?.dictionary else { return false }
            var object:CGPDFObjectRef?
            return CGPDFDictionaryGetObject(dictionary,"AF",&object)
        }
    }

    private static func withoutSourceMetadata(_ source:PDFDocument,clearAnnotationMetadata:Bool,token:CancellationToken) throws -> PDFDocument {
        // Copying pages does not carry the catalog's AcroForm field registry.
        // Retaining orphan widget appearances would silently break editability.
        let catalog=source.documentRef?.catalog
        func hasKey(_ dictionary:CGPDFDictionaryRef?,_ key:String)->Bool {
            guard let dictionary else { return false }
            var object:CGPDFObjectRef?
            return CGPDFDictionaryGetObject(dictionary,key,&object)
        }
        var names:CGPDFDictionaryRef?
        if let catalog { _=CGPDFDictionaryGetDictionary(catalog,"Names",&names) }
        let pages=(0..<source.pageCount).compactMap{source.page(at:$0)}
        let feature:String?
        if hasKey(catalog,"AcroForm") || pages.contains(where:{$0.annotations.contains(where:{$0.type=="Widget"})}) { feature="form fields" }
        else if hasKey(names,"EmbeddedFiles") || hasKey(catalog,"AF") || hasKey(catalog,"Collection") || pages.contains(where:{hasKey($0.pageRef?.dictionary,"AF")}) { feature="embedded files" }
        else if hasKey(catalog,"OCProperties") { feature="optional content layers" }
        else if hasKey(catalog,"StructTreeRoot") { feature="accessibility tags" }
        else { feature=nil }
        if let feature { throw NashiError.unsupported("This PDF contains \(feature). Metadata cleanup cannot safely preserve those features. No copy was exported.") }
        let result=PDFDocument()
        for index in 0..<source.pageCount {
            try token.check()
            guard let page=source.page(at:index)?.copy() as? PDFPage else { throw NashiError.failed("A PDF page could not be copied.") }
            for annotation in page.annotations {
                if clearAnnotationMetadata {
                    // Widget /T is its field name, not an annotation author.
                    if annotation.type != "Widget" { annotation.userName=nil }
                    annotation.modificationDate=nil
                    annotation.removeValue(forAnnotationKey:PDFAnnotationKey(rawValue:"/CreationDate"))
                }
            }
            result.insert(page,at:result.pageCount)
        }
        func destination(_ original:PDFDestination)->PDFDestination? {
            guard let page=original.page else { return nil }
            let index=source.index(for:page)
            guard index<result.pageCount,let copy=result.page(at:index) else { return nil }
            let mapped=PDFDestination(page:copy,at:original.point);mapped.zoom=original.zoom;return mapped
        }
        for index in 0..<result.pageCount {
            for annotation in result.page(at:index)?.annotations ?? [] {
                if let action=annotation.action as? PDFActionGoTo,let mapped=destination(action.destination) { annotation.action=PDFActionGoTo(destination:mapped) }
            }
        }
        var visited=Set<ObjectIdentifier>()
        func outline(_ original:PDFOutline,depth:Int) throws -> PDFOutline {
            try token.check()
            guard depth<512,visited.insert(ObjectIdentifier(original)).inserted else { throw NashiError.invalidInput("The PDF has an invalid bookmark tree.") }
            let copy=PDFOutline();copy.label=original.label
            if let originalDestination=original.destination,let mapped=destination(originalDestination) { copy.destination=mapped }
            else { copy.action=original.action?.copy() as? PDFAction }
            for index in 0..<original.numberOfChildren {
                if let child=original.child(at:index) { copy.insertChild(try outline(child,depth:depth+1),at:copy.numberOfChildren) }
            }
            copy.isOpen=original.isOpen;return copy
        }
        if let root=source.outlineRoot { result.outlineRoot=try outline(root,depth:0) }
        return result
    }
    public static func uniformWidth(_ document:PDFDocument,token:CancellationToken = .init()) throws -> PDFDocument {
        let pages=(0..<document.pageCount).compactMap{document.page(at:$0)}
        func displayedSize(_ page:PDFPage)->CGSize { let b=page.bounds(for:.cropBox);return abs(page.rotation)%180==90 ? CGSize(width:b.height,height:b.width):b.size }
        guard let narrowest=pages.map({displayedSize($0).width}).min(),narrowest>0 else { throw NashiError.invalidInput("The PDF has no valid page sizes.") }
        let data=NSMutableData();guard let consumer=CGDataConsumer(data:data),let context=CGContext(consumer:consumer,mediaBox:nil,nil) else { throw NashiError.failed("Could not scale PDF pages.") }
        for page in pages {
            try token.check();guard let ref=page.pageRef else { continue };let size=displayedSize(page),scale=narrowest/size.width
            var box=CGRect(x:0,y:0,width:narrowest,height:size.height*scale)
            let boxData=Data(bytes:&box,count:MemoryLayout<CGRect>.size)
            context.beginPDFPage([kCGPDFContextMediaBox:boxData] as CFDictionary)
            let rotation=Int32(page.rotation)-Int32(ref.rotationAngle)
            let transform=ref.getDrawingTransform(.cropBox,rect:box,rotate:rotation,preserveAspectRatio:true)
            context.concatenate(transform);context.drawPDFPage(ref)
            for annotation in page.annotations { annotation.draw(with:.mediaBox,in:context) }
            context.endPDFPage()
        }
        context.closePDF();guard let output=PDFDocument(data:data as Data) else { throw NashiError.failed("Could not build scaled PDF.") };output.documentAttributes=document.documentAttributes;return output
    }
}
