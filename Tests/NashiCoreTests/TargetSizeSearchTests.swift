import XCTest
import CoreGraphics
@testable import NashiCore

/// Mirrors windows/Nashi.Verify/TargetSizeChecks.cs so both platforms share the same expectations.
final class TargetSizeSearchTests: XCTestCase {
    private func smooth(_ quality:Int,_ scale:Double)->Int64 { Int64(1000+Double(quality*quality)*10*scale*scale) }

    func testReturnsHighestFittingQualityWithBoundedProbes() throws {
        var calls=0
        let fit=try TargetSizeSearch.run(originalBytes:200000,targetBytes:40000,startQuality:90,lossy:true,allowResize:true){ calls+=1;return self.smooth($0,$1) }
        let best=try XCTUnwrap(fit.best)
        XCTAssertEqual(fit.status,.achieved);XCTAssertEqual(best.scale,1);XCTAssertLessThanOrEqual(best.bytes,40000)
        XCTAssertGreaterThan(smooth(best.quality+1,1),40000);XCTAssertEqual(fit.attempts,calls);XCTAssertLessThanOrEqual(calls,8)
    }

    func testNonMonotonicCurveNeverReturnsOversizedCandidate() throws {
        let bumpy=try TargetSizeSearch.run(originalBytes:200000,targetBytes:30000,startQuality:95,lossy:true,allowResize:true){ q,s in q==50 ? 20000:self.smooth(q,s)+(q%7==0 ? 15000:0) }
        XCTAssertEqual(bumpy.status,.achieved);XCTAssertLessThanOrEqual(try XCTUnwrap(bumpy.best).bytes,30000)
    }

    func testResizesBeforeDroppingBelowQualityFloorAndReportsUnreachable() throws {
        let resized=try XCTUnwrap(TargetSizeSearch.run(originalBytes:200000,targetBytes:9000,startQuality:90,lossy:true,allowResize:true,encode:smooth).best)
        XCTAssertLessThan(resized.scale,1);XCTAssertGreaterThanOrEqual(resized.quality,TargetSizeSearch.qualityFloor)
        // Without resizing the search reports the target as unreachable (the floor always applies).
        let quality=try TargetSizeSearch.run(originalBytes:200000,targetBytes:5000,startQuality:90,lossy:true,allowResize:false,encode:smooth)
        XCTAssertEqual(quality.status,.bestEffortAboveTarget);XCTAssertNil(quality.best);XCTAssertGreaterThan(quality.attempts,0)
    }

    func testLosslessAlreadyUnderTargetPlateauAndInvalidTarget() throws {
        let lossless=try XCTUnwrap(TargetSizeSearch.run(originalBytes:200000,targetBytes:60000,startQuality:100,lossy:false,allowResize:true){ _,s in Int64(200000*s*s) }.best)
        XCTAssertEqual(lossless.quality,100);XCTAssertLessThanOrEqual(lossless.bytes,60000)
        XCTAssertEqual(try TargetSizeSearch.run(originalBytes:1000,targetBytes:1000,startQuality:80,lossy:true,allowResize:true){ _,_ in XCTFail("encoded");return 0 }.status,.alreadyUnderTarget)
        let flat=try TargetSizeSearch.run(originalBytes:200000,targetBytes:10,startQuality:90,lossy:true,allowResize:true){ _,_ in 50000 }
        XCTAssertEqual(flat.status,.bestEffortAboveTarget);XCTAssertLessThanOrEqual(flat.attempts,TargetSizeSearch.maxAttempts)
        XCTAssertThrowsError(try TargetSizeSearch.run(originalBytes:200000,targetBytes:0,startQuality:90,lossy:true,allowResize:true,encode:smooth))
    }

    func testEveryScaleStepIsReachedWithoutRepeatedProbes() throws {
        var seen:[String]=[]
        let outcome=try TargetSizeSearch.run(originalBytes:200000,targetBytes:10,startQuality:90,lossy:true,allowResize:true){ q,s in seen.append("\(q)@\(s)");return 50000 }
        XCTAssertEqual(Set(seen).count,seen.count);XCTAssertEqual(outcome.attempts,seen.count)
        XCTAssertTrue(seen.contains("\(TargetSizeSearch.qualityFloor)@\(TargetSizeSearch.scales.last!)"))
    }

    func testTransformedOutputIsMeasuredEvenWhenSourceIsUnderTarget() throws {
        // compress passes .max when the source cannot be kept (resized or re-encoded output).
        let outcome=try TargetSizeSearch.run(originalBytes:.max,targetBytes:30000,startQuality:90,lossy:true,allowResize:true,encode:smooth)
        XCTAssertEqual(outcome.status,.achieved);XCTAssertGreaterThan(outcome.attempts,0);XCTAssertLessThanOrEqual(try XCTUnwrap(outcome.best).bytes,30000)
    }

    func testCompressionMeasuresUpscaledOutputWhenSourceIsUnderTarget() throws {
        let folder=try OutputFiles.temporaryDirectory();defer { try? FileManager.default.removeItem(at:folder) }
        let input=folder.appendingPathComponent("small.png")
        // Seeded noise keeps the fixture detailed, synthetic and deterministic.
        var seed:UInt64=42,pixels=[UInt8](repeating:255,count:16*12*4)
        for index in pixels.indices where index%4 != 3 { seed=seed &* 6364136223846793005 &+ 1442695040888963407;pixels[index]=UInt8(truncatingIfNeeded:seed>>56) }
        // The context must not outlive the buffer, so the image is created and written inside the scope.
        try pixels.withUnsafeMutableBytes { buffer in
            let context=try XCTUnwrap(CGContext(data:buffer.baseAddress,width:16,height:12,bitsPerComponent:8,bytesPerRow:16*4,space:CGColorSpace(name:CGColorSpace.sRGB)!,bitmapInfo:CGImageAlphaInfo.premultipliedLast.rawValue))
            try ImageEngine.write(try XCTUnwrap(context.makeImage()),to:input)
        }
        let original=try Data(contentsOf:input),target=Int64(original.count)+1
        var options=OperationOptions();options.width=120;options.height=90
        let expanded=try XCTUnwrap(ImageEngine.compress(input,options:options,token:.init()).first)
        XCTAssertGreaterThan(Int64(try Data(contentsOf:expanded).count),target)
        options.targetBytes=target
        let compressed=try XCTUnwrap(ImageEngine.compress(input,options:options,token:.init()).first)
        XCTAssertLessThanOrEqual(Int64(try Data(contentsOf:compressed).count),target)
        XCTAssertEqual(try Data(contentsOf:input),original)
    }

    func testOnePixelWideImageShrinksLongEdge() throws {
        let folder=try OutputFiles.temporaryDirectory();defer { try? FileManager.default.removeItem(at:folder) }
        // Large noise PNG: incompressible, so reaching a quarter-sized target requires shrinking the long edge.
        let input=folder.appendingPathComponent("thin.png")
        var pixels=[UInt8](repeating:0,count:1*2000*4)
        var seed:UInt64=7
        for index in pixels.indices where index%4 != 3 { seed=seed &* 6364136223846793005 &+ 1442695040888963407;pixels[index]=UInt8(truncatingIfNeeded:seed>>56) }
        try pixels.withUnsafeMutableBytes { buffer in
            let context=try XCTUnwrap(CGContext(data:buffer.baseAddress,width:1,height:2000,bitsPerComponent:8,bytesPerRow:4,space:CGColorSpace(name:CGColorSpace.sRGB)!,bitmapInfo:CGImageAlphaInfo.premultipliedLast.rawValue))
            try ImageEngine.write(try XCTUnwrap(context.makeImage()),to:input)
        }
        let original=try Data(contentsOf:input),bytes=Int64(original.count)
        // Large noise PNG: incompressible, so reaching a quarter-sized target requires shrinking the long edge.
        let target=max(bytes/4,1)
        var options=OperationOptions();options.outputDirectory=folder;options.targetBytes=target;options.preferQuality=false
        let compressed=try XCTUnwrap(ImageEngine.compress(input,options:options,token:.init()).first)
        let result=try ImageEngine.load(compressed)
        XCTAssertEqual(result.width,1,"1-pixel-wide image must keep its short edge at one pixel")
        XCTAssertLessThan(result.height,2000,"Long edge of a 1-pixel-wide image must shrink")
        XCTAssertLessThanOrEqual(Int64(try Data(contentsOf:compressed).count),target)
    }

    func testOnePixelTallImageShrinksLongEdge() throws {
        let folder=try OutputFiles.temporaryDirectory();defer { try? FileManager.default.removeItem(at:folder) }
        // Large noise PNG: incompressible, so reaching a quarter-sized target requires shrinking the long edge.
        let input=folder.appendingPathComponent("flat.png")
        var pixels=[UInt8](repeating:0,count:2000*1*4)
        var seed:UInt64=7
        for index in pixels.indices where index%4 != 3 { seed=seed &* 6364136223846793005 &+ 1442695040888963407;pixels[index]=UInt8(truncatingIfNeeded:seed>>56) }
        try pixels.withUnsafeMutableBytes { buffer in
            let context=try XCTUnwrap(CGContext(data:buffer.baseAddress,width:2000,height:1,bitsPerComponent:8,bytesPerRow:8000,space:CGColorSpace(name:CGColorSpace.sRGB)!,bitmapInfo:CGImageAlphaInfo.premultipliedLast.rawValue))
            try ImageEngine.write(try XCTUnwrap(context.makeImage()),to:input)
        }
        let original=try Data(contentsOf:input),bytes=Int64(original.count)
        let target=max(bytes/4,1)
        var options=OperationOptions();options.outputDirectory=folder;options.targetBytes=target;options.preferQuality=false
        let compressed=try XCTUnwrap(ImageEngine.compress(input,options:options,token:.init()).first)
        let result=try ImageEngine.load(compressed)
        XCTAssertEqual(result.height,1,"1-pixel-tall image must keep its short edge at one pixel")
        XCTAssertLessThan(result.width,2000,"Long edge of a 1-pixel-tall image must shrink")
        XCTAssertLessThanOrEqual(Int64(try Data(contentsOf:compressed).count),target)
    }

    /// Seeded noise PNG: lossless and hard to compress, so a small target needs fewer pixels.
    private func noisePNG(_ width:Int,_ height:Int,in folder:URL) throws -> URL {
        let url=folder.appendingPathComponent("noise-\(width)x\(height).png")
        var seed:UInt64=7,pixels=[UInt8](repeating:255,count:width*height*4)
        for index in pixels.indices where index%4 != 3 { seed=seed &* 6364136223846793005 &+ 1442695040888963407;pixels[index]=UInt8(truncatingIfNeeded:seed>>56) }
        try pixels.withUnsafeMutableBytes { buffer in
            let context=try XCTUnwrap(CGContext(data:buffer.baseAddress,width:width,height:height,bitsPerComponent:8,bytesPerRow:width*4,space:CGColorSpace(name:CGColorSpace.sRGB)!,bitmapInfo:CGImageAlphaInfo.premultipliedLast.rawValue))
            try ImageEngine.write(try XCTUnwrap(context.makeImage()),to:url)
        }
        return url
    }

    func testQualityPriorityKeepsDimensionsOrRefusesWithoutOutput() throws {
        let folder=try OutputFiles.temporaryDirectory();defer { try? FileManager.default.removeItem(at:folder) }
        let input=try noisePNG(256,192,in:folder),original=try Data(contentsOf:input),bytes=Int64(original.count)
        var options=OperationOptions();options.outputDirectory=folder;options.targetBytes=bytes/4;options.preferQuality=true
        // A lossless image four times too large cannot fit without fewer pixels.
        XCTAssertThrowsError(try ImageEngine.compress(input,options:options,token:.init())) { error in
            XCTAssertTrue("\(error)".contains("without lowering the resolution"),"\(error)")
        }
        XCTAssertEqual(try FileManager.default.contentsOfDirectory(atPath:folder.path),[input.lastPathComponent])
        // Size priority reaches the same target by resizing.
        options.preferQuality=false
        let resized=try XCTUnwrap(ImageEngine.compress(input,options:options,token:.init()).first)
        XCTAssertLessThanOrEqual(Int64(try Data(contentsOf:resized).count),bytes/4)
        XCTAssertLessThan(try ImageEngine.load(resized).width,256)
        // A reachable target under Quality priority keeps the dimensions.
        options.preferQuality=true;options.targetBytes=bytes*2
        let kept=try XCTUnwrap(ImageEngine.compress(input,options:options,token:.init()).first),image=try ImageEngine.load(kept)
        XCTAssertEqual(image.width,256);XCTAssertEqual(image.height,192)
        XCTAssertEqual(try Data(contentsOf:input),original)
    }

    func testCancellationStopsEncoding() {
        let token=CancellationToken();var probes=0
        XCTAssertThrowsError(try TargetSizeSearch.run(originalBytes:200000,targetBytes:10,startQuality:90,lossy:true,allowResize:true,token:token){ q,s in probes+=1;if probes==3 { token.cancel() };return self.smooth(q,s) })
        XCTAssertEqual(probes,3)
    }
}
