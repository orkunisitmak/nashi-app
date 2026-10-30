import Foundation
import PDFKit

public struct PDFPageBox: Codable, Equatable, Sendable {
    public var x:Double, y:Double, width:Double, height:Double
    public init(_ rect:CGRect) { x=rect.minX;y=rect.minY;width=rect.width;height=rect.height }
    public var rect:CGRect { CGRect(x:x,y:y,width:width,height:height) }
    var isValid:Bool { [x,y,width,height].allSatisfy(\.isFinite) && width>0 && height>0 && width<=14_400 && height<=14_400 }
}
public struct PDFWorkspacePage: Codable, Sendable {
    public var sourceIndex:Int?
    public var pageIndex:Int
    public var rotation:Int
    public var cropBox:PDFPageBox?
    public var blankSize:PDFPageBox?
    public init(sourceIndex:Int?,pageIndex:Int,rotation:Int=0,cropBox:PDFPageBox?=nil,blankSize:PDFPageBox?=nil) {
        self.sourceIndex=sourceIndex;self.pageIndex=pageIndex;self.rotation=rotation;self.cropBox=cropBox;self.blankSize=blankSize
    }
}

extension PDFEngine {
    /// Applies a complete page plan and publishes one copy, preserving source files and rich-PDF checks.
    /// Simple encrypted sources are copied through PDFKit before the shared writer applies every edit.
    static func workspace(inputs:[URL],options:OperationOptions,token:CancellationToken)throws->[URL] {
        let plan=options.pdfWorkspacePages
        guard let first=inputs.first,!plan.isEmpty,plan.count<=10_000 else{throw NashiError.invalidInput("Choose between 1 and 10,000 output pages.")}
        let work=try OutputFiles.temporaryDirectory();defer{try? FileManager.default.removeItem(at:work)}
        // Decrypted intermediate copies must be private and removed on every exit path.
        try FileManager.default.setAttributes([.posixPermissions:0o700],ofItemAtPath:work.path)
        var sources:[URL]=[],selections:[[Int]]=[],groups:[Int:Int]=[:],order:[(Int,Int)]=[]
        let blank=PDFDocument();var blankGroup:Int?
        for item in plan {
            try token.check()
            guard item.rotation%90==0,item.cropBox?.isValid != false else{throw NashiError.invalidInput("Choose valid page rotations and crop bounds.")}
            let group:Int,index:Int
            if let source=item.sourceIndex {
                guard inputs.indices.contains(source),item.pageIndex>=0 else{throw NashiError.invalidInput("A selected PDF source is outside the workspace.")}
                if let existing=groups[source] { group=existing }
                else {
                    group=sources.count;groups[source]=group
                    sources.append(try workspaceSource(inputs[source],password:options.password,work:work,token:token))
                    selections.append([])
                }
                index=item.pageIndex
            } else {
                guard let size=item.blankSize,size.isValid else{throw NashiError.invalidInput("Choose a valid blank page size.")}
                if let existing=blankGroup { group=existing }
                else { group=sources.count;blankGroup=group;sources.append(work.appendingPathComponent("Blank pages.pdf"));selections.append([]) }
                index=blank.pageCount;let page=PDFPage();page.setBounds(size.rect,for:.mediaBox);blank.insert(page,at:index)
            }
            order.append((group,selections[group].count));selections[group].append(index)
        }
        if let group=blankGroup,!blank.write(to:sources[group]) { throw NashiError.failed("Could not create blank PDF pages.") }
        var offsets:[Int]=[],offset=0
        for selection in selections { offsets.append(offset);offset+=selection.count }
        let outputOrder=order.map{offsets[$0.0]+$0.1}
        let rotations=Dictionary(uniqueKeysWithValues:plan.enumerated().map{($0.offset,$0.element.rotation)})
        let crops=Dictionary(uniqueKeysWithValues:plan.enumerated().compactMap{ index,page in page.cropBox.map{(index,$0)} })
        if options.pdfWidthMode != .original {
            let documents=try sources.map{try open($0,password:options.password)}
            guard !documents.contains(where:requiresPreservingWrite) else{throw NashiError.unsupported("Uniform page width cannot safely preserve this PDF's form fields, attachments, links or other document features. Turn off Uniform width and try again. No copy was exported.")}
        }
        let bytes=try PDFPreservingCodec.transform(sources,selections:selections,rotations:rotations,outputOrder:outputOrder,crops:crops,widthMode:options.pdfWidthMode,customWidth:options.pdfCustomWidth,token:token)
        let output=OutputFiles.unique(for:first,extension:"pdf",suffix:options.pdfExtractSelection ? " - Selected Pages":" - Organized",directory:options.outputDirectory)
        try bytes.write(to:output,options:.atomic)
        return [output]
    }

    /// Returns an ordinary PDF unchanged or creates a private, unencrypted copy of a simple encrypted source.
    /// PDFKit must unlock with the supplied password; rich encrypted documents are refused before page copying.
    /// The caller owns `work` and removes it after success, cancellation, or failure.
    private static func workspaceSource(_ input:URL,password:String,work:URL,token:CancellationToken)throws->URL {
        try token.check()
        let attributes=try input.resourceValues(forKeys:[.isRegularFileKey,.fileSizeKey])
        guard attributes.isRegularFile == true,let size=attributes.fileSize,size>0,size<=100_000_000 else {
            throw NashiError.invalidInput("This PDF is empty, is not a regular file, or exceeds the 100 MB page-editing limit.")
        }
        let source=try open(input,password:password)
        guard source.isEncrypted else{return input}
        guard !requiresPreservingWrite(source) else {
            throw NashiError.unsupported("This password-protected PDF contains document features that this page operation cannot safely preserve. Use an unencrypted copy for form fields, attachments or links. No copy was exported.")
        }
        // Writing the unlocked source itself retains its encryption. Copy pages into a fresh document instead.
        let unlocked=PDFDocument()
        for index in 0..<source.pageCount {
            try token.check()
            guard let page=source.page(at:index)?.copy() as? PDFPage else{throw NashiError.failed("A PDF page could not be copied.")}
            unlocked.insert(page,at:unlocked.pageCount)
        }
        unlocked.documentAttributes=source.documentAttributes
        let output=work.appendingPathComponent("Unlocked-"+UUID().uuidString+".pdf")
        try token.check()
        guard unlocked.write(to:output),let reopened=PDFDocument(url:output),!reopened.isEncrypted,
              !reopened.isLocked,reopened.pageCount==source.pageCount else {
            throw NashiError.failed("Could not save the unlocked PDF pages.")
        }
        try token.check()
        return output
    }

}
