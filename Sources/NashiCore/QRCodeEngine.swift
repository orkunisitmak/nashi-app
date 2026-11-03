import Foundation
import AppKit
import PDFKit
import Vision

public struct QRCodeMatch:Codable,Sendable,Identifiable {
    public var id=UUID()
    public let sourceName:String
    public let sourcePath:String
    /// One-based PDF page, absent for an ordinary image.
    public let page:Int?
    public let text:String
}
public struct QRCodeScanReport:Codable,Sendable {
    public var results:[QRCodeMatch]=[]
    public var errors:[String]=[]
    public init() {}
}

public enum QRCodeEngine {
    public static func supports(_ url:URL)->Bool { FileCategory.detect(url) == .image || FileCategory.detect(url) == .pdf || url.pathExtension.lowercased()=="gif" }
    public static func scan(_ inputs:[URL],password:String?=nil,token:CancellationToken = .init(),progress:ProgressHandler={_,_ in}) throws -> QRCodeScanReport {
        var report=QRCodeScanReport()
        for (index,input) in inputs.enumerated() {
            try token.check()
            do {
                guard supports(input) else { throw NashiError.unsupported("Choose an image or PDF.") }
                if FileCategory.detect(input) == .pdf {
                    guard let document=PDFDocument(url:input) else { throw NashiError.invalidInput("Could not read this PDF.") }
                    if document.isLocked { guard let password,document.unlock(withPassword:password) else { throw NashiError.invalidInput("This PDF requires a valid password.") } }
                    for pageIndex in 0..<document.pageCount {
                        try token.check()
                        guard let page=document.page(at:pageIndex) else { continue }
                        // Read only what viewers show: the CropBox.
                        let box=page.bounds(for:.cropBox),scale=min(3,4096/max(box.width,box.height))
                        let thumbnail=page.thumbnail(of:NSSize(width:max(1,box.width*scale),height:max(1,box.height*scale)),for:.cropBox)
                        guard let image=thumbnail.cgImage(forProposedRect:nil,context:nil,hints:nil) else { throw NashiError.failed("Could not render PDF page \(pageIndex+1).") }
                        report.results += try scanImage(image,input:input,page:pageIndex+1,token:token)
                        progress((Double(index)+Double(pageIndex+1)/Double(max(1,document.pageCount)))/Double(max(1,inputs.count)),"\(input.lastPathComponent), page \(pageIndex+1)")
                    }
                } else {
                    report.results += try scanImage(ImageEngine.load(input),input:input,page:nil,token:token)
                }
            } catch {
                try token.check()
                report.errors.append("\(input.lastPathComponent): \(error.localizedDescription)")
            }
            progress(Double(index+1)/Double(max(1,inputs.count)),input.lastPathComponent)
        }
        return report
    }
    public static func export(_ inputs:[URL],options:OperationOptions,token:CancellationToken,progress:ProgressHandler={_,_ in}) throws -> [URL] {
        guard let first=inputs.first else { throw NashiError.invalidInput("Choose a file to scan.") }
        let report=try scan(inputs,password:options.password,token:token,progress:progress)
        let output=OutputFiles.unique(for:first,extension:"json",suffix:" - QR Codes",directory:options.outputDirectory)
        let encoder=JSONEncoder();encoder.outputFormatting=[.prettyPrinted,.sortedKeys]
        try token.check();try encoder.encode(report).write(to:output,options:.atomic)
        return [output]
    }
    private static func scanImage(_ image:CGImage,input:URL,page:Int?,token:CancellationToken) throws -> [QRCodeMatch] {
        try token.check()
        let request=VNDetectBarcodesRequest();request.symbologies=[.qr]
        try VNImageRequestHandler(cgImage:image).perform([request]);try token.check()
        return request.results?.compactMap { observation in
            observation.payloadStringValue.map{QRCodeMatch(sourceName:input.lastPathComponent,sourcePath:input.path,page:page,text:$0)}
        } ?? []
    }
}
