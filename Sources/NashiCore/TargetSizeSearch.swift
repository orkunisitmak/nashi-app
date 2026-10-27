import Foundation

public enum TargetSizeStatus: Equatable, Sendable { case achieved, alreadyUnderTarget, bestEffortAboveTarget }

/// One measured encode: quality 1-100, scale of the longest edge, and the real encoded size.
public struct TargetSizeCandidate: Equatable, Sendable { public let quality:Int,scale:Double,bytes:Int64 }

public struct TargetSizeOutcome: Equatable, Sendable {
    public let status:TargetSizeStatus,best:TargetSizeCandidate?,smallest:TargetSizeCandidate?,attempts:Int
}

/// Finds the highest-quality encode that fits a byte limit by measuring real outputs.
/// Encoded size is not linear and not always monotonic in quality, so every probe is
/// measured and the best valid candidate seen is kept instead of trusting the bisection end.
/// The Windows twin is windows/Nashi.Core/TargetSizeSearch.cs; keep both in step.
public enum TargetSizeSearch {
    /// Below this JPEG/WebP/HEIC quality block artifacts dominate; resizing preserves more detail.
    public static let qualityFloor=30
    /// Longest-edge scale steps tried after quality alone cannot fit.
    public static let scales:[Double]=[1,0.85,0.7,0.55,0.4,0.3,0.2,0.12,0.08,0.05]
    /// Bisection over 1-100 needs at most 7 probes per scale; this caps pathological inputs.
    public static let maxAttempts=64

    public static func run(originalBytes:Int64,targetBytes:Int64,startQuality:Int,lossy:Bool,allowResize:Bool,token:CancellationToken = .init(),encode:(Int,Double) throws->Int64) throws->TargetSizeOutcome {
        guard targetBytes>0 else { throw NashiError.invalidInput("Choose a positive target file size.") }
        if originalBytes<=targetBytes { return .init(status:.alreadyUnderTarget,best:nil,smallest:nil,attempts:0) }
        var attempts=0,smallest:TargetSizeCandidate?
        func probe(_ quality:Int,_ scale:Double) throws->TargetSizeCandidate {
            try token.check();attempts+=1
            let candidate=TargetSizeCandidate(quality:quality,scale:scale,bytes:try encode(quality,scale))
            if smallest == nil || candidate.bytes<smallest!.bytes { smallest=candidate };return candidate
        }
        let top=max(1,min(100,startQuality)),floor=min(qualityFloor,top)
        for scale in allowResize ? scales : [1] {
            if attempts>=maxAttempts { break }
            if !lossy {
                let whole=try probe(top,scale)
                if whole.bytes<=targetBytes { return .init(status:.achieved,best:whole,smallest:smallest,attempts:attempts) };continue
            }
            let first=try probe(top,scale)
            if first.bytes<=targetBytes { return .init(status:.achieved,best:first,smallest:smallest,attempts:attempts) }
            var best:TargetSizeCandidate?,low=floor,high=top-1
            while low<=high && attempts<maxAttempts {
                let middle=low+(high-low+1)/2,candidate=try probe(middle,scale)
                if candidate.bytes<=targetBytes { if best == nil || candidate.quality>best!.quality { best=candidate };low=middle+1 } else { high=middle-1 }
            }
            // A non-monotonic curve can make every bisection probe miss although the floor fits.
            if best == nil && floor<top && attempts<maxAttempts { let last=try probe(floor,scale);if last.bytes<=targetBytes { best=last } }
            if let best { return .init(status:.achieved,best:best,smallest:smallest,attempts:attempts) }
        }
        return .init(status:.bestEffortAboveTarget,best:nil,smallest:smallest,attempts:attempts)
    }

    /// Plain description of the chosen settings, e.g. "quality 72 · 85% size".
    public static func describe(_ candidate:TargetSizeCandidate,lossy:Bool)->String {
        let parts=[lossy ? "quality \(candidate.quality)":nil,candidate.scale<1 ? "\(Int((candidate.scale*100).rounded()))% size":nil].compactMap{$0}
        return parts.isEmpty ? "original settings":parts.joined(separator:" · ")
    }
}
