namespace Nashi.Core;

public enum TargetSizeStatus { Achieved, AlreadyUnderTarget, BestEffortAboveTarget }

/// <summary>One measured encode: quality 1-100, scale of the longest edge, and the real encoded size.</summary>
public sealed record TargetSizeCandidate(int Quality,double Scale,long Bytes);

public sealed record TargetSizeOutcome(TargetSizeStatus Status,TargetSizeCandidate? Best,TargetSizeCandidate? Smallest,int Attempts);

/// <summary>
/// Finds the highest-quality encode that fits a byte limit by measuring real outputs.
/// Encoded size is not linear and not always monotonic in quality, so every probe is
/// measured and the best valid candidate seen is kept instead of trusting the bisection end.
/// The Mac twin is Sources/NashiCore/TargetSizeSearch.swift; keep both in step.
/// </summary>
public static class TargetSizeSearch
{
    /// Below this JPEG/WebP/HEIC quality block artifacts dominate; resizing preserves more detail.
    public const int QualityFloor=30;
    /// Longest-edge scale steps tried after quality alone cannot fit (Size priority).
    public static readonly double[] Scales=[1,.85,.7,.55,.4,.3,.2,.12,.08,.05];
    /// Bisection over 1-100 needs at most 7 probes per scale; this caps pathological inputs.
    public const int MaxAttempts=64;

    public static TargetSizeOutcome Run(long originalBytes,long targetBytes,int startQuality,bool lossy,bool allowResize,Func<int,double,long> encode,CancellationToken token=default)
    {
        if(targetBytes<=0)throw new ArgumentException("Choose a positive target size.");
        if(originalBytes<=targetBytes)return new(TargetSizeStatus.AlreadyUnderTarget,null,null,0);
        var attempts=0;TargetSizeCandidate? smallest=null;
        TargetSizeCandidate Probe(int quality,double scale)
        {
            token.ThrowIfCancellationRequested();attempts++;var candidate=new TargetSizeCandidate(quality,scale,encode(quality,scale));
            if(smallest is null||candidate.Bytes<smallest.Bytes)smallest=candidate;return candidate;
        }
        var top=Math.Clamp(startQuality,1,100);var floor=Math.Min(QualityFloor,top);
        foreach(var scale in allowResize?Scales:Scales[..1])
        {
            if(attempts>=MaxAttempts)break;
            if(!lossy){var whole=Probe(top,scale);if(whole.Bytes<=targetBytes)return new(TargetSizeStatus.Achieved,whole,smallest,attempts);continue;}
            var best=default(TargetSizeCandidate);var first=Probe(top,scale);if(first.Bytes<=targetBytes)return new(TargetSizeStatus.Achieved,first,smallest,attempts);
            var low=floor;var high=top-1;
            while(low<=high&&attempts<MaxAttempts)
            {
                var middle=low+(high-low+1)/2;var candidate=Probe(middle,scale);
                if(candidate.Bytes<=targetBytes){if(best is null||candidate.Quality>best.Quality)best=candidate;low=middle+1;}else high=middle-1;
            }
            // A non-monotonic curve can make every bisection probe miss although the floor fits.
            if(best is null&&floor<top&&attempts<MaxAttempts){var last=Probe(floor,scale);if(last.Bytes<=targetBytes)best=last;}
            if(best is not null)return new(TargetSizeStatus.Achieved,best,smallest,attempts);
        }
        return new(TargetSizeStatus.BestEffortAboveTarget,null,smallest,attempts);
    }

    /// Plain description of the chosen settings, e.g. "quality 72 · 85% size".
    public static string Describe(TargetSizeCandidate candidate,bool lossy)=>
        string.Join(" · ",new[]{lossy?$"quality {candidate.Quality}":null,candidate.Scale<1?$"{Math.Round(candidate.Scale*100)}% size":null}.Where(s=>s is not null))is {Length:>0} text?text:"original settings";
}
