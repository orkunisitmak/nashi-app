import Foundation
import AppKit
import ImageIO
import UniformTypeIdentifiers
import CoreImage
import CoreText
import Vision

public enum ImageEngine {
    private static let imageColorSpace = CGColorSpace(name: CGColorSpace.sRGB)!
    private static let imageContext = CIContext(options: [.cacheIntermediates: false])
    public static func load(_ url: URL) throws -> CGImage {
        if let source = CGImageSourceCreateWithURL(url as CFURL, nil),
           let image = CGImageSourceCreateThumbnailAtIndex(source, 0, [kCGImageSourceCreateThumbnailFromImageAlways:true, kCGImageSourceCreateThumbnailWithTransform:true, kCGImageSourceThumbnailMaxPixelSize:32000] as CFDictionary) { return image }
        if let image = NSImage(contentsOf: url), let cg = image.cgImage(forProposedRect:nil,context:nil,hints:nil) { return cg }
        // FFmpeg handles additional decoders (including animated inputs' first frame).
        let temp = try OutputFiles.temporaryDirectory(); defer { try? FileManager.default.removeItem(at:temp) }
        let png = temp.appendingPathComponent("decoded.png")
        try ProcessRunner.run(Dependencies.executable("ffmpeg"), ["-v","error","-i",url.path,"-frames:v","1",png.path])
        guard let source=CGImageSourceCreateWithURL(png as CFURL,nil),let cg=CGImageSourceCreateImageAtIndex(source,0,nil) else { throw NashiError.invalidInput("This image could not be decoded.") }
        return cg
    }

    public static func convert(_ input: URL, to format: String, options: OperationOptions, token: CancellationToken) throws -> [URL] {
        try token.check()
        if format == "pdf" { return try PDFEngine.create(inputs:[input],options:options,token:token) }
        let image = try load(input)
        let output=OutputFiles.unique(for:input,extension:format,directory:options.outputDirectory)
        var complete=false;defer { if !complete { try? FileManager.default.removeItem(at:output) } }
        let metadata = options.preserveMetadata ? CGImageSourceCreateWithURL(input as CFURL,nil).map { imageProperties($0,input:input) as CFDictionary } : nil
        try write(image,to:output,quality:options.quality,metadata:metadata,token:token)
        if options.preserveMetadata { try ImageMetadataEngine.copyMetadata(from:input,to:output,token:token) }
        complete=true
        return [output]
    }

    public static func write(_ image: CGImage, to url: URL, quality: Double = 0.8, metadata: CFDictionary? = nil, token: CancellationToken = .init()) throws {
        let ext=url.pathExtension.lowercased()
        let type: String
        switch ext {
        case "jpg","jpeg": type="public.jpeg"
        case "png": type="public.png"
        case "heic","heif": type="public.heic"
        case "tiff","tif": type="public.tiff"
        case "bmp": type="com.microsoft.bmp"
        case "gif": type="com.compuserve.gif"
        case "webp": type="org.webmproject.webp"
        case "avif": type="public.avif"
        default: throw NashiError.unsupported("Unsupported image output: \(ext)")
        }
        if (CGImageDestinationCopyTypeIdentifiers() as! [String]).contains(type),let destination=CGImageDestinationCreateWithURL(url as CFURL,type as CFString,1,nil) {
            var properties = (metadata as? [CFString:Any]) ?? [:]
            properties[kCGImageDestinationLossyCompressionQuality] = max(0.01,min(1,quality))
            properties[kCGImagePropertyOrientation] = 1
            CGImageDestinationAddImage(destination,image,properties as CFDictionary)
            guard CGImageDestinationFinalize(destination) else { throw NashiError.failed("Could not write \(url.lastPathComponent)") }
        } else {
            let temp=try OutputFiles.temporaryDirectory(); defer { try? FileManager.default.removeItem(at:temp) }
            let png=temp.appendingPathComponent("image.png")
            try write(image,to:png,quality:1)
            var args=["-v","error","-i",png.path,"-frames:v","1"]
            if ext == "webp" { args += ["-c:v","libwebp","-quality",String(Int(quality*100))] }
            if ext == "avif" { args += ["-c:v","libaom-av1","-still-picture","1","-cpu-used","6","-crf",String(Int((1-quality)*50)+8)] }
            args += [url.path]
            try ProcessRunner.run(Dependencies.executable("ffmpeg"),args,token:token)
        }
    }

    public static func tool(_ kind: ToolKind, inputs: [URL], options: OperationOptions, token: CancellationToken) throws -> [URL] {
        if kind == .createCollage { return try collage(inputs,options:options,token:token) }
        var outputs:[URL]=[]
        for input in inputs {
            do {
                try token.check()
                if kind == .removeMetadata {
                    if let changes = options.imageMetadata {
                        outputs.append(try editMetadata(input, changes: changes, options: options, token: token)); continue
                    }
                    if !options.metadata.isEmpty {
                        var changes = ImageMetadataOptions(); changes.updates = options.metadata
                        outputs.append(try editMetadata(input, changes: changes, options: options, token: token)); continue
                    }
                    var changes=ImageMetadataOptions();changes.removeAll=true
                    outputs.append(try editMetadata(input,changes:changes,options:options,token:token));continue
                }
                var image=try load(input)
                let transparentFrame = kind == .frameImage && color(options.imageFrame?.backgroundColor ?? options.backgroundColor).alpha < 1
                let ext = transparentFrame || ["svg","gif"].contains(input.pathExtension.lowercased()) ? "png" : input.pathExtension
                let suffix: String
                switch kind {
                case .compress: outputs += try compress(input,options:options,token:token); continue
                case .cropImage:
                    image=try preview(image,tool:.cropImage,options:options)
                    suffix=" - Cropped"
                case .editImage:
                    image=try edit(image,options:options); suffix=" - Edited"
                case .annotateImage: image=try annotate(image,options:options); suffix=" - Annotated"
                case .redactImage: image=try redact(image,options:options); suffix=" - Redacted"
                case .frameImage: image=try frame(image,options:options); suffix=" - Framed"
                default: throw NashiError.unsupported("\(kind.title) is not an image operation.")
                }
                let output=OutputFiles.unique(for:input,extension:kind == .annotateImage ? "png" : ext,suffix:suffix,directory:options.outputDirectory)
                try write(image,to:output,quality:options.quality)
                outputs.append(output)
            } catch NashiError.invalidInput(let message) where kind == .cropImage || kind == .redactImage {
                throw NashiError.invalidInput("\(input.lastPathComponent): \(message)")
            } catch NashiError.cancelled {
                throw NashiError.cancelled
            } catch let error where kind == .cropImage || kind == .redactImage {
                throw NashiError.failed("\(input.lastPathComponent): \(error.localizedDescription)")
            }
        }
        return outputs
    }

    public static func resized(_ image: CGImage, width: Int, height: Int) throws -> CGImage {
        guard width>0,height>0,width<=32000,height<=32000,let ctx=CGContext(data:nil,width:width,height:height,bitsPerComponent:8,bytesPerRow:0,space:imageColorSpace,bitmapInfo:CGImageAlphaInfo.premultipliedLast.rawValue) else { throw NashiError.invalidInput("Image dimensions must be between 1 and 32,000 pixels.") }
        ctx.interpolationQuality = .high
        ctx.draw(image,in:CGRect(x:0,y:0,width:width,height:height))
        guard let result=ctx.makeImage() else { throw NashiError.failed("Could not resize image.") }; return result
    }

    /// Renders editor changes without producing a file, for live window previews.
    public static func preview(_ image:CGImage,tool:ToolKind = .editImage,options:OperationOptions) throws -> CGImage {
        switch tool {
        case .editImage: return try edit(image,options:options)
        case .annotateImage: return try annotate(image,options:options)
        case .redactImage: return try redact(image,options:options)
        case .frameImage: return try frame(image,options:options)
        case .cropImage:
            guard let crop=options.crop else { throw NashiError.invalidInput("Choose a crop rectangle.") }
            guard [crop.x,crop.y,crop.width,crop.height].allSatisfy(\.isFinite) else { throw NashiError.invalidInput("Crop coordinates must be finite.") }
            guard crop.x>=0,crop.y>=0,crop.width>=1,crop.height>=1 else { throw NashiError.invalidInput("Crop origins must be nonnegative and dimensions at least 1 × 1 pixel.") }
            let width=Double(image.width),height=Double(image.height)
            // Validate the requested rectangle before any resize. Subtraction avoids
            // overflowing the far edge for very large, otherwise finite coordinates.
            guard crop.x<=width,crop.y<=height,crop.width<=width-crop.x,crop.height<=height-crop.y else { throw NashiError.invalidInput("The crop rectangle must fit completely inside the \(image.width) × \(image.height) pixel image.") }
            // Match the numeric fields' whole-pixel values. Core Graphics otherwise
            // expands fractional rectangles to integral bounds, adding rows/columns.
            let r=CGRect(x:crop.x.rounded(.down),y:crop.y.rounded(.down),width:crop.width.rounded(.down),height:crop.height.rounded(.down))
            guard let result=image.cropping(to:r) else { throw NashiError.invalidInput("The crop rectangle could not be read from the image.") }
            return try resizeUsingOptions(result,options:options)
        default: throw NashiError.unsupported("\(tool.title) does not have a single-image preview.")
        }
    }

    public static func editedImage(_ image: CGImage, options: OperationOptions) throws -> CGImage {
        try edit(image, options: options)
    }

    private static func resizeUsingOptions(_ image: CGImage, options: OperationOptions) throws -> CGImage {
        guard options.width != nil || options.height != nil else { return image }
        for value in [options.width,options.height].compactMap({$0}) { guard value>0,value<=32000 else { throw NashiError.invalidInput("Image dimensions must be between 1 and 32,000 pixels.") } }
        let ratio = Double(image.height) / Double(image.width)
        // Round the derived side and keep at least one pixel for very thin images.
        let width = options.width ?? max(1, Int((Double(options.height!) / ratio).rounded()))
        let height = options.height ?? max(1, Int((Double(width) * ratio).rounded()))
        return try resized(image, width: width, height: height)
    }

    private static func edit(_ image: CGImage, options: OperationOptions) throws -> CGImage {
        let a = options.imageAdjustments
        let values = [a.exposure,a.brightness,a.contrast,a.highlights,a.shadows,a.whites,a.blacks,a.gamma,a.temperature,a.tint,a.saturation,a.vibrance,a.hue,a.clarity,a.texture,a.fade,a.blur,a.sharpen,a.dehaze,a.grain,a.noiseReduction,a.vignette,options.brightness,options.contrast,options.saturation]
        guard values.allSatisfy(\.isFinite), a.gamma > 0 else { throw NashiError.invalidInput("Image adjustments must be finite and gamma must be positive.") }
        var ci=CIImage(cgImage:image)
        let bounds = ci.extent
        if a.noiseReduction > 0 { ci = ci.applyingFilter("CINoiseReduction", parameters: ["inputNoiseLevel": min(1,a.noiseReduction) * 0.1, "inputSharpness": 0]) }
        if a.exposure != 0 { ci = ci.applyingFilter("CIExposureAdjust", parameters: [kCIInputEVKey: min(10,max(-10,a.exposure))]) }
        if a.temperature != 0 || a.tint != 0 {
            ci = ci.applyingFilter("CITemperatureAndTint", parameters: ["inputNeutral": CIVector(x:6500,y:0), "inputTargetNeutral": CIVector(x:6500 + max(-1,min(1,a.temperature)) * 3000, y:max(-1,min(1,a.tint)) * 100)])
        }
        ci=ci.applyingFilter("CIColorControls",parameters:[kCIInputBrightnessKey:options.brightness+a.brightness,kCIInputContrastKey:max(0,options.contrast*a.contrast),kCIInputSaturationKey:max(0,options.saturation*a.saturation)])
        if a.highlights != 0 || a.shadows != 0 || a.whites != 0 || a.blacks != 0 {
            // Five monotonic luminance anchors let the four tonal ranges vary independently.
            let xs: [Double] = [0,0.25,0.5,0.75,1]
            var ys = xs.map { x in
                max(0,min(1,x + a.blacks * 0.25 * pow(1-x,4) + a.whites * 0.25 * pow(x,4)
                    + a.shadows * 0.5 * 4*x*pow(1-x,3) + a.highlights * 0.5 * 4*pow(x,3)*(1-x)))
            }
            for index in 1..<ys.count { ys[index] = max(ys[index-1], ys[index]) }
            var parameters: [String:Any] = [:]
            for index in xs.indices { parameters["inputPoint\(index)"] = CIVector(x:xs[index],y:ys[index]) }
            ci = ci.applyingFilter("CIToneCurve",parameters:parameters)
        }
        if a.gamma != 1 { ci = ci.applyingFilter("CIGammaAdjust",parameters:["inputPower":max(0.1,min(5,a.gamma))]) }
        if a.vibrance != 0 { ci = ci.applyingFilter("CIVibrance",parameters:["inputAmount":max(-1,min(1,a.vibrance))]) }
        if a.hue != 0 { ci = ci.applyingFilter("CIHueAdjust",parameters:["inputAngle":a.hue * .pi / 180]) }
        if a.dehaze != 0 { ci = ci.applyingFilter("CIColorControls",parameters:[kCIInputContrastKey:1 + max(-1,min(1,a.dehaze))*0.65,kCIInputBrightnessKey:-a.dehaze*0.045,kCIInputSaturationKey:1+a.dehaze*0.12]) }
        if a.clarity > 0 { ci = ci.applyingFilter("CIUnsharpMask",parameters:[kCIInputRadiusKey:4,kCIInputIntensityKey:min(1,a.clarity)*1.5]) }
        if a.clarity < 0 { ci = ci.clampedToExtent().applyingFilter("CIGaussianBlur",parameters:[kCIInputRadiusKey:min(1,-a.clarity)*3]).cropped(to:bounds) }
        if a.sharpen > 0 { ci = ci.applyingFilter("CISharpenLuminance",parameters:[kCIInputSharpnessKey:min(2,a.sharpen)]) }
        if a.texture > 0 { ci=ci.applyingFilter("CIUnsharpMask",parameters:[kCIInputRadiusKey:1.5,kCIInputIntensityKey:min(1,a.texture)*2]) }
        if a.texture < 0 { ci=ci.applyingFilter("CINoiseReduction",parameters:["inputNoiseLevel":min(1,-a.texture)*0.15,"inputSharpness":0]) }
        if a.fade > 0 {
            let amount=min(1,a.fade),scale=1-amount*0.4,bias=amount*0.15
            ci=ci.applyingFilter("CIColorMatrix",parameters:["inputRVector":CIVector(x:scale,y:0,z:0,w:0),"inputGVector":CIVector(x:0,y:scale,z:0,w:0),"inputBVector":CIVector(x:0,y:0,z:scale,w:0),"inputBiasVector":CIVector(x:bias,y:bias,z:bias,w:0)])
        }
        if a.blur > 0 { ci=ci.clampedToExtent().applyingFilter("CIGaussianBlur",parameters:[kCIInputRadiusKey:min(100,a.blur)]).cropped(to:bounds) }
        if a.grain > 0, let random = CIFilter(name:"CIRandomGenerator")?.outputImage {
            let monochrome = CIVector(x:0.3333,y:0.3333,z:0.3334,w:0)
            let grain = random.cropped(to:bounds).applyingFilter("CIColorMatrix",parameters:["inputRVector":monochrome,"inputGVector":monochrome,"inputBVector":monochrome,"inputAVector":CIVector(x:0,y:0,z:0,w:0),"inputBiasVector":CIVector(x:0,y:0,z:0,w:min(1,a.grain)*0.25)])
            ci = grain.composited(over:ci)
        }
        if a.vignette != 0 { ci = ci.applyingFilter("CIVignette",parameters:[kCIInputIntensityKey:max(-2,min(2,a.vignette)),kCIInputRadiusKey:Double(min(image.width,image.height))*0.5]) }
        ci = ci.cropped(to:bounds)
        if options.flipHorizontal { ci=ci.transformed(by:CGAffineTransform(scaleX:-1,y:1)) }
        let rotation=((options.rotation%360)+360)%360
        switch rotation {
        case 0: break
        // Positive rotation follows the editor's Rotate Right action (clockwise).
        case 90: ci=ci.oriented(.right)
        case 180: ci=ci.oriented(.down)
        case 270: ci=ci.oriented(.left)
        default: ci=ci.transformed(by:CGAffineTransform(rotationAngle:-CGFloat(rotation) * .pi / 180))
        }
        guard let output=imageContext.createCGImage(ci,from:ci.extent.integral) else { throw NashiError.failed("Could not edit image.") }
        return try resizeUsingOptions(output,options:options)
    }

    private static func canvas(_ width:Int,_ height:Int) throws -> CGContext {
        guard width>0,height>0,width<=32000,height<=32000,let ctx=CGContext(data:nil,width:width,height:height,bitsPerComponent:8,bytesPerRow:0,space:imageColorSpace,bitmapInfo:CGImageAlphaInfo.premultipliedLast.rawValue) else { throw NashiError.failed("Could not allocate image canvas.") }; return ctx
    }
    /// Reads #RGB, #RGBA, #RRGGBB or #RRGGBBAA (the "#" is optional); nil for anything else.
    public static func parsedColor(_ hex: String) -> CGColor? {
        var s=hex.trimmingCharacters(in:.whitespaces)
        if s.hasPrefix("#") { s.removeFirst() }
        // ASCII only: UInt64(_:radix:) also accepts a sign, and Character.isHexDigit full-width digits.
        guard [3,4,6,8].contains(s.utf8.count),s.utf8.allSatisfy({(48...57).contains($0) || (65...70).contains($0) || (97...102).contains($0)}) else { return nil }
        if s.count <= 4 { s = s.map { "\($0)\($0)" }.joined() }
        guard var n=UInt64(s,radix:16) else { return nil }
        if s.count == 6 { n = n<<8 | 0xFF }
        return CGColor(colorSpace:imageColorSpace,components:[CGFloat((n>>24)&255)/255,CGFloat((n>>16)&255)/255,CGFloat((n>>8)&255)/255,CGFloat(n&255)/255])
    }
    /// Lenient for pickers and swatches: an unreadable value shows as white.
    public static func color(_ hex: String) -> CGColor {
        parsedColor(hex) ?? CGColor(colorSpace:imageColorSpace,components:[1,1,1,1])!
    }
    /// Rendering refuses an unreadable color instead of silently painting another one.
    static func validatedColor(_ hex: String) throws -> CGColor {
        guard let color=parsedColor(hex) else { throw NashiError.invalidInput("“\(hex)” is not a color. Use #RGB, #RGBA, #RRGGBB or #RRGGBBAA.") }
        return color
    }

    public static func annotatedImage(_ image: CGImage, options: OperationOptions) throws -> CGImage { try annotate(image,options:options) }

    private static func annotate(_ image: CGImage, options: OperationOptions) throws -> CGImage {
        let ctx=try canvas(image.width,image.height); ctx.draw(image,in:CGRect(x:0,y:0,width:image.width,height:image.height))
        var annotations = options.annotations.map { legacy -> ImageAnnotation in
            var item = ImageAnnotation(kind:ImageAnnotationKind(rawValue:legacy.kind) ?? .text,rect:legacy.rect)
            item.text=legacy.text;item.color=legacy.color;item.fontSize=max(12,legacy.rect.height*0.7);item.strokeWidth=max(2,legacy.rect.height/15)
            if item.kind == .number { item.number=Int(legacy.text) ?? 1 }
            return item
        }
        annotations += options.imageAnnotations
        try drawAnnotations(annotations,in:ctx,source:image)
        guard let result=ctx.makeImage() else { throw NashiError.failed("Could not annotate image.") };return result
    }

    /// Shared renderer for exports and the in-progress annotation canvas.
    /// The context uses bottom-left coordinates in source-image pixels.
    public static func drawAnnotations(_ annotations:[ImageAnnotation],in ctx:CGContext,source image:CGImage) throws {
        for item in annotations {
            let rect=item.rect
            guard [rect.x,rect.y,rect.width,rect.height,item.strokeWidth,item.opacity,item.fontSize,item.magnification].allSatisfy(\.isFinite) else { throw NashiError.invalidInput("Annotation coordinates must be finite.") }
            let r=CGRect(x:rect.x,y:Double(image.height)-rect.y-rect.height,width:rect.width,height:rect.height).standardized
            let point: (ImagePoint) -> CGPoint = { CGPoint(x:$0.x,y:Double(image.height)-$0.y) }
            ctx.saveGState();defer { ctx.restoreGState() }
            ctx.setAlpha(max(0,min(1,item.opacity)));ctx.setStrokeColor(try validatedColor(item.color));ctx.setFillColor(try validatedColor(item.fillColor ?? item.color));ctx.setLineWidth(max(0.5,item.strokeWidth));ctx.setLineCap(.round);ctx.setLineJoin(.round)
            switch item.kind {
            case .rectangle:
                if item.fillColor != nil { ctx.fill(r) };ctx.stroke(r)
            case .ellipse:
                if item.fillColor != nil { ctx.fillEllipse(in:r) };ctx.strokeEllipse(in:r)
            case .line,.arrow:
                let points = item.points.count >= 2 ? item.points.map(point) : [point(.init(x:rect.x,y:rect.y)),point(.init(x:rect.x+rect.width,y:rect.y+rect.height))]
                guard let start=points.first,let end=points.last else { continue }
                ctx.move(to:start);for next in points.dropFirst() { ctx.addLine(to:next) };ctx.strokePath()
                if item.kind == .arrow {
                    let previous=points[points.count-2],angle=atan2(end.y-previous.y,end.x-previous.x),size=max(10,item.strokeWidth*4)
                    let left=CGPoint(x:end.x-size*cos(angle - .pi/6),y:end.y-size*sin(angle - .pi/6))
                    let right=CGPoint(x:end.x-size*cos(angle + .pi/6),y:end.y-size*sin(angle + .pi/6))
                    ctx.move(to:left);ctx.addLine(to:end);ctx.addLine(to:right);ctx.strokePath()
                }
            case .freehand,.highlight:
                let points=item.points.isEmpty ? [point(.init(x:rect.x,y:rect.y)),point(.init(x:rect.x+rect.width,y:rect.y+rect.height))] : item.points.map(point)
                if item.kind == .highlight { ctx.setAlpha(max(0,min(1,item.opacity))*0.35);ctx.setBlendMode(.multiply);ctx.setLineWidth(max(12,item.strokeWidth)) }
                if let first=points.first { ctx.move(to:first);for next in points.dropFirst() { ctx.addLine(to:next) };ctx.strokePath() }
            case .text:
                drawText(item.text,in:r,context:ctx,color:try validatedColor(item.color),fontSize:item.fontSize,fontFamily:item.fontFamily)
            case .number:
                ctx.fillEllipse(in:r)
                drawCenteredText(String(item.number),in:r,context:ctx,color:CGColor(gray:1,alpha:1),fontSize:min(r.width,r.height)*0.58)
            case .callout:
                let body=CGRect(x:r.minX,y:r.minY+r.height*0.2,width:r.width,height:r.height*0.8)
                ctx.addPath(CGPath(roundedRect:body,cornerWidth:8,cornerHeight:8,transform:nil));ctx.fillPath()
                ctx.move(to:CGPoint(x:body.minX+body.width*0.18,y:body.minY+1));ctx.addLine(to:CGPoint(x:body.minX+body.width*0.1,y:r.minY));ctx.addLine(to:CGPoint(x:body.minX+body.width*0.4,y:body.minY+1));ctx.fillPath()
                drawCenteredText(item.text.isEmpty ? String(item.number) : item.text,in:body.insetBy(dx:6,dy:2),context:ctx,color:CGColor(gray:1,alpha:1),fontSize:item.fontSize)
            case .magnifier:
                guard item.magnification > 0 else { throw NashiError.invalidInput("Magnification must be positive.") }
                let source=item.sourceRect ?? EditRect(x:rect.x+rect.width*(1-1/item.magnification)/2,y:rect.y+rect.height*(1-1/item.magnification)/2,width:rect.width/item.magnification,height:rect.height/item.magnification)
                let sourcePixels=CGRect(x:source.x,y:source.y,width:source.width,height:source.height).intersection(CGRect(x:0,y:0,width:image.width,height:image.height))
                if let crop=image.cropping(to:sourcePixels), !sourcePixels.isEmpty {
                    ctx.saveGState();ctx.addEllipse(in:r);ctx.clip();ctx.interpolationQuality = .high;ctx.draw(crop,in:r);ctx.restoreGState();ctx.strokeEllipse(in:r)
                }
            case .image:
                guard let imageURL=item.imageURL else { throw NashiError.invalidInput("Choose an image for the annotation.") }
                let overlay=try load(imageURL);drawFitted(overlay,in:r,context:ctx,fit:.fit)
            }
        }
    }

    private static func drawText(_ text:String,in rect:CGRect,context:CGContext,color:CGColor,fontSize:Double,fontFamily:String="system") {
        let design:NSFontDescriptor.SystemDesign = fontFamily=="rounded" ? .rounded:fontFamily=="serif" ? .serif:fontFamily=="monospaced" ? .monospaced:.default
        let system=NSFont.systemFont(ofSize:max(1,fontSize),weight:.semibold)
        let chosen=system.fontDescriptor.withDesign(design).flatMap{NSFont(descriptor:$0,size:max(1,fontSize))} ?? system
        let font=chosen as CTFont
        let attributed=NSAttributedString(string:text,attributes:[NSAttributedString.Key(kCTFontAttributeName as String):font,NSAttributedString.Key(kCTForegroundColorAttributeName as String):color])
        let setter=CTFramesetterCreateWithAttributedString(attributed)
        let path=CGPath(rect:rect,transform:nil)
        CTFrameDraw(CTFramesetterCreateFrame(setter,CFRange(location:0,length:0),path,nil),context)
    }

    private static func drawCenteredText(_ text:String,in rect:CGRect,context:CGContext,color:CGColor,fontSize:Double) {
        let font=CTFontCreateWithName("Helvetica-Bold" as CFString,max(1,fontSize),nil)
        let line=CTLineCreateWithAttributedString(NSAttributedString(string:text,attributes:[NSAttributedString.Key(kCTFontAttributeName as String):font,NSAttributedString.Key(kCTForegroundColorAttributeName as String):color]))
        var ascent:CGFloat=0,descent:CGFloat=0
        let width=CTLineGetTypographicBounds(line,&ascent,&descent,nil)
        context.textPosition=CGPoint(x:rect.midX-width/2,y:rect.midY-(ascent-descent)/2);CTLineDraw(line,context)
    }

    private static func drawFitted(_ image:CGImage,in rect:CGRect,context:CGContext,fit:ImageFit) {
        let sx=rect.width/CGFloat(image.width),sy=rect.height/CGFloat(image.height),scale=fit == .fill ? max(sx,sy) : min(sx,sy)
        let target=CGRect(x:rect.midX-CGFloat(image.width)*scale/2,y:rect.midY-CGFloat(image.height)*scale/2,width:CGFloat(image.width)*scale,height:CGFloat(image.height)*scale)
        context.saveGState();context.clip(to:rect);context.interpolationQuality = .high;context.draw(image,in:target);context.restoreGState()
    }

    public static func redactedImage(_ image: CGImage, options: OperationOptions) throws -> CGImage { try redact(image,options:options) }

    private static func redact(_ image: CGImage, options: OperationOptions) throws -> CGImage {
        var settings=options.imageRedaction ?? ImageRedactionOptions()
        if options.imageRedaction == nil {
            settings.style=options.redactionMode == "black" || options.redactionMode == "solid" ? .solid : options.redactionMode == "pixelate" ? .pixelate : .blur
        }
        var regions=options.redactions
        if settings.detectFaces || settings.detectText {
            regions += try recognizedRegions(image,faces:settings.detectFaces,text:settings.detectText,padding:settings.detectionPadding).map(\.rect)
        }
        guard !regions.isEmpty else { throw NashiError.invalidInput("Select an area to redact, or detect faces or text.") }
        guard settings.blurRadius.isFinite,settings.pixelSize.isFinite else { throw NashiError.invalidInput("Redaction strength must be finite.") }
        // A batch reuses the first preview's absolute pixel selections. Validate
        // every region against each image before rendering so a smaller image
        // cannot be exported unchanged with a successful "Redacted" filename.
        let width=Double(image.width),height=Double(image.height)
        for rect in regions {
            guard [rect.x,rect.y,rect.width,rect.height].allSatisfy(\.isFinite) else { throw NashiError.invalidInput("Redaction coordinates must be finite for the \(image.width) × \(image.height) pixel image.") }
            guard rect.x>=0,rect.y>=0,rect.width>0,rect.height>0,
                  rect.x<=width,rect.y<=height,rect.width<=width-rect.x,rect.height<=height-rect.y else {
                throw NashiError.invalidInput("Every redaction area must have positive dimensions and fit completely inside the \(image.width) × \(image.height) pixel image. Adjust the areas for this image and try again.")
            }
        }
        let ctx=try canvas(image.width,image.height);ctx.draw(image,in:CGRect(x:0,y:0,width:image.width,height:image.height))
        for rect in regions {
            let pixels=CGRect(x:rect.x,y:rect.y,width:rect.width,height:rect.height).integral
            let target=CGRect(x:pixels.minX,y:CGFloat(image.height)-pixels.maxY,width:pixels.width,height:pixels.height)
            // Solid redaction replaces the selected pixels; an editor color's
            // alpha must never blend the original content back into the result.
            if settings.style == .solid { ctx.setFillColor(try validatedColor(settings.color).copy(alpha:1)!);ctx.fill(target) }
            else if let part=image.cropping(to:pixels) {
                let original=CIImage(cgImage:part),bounds=original.extent
                let filtered:CIImage
                if settings.style == .blur { filtered=original.clampedToExtent().applyingFilter("CIGaussianBlur",parameters:[kCIInputRadiusKey:max(1,settings.blurRadius)]).cropped(to:bounds) }
                else { filtered=original.clampedToExtent().applyingFilter("CIPixellate",parameters:[kCIInputScaleKey:max(2,settings.pixelSize),kCIInputCenterKey:CIVector(x:bounds.midX,y:bounds.midY)]).cropped(to:bounds) }
                guard let cg=imageContext.createCGImage(filtered,from:bounds) else { throw NashiError.failed("Could not render the redaction.") };ctx.draw(cg,in:target)
            }
        }
        guard let result=ctx.makeImage() else { throw NashiError.failed("Could not redact image.") };return result
    }

    public static func recognizedRegions(_ input:URL,faces:Bool=true,text:Bool=true,padding:Double=0.02) throws -> [DetectedImageRegion] {
        try recognizedRegions(load(input),faces:faces,text:text,padding:padding)
    }

    public static func recognizedRegions(_ image:CGImage,faces:Bool=true,text:Bool=true,padding:Double=0.02) throws -> [DetectedImageRegion] {
        guard padding.isFinite,padding >= 0 else { throw NashiError.invalidInput("Detection padding must be nonnegative.") }
        var requests:[VNRequest]=[]
        let faceRequest=VNDetectFaceRectanglesRequest(),textRequest=VNRecognizeTextRequest()
        textRequest.recognitionLevel = .accurate;textRequest.usesLanguageCorrection = true
        if faces { requests.append(faceRequest) };if text { requests.append(textRequest) }
        guard !requests.isEmpty else { return [] }
        try VNImageRequestHandler(cgImage:image).perform(requests)
        let bounds=CGRect(x:0,y:0,width:image.width,height:image.height)
        func pixels(_ box:CGRect)->EditRect {
            let r=CGRect(x:box.minX*Double(image.width),y:(1-box.maxY)*Double(image.height),width:box.width*Double(image.width),height:box.height*Double(image.height))
            let expanded=r.insetBy(dx:-max(2,r.width*padding),dy:-max(2,r.height*padding)).intersection(bounds).integral
            return EditRect(x:expanded.minX,y:expanded.minY,width:expanded.width,height:expanded.height)
        }
        var regions=(faces ? faceRequest.results ?? [] : []).map { DetectedImageRegion(kind:.face,rect:pixels($0.boundingBox),confidence:Double($0.confidence)) }
        regions += (text ? textRequest.results ?? [] : []).map { result in
            DetectedImageRegion(kind:.text,rect:pixels(result.boundingBox),confidence:Double(result.topCandidates(1).first?.confidence ?? result.confidence),text:result.topCandidates(1).first?.string)
        }
        return regions
    }

    public static func framedImage(_ image:CGImage,options:OperationOptions) throws -> CGImage { try frame(image,options:options) }

    private static func frame(_ image: CGImage, options: OperationOptions) throws -> CGImage {
        var settings=options.imageFrame ?? ImageFrameOptions()
        if options.imageFrame == nil { settings.padding=options.padding;settings.cornerRadius=Double(options.cornerRadius);settings.backgroundColor=options.backgroundColor;settings.shadowRadius=0 }
        guard [settings.position.x,settings.position.y,settings.imageScale,settings.cornerRadius,settings.backgroundBlur,settings.shadowRadius,settings.shadowOpacity,settings.gradientAngle].allSatisfy(\.isFinite),settings.imageScale > 0 else { throw NashiError.invalidInput("Frame settings must be finite, and image scale must be positive.") }
        let padding=max(0,settings.padding)
        guard padding<=32000,settings.imageScale<=100 else { throw NashiError.invalidInput("Frame padding or image scale is too large.") }
        for value in [settings.width,settings.height].compactMap({$0}) { guard value>0,value<=32000 else { throw NashiError.invalidInput("Canvas dimensions must be between 1 and 32,000 pixels.") } }
        var width=settings.width ?? image.width+padding*2,height=settings.height ?? image.height+padding*2
        func dimension(_ number:Double) throws -> Int {
            guard number.isFinite,number>=1,number<=32000 else { throw NashiError.invalidInput("Canvas dimensions must be between 1 and 32,000 pixels.") };return Int(number.rounded(.up))
        }
        if let ratio=settings.aspectRatio {
            guard ratio.isFinite,ratio>0 else { throw NashiError.invalidInput("Canvas aspect ratio must be positive.") }
            if settings.width != nil && settings.height == nil { height=try dimension(Double(width)/ratio) }
            else if settings.height != nil && settings.width == nil { width=try dimension(Double(height)*ratio) }
            else if settings.width == nil && settings.height == nil {
                if Double(width)/Double(height)<ratio { width=try dimension(Double(height)*ratio) }
                else { height=try dimension(Double(width)/ratio) }
            }
        }
        let ctx=try canvas(width,height),bounds=CGRect(x:0,y:0,width:width,height:height)
        ctx.setFillColor(try validatedColor(settings.backgroundColor));ctx.fill(bounds)
        switch settings.backdrop {
        case .solid: break
        case .gradient:
            let colors=settings.gradientColors.isEmpty ? [settings.backgroundColor,settings.backgroundColor] : settings.gradientColors
            guard let gradient=CGGradient(colorsSpace:imageColorSpace,colors:try colors.map(validatedColor) as CFArray,locations:nil) else { throw NashiError.invalidInput("Choose gradient colors.") }
            let angle=settings.gradientAngle * .pi/180,dx=cos(angle)*Double(width)/2,dy=sin(angle)*Double(height)/2
            ctx.drawLinearGradient(gradient,start:CGPoint(x:Double(width)/2-dx,y:Double(height)/2-dy),end:CGPoint(x:Double(width)/2+dx,y:Double(height)/2+dy),options:[.drawsBeforeStartLocation,.drawsAfterEndLocation])
        case .image:
            guard let url=settings.backgroundImage else { throw NashiError.invalidInput("Choose a background image.") }
            var backdrop=try load(url)
            if settings.backgroundBlur > 0 {
                let ci=CIImage(cgImage:backdrop),blur=ci.clampedToExtent().applyingFilter("CIGaussianBlur",parameters:[kCIInputRadiusKey:settings.backgroundBlur]).cropped(to:ci.extent)
                guard let rendered=imageContext.createCGImage(blur,from:ci.extent) else { throw NashiError.failed("Could not blur background.") };backdrop=rendered
            }
            drawFitted(backdrop,in:bounds,context:ctx,fit:.fill)
        }
        let availableWidth=max(1,width-padding*2),availableHeight=max(1,height-padding*2)
        let scale=min(1,min(Double(availableWidth)/Double(image.width),Double(availableHeight)/Double(image.height)))*settings.imageScale
        let imageWidth=Double(image.width)*scale,imageHeight=Double(image.height)*scale
        let x=(Double(width)-imageWidth)*max(0,min(1,settings.position.x)),top=(Double(height)-imageHeight)*max(0,min(1,settings.position.y))
        let r=CGRect(x:x,y:Double(height)-top-imageHeight,width:imageWidth,height:imageHeight)
        let radius=max(0,min(settings.cornerRadius,min(r.width,r.height)/2)),path=CGPath(roundedRect:r,cornerWidth:radius,cornerHeight:radius,transform:nil)
        if settings.shadowRadius>0,settings.shadowOpacity>0 {
            ctx.saveGState();ctx.setShadow(offset:CGSize(width:0,height:-settings.shadowRadius/4),blur:settings.shadowRadius,color:CGColor(gray:0,alpha:max(0,min(1,settings.shadowOpacity))));ctx.setFillColor(CGColor(gray:0,alpha:1));ctx.addPath(path);ctx.fillPath();ctx.restoreGState()
        }
        ctx.saveGState();ctx.addPath(path);ctx.clip();ctx.interpolationQuality = .high;ctx.draw(image,in:r);ctx.restoreGState()
        guard let result=ctx.makeImage() else { throw NashiError.failed("Could not frame image.") };return result
    }

    private static func collage(_ inputs: [URL], options: OperationOptions, token: CancellationToken) throws -> [URL] {
        guard let first=inputs.first else { throw NashiError.invalidInput("Choose images for your collage.") }
        if let settings=options.collage {
            let images=try inputs.map { input in try token.check();return try load(input) }
            let output=OutputFiles.unique(for:first,extension:"png",suffix:" - Collage",directory:options.outputDirectory)
            try write(collageImage(images,settings:settings),to:output);return [output]
        }
        let images=try inputs.map(load), columns=max(1,min(inputs.count,options.collageColumns)),rows=(inputs.count+columns-1)/columns
        let cellWidth=options.width ?? 600,cellHeight=options.height ?? 600,gap=max(0,options.padding)
        let width=columns*cellWidth+(columns+1)*gap,height=rows*cellHeight+(rows+1)*gap
        let ctx=try canvas(width,height);ctx.setFillColor(try validatedColor(options.backgroundColor));ctx.fill(CGRect(x:0,y:0,width:width,height:height))
        for (index,image) in images.enumerated() {
            try token.check()
            let scale=min(Double(cellWidth)/Double(image.width),Double(cellHeight)/Double(image.height))
            let w=Double(image.width)*scale,h=Double(image.height)*scale
            let x=Double(gap+(index%columns)*(cellWidth+gap))+(Double(cellWidth)-w)/2
            let y=Double(height-gap-(index/columns+1)*cellHeight-(index/columns)*gap)+(Double(cellHeight)-h)/2
            ctx.draw(image,in:CGRect(x:x,y:y,width:w,height:h))
        }
        let output=OutputFiles.unique(for:first,extension:"png",suffix:" - Collage",directory:options.outputDirectory)
        try write(ctx.makeImage()!,to:output);return [output]
    }

    public static func collageImage(_ inputs:[CGImage],settings:CollageOptions) throws -> CGImage {
        guard inputs.count>=2 else { throw NashiError.invalidInput("Choose at least two images for a collage.") }
        guard settings.aspectRatio.isFinite,settings.aspectRatio>0,settings.cornerRadius.isFinite else { throw NashiError.invalidInput("Collage ratio and corner radius must be valid.") }
        let images:[CGImage]
        if settings.imageOrder.isEmpty { images=inputs }
        else {
            guard settings.imageOrder.count==inputs.count,Set(settings.imageOrder)==Set(inputs.indices) else { throw NashiError.invalidInput("Image order must include every image exactly once.") }
            images=settings.imageOrder.map { inputs[$0] }
        }
        guard settings.width>0,settings.width<=32000 else { throw NashiError.invalidInput("Collage dimensions must be between 1 and 32,000 pixels.") }
        let calculatedHeight=Double(settings.width)/settings.aspectRatio
        if settings.height == nil { guard calculatedHeight.isFinite,calculatedHeight>=1,calculatedHeight<=32000 else { throw NashiError.invalidInput("Collage dimensions must be between 1 and 32,000 pixels.") } }
        let width=settings.width,height=settings.height ?? Int(calculatedHeight.rounded())
        let ctx=try canvas(width,height),bounds=CGRect(x:0,y:0,width:width,height:height)
        ctx.setFillColor(try validatedColor(settings.backgroundColor));ctx.fill(bounds)
        if let backdrop=settings.backgroundImage { drawFitted(try load(backdrop),in:bounds,context:ctx,fit:.fill) }
        let padding=Double(max(0,settings.outerPadding)),gap=Double(max(0,settings.spacing)),area=bounds.insetBy(dx:padding,dy:padding)
        guard area.width>0,area.height>0 else { throw NashiError.invalidInput("Collage padding leaves no space for images.") }
        var cells:[CGRect]=[]
        if settings.layout == .featured {
            let featureWidth=(area.width-gap)*2/3,otherWidth=area.width-gap-featureWidth,otherHeight=(area.height-gap*Double(images.count-2))/Double(images.count-1)
            guard featureWidth>0,otherWidth>0,otherHeight>0 else { throw NashiError.invalidInput("Reduce collage spacing or increase the canvas.") }
            cells.append(CGRect(x:area.minX,y:area.minY,width:featureWidth,height:area.height))
            for index in 1..<images.count { cells.append(CGRect(x:area.minX+featureWidth+gap,y:area.maxY-Double(index)*otherHeight-Double(index-1)*gap,width:otherWidth,height:otherHeight)) }
        } else {
            let columns=settings.layout == .row ? images.count : settings.layout == .column ? 1 : max(1,min(images.count,settings.columns))
            let rows=(images.count+columns-1)/columns,cellWidth=(area.width-gap*Double(columns-1))/Double(columns),cellHeight=(area.height-gap*Double(rows-1))/Double(rows)
            guard cellWidth>0,cellHeight>0 else { throw NashiError.invalidInput("Reduce collage spacing or increase the canvas.") }
            for index in images.indices { cells.append(CGRect(x:area.minX+Double(index%columns)*(cellWidth+gap),y:area.maxY-Double(index/columns+1)*cellHeight-Double(index/columns)*gap,width:cellWidth,height:cellHeight)) }
        }
        for (image,cell) in zip(images,cells) {
            ctx.saveGState();let radius=max(0,min(settings.cornerRadius,min(cell.width,cell.height)/2));ctx.addPath(CGPath(roundedRect:cell,cornerWidth:radius,cornerHeight:radius,transform:nil));ctx.clip();drawFitted(image,in:cell,context:ctx,fit:settings.imageFit);ctx.restoreGState()
        }
        guard let result=ctx.makeImage() else { throw NashiError.failed("Could not create collage.") };return result
    }

    public static func compress(_ input: URL, options: OperationOptions, token: CancellationToken) throws -> [URL] {
        try token.check()
        var image=try load(input)
        let originalWidth=image.width,originalHeight=image.height
        image=try resizeUsingOptions(image,options:options)
        let inputSize=Int64((try input.resourceValues(forKeys:[.fileSizeKey])).fileSize ?? 0)
        if let percent=options.percentage { guard percent.isFinite,percent>0,percent<100 else { throw NashiError.invalidInput("Percentage reduction must be greater than 0 and less than 100.") } }
        let target=options.targetBytes ?? options.percentage.map { Int64(Double(inputSize)*(1-$0/100)) }
        if let target { guard target>0 else { throw NashiError.invalidInput("Choose a positive target file size.") } }
        let ext=["jpg","jpeg","heic","webp","avif","png"].contains(input.pathExtension.lowercased()) ? input.pathExtension : "jpg"
        let output=OutputFiles.unique(for:input,extension:ext,suffix:" - Compressed",directory:options.outputDirectory)
        // Attempts are encoded privately; a failed or cancelled pass leaves no partial output.
        let temporary=output.deletingLastPathComponent().appendingPathComponent(".nashi-\(UUID().uuidString)").appendingPathExtension(output.pathExtension)
        defer { try? FileManager.default.removeItem(at:temporary) }
        var quality=max(0.01,min(1,options.quality))
        for iteration in 0..<24 {
            try token.check();try? FileManager.default.removeItem(at:temporary)
            try write(image,to:temporary,quality:quality)
            // The same URL is replaced on each pass; resourceValues can retain its first size.
            let size=((try FileManager.default.attributesOfItem(atPath:temporary.path))[.size] as? NSNumber)?.int64Value ?? 0
            if target == nil || size <= target! {
                if image.width==originalWidth,image.height==originalHeight { try OutputFiles.retainSmallerOriginal(input,output:temporary,target:target) }
                try FileManager.default.moveItem(at:temporary,to:output)
                return [output]
            }
            if quality>0.15 && !["png"].contains(ext.lowercased()) { quality *= 0.72 }
            else { image=try resized(image,width:max(1,Int(Double(image.width)*0.8)),height:max(1,Int(Double(image.height)*0.8))) }
            if iteration == 23 { throw NashiError.failed("The requested file size could not be reached. Choose a larger target.") }
        }
        return [output]
    }

    public static func metadata(_ input: URL) -> [String:String] {
        (try? readMetadata(input)) ?? [:]
    }

    /// Editors must distinguish a complete read from an empty metadata set.
    public static func readMetadata(_ input:URL,token:CancellationToken = .init()) throws -> [String:String] {
        try token.check()
        guard let source=CGImageSourceCreateWithURL(input as CFURL,nil),CGImageSourceCopyPropertiesAtIndex(source,0,nil) != nil else { throw NashiError.invalidInput("This image's metadata could not be read.") }
        let props=imageProperties(source,input:input)
        var result:[String:String]=[:]
        func flatten(_ dict:[String:Any],_ prefix:String) { for (k,v) in dict { if let sub=v as? [String:Any] { flatten(sub,prefix+k+".") } else { result[prefix+k]="\(v)" } } }
        flatten(props,"")
        let extra=try ImageMetadataEngine.fields(input,token:token)
        result.merge(extra) { _,new in new }
        try token.check()
        return result
    }

    private static func imageProperties(_ source:CGImageSource,input:URL)->[String:Any] {
        var properties=(CGImageSourceCopyPropertiesAtIndex(source,0,nil) as? [String:Any]) ?? [:]
        // ImageIO omits IFD0 text when some files lack an orientation tag.
        // Recover it before displaying, converting, or editing unrelated fields.
        let format=input.pathExtension.lowercased()
        var exif:Data?
        if ["webp","jpg","jpeg","tiff","tif"].contains(format),let data=try? Data(contentsOf:input) {
            if format=="webp" { exif=try? WebPMetadataContainer.chunks(data).first(where:{$0.name=="EXIF"})?.payload }
            else if format=="jpg" || format=="jpeg" { exif=try? WebPMetadataContainer.exifPayload(fromJPEG:data) }
            else { exif=data }
        }
        if let exif {
            let recovered=WebPMetadataContainer.tiffTextFields(exif)
            if !recovered.isEmpty { var tiff=(properties[kCGImagePropertyTIFFDictionary as String] as? [String:Any]) ?? [:];for (key,value) in recovered { tiff[key]=value };properties[kCGImagePropertyTIFFDictionary as String]=tiff }
        }
        return properties
    }

    /// Writes a new file, leaving the source intact. Per-frame GIF timing is preserved.
    public static func editMetadata(_ input:URL,changes:ImageMetadataOptions,options:OperationOptions = .init(),token:CancellationToken = .init()) throws -> URL {
        try ImageMetadataEngine.edit(input,changes:changes,options:options,token:token)
    }

    public static func qrCodes(_ input: URL) throws -> [String] {
        let request=VNDetectBarcodesRequest();request.symbologies=[.qr]
        try VNImageRequestHandler(cgImage:load(input)).perform([request])
        return request.results?.compactMap(\.payloadStringValue) ?? []
    }
}
