import XCTest
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
        let quality=try TargetSizeSearch.run(originalBytes:200000,targetBytes:5000,startQuality:90,lossy:true,allowResize:false,encode:smooth)
        XCTAssertEqual(quality.status,.bestEffortAboveTarget);XCTAssertNil(quality.best);XCTAssertGreaterThan(try XCTUnwrap(quality.smallest).bytes,5000)
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

    func testCancellationStopsEncodingAndDescriptionIsPlain() {
        let token=CancellationToken();var probes=0
        XCTAssertThrowsError(try TargetSizeSearch.run(originalBytes:200000,targetBytes:10,startQuality:90,lossy:true,allowResize:true,token:token){ q,s in probes+=1;if probes==3 { token.cancel() };return self.smooth(q,s) })
        XCTAssertEqual(probes,3)
        XCTAssertEqual(TargetSizeSearch.describe(.init(quality:72,scale:0.85,bytes:1),lossy:true),"quality 72 · 85% size")
        XCTAssertEqual(TargetSizeSearch.describe(.init(quality:100,scale:1,bytes:1),lossy:false),"original settings")
    }
}
