using Nashi.Core;
internal static class FitLayoutChecks
{
    internal static void Run(Action<bool,string> check)
    {
        // A 4000×3000 landscape photo into an Instagram portrait post (1080×1350).
        var fit=FitLayout.Place(4000,3000,1080,1350,FitMode.Fit);
        check(fit.X==0&&fit.Width==1080&&Math.Abs(fit.Height-810)<1e-9&&Math.Abs(fit.Y-270)<1e-9,"Fit must keep the whole photo, centred with even margins: "+fit);
        var fill=FitLayout.Place(4000,3000,1080,1350,FitMode.Fill);
        check(fill.Y==0&&fill.Height==1350&&Math.Abs(fill.Width-1800)<1e-9&&Math.Abs(fill.X+360)<1e-9,"Fill must cover the canvas and crop evenly: "+fill);
        check(FitLayout.Place(4000,3000,1080,1350,FitMode.Stretch)==new FitLayout.Placement(0,0,1080,1350),"Stretch must use the exact size");
        check(FitLayout.Visible(4000,3000,1080,1350)==(800,0,2400,3000),"Fill must keep the centre of the photo: "+FitLayout.Visible(4000,3000,1080,1350));
        var same=FitLayout.Place(1920,1080,1280,720,FitMode.Fit);check(same==new FitLayout.Placement(0,0,1280,720)&&FitLayout.Place(1920,1080,1280,720,FitMode.Fill)==same,"The same shape must fill exactly in every mode");
        var tall=FitLayout.Place(1000,3000,1080,1080,FitMode.Fit);check(tall.Y==0&&tall.Height==1080&&Math.Abs(tall.X-360)<1e-9,"A tall image must get side margins");
        var odd=FitLayout.Place(333,777,1000,1000,FitMode.Fit);check(odd.Height==1000&&odd.X>0&&odd.X+odd.Width<=1000.000001,"Odd sizes must stay inside the canvas");
        var tiny=FitLayout.Place(10,10,3840,2160,FitMode.Fit);check(tiny.Height==2160&&Math.Abs(tiny.Width-2160)<1e-9,"Small images are enlarged to the template size");
        foreach(var bad in new[]{(0,10,10,10),(10,10,0,10),(10,-1,10,10)})
        {try{FitLayout.Place(bad.Item1,bad.Item2,bad.Item3,bad.Item4,FitMode.Fit);check(false,"An empty size was accepted");}catch(ArgumentException){check(true,"Empty size refused");}}
        check(FitLayout.Templates.Length>=20&&FitLayout.Templates.All(t=>t.Width is >0 and <=30000&&t.Height is >0 and <=30000)&&FitLayout.Templates.Select(t=>(t.Group,t.Name)).Distinct().Count()==FitLayout.Templates.Length,"Templates must have valid, distinct sizes");
        var passport=FitLayout.Templates.Single(t=>t.Name.StartsWith("Passport"));check((passport.Width,passport.Height)==(413,531),"A 35×45 mm passport photo is 413×531 px at 300 dpi");
        var a4=FitLayout.Templates.Single(t=>t.Name=="A4 portrait");check((a4.Width,a4.Height)==(2480,3508),"A4 is 2480×3508 px at 300 dpi");
        check(FitLayout.Templates.Single(t=>t.Name=="Portrait post").Ratio==0.8,"Instagram portrait posts are 4:5");
    }
}
