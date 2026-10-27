using Nashi.Core;

internal static class TargetSizeChecks
{
    /// Synthetic size curves exercise the search without encoders, so they run identically on every machine.
    internal static void Run(Action<bool,string> check)
    {
        static long Smooth(int quality,double scale)=>(long)(1000+quality*quality*10*scale*scale);
        var calls=0;var fit=TargetSizeSearch.Run(200000,40000,90,true,true,(q,s)=>{calls++;return Smooth(q,s);});
        check(fit.Status==TargetSizeStatus.Achieved&&fit.Best is {Scale:1}&&fit.Best.Bytes<=40000&&Smooth(fit.Best.Quality+1,1)>40000,"Target search did not return the highest fitting quality");
        check(fit.Attempts==calls&&calls<=8,"Target search measured more candidates than a bisection needs");
        // A dip at quality 50 makes size non-monotonic; the chosen candidate must still be measured under the limit.
        var bumpy=TargetSizeSearch.Run(200000,30000,95,true,true,(q,s)=>q==50?20000:Smooth(q,s)+(q%7==0?15000:0));
        check(bumpy.Status==TargetSizeStatus.Achieved&&bumpy.Best!.Bytes<=30000,"Non-monotonic size curve returned an oversized candidate");
        var resized=TargetSizeSearch.Run(200000,9000,90,true,true,Smooth);
        check(resized.Status==TargetSizeStatus.Achieved&&resized.Best!.Scale<1&&resized.Best.Quality>=TargetSizeSearch.QualityFloor,"Size priority did not resize before dropping below the quality floor");
        var quality=TargetSizeSearch.Run(200000,5000,90,true,false,Smooth);
        check(quality.Status==TargetSizeStatus.BestEffortAboveTarget&&quality.Best is null&&quality.Smallest!.Bytes>5000,"An unreachable target was reported as achieved");
        var lossless=TargetSizeSearch.Run(200000,60000,100,false,true,(q,s)=>(long)(200000*s*s));
        check(lossless.Status==TargetSizeStatus.Achieved&&lossless.Best!.Quality==100&&lossless.Best.Bytes<=60000,"Lossless search changed quality or missed a reachable size");
        check(TargetSizeSearch.Run(1000,1000,80,true,true,(_,_)=>throw new Exception("encoded")).Status==TargetSizeStatus.AlreadyUnderTarget,"A file already under target was re-encoded");
        var flat=TargetSizeSearch.Run(200000,10,90,true,true,(_,_)=>50000);check(flat.Attempts<=TargetSizeSearch.MaxAttempts&&flat.Status==TargetSizeStatus.BestEffortAboveTarget,"A plateau exceeded the attempt cap");
        var seen=new List<(int,double)>();var ladder=TargetSizeSearch.Run(200000,10,90,true,true,(q,sc)=>{seen.Add((q,sc));return 50000;});
        check(seen.Distinct().Count()==seen.Count&&seen.Contains((TargetSizeSearch.QualityFloor,TargetSizeSearch.Scales[^1]))&&ladder.Attempts==seen.Count,"Target search repeated a probe or stopped before the last scale step");
        try{TargetSizeSearch.Run(200000,0,90,true,true,Smooth);check(false,"A zero target was accepted");}catch(ArgumentException){check(true,"");}
        using var cancel=new CancellationTokenSource();var probes=0;
        try{TargetSizeSearch.Run(200000,10,90,true,true,(q,s)=>{if(++probes==3)cancel.Cancel();return Smooth(q,s);},cancel.Token);check(false,"Cancellation was ignored");}catch(OperationCanceledException){check(probes==3,"Search encoded after cancellation");}
        check(TargetSizeSearch.Describe(new(72,.85,1),true)=="quality 72 · 85% size"&&TargetSizeSearch.Describe(new(100,1,1),false)=="original settings","Chosen settings description changed");
    }
}
