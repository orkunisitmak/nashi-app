import XCTest
import AppKit
import CoreText
import ImageIO
@testable import NashiCore

final class ImageParityTests: XCTestCase {
    private func canvas(_ width:Int=128,_ height:Int=96,color:String?=nil) throws -> CGImage {
        let context=try XCTUnwrap(CGContext(data:nil,width:width,height:height,bitsPerComponent:8,bytesPerRow:width*4,space:CGColorSpace(name:CGColorSpace.sRGB)!,bitmapInfo:CGImageAlphaInfo.premultipliedLast.rawValue))
        if let color { context.setFillColor(ImageEngine.color(color));context.fill(CGRect(x:0,y:0,width:width,height:height)) }
        else {
            for y in 0..<height {
                for x in 0..<width {
                    let checker=((x/5+y/7)%2)==0
                    context.setFillColor(CGColor(red:CGFloat(x)/CGFloat(width),green:CGFloat(y)/CGFloat(height),blue:checker ? 0.8 : 0.1,alpha:1))
                    context.fill(CGRect(x:x,y:y,width:1,height:1))
                }
            }
        }
        return try XCTUnwrap(context.makeImage())
    }

    private func bytes(_ image:CGImage) throws -> [UInt8] {
        var pixels=[UInt8](repeating:0,count:image.width*image.height*4)
        try pixels.withUnsafeMutableBytes { buffer in
            let ctx=try XCTUnwrap(CGContext(data:buffer.baseAddress,width:image.width,height:image.height,bitsPerComponent:8,bytesPerRow:image.width*4,space:CGColorSpace(name:CGColorSpace.sRGB)!,bitmapInfo:CGImageAlphaInfo.premultipliedLast.rawValue))
            ctx.draw(image,in:CGRect(x:0,y:0,width:image.width,height:image.height))
        }
        return pixels
    }

    private func pixel(_ image:CGImage,_ x:Int,_ y:Int) throws -> [UInt8] {
        let piece=try XCTUnwrap(image.cropping(to:CGRect(x:x,y:y,width:1,height:1)))
        return Array(try bytes(piece).prefix(3))
    }

    private func difference(_ lhs:CGImage,_ rhs:CGImage) throws -> Double {
        let a=try bytes(lhs),b=try bytes(rhs)
        XCTAssertEqual(a.count,b.count)
        return zip(a,b).enumerated().filter { $0.offset%4 != 3 }.reduce(0.0) { $0+Double(abs(Int($1.element.0)-Int($1.element.1))) } / Double(a.count/4*3)
    }

    func testEveryPhotoAdjustmentChangesTheRenderedPixels() throws {
        let input=try canvas(),neutral=try ImageEngine.preview(input,options:.init())
        let controls:[(String,(inout ImageAdjustments)->Void)]=[
            ("exposure",{$0.exposure=1}),("brightness",{$0.brightness=0.15}),
            ("contrast",{$0.contrast=1.5}),("highlights",{$0.highlights = -0.8}),
            ("shadows",{$0.shadows=0.8}),("whites",{$0.whites = -0.8}),
            ("blacks",{$0.blacks=0.6}),("gamma",{$0.gamma=1.6}),
            ("temperature",{$0.temperature=0.8}),("tint",{$0.tint=0.8}),
            ("saturation",{$0.saturation=0.2}),("vibrance",{$0.vibrance=0.8}),
            ("hue",{$0.hue=90}),("clarity",{$0.clarity=0.8}),
            ("sharpen",{$0.sharpen=1}),("dehaze",{$0.dehaze=0.8}),
            ("grain",{$0.grain=0.8}),("noiseReduction",{$0.noiseReduction=1}),
            ("vignette",{$0.vignette=1.5}),("texture",{$0.texture=1}),
            ("fade",{$0.fade=0.8}),("blur",{$0.blur=3})
        ]
        for (name,set) in controls {
            var options=OperationOptions();set(&options.imageAdjustments)
            let output=try ImageEngine.preview(input,options:options)
            XCTAssertEqual(output.width,input.width,name);XCTAssertEqual(output.height,input.height,name)
            XCTAssertGreaterThan(try difference(output,neutral),0.01,"\(name) did not change the rendered pixels")
        }
        var invalid=OperationOptions();invalid.imageAdjustments.gamma=0
        XCTAssertThrowsError(try ImageEngine.preview(input,options:invalid))
    }

    func testCropPreviewAndFrameProduceRequestedGeometry() throws {
        let input=try canvas(80,40,color:"#0000FF")
        var crop=OperationOptions();crop.crop=EditRect(x:10,y:5,width:40,height:20);crop.width=100;crop.height=50
        let cropped=try ImageEngine.preview(input,tool:.cropImage,options:crop)
        XCTAssertEqual(cropped.width,100);XCTAssertEqual(cropped.height,50)
        var rotate=OperationOptions();rotate.rotation=90
        let rotated=try ImageEngine.preview(input,options:rotate)
        XCTAssertEqual(rotated.width,40);XCTAssertEqual(rotated.height,80)
        var settings=ImageFrameOptions();settings.padding=10;settings.width=160;settings.height=100;settings.cornerRadius=8;settings.shadowRadius=5;settings.backgroundColor="#FF0000"
        var frame=OperationOptions();frame.imageFrame=settings
        let output=try ImageEngine.preview(input,tool:.frameImage,options:frame)
        XCTAssertEqual(output.width,160);XCTAssertEqual(output.height,100)
        XCTAssertEqual(try pixel(output,0,0),[255,0,0]);XCTAssertEqual(try pixel(output,80,50),[0,0,255])
        settings.aspectRatio=16.0/9;settings.width=nil;settings.height=nil;frame.imageFrame=settings
        let ratio=try ImageEngine.preview(input,tool:.frameImage,options:frame)
        XCTAssertEqual(Double(ratio.width)/Double(ratio.height),16.0/9,accuracy:0.02)
    }

    func testGradientImageBlurAndPositionChangeTheBackdrop() throws {
        let input=try canvas(24,24,color:"#0000FF")
        var settings=ImageFrameOptions();settings.width=120;settings.height=80;settings.padding=10;settings.cornerRadius=0;settings.shadowRadius=0;settings.backdrop = .gradient;settings.gradientColors=["#FF0000","#00FF00"];settings.position = .init(x:0,y:0)
        var options=OperationOptions();options.imageFrame=settings
        let gradient=try ImageEngine.preview(input,tool:.frameImage,options:options)
        XCTAssertNotEqual(try pixel(gradient,110,5),try pixel(gradient,110,75))
        XCTAssertEqual(try pixel(gradient,2,2),[0,0,255])
        let folder=try OutputFiles.temporaryDirectory();defer { try? FileManager.default.removeItem(at:folder) }
        let background=folder.appendingPathComponent("background.png");try ImageEngine.write(canvas(80,80),to:background)
        settings.backdrop = .image;settings.backgroundImage=background;settings.backgroundBlur=0;options.imageFrame=settings
        let sharp=try ImageEngine.preview(input,tool:.frameImage,options:options)
        settings.backgroundBlur=10;options.imageFrame=settings
        let blurred=try ImageEngine.preview(input,tool:.frameImage,options:options)
        XCTAssertGreaterThan(try difference(sharp,blurred),2)
    }

    func testCollageLayoutsFitFillAndSelectionOrder() throws {
        let images=try [canvas(60,30,color:"#FF0000"),canvas(30,60,color:"#00FF00"),canvas(60,30,color:"#0000FF")]
        var settings=CollageOptions();settings.width=180;settings.height=120;settings.spacing=0;settings.outerPadding=0;settings.cornerRadius=0
        for layout in CollageLayout.allCases {
            settings.layout=layout
            let result=try ImageEngine.collageImage(images,settings:settings)
            XCTAssertEqual(result.width,180);XCTAssertEqual(result.height,120)
            XCTAssertEqual(try pixel(result,5,5),[255,0,0],layout.rawValue)
        }
        settings.layout = .row;settings.imageOrder=[2,1,0]
        let reordered=try ImageEngine.collageImage(images,settings:settings)
        XCTAssertEqual(try pixel(reordered,5,5),[0,0,255])
        settings.imageOrder=[];settings.imageFit = .fit
        let fitted=try ImageEngine.collageImage(images,settings:settings)
        XCTAssertEqual(try pixel(fitted,5,5),[255,255,255])
        settings.imageOrder=[0,0,1]
        XCTAssertThrowsError(try ImageEngine.collageImage(images,settings:settings))
    }

    /// 1000 px split into 3 or 7 cells puts the shared edges inside pixels; with no spacing the
    /// background used to show through as a hairline between images that should touch.
    func testCollageCellsWithoutSpacingTouchWithoutSeams() throws {
        let white=try canvas(50,50,color:"#FFFFFF")
        var settings=CollageOptions();settings.layout = .row;settings.width=1000;settings.height=100;settings.spacing=0;settings.outerPadding=0;settings.cornerRadius=0;settings.backgroundColor="#000000"
        for count in [3,7] {
            let result=try ImageEngine.collageImage(Array(repeating:white,count:count),settings:settings),row=try bytes(try XCTUnwrap(result.cropping(to:CGRect(x:0,y:50,width:1000,height:1))))
            XCTAssertEqual(stride(from:0,to:row.count,by:4).filter { row[$0] != 255 }.map { $0/4 },[],"\(count) images")
        }
    }

    func testSolidBlurAndPixelateRedactionsAffectOnlySelectedRegion() throws {
        let input=try canvas(128,128)
        var options=OperationOptions();options.redactions=[.init(x:32,y:32,width:64,height:64)]
        var settings=ImageRedactionOptions();settings.color="#000000";settings.pixelSize=11
        for style in ImageRedactionStyle.allCases {
            settings.style=style;options.imageRedaction=settings
            let result=try ImageEngine.preview(input,tool:.redactImage,options:options)
            XCTAssertEqual(try pixel(result,4,4),try pixel(input,4,4),style.rawValue)
            XCTAssertGreaterThan(try difference(result,input),0.1,style.rawValue)
            if style == .solid { XCTAssertEqual(try pixel(result,48,48),[0,0,0]) }
        }
    }

    func testRichAnnotationKindsAndImportedImageRender() throws {
        let input=try canvas(180,160,color:"#FFFFFF")
        let folder=try OutputFiles.temporaryDirectory();defer { try? FileManager.default.removeItem(at:folder) }
        let overlay=folder.appendingPathComponent("overlay.png");try ImageEngine.write(canvas(20,20,color:"#0000FF"),to:overlay)
        for kind in ImageAnnotationKind.allCases {
            var annotation=ImageAnnotation(kind:kind,rect:.init(x:30,y:30,width:90,height:70))
            annotation.text="Hello";annotation.color="#FF0000";annotation.number=7;annotation.strokeWidth=8
            annotation.imageURL=overlay;annotation.points=[.init(x:30,y:30),.init(x:60,y:80),.init(x:120,y:50)]
            var options=OperationOptions();options.imageAnnotations=[annotation]
            // A magnifier over uniform white needs a visible border to distinguish its result.
            let output=try ImageEngine.preview(input,tool:.annotateImage,options:options)
            XCTAssertEqual(output.width,input.width);XCTAssertEqual(output.height,input.height)
            XCTAssertGreaterThan(try difference(output,input),0.05,kind.rawValue)
            if kind == .image { XCTAssertEqual(try pixel(output,75,65),[0,0,255]) }
        }
    }

    func testMetadataUpdatesRemovalsAndOriginalPreservation() throws {
        let folder=try OutputFiles.temporaryDirectory();defer { try? FileManager.default.removeItem(at:folder) }
        let input=folder.appendingPathComponent("source.jpg"),cg=try canvas()
        try ImageEngine.write(cg,to:input,quality:1,metadata:[kCGImagePropertyTIFFDictionary:[kCGImagePropertyTIFFArtist:"Before",kCGImagePropertyTIFFCopyright:"Old copyright"]] as CFDictionary)
        let original=try Data(contentsOf:input)
        var changes=ImageMetadataOptions();changes.updates=["author":"After","comment":"Local edit"];changes.removals=["{TIFF}.Copyright"]
        let output=try ImageEngine.editMetadata(input,changes:changes)
        let metadata=ImageEngine.metadata(output)
        XCTAssertEqual(metadata["{TIFF}.Artist"],"After")
        XCTAssertEqual(metadata["{Exif}.UserComment"],"Local edit")
        XCTAssertNil(metadata["{TIFF}.Copyright"])
        XCTAssertEqual(try Data(contentsOf:input),original)
        changes=ImageMetadataOptions();changes.removeAll=true
        let clean=try ImageEngine.editMetadata(input,changes:changes)
        XCTAssertNil(ImageEngine.metadata(clean)["{TIFF}.Artist"])
        let cleanImage=try ImageEngine.load(clean);XCTAssertEqual(cleanImage.width,cg.width);XCTAssertEqual(cleanImage.height,cg.height)
    }

    func testMetadataRemovalPreservesGIFFramesAndTiming() throws {
        let folder=try OutputFiles.temporaryDirectory();defer { try? FileManager.default.removeItem(at:folder) }
        let input=folder.appendingPathComponent("animated.gif")
        let destination=try XCTUnwrap(CGImageDestinationCreateWithURL(input as CFURL,"com.compuserve.gif" as CFString,2,nil))
        CGImageDestinationSetProperties(destination,[kCGImagePropertyGIFDictionary:[kCGImagePropertyGIFLoopCount:3]] as CFDictionary)
        for (index,color) in ["#FF0000","#00FF00"].enumerated() {
            CGImageDestinationAddImage(destination,try canvas(32,32,color:color),[kCGImagePropertyGIFDictionary:[kCGImagePropertyGIFDelayTime:0.07+Double(index)*0.06]] as CFDictionary)
        }
        XCTAssertTrue(CGImageDestinationFinalize(destination))
        var original=try Data(contentsOf:input);XCTAssertEqual(original.last,0x3b);original.removeLast()
        let comment=Data("Private comment".utf8);original.append(contentsOf:[0x21,0xfe,UInt8(comment.count)]);original.append(comment);original.append(contentsOf:[0,0x3b]);try original.write(to:input)
        // Exercise the same category dispatcher used by the app: GIF is video for
        // conversion, but metadata removal must retain its animation structure.
        let result=try XCTUnwrap(ConversionEngine.run(ConversionRequest(inputs:[input],operation:.tool(.removeMetadata))).first)
        let source=try XCTUnwrap(CGImageSourceCreateWithURL(result as CFURL,nil));XCTAssertEqual(CGImageSourceGetCount(source),2)
        let global=try XCTUnwrap(CGImageSourceCopyProperties(source,nil) as? [String:Any])
        let globalGIF=try XCTUnwrap(global[kCGImagePropertyGIFDictionary as String] as? [String:Any]);XCTAssertEqual((globalGIF[kCGImagePropertyGIFLoopCount as String] as? NSNumber)?.intValue,3)
        XCTAssertNil(try Data(contentsOf:result).range(of:comment));XCTAssertEqual(try Data(contentsOf:input),original)
        for index in 0..<2 {
            let properties=try XCTUnwrap(CGImageSourceCopyPropertiesAtIndex(source,index,nil) as? [String:Any])
            let gif=try XCTUnwrap(properties[kCGImagePropertyGIFDictionary as String] as? [String:Any])
            XCTAssertEqual(try XCTUnwrap(gif[kCGImagePropertyGIFDelayTime as String] as? Double),0.07+Double(index)*0.06,accuracy:0.001)
        }
    }

    func testJPEGWithoutOrientationExposesAndRetainsExistingAuthorWhenAnotherTagChanges() throws {
        let folder=try OutputFiles.temporaryDirectory();defer{try? FileManager.default.removeItem(at:folder)}
        let input=folder.appendingPathComponent("No orientation.jpg")
        let destination=try XCTUnwrap(CGImageDestinationCreateWithURL(input as CFURL,"public.jpeg" as CFString,1,nil))
        CGImageDestinationAddImage(destination,try canvas(),[kCGImagePropertyTIFFDictionary:[kCGImagePropertyTIFFArtist:"Existing Author",kCGImagePropertyTIFFCopyright:"Existing Copyright"]] as CFDictionary)
        XCTAssertTrue(CGImageDestinationFinalize(destination))
        let bytes=try Data(contentsOf:input)
        let raw=WebPMetadataContainer.tiffTextFields(try WebPMetadataContainer.exifPayload(fromJPEG:bytes))
        XCTAssertEqual(raw["Artist"],"Existing Author")
        XCTAssertEqual(ImageEngine.metadata(input)["{TIFF}.Artist"],"Existing Author")
        var changes=ImageMetadataOptions();changes.updates=["comment":"A separate tag"]
        let edited=try ImageEngine.editMetadata(input,changes:changes)
        XCTAssertEqual(ImageEngine.metadata(edited)["{TIFF}.Artist"],"Existing Author")
        XCTAssertEqual(ImageEngine.metadata(edited)["{TIFF}.Copyright"],"Existing Copyright")
        XCTAssertEqual(ImageEngine.metadata(edited)["{Exif}.UserComment"],"A separate tag")
        let converted=try XCTUnwrap(ImageEngine.convert(input,to:"tiff",options:.init(),token:.init()).first)
        XCTAssertEqual(ImageEngine.metadata(converted)["{TIFF}.Artist"],"Existing Author")
        changes=ImageMetadataOptions();changes.removals=["{TIFF}.Artist"]
        let removed=try ImageEngine.editMetadata(input,changes:changes)
        XCTAssertNil(ImageEngine.metadata(removed)["{TIFF}.Artist"])
        XCTAssertEqual(try Data(contentsOf:input),bytes)
    }

    func testCompressionResizesAndMeetsTargetWithoutChangingOriginal() throws {
        let folder=try OutputFiles.temporaryDirectory();defer { try? FileManager.default.removeItem(at:folder) }
        let input=folder.appendingPathComponent("source.png");try ImageEngine.write(canvas(256,192),to:input)
        let original=try Data(contentsOf:input)
        var options=OperationOptions();options.width=120;options.height=90
        let resized=try XCTUnwrap(ImageEngine.compress(input,options:options,token:.init()).first)
        let preview=try ImageEngine.load(resized);XCTAssertEqual(preview.width,120);XCTAssertEqual(preview.height,90)
        options.targetBytes=2000
        let compressed=try XCTUnwrap(ImageEngine.compress(input,options:options,token:.init()).first)
        XCTAssertLessThanOrEqual(try Data(contentsOf:compressed).count,2000)
        XCTAssertEqual(try Data(contentsOf:input),original)
        options.targetBytes=0
        XCTAssertThrowsError(try ImageEngine.compress(input,options:options,token:.init()))
    }

    func testCompressKeepsHEIFInputsInHEIFLikeHEIC() throws {
        let folder=try OutputFiles.temporaryDirectory();defer { try? FileManager.default.removeItem(at:folder) }
        let heic=folder.appendingPathComponent("photo.heic");try ImageEngine.write(canvas(),to:heic,quality:0.9)
        func type(_ url:URL)->String? { CGImageSourceCreateWithURL(url as CFURL,nil).flatMap { CGImageSourceGetType($0) as String? } }
        guard type(heic)=="public.heic" else { throw XCTSkip("HEIC encoding is unavailable on this Mac.") }
        // The catalog offers Compress for .heif as an alias of .heic.
        let heif=folder.appendingPathComponent("photo.heif");try FileManager.default.copyItem(at:heic,to:heif)
        XCTAssertTrue(FeatureCatalog.tools(for:[heif]).contains(.compress))
        var options=OperationOptions();options.outputDirectory=folder.appendingPathComponent("out")
        for input in [heic,heif] {
            let output=try XCTUnwrap(ConversionEngine.run(ConversionRequest(inputs:[input],operation:.tool(.compress),options:options)).first)
            XCTAssertEqual(output.pathExtension,input.pathExtension)
            XCTAssertTrue(["public.heic","public.heif"].contains(type(output) ?? ""),"\(output.lastPathComponent) is \(type(output) ?? "unreadable")")
        }
    }

    func testRemovingOrientationMetadataPreservesDisplayedDimensions() throws {
        let folder=try OutputFiles.temporaryDirectory();defer { try? FileManager.default.removeItem(at:folder) }
        let input=folder.appendingPathComponent("rotated.jpg")
        let destination=try XCTUnwrap(CGImageDestinationCreateWithURL(input as CFURL,"public.jpeg" as CFString,1,nil))
        CGImageDestinationAddImage(destination,try canvas(80,40),[kCGImagePropertyOrientation:6] as CFDictionary)
        XCTAssertTrue(CGImageDestinationFinalize(destination))
        let before=try ImageEngine.load(input);XCTAssertEqual(before.width,40);XCTAssertEqual(before.height,80)
        var changes=ImageMetadataOptions();changes.removeAll=true
        let clean=try ImageEngine.editMetadata(input,changes:changes)
        let after=try ImageEngine.load(clean);XCTAssertEqual(after.width,40);XCTAssertEqual(after.height,80)
    }

    func testWebPMetadataEditsPreserveEncodedImagePayload() throws {
        let folder=try OutputFiles.temporaryDirectory();defer { try? FileManager.default.removeItem(at:folder) }
        let input=folder.appendingPathComponent("source.webp");try ImageEngine.write(canvas(80,60),to:input)
        let original=try Data(contentsOf:input),originalImage=try ImageEngine.load(input)
        var changes=ImageMetadataOptions();changes.updates=["author":"Local Author","comment":"Keep the pixels"]
        let updated=try ImageEngine.editMetadata(input,changes:changes)
        XCTAssertEqual(ImageEngine.metadata(updated)["{TIFF}.Artist"],"Local Author",String(describing:ImageEngine.metadata(updated)))
        XCTAssertEqual(ImageEngine.metadata(updated)["{Exif}.UserComment"],"Keep the pixels")
        let imageChunks:Set<String>=["VP8 ","VP8L","ALPH","ANIM","ANMF"]
        let payloads=try WebPMetadataContainer.chunks(original).filter{imageChunks.contains($0.name)}.map(\.payload)
        XCTAssertEqual(try WebPMetadataContainer.chunks(Data(contentsOf:updated)).filter{imageChunks.contains($0.name)}.map(\.payload),payloads)
        XCTAssertEqual(try difference(originalImage,ImageEngine.load(updated)),0)
        changes=ImageMetadataOptions();changes.removeAll=true
        let clean=try ImageEngine.editMetadata(updated,changes:changes)
        XCTAssertNil(ImageEngine.metadata(clean)["{TIFF}.Artist"])
        // Color-space/orientation essentials may retain a minimal EXIF container.
        // Identity fields and the XMP packet must be gone; samples remain exact.
        XCTAssertNil(ImageEngine.metadata(clean)["{Exif}.UserComment"])
        XCTAssertFalse(try WebPMetadataContainer.chunks(Data(contentsOf:clean)).contains{$0.name=="XMP "})
        XCTAssertEqual(try WebPMetadataContainer.chunks(Data(contentsOf:clean)).filter{imageChunks.contains($0.name)}.map(\.payload),payloads)
        XCTAssertEqual(try difference(originalImage,ImageEngine.load(clean)),0)
        XCTAssertEqual(try Data(contentsOf:input),original)
        XCTAssertThrowsError(try WebPMetadataContainer.chunks(Data("RIFFbroken".utf8)))
    }

    func testMetadataOnlyWritesPreserveImageDataAcrossContainersAndXMP() throws {
        let folder=try OutputFiles.temporaryDirectory();defer { try? FileManager.default.removeItem(at:folder) }
        for format in ["jpg","png","tiff","heic"] {
            let input=folder.appendingPathComponent("source").appendingPathExtension(format)
            try ImageEngine.write(canvas(80,60),to:input,quality:0.65)
            let original=try Data(contentsOf:input),hash=try ImageMetadataEngine.encodedImageHash(input)
            var changes=ImageMetadataOptions();changes.updates=["author":"Uncompressed metadata edit","{XMP-dc}.Title":"Private XMP title"]
            let edited=try ImageEngine.editMetadata(input,changes:changes)
            XCTAssertEqual(try ImageMetadataEngine.encodedImageHash(edited),hash,format)
            XCTAssertEqual(ImageEngine.metadata(edited)["{XMP-dc}.Title"],"Private XMP title",format)
            XCTAssertEqual(try difference(ImageEngine.load(input),ImageEngine.load(edited)),0,format)
            changes=ImageMetadataOptions();changes.removeAll=true
            let clean=try ImageEngine.editMetadata(edited,changes:changes)
            XCTAssertEqual(try ImageMetadataEngine.encodedImageHash(clean),hash,format)
            XCTAssertNil(ImageEngine.metadata(clean)["{TIFF}.Artist"],format)
            XCTAssertNil(ImageEngine.metadata(clean)["{XMP-dc}.Title"],format)
            XCTAssertEqual(try difference(ImageEngine.load(input),ImageEngine.load(clean)),0,format)
            XCTAssertEqual(try Data(contentsOf:input),original,format)
        }
    }

    func testMetadataRejectsStructuralAndUnappliedEditsWithoutPublishing() throws {
        let folder=try OutputFiles.temporaryDirectory();defer { try? FileManager.default.removeItem(at:folder) }
        let input=folder.appendingPathComponent("source.jpg");try ImageEngine.write(canvas(),to:input)
        let original=try Data(contentsOf:input)
        for field in ["File:FileName","{TIFF}.Orientation","{XMP-unknown}.NoSuchWritableTag"] {
            var changes=ImageMetadataOptions();changes.updates=[field:"Wrong"]
            XCTAssertThrowsError(try ImageEngine.editMetadata(input,changes:changes),field)
            XCTAssertEqual(try FileManager.default.contentsOfDirectory(atPath:folder.path),["source.jpg"])
            XCTAssertEqual(try Data(contentsOf:input),original)
        }
    }

    func testDeclaredCustomXMPFieldsCanBeEditedAndRemovedWithoutReencoding() throws {
        let folder=try OutputFiles.temporaryDirectory();defer { try? FileManager.default.removeItem(at:folder) }
        let source=folder.appendingPathComponent("custom.jpg");try ImageEngine.write(canvas(80,60),to:source,quality:0.65)
        let packet=folder.appendingPathComponent("fixture.xmp")
        try Data("<x:xmpmeta xmlns:x='adobe:ns:meta/'><rdf:RDF xmlns:rdf='http://www.w3.org/1999/02/22-rdf-syntax-ns#'><rdf:Description rdf:about='' xmlns:custom='https://example.com/test-schema/' custom:Other='Keep this'><custom:Secret>Before</custom:Secret></rdf:Description></rdf:RDF></x:xmpmeta>".utf8).write(to:packet)
        let root=URL(fileURLWithPath:#filePath).deletingLastPathComponent().deletingLastPathComponent().deletingLastPathComponent()
        let script=root.appendingPathComponent("Tools/ExifTool/exiftool")
        try ProcessRunner.run(URL(fileURLWithPath:"/usr/bin/perl"),[script.path,"-config","","-overwrite_original","-XMP<="+packet.path,source.path])
        let original=try Data(contentsOf:source),hash=try ImageMetadataEngine.encodedImageHash(source)
        XCTAssertEqual(ImageEngine.metadata(source)["{XMP-custom}.Secret"],"Before")
        var changes=ImageMetadataOptions();changes.updates=["{XMP-custom}.Secret":"After & İstanbul"]
        let edited=try ImageEngine.editMetadata(source,changes:changes)
        XCTAssertEqual(ImageEngine.metadata(edited)["{XMP-custom}.Secret"],"After & İstanbul")
        XCTAssertEqual(ImageEngine.metadata(edited)["{XMP-custom}.Other"],"Keep this")
        XCTAssertEqual(try ImageMetadataEngine.encodedImageHash(edited),hash)
        let raw=try ProcessRunner.run(URL(fileURLWithPath:"/usr/bin/perl"),[script.path,"-config","","-b","-XMP",edited.path]).output
        XCTAssertTrue(String(decoding:raw,as:UTF8.self).contains("After &amp; İstanbul"))
        changes=ImageMetadataOptions();changes.removals=["{XMP-custom}.Secret"]
        let removed=try ImageEngine.editMetadata(edited,changes:changes)
        XCTAssertNil(ImageEngine.metadata(removed)["{XMP-custom}.Secret"])
        XCTAssertEqual(ImageEngine.metadata(removed)["{XMP-custom}.Other"],"Keep this")
        XCTAssertEqual(try ImageMetadataEngine.encodedImageHash(removed),hash)
        changes=ImageMetadataOptions();changes.removeAll=true
        let clean=try ImageEngine.editMetadata(edited,changes:changes)
        XCTAssertNil(ImageEngine.metadata(clean)["{XMP-custom}.Other"])
        XCTAssertEqual(try ImageMetadataEngine.encodedImageHash(clean),hash)
        XCTAssertEqual(try Data(contentsOf:source),original)
    }

    func testConversionMetadataPreferenceAndAnnotationFontsHaveRealEffects() throws {
        let folder=try OutputFiles.temporaryDirectory();defer { try? FileManager.default.removeItem(at:folder) }
        let source=folder.appendingPathComponent("source.jpg");try ImageEngine.write(canvas(200,120),to:source)
        var changes=ImageMetadataOptions();changes.updates=["author":"Private author","{XMP-dc}.Title":"Private title"]
        let tagged=try ImageEngine.editMetadata(source,changes:changes)
        var options=OperationOptions();options.preserveMetadata=true
        let preserved=try XCTUnwrap(ImageEngine.convert(tagged,to:"png",options:options,token:.init()).first)
        XCTAssertEqual(ImageEngine.metadata(preserved)["{TIFF}.Artist"],"Private author")
        XCTAssertEqual(ImageEngine.metadata(preserved)["{XMP-dc}.Title"],"Private title")
        options.preserveMetadata=false
        let stripped=try XCTUnwrap(ImageEngine.convert(tagged,to:"png",options:options,token:.init()).first)
        XCTAssertNil(ImageEngine.metadata(stripped)["{TIFF}.Artist"])
        XCTAssertNil(ImageEngine.metadata(stripped)["{XMP-dc}.Title"])
        var rendered:[[UInt8]]=[]
        for family in ["system","rounded","serif","monospaced"] {
            var a=ImageAnnotation(kind:.text,rect:EditRect(x:5,y:5,width:185,height:100));a.text="Font Ab 123";a.fontSize=24;a.fontFamily=family
            options.imageAnnotations=[a]
            rendered.append(try bytes(ImageEngine.annotatedImage(canvas(200,120,color:"#FFFFFF"),options:options)))
        }
        for i in 1..<rendered.count { XCTAssertFalse(rendered[0]==rendered[i],"Font design \(i) must change glyph rendering") }
    }

    func testFaceDetectionOnPublicFixtureDrivesActualRedaction() throws {
        let url=try XCTUnwrap(Bundle.module.url(forResource:"astronaut",withExtension:"png",subdirectory:"Fixtures"))
        let original=try Data(contentsOf:url),image=try ImageEngine.load(url)
        let regions=try ImageEngine.recognizedRegions(image,faces:true,text:false)
        let face=try XCTUnwrap(regions.first(where:{$0.kind == .face}))
        XCTAssertGreaterThan(face.rect.width,50);XCTAssertGreaterThan(face.rect.height,50)
        XCTAssertTrue((150...300).contains(face.rect.x+face.rect.width/2));XCTAssertTrue((50...220).contains(face.rect.y+face.rect.height/2))
        var options=OperationOptions(),redaction=ImageRedactionOptions();redaction.style = .solid;redaction.color="#000000";redaction.detectFaces=true;options.imageRedaction=redaction
        let result=try ImageEngine.redactedImage(image,options:options)
        XCTAssertEqual(try pixel(result,Int(face.rect.x+face.rect.width/2),Int(face.rect.y+face.rect.height/2)),[0,0,0])
        XCTAssertEqual(try pixel(result,5,5),try pixel(image,5,5));XCTAssertEqual(try Data(contentsOf:url),original)
    }

    func testLocalTextDetectionReturnsPixelBoxesAndDrivesRedaction() throws {
        let width=640,height=240
        let context=try XCTUnwrap(CGContext(data:nil,width:width,height:height,bitsPerComponent:8,bytesPerRow:width*4,space:CGColorSpace(name:CGColorSpace.sRGB)!,bitmapInfo:CGImageAlphaInfo.premultipliedLast.rawValue))
        context.setFillColor(CGColor(gray:1,alpha:1));context.fill(CGRect(x:0,y:0,width:width,height:height))
        let font=CTFontCreateWithName("Helvetica-Bold" as CFString,52,nil)
        let text=NSAttributedString(string:"PRIVATE 12345",attributes:[NSAttributedString.Key(kCTFontAttributeName as String):font,NSAttributedString.Key(kCTForegroundColorAttributeName as String):CGColor(gray:0,alpha:1)])
        context.textPosition=CGPoint(x:30,y:100);CTLineDraw(CTLineCreateWithAttributedString(text),context)
        let image=try XCTUnwrap(context.makeImage())
        let regions=try ImageEngine.recognizedRegions(image,faces:false,text:true)
        XCTAssertTrue(regions.contains { $0.text?.contains("PRIVATE")==true })
        for region in regions { XCTAssertGreaterThan(region.rect.width,10);XCTAssertGreaterThanOrEqual(region.rect.x,0);XCTAssertLessThanOrEqual(region.rect.y+region.rect.height,Double(height)) }
        var settings=ImageRedactionOptions();settings.detectText=true
        var options=OperationOptions();options.imageRedaction=settings
        let redacted=try ImageEngine.preview(image,tool:.redactImage,options:options)
        XCTAssertGreaterThan(try difference(redacted,image),1)
    }
}
