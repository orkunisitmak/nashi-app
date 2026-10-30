using Nashi.Core;
internal static class WindowShortcutChecks
{
    public static void Run(Action<bool,string> check,string root)
    {
        var area=new WindowFrame(0,40,1920,1000);var top=WindowPlacement.TopHalf.Frame(area);var bottom=WindowPlacement.BottomHalf.Frame(area);
        check(top==new WindowFrame(0,40,1920,500)&&bottom==new WindowFrame(0,540,1920,500),"Top/bottom halves do not split the work area");
        check(WindowPlacement.TopHalf.Frame(area,0,700).Y==area.Y&&WindowPlacement.BottomHalf.Frame(area,0,700).Bottom==area.Bottom,"A minimum height lost the top/bottom anchor");
        foreach(var placement in Enum.GetValues<WindowPlacement>())if(placement.VerticalRange()==(0,1))check(placement.Frame(area).Y==area.Y&&placement.Frame(area).Height==area.Height,"Side layout changed the usable vertical frame");

        // Laptop in the middle, a display on each side and one stacked above.
        var laptop=new WindowFrame(0,0,1512,920);var right=new WindowFrame(1512,-100,2560,1400);var left=new WindowFrame(-1920,0,1920,1050);var above=new WindowFrame(0,-900,1512,900);
        WindowFrame[] displays=[laptop,right,left,above];
        check(WindowPlacement.RightHalf.Destination(new(10,10,500,400),displays,0)==(WindowPlacement.RightHalf,0),"An unplaced window skipped to another display");
        check(WindowPlacement.RightHalf.Destination(WindowPlacement.RightHalf.Frame(laptop),displays,0)==(WindowPlacement.LeftHalf,1),"Repeated right half did not continue on the next display");
        check(WindowPlacement.RightHalf.Destination(WindowPlacement.LeftHalf.Frame(right),displays,1)==(WindowPlacement.RightHalf,1),"The next display's left half did not move on to its right half");
        check(WindowPlacement.RightHalf.Destination(WindowPlacement.RightHalf.Frame(right),displays,1)==(WindowPlacement.RightHalf,1),"The rightmost display wrapped around");
        check(WindowPlacement.LeftThird.Destination(WindowPlacement.LeftThird.Frame(laptop),displays,0)==(WindowPlacement.RightThird,2),"Repeated left third did not continue on the left display");
        check(WindowPlacement.RightHalf.Destination(WindowPlacement.RightHalf.Frame(laptop,1000),displays,0)==(WindowPlacement.LeftHalf,1),"A minimum width stopped the display hop");
        foreach(var placement in new[]{WindowPlacement.MiddleThird,WindowPlacement.TopHalf,WindowPlacement.BottomHalf,WindowPlacement.Maximize})check(placement.Destination(placement.Frame(laptop),displays,0)==(placement,0),"A centered or vertical layout left its display");

        var defaults=Enum.GetValues<WindowPlacement>().Select(p=>p.Default()).ToList();
        check(defaults.Distinct().Count()==defaults.Count&&defaults.All(s=>s.IsValid&&s!=WindowShortcut.DownloadLink),"Default window shortcuts collide");
        check(WindowPlacement.LeftHalf.Default().Text=="Ctrl+Alt+←"&&WindowPlacement.LeftThird.Default().Text=="Ctrl+Alt+Win+←"&&WindowPlacement.RightTwoThirds.Default().Text=="Ctrl+Alt+Shift+→"&&WindowPlacement.Maximize.Default().Text=="Ctrl+Alt+F","Default window shortcut labels changed");
        var store=new PreferencesStore(Path.Combine(root,"Window shortcuts"));var custom=new WindowShortcut(WindowShortcut.Control|WindowShortcut.Alt,0x4A);
        check(new WindowShortcut(WindowShortcut.Shift,0x4A).IsValid==false&&new WindowShortcut(WindowShortcut.Control,0x11).IsValid==false,"Shift-only or modifier-only shortcuts were accepted");
        var (saved,_)=store.Value.WithShortcut(WindowPlacement.LeftThird,custom);check(saved is not null,"A free shortcut was refused");store.Save(saved!);
        var (cleared,_)=store.Value.WithShortcut(WindowPlacement.LeftHalf,null);store.Save(cleared!);
        var reopened=new PreferencesStore(Path.Combine(root,"Window shortcuts")).Value;
        check(reopened.Shortcut(WindowPlacement.LeftThird)==custom&&reopened.Shortcut(WindowPlacement.LeftHalf) is null&&reopened.Shortcut(WindowPlacement.Maximize)==WindowPlacement.Maximize.Default(),"Window shortcuts did not persist, clear or fall back to defaults");
        check(reopened.WithShortcut(WindowPlacement.RightThird,custom)==(null,"Left third")&&reopened.WithShortcut(WindowPlacement.Maximize,WindowShortcut.DownloadLink)==(null,"Download Link…")&&reopened.WithShortcut(WindowPlacement.Maximize,new(WindowShortcut.Shift,0x4A))==(null,null),"Conflicting or invalid window shortcuts were accepted");
        // A cleared shortcut frees its combination for another layout.
        check(reopened.WithShortcut(WindowPlacement.RightHalf,WindowPlacement.LeftHalf.Default()).Value is not null,"A cleared shortcut still blocked its combination");
        try{store.Save(store.Value with{WindowShortcuts=new(store.Value.WindowShortcuts){["RightHalf"]=custom}});throw new Exception("Duplicate stored window shortcuts accepted");}catch(ArgumentException){check(true,"Stored window shortcut collision validation");}
        check(AppLocalization.Text("Ctrl+Alt+← is already used by Sol yarı.","tr")=="Ctrl+Alt+← zaten Sol yarı için kullanılıyor.","Window shortcut conflict copy stayed in English");
    }
}
