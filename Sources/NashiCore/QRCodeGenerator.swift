import Foundation
import CoreImage
import CoreGraphics

public enum QRCodeGenerator {
    public static func image(text:String,size:Int=512,correction:String="M")throws->CGImage {
        guard !text.trimmingCharacters(in:.whitespacesAndNewlines).isEmpty,text.utf8.count<=2000 else { throw NashiError.invalidInput("Enter between 1 and 2,000 UTF-8 bytes for the QR code.") }
        guard (64...2048).contains(size),["L","M","Q","H"].contains(correction) else { throw NashiError.invalidInput("Choose a QR size between 64 and 2,048 pixels and L, M, Q, or H correction.") }
        guard let filter=CIFilter(name:"CIQRCodeGenerator") else { throw NashiError.failed("Could not create the QR code.") }
        filter.setValue(Data(text.utf8),forKey:"inputMessage");filter.setValue(correction,forKey:"inputCorrectionLevel")
        guard let code=filter.outputImage,let bitmap=CIContext().createCGImage(code,from:code.extent) else { throw NashiError.invalidInput("The text is too long for this QR correction level.") }
        let modules=bitmap.width,scale=size/(modules+8)
        guard scale>=1 else { throw NashiError.invalidInput("Choose a larger QR image size for this text.") }
        guard let context=CGContext(data:nil,width:size,height:size,bitsPerComponent:8,bytesPerRow:0,space:CGColorSpaceCreateDeviceRGB(),bitmapInfo:CGImageAlphaInfo.noneSkipLast.rawValue) else { throw NashiError.failed("Could not create the QR code.") }
        context.setFillColor(CGColor(gray:1,alpha:1));context.fill(CGRect(x:0,y:0,width:size,height:size));context.interpolationQuality = .none
        let side=modules*scale,offset=(size-side)/2
        context.draw(bitmap,in:CGRect(x:offset,y:offset,width:side,height:side))
        guard let output=context.makeImage() else { throw NashiError.failed("Could not create the QR code.") };return output
    }
    public static func export(text:String,size:Int=512,correction:String="M",directory:URL,filename:String="QR Code.png",token:CancellationToken = .init())throws->URL {
        try token.check()
        guard directory.isFileURL,filename==URL(fileURLWithPath:filename).lastPathComponent,!filename.contains("/"),filename.lowercased().hasSuffix(".png") else { throw NashiError.invalidInput("Choose a local folder and a PNG filename.") }
        let image=try image(text:text,size:size,correction:correction)
        try token.check()
        let transaction=ExportTransaction();defer { transaction.cleanUp() }
        let area=try transaction.makeArea(destination:directory),output=area.directory.appendingPathComponent(filename)
        try ImageEngine.write(image,to:output,quality:1,token:token)
        try transaction.record([output],in:area)
        return try transaction.publish(token:token)[0]
    }
}
