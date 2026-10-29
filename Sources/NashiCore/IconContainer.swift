import Foundation
import CoreGraphics
import ImageIO

// A Windows icon holds one PNG per size. The image is fitted into each square without
// cropping; transparent padding fills the rest. Smaller sources are scaled up so every
// size exists, because Explorer otherwise shows a small entry inside a blank large tile.
// Windows writes the same layout (windows/Nashi.Core/IconCodec.cs).
public enum IconContainer {
    public static let sizes=[16,24,32,48,64,128,256]

    public static func write(_ image:CGImage,to url:URL,token:CancellationToken = .init()) throws {
        // Shrink very large photos once, so each size is resampled from at most 256 px.
        let largest=sizes[sizes.count-1]
        let source=max(image.width,image.height)>largest ? try fitted(image,size:largest,padded:false) : image
        var pngs:[Data]=[]
        for size in sizes {
            try token.check()
            pngs.append(try png(fitted(source,size:size,padded:true)))
        }
        try token.check()
        // Never replace an existing file; callers pass a unique output name.
        try pack(pngs).write(to:url,options:.withoutOverwriting)
    }

    // ICONDIR (6 bytes), one 16-byte ICONDIRENTRY per image, then the PNG data. 256 px is stored as 0.
    public static func pack(_ pngs:[Data]) throws -> Data {
        guard !pngs.isEmpty,pngs.count<=Int(UInt16.max) else { throw NashiError.invalidInput("Choose at least one icon size.") }
        var data=Data()
        func u16(_ value:Int) { data.append(UInt8(value & 0xff));data.append(UInt8((value>>8) & 0xff)) }
        func u32(_ value:Int) { for shift in stride(from:0,to:32,by:8) { data.append(UInt8((value>>shift) & 0xff)) } }
        u16(0);u16(1);u16(pngs.count)
        var offset=6+16*pngs.count
        for png in pngs {
            let (width,height)=try pngSize(png)
            guard (1...256).contains(width),(1...256).contains(height) else { throw NashiError.invalidInput("Icon images must be 1 to 256 pixels.") }
            data.append(UInt8(width%256));data.append(UInt8(height%256));data.append(0);data.append(0)
            u16(1);u16(32);u32(png.count);u32(offset);offset+=png.count
        }
        for png in pngs { data.append(png) }
        return data
    }

    // Matches ImageMagick's centered extent on Windows: odd padding puts the extra pixel at the bottom/right.
    private static func fitted(_ image:CGImage,size:Int,padded:Bool) throws -> CGImage {
        let scale=Double(size)/Double(max(image.width,image.height))
        let width=max(1,Int((Double(image.width)*scale).rounded())),height=max(1,Int((Double(image.height)*scale).rounded()))
        let canvasWidth=padded ? size:width,canvasHeight=padded ? size:height
        guard let space=CGColorSpace(name:CGColorSpace.sRGB),
              let context=CGContext(data:nil,width:canvasWidth,height:canvasHeight,bitsPerComponent:8,bytesPerRow:0,space:space,bitmapInfo:CGImageAlphaInfo.premultipliedLast.rawValue) else { throw NashiError.failed("Could not allocate the icon canvas.") }
        context.clear(CGRect(x:0,y:0,width:canvasWidth,height:canvasHeight))
        context.interpolationQuality = .high
        let left=(canvasWidth-width)/2,top=(canvasHeight-height)/2
        context.draw(image,in:CGRect(x:left,y:canvasHeight-height-top,width:width,height:height))
        guard let result=context.makeImage() else { throw NashiError.failed("Could not draw the icon.") }
        return result
    }

    private static func png(_ image:CGImage) throws -> Data {
        let data=NSMutableData()
        guard let destination=CGImageDestinationCreateWithData(data,"public.png" as CFString,1,nil) else { throw NashiError.failed("Could not encode the icon.") }
        CGImageDestinationAddImage(destination,image,nil)
        guard CGImageDestinationFinalize(destination) else { throw NashiError.failed("Could not encode the icon.") }
        return data as Data
    }

    private static func pngSize(_ png:Data) throws -> (Int,Int) {
        let bytes=[UInt8](png.prefix(24))
        guard bytes.count==24,bytes[0]==0x89,bytes[1]==0x50,bytes[12]==0x49,bytes[15]==0x52 else { throw NashiError.invalidInput("Icon images must be PNG data.") }
        func big(_ index:Int)->Int { Int(bytes[index])<<24|Int(bytes[index+1])<<16|Int(bytes[index+2])<<8|Int(bytes[index+3]) }
        return (big(16),big(20))
    }
}
